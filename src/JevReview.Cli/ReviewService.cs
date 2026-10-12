namespace JevReview;

public static class ReviewService
{
    public static ReviewRubric LoadRubric(string path)
    {
        var rubric = JsonFiles.Read<ReviewRubric>(path);
        if (string.IsNullOrWhiteSpace(rubric.Version) || string.IsNullOrWhiteSpace(rubric.Instructions) || rubric.Labels == null ||
            !rubric.Labels.Keys.Order().SequenceEqual(Evaluator.Labels.Order()) || rubric.Labels.Values.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("invalid_rubric");
        return rubric;
    }

    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public static string RequireApiKey() => Environment.GetEnvironmentVariable("TYPESAFE_API_KEY") is { Length: > 0 } key
        && !string.IsNullOrWhiteSpace(key) ? key : throw new ArgumentException("missing_TYPESAFE_API_KEY");

    public static async Task<ReviewRun> StartAsync(CommandOptions command, CancellationToken cancellationToken)
    {
        // Freeze and validate rubric/configuration before collecting bulk input.
        var rubric = LoadRubric(command.Get("rubric", "rubrics/refactor-review.v1.json"));
        var mode = command.Get("mode", "fake");
        var fakePath = command.Get("fake-responses", "fixtures/fake/dev-responses.json");
        var options = new RunOptions
        {
            Mode = mode, Model = command.Get("model", mode == "live" ? "jev-latest" : "fake-v1"),
            Concurrency = command.Integer("concurrency", 1), TimeoutSeconds = command.Integer("timeout-seconds", 30),
            MaxRetries = command.Integer("max-retries", 2), MaxInputBytes = command.Integer("max-input-bytes", 24000),
            Policy = command.Has("policy") ? JsonFiles.Read<PolicyOptions>(command.Require("policy")) : new(),
            FakeFixtureHash = mode == "fake" && File.Exists(fakePath) ? JsonFiles.Hash(File.ReadAllText(fakePath)) : null
        };
        ReviewRunner.ValidateOptions(options);
        if (command.Has("dataset") == command.Has("git")) throw new ArgumentException("choose_dataset_or_git");
        ReviewItem[] items;
        Dictionary<string, string> source;
        if (command.Has("git"))
        {
            var collected = await GitCollector.CollectAsync(command.Require("git"), command.Require("base"), command.Get("head", "HEAD"),
                command.Require("task"), command.Has("merge-base"), cancellationToken);
            items = collected.Items;
            source = collected.Source;
        }
        else
        {
            var path = Path.GetFullPath(command.Require("dataset"));
            items = DatasetCollector.Load(path);
            source = new() { ["kind"] = "dataset", ["path"] = path };
        }
        var output = command.Get("output", Path.Combine("runs", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]));
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new ArgumentException("output_directory_not_empty");
        using var http = CreateHttpClient();
        IJudgeClient client = mode == "live" ? new JevJudgeClient(http, RequireApiKey(), options) :
            new FakeJudgeClient(File.Exists(fakePath) ? JsonFiles.Read<Dictionary<string, FakeAnswer>>(fakePath) : new());
        return await new ReviewRunner(client).RunAsync(items, rubric, options, output, cancellationToken, command.Optional("resume"), source);
    }
}
