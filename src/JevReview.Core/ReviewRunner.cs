using System.Diagnostics;
using System.Text;

namespace JevReview;

public sealed class ReviewRunner(IJudgeClient client)
{
    public async Task<ReviewRun> RunAsync(ReviewItem[] items, ReviewRubric rubric, RunOptions options,
        string outputDirectory, CancellationToken cancellationToken, string? resumeDirectory = null,
        Dictionary<string, string>? source = null)
    {
        ValidateOptions(options);
        if (items.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("duplicate_item_ids");
        if (resumeDirectory != null && options.Mode == "live" && options.Model is "jev-latest" or "jev-preview")
            throw new ArgumentException("resume_requires_pinned_live_model");
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        var rubricHash = JsonFiles.Hash(JsonFiles.Serialize(rubric));
        var configHash = JsonFiles.Hash(JsonFiles.Serialize(options));
        var policy = new ReviewPolicy(options.Policy);
        var cached = resumeDirectory == null ? [] : JsonFiles.ReadLines<Verdict>(Path.Combine(resumeDirectory, "verdicts.jsonl"));
        var cache = cached.Where(x => x.Status == "classified" && x.RubricHash == rubricHash && x.ConfigHash == configHash)
            .ToDictionary(x => x.Id, StringComparer.Ordinal);
        var verdicts = new Verdict[items.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, items.Length),
            new ParallelOptions { MaxDegreeOfParallelism = options.Concurrency }, async (index, _) =>
        {
            var item = items[index];
            var inputHash = JsonFiles.Hash(JsonFiles.Serialize(item));
            Verdict Error(string status, string code, int attempts = 0, double latency = 0) =>
                new(item.Id, item.Path ?? "", inputHash, rubricHash, configHash, status, null, true, [code], code, attempts, latency);
            if (string.IsNullOrWhiteSpace(item.Task) || string.IsNullOrWhiteSpace(item.Path) || string.IsNullOrWhiteSpace(item.Diff))
            {
                verdicts[index] = Error("excluded", "invalid_item");
                return;
            }
            if (Encoding.UTF8.GetByteCount(JevJudgeClient.BuildRequest(item, rubric, options.Model)) > options.MaxInputBytes)
            {
                verdicts[index] = Error("excluded", "input_too_large");
                return;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                verdicts[index] = Error("unjudged", "cancelled");
                return;
            }
            if (cache.TryGetValue(item.Id, out var old) && old.InputHash == inputHash && old.Prediction != null &&
                (options.Mode != "live" || old.Prediction.Model == options.Model))
            {
                try
                {
                    JevResponseParser.Validate(old.Prediction, rubric);
                    var reasons = policy.Reasons(item, old.Prediction);
                    verdicts[index] = old with { Reasons = reasons, Flagged = reasons.Length > 0, Attempts = 0, LatencyMilliseconds = 0, Reused = true };
                    return;
                }
                catch (JudgeException) { /* Invalid successful cache entry is a miss, not a reason to lose the run. */ }
            }
            var timer = Stopwatch.StartNew();
            for (var attempt = 1; attempt <= options.MaxRetries + 1; attempt++)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                    var result = await client.JudgeAsync(item, rubric, timeout.Token);
                    JevResponseParser.Validate(result, rubric);
                    var reasons = policy.Reasons(item, result);
                    verdicts[index] = new(item.Id, item.Path, inputHash, rubricHash, configHash, "classified", result,
                        reasons.Length > 0, reasons, null, attempt, timer.Elapsed.TotalMilliseconds);
                    return;
                }
                catch (Exception exception) when (exception is JudgeException or OperationCanceledException or HttpRequestException)
                {
                    var error = exception as JudgeException ?? new JudgeException(
                        exception is OperationCanceledException ? cancellationToken.IsCancellationRequested ? "cancelled" : "timeout" : "network_error",
                        !cancellationToken.IsCancellationRequested);
                    if (!error.Retryable || attempt > options.MaxRetries || cancellationToken.IsCancellationRequested)
                    {
                        verdicts[index] = Error("unjudged", error.Code, attempt, timer.Elapsed.TotalMilliseconds);
                        return;
                    }
                    var delay = error.RetryAfter ?? TimeSpan.FromMilliseconds(options.RetryDelayMilliseconds * Math.Pow(2, attempt - 1));
                    delay = TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 0, 30_000));
                    try { await Task.Delay(delay, cancellationToken); }
                    catch (OperationCanceledException)
                    {
                        verdicts[index] = Error("unjudged", "cancelled", attempt, timer.Elapsed.TotalMilliseconds);
                        return;
                    }
                }
            }
        });
        var counts = new RunCounts(items.Length, verdicts.Count(x => x.Status == "classified"),
            verdicts.Count(x => x.Status == "unjudged"), verdicts.Count(x => x.Status == "excluded"), verdicts.Count(x => x.Flagged));
        if (counts.Total != counts.Classified + counts.FailedOrUnjudged + counts.ExcludedWithReason)
            throw new InvalidOperationException("count_reconciliation_failed");
        var manifest = new RunManifest(Path.GetFileName(Path.GetFullPath(outputDirectory)), started, DateTimeOffset.UtcNow,
            rubric.Version, rubricHash, JsonFiles.Hash(JsonFiles.Serialize(items)), configHash, options,
            verdicts.Where(x => x.Prediction != null).Select(x => x.Prediction!.Model).Distinct().Order().ToArray(),
            counts, clock.Elapsed.TotalMilliseconds, source);
        var run = new ReviewRun(manifest, verdicts, Path.GetFullPath(outputDirectory));
        await RunReporter.WriteAsync(run);
        return run;
    }

    public static void ValidateOptions(RunOptions options)
    {
        if (options.Mode is not ("fake" or "live") || string.IsNullOrWhiteSpace(options.Model) ||
            options.Concurrency is < 1 or > 32 || options.MaxRetries is < 0 or > 5 || options.TimeoutSeconds is < 1 or > 600 ||
            options.MaxInputBytes is < 1 or > 1_048_576 || options.RetryDelayMilliseconds is < 0 or > 30_000 ||
            options.Policy == null || options.Policy.AlwaysReviewPaths == null ||
            !double.IsFinite(options.Policy.MinProbability) || options.Policy.MinProbability is < 0 or > 1 ||
            !double.IsFinite(options.Policy.SampleRate) || options.Policy.SampleRate is < 0 or > 1 ||
            options.Policy.AlwaysReviewPaths.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("invalid_run_configuration");
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query))
            throw new ArgumentException("endpoint_requires_https_without_credentials");
    }
}
