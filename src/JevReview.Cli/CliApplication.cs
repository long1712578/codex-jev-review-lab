using System.Text.Json;

namespace JevReview;

public static class CliApplication
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                await output.WriteLineAsync("jev-review doctor | classify | inspect | verify | evaluate | mcp\nSee README.md for options and examples.");
                return 0;
            }
            var options = CommandOptions.Parse(args[1..]);
            switch (args[0])
            {
                case "mcp":
                    options.Allow("workspace", "allow-live");
                    await new McpServer(options.Get("workspace", Environment.CurrentDirectory), options.Has("allow-live"))
                        .RunAsync(Console.In, output, cancellationToken);
                    return 0;
                case "doctor":
                    options.Allow("mode", "rubric", "check-live", "model");
                    if (options.Get("mode", "fake") is not ("fake" or "live")) throw new ArgumentException("invalid_mode");
                    var checkedRubricPath = options.Get("rubric", "rubrics/refactor-review.v1.json");
                    var checkedRubric = ReviewService.LoadRubric(checkedRubricPath);
                    await output.WriteLineAsync($".NET runtime: {Environment.Version}. Workspace: {Environment.CurrentDirectory}");
                    await output.WriteLineAsync($"rubric: valid ({checkedRubric.Version}). Path: {Path.GetFullPath(checkedRubricPath)}");
                    var hasKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));
                    await output.WriteLineAsync($"credential TYPESAFE_API_KEY: {(hasKey ? "present" : "absent")}. No key values are printed.");
                    if (options.Has("check-live"))
                    {
                        var rubric = checkedRubric;
                        using var http = ReviewService.CreateHttpClient();
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(30));
                        var client = new JevJudgeClient(http, CliEnvironment.RequireApiKey(), new() { Mode = "live", Model = options.Get("model", "jev-latest") });
                        var result = await client.JudgeAsync(new("smoke", "Check a local variable rename", "Smoke.cs", "-var x = 1;\n+var y = 1;", "Only the local identifier is renamed; no subsequent usages."), rubric, timeout.Token);
                        await output.WriteLineAsync($"live smoke: valid response, actual model {result.Model}, label {result.Label}.");
                    }
                    else await output.WriteLineAsync("Offline check only. --check-live sends one billed synthetic request.");
                    return options.Get("mode", "fake") == "live" && !hasKey ? 2 : 0;
                case "classify":
                    options.Allow("dataset", "rubric", "mode", "model", "output", "fake-responses", "policy", "resume", "concurrency", "timeout-seconds", "max-retries", "max-input-bytes", "git", "base", "head", "merge-base", "task");
                    var request = CliReviewRequestParser.Parse(options);
                    var run = await ReviewService.StartAsync(request, cancellationToken,
                        request.Options.Mode == "live" ? CliEnvironment.RequireApiKey() : null);
                    await output.WriteLineAsync(JsonFiles.Serialize(new { runId = run.Manifest.RunId, output = run.OutputDirectory, counts = run.Manifest.Counts, models = run.Manifest.ActualModels }));
                    return cancellationToken.IsCancellationRequested ? 130 : run.Manifest.Counts.FailedOrUnjudged + run.Manifest.Counts.ExcludedWithReason > 0 ? 1 : 0;
                case "inspect":
                    options.Allow("run", "id");
                    var id = options.Require("id");
                    var verdict = JsonFiles.ReadLines<Verdict>(Path.Combine(options.Require("run"), "verdicts.jsonl")).SingleOrDefault(x => x.Id == id)
                        ?? throw new ArgumentException("unknown_item_id");
                    await output.WriteLineAsync(JsonFiles.Serialize(verdict));
                    return 0;
                case "verify":
                    options.Allow("run", "file");
                    await VerificationReporter.WriteAsync(options.Require("run"), JsonFiles.Read<Verification[]>(options.Require("file")));
                    await output.WriteLineAsync("Verification records saved; report.md updated.");
                    return 0;
                case "evaluate":
                    options.Allow("run", "gold", "baseline", "output", "inference-root");
                    await EvaluationReporter.WriteAsync(options.Require("run"), options.Require("gold"), options.Optional("baseline"),
                        options.Require("output"), options.Get("inference-root", Environment.CurrentDirectory));
                    await output.WriteLineAsync("Evaluation saved: metrics.json and evaluation.md. Missing denominators are N/A.");
                    return 0;
                default: throw new ArgumentException("unknown_command");
            }
        }
        catch (OperationCanceledException) { await output.WriteLineAsync("error: cancelled"); return 130; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or JsonException or JudgeException or InvalidOperationException)
        {
            // Do not forward exception messages: filesystem paths, JSON snippets and HTTP bodies may contain secrets.
            var code = e is JudgeException judge ? judge.Code : e is ArgumentException argument && argument.Message.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
                ? argument.Message : "invalid_configuration_or_input";
            await output.WriteLineAsync($"error: {code}");
            return 2;
        }
    }
}
