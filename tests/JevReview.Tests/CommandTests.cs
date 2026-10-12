using JevReview;
using Xunit;

namespace JevReview.Tests;

public sealed class CommandTests
{
    [Fact]
    public async Task ClassifyProducesFiveArtifactsAndOnlySummaryOnStdout()
    {
        using var temp = new TempDirectory();
        var data = System.IO.Path.Combine(temp.Path, "items.json");
        var rubric = System.IO.Path.Combine(temp.Path, "rubric.json");
        var output = System.IO.Path.Combine(temp.Path, "run");
        await JsonFiles.WriteAsync(data, new[] { CoreTests.Item(diff: "secret-diff") });
        await JsonFiles.WriteAsync(rubric, CoreTests.Rubric);
        using var writer = new StringWriter();
        var exit = await CliApplication.RunAsync(["classify", "--dataset", data, "--rubric", rubric, "--output", output], writer, default);
        Assert.Equal(0, exit);
        Assert.DoesNotContain("secret-diff", writer.ToString());
        Assert.Equal("classified", JsonFiles.ReadLines<Verdict>(System.IO.Path.Combine(output, "verdicts.jsonl"))[0].Status);
        foreach (var file in new[] { "manifest.json", "verdicts.jsonl", "flagged.json", "errors.jsonl", "report.md" })
            Assert.True(File.Exists(System.IO.Path.Combine(output, file)));
    }

    [Theory]
    [InlineData("--unknown", "x")]
    [InlineData("--concurrency", "0")]
    [InlineData("--mode", "invalid")]
    public async Task InvalidArgumentsFailBeforeInference(string option, string value)
    {
        using var writer = new StringWriter();
        var exit = await CliApplication.RunAsync(["classify", option, value], writer, default);
        Assert.Equal(2, exit);
        Assert.Contains("error", writer.ToString());
    }

    [Fact]
    public async Task DoctorDoesNotEchoCredentialValue()
    {
        using var temp = new TempDirectory();
        var rubric = System.IO.Path.Combine(temp.Path, "rubric.json");
        await JsonFiles.WriteAsync(rubric, CoreTests.Rubric);
        using var writer = new StringWriter();
        var exit = await CliApplication.RunAsync(["doctor", "--rubric", rubric], writer, default);
        Assert.Equal(0, exit);
        Assert.Contains("credential", writer.ToString());
        var key = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        if (!string.IsNullOrEmpty(key)) Assert.DoesNotContain(key, writer.ToString());
    }

    [Fact]
    public void MissingPredictionsStayInMetricsDenominator()
    {
        var gold = new[] { new GoldLabel("a", "behavior_change", true, "boundary"), new GoldLabel("b", "refactor_only", false, "rename") };
        var metrics = Evaluator.Compute(gold, []);
        Assert.Equal(2, metrics.Missing);
        Assert.Equal(0, metrics.Coverage);
        Assert.Equal(1, metrics.FalseNegative);
        Assert.Equal(new[] { "a" }, metrics.FalseNegativeIds);
        Assert.Equal(0, metrics.Recall);
        Assert.Null(metrics.Precision);
        Assert.Null(Evaluator.Compute([], []).Recall);
    }

    [Fact]
    public void FailedFlaggedPredictionCountsAsEscalationButHasNoClassLabel()
    {
        var v = new Verdict("a", "A.cs", "i", "r", "c", "unjudged", null, true, ["timeout"], "timeout", 3, 100);
        var metrics = Evaluator.Compute([new("a", "behavior_change", true, "boundary")], [v]);
        Assert.Equal(1, metrics.TruePositive);
        Assert.Equal(1, metrics.ErrorRate);
        Assert.Equal(0, metrics.Coverage);
        Assert.Equal(1, metrics.ConfusionMatrix["behavior_change"]["unjudged"]);
    }

    [Fact]
    public void VerificationRequiresEvidenceAndKnownIds()
    {
        var verdict = new Verdict("a", "A.cs", "i", "r", "c", "classified", CoreTests.Result("mixed"), true, ["mixed"], null, 1, 10);
        Assert.Throws<ArgumentException>(() => VerificationReporter.Validate([new("a", "confirmed", "Changed", [], "mixed")], [verdict]));
        Assert.Throws<ArgumentException>(() => VerificationReporter.Validate([new("wrong", "unresolved", "Missing caller", [])], [verdict]));
        VerificationReporter.Validate([new("a", "confirmed", "Removed guard", [new("A.cs", 10, "Guard removed")], "mixed")], [verdict]);
    }

    [Fact]
    public void NullRecordsAreRejectedWithInputError()
    {
        var nullGold = System.Text.Json.JsonSerializer.Deserialize<GoldLabel[]>("[null]", JsonFiles.Options)!;
        var nullVerification = System.Text.Json.JsonSerializer.Deserialize<Verification[]>("[null]", JsonFiles.Options)!;
        Assert.Throws<ArgumentException>(() => Evaluator.Compute(nullGold, []));
        Assert.Throws<ArgumentException>(() => VerificationReporter.Validate(nullVerification, []));
    }

    [Fact]
    public async Task DoctorValidatesRubricAndModeWithoutLiveCall()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "invalid-rubric.json");
        await File.WriteAllTextAsync(path, "{}");
        using var output = new StringWriter();
        Assert.Equal(2, await CliApplication.RunAsync(["doctor", "--rubric", path], output, default));
        Assert.Equal(2, await CliApplication.RunAsync(["doctor", "--mode", "invalid"], output, default));
    }

    [Fact]
    public async Task EvaluateCommandReadsExternalGoldAndWritesHonestMissingBaselineReport()
    {
        using var inference = new TempDirectory();
        using var evaluator = new TempDirectory();
        var runFolder = System.IO.Path.Combine(inference.Path, "run");
        await new ReviewRunner(new FakeJudgeClient(new())).RunAsync([CoreTests.Item("toy")], CoreTests.Rubric, new(), runFolder, default);
        var goldPath = System.IO.Path.Combine(evaluator.Path, "toy-gold.json");
        await JsonFiles.WriteAsync(goldPath, new[] { new GoldLabel("toy", "behavior_change", true, "Synthetic evaluator contract test, not model-quality measurement") });
        using var output = new StringWriter();
        var evalPath = System.IO.Path.Combine(evaluator.Path, "metrics");
        var exit = await CliApplication.RunAsync(["evaluate", "--run", runFolder, "--gold", goldPath, "--output", evalPath, "--inference-root", inference.Path], output, default);
        Assert.Equal(0, exit);
        using var metrics = System.Text.Json.JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(evalPath, "metrics.json")));
        Assert.Equal(1, metrics.RootElement.GetProperty("pipeline").GetProperty("total").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, metrics.RootElement.GetProperty("baseline").ValueKind);
        Assert.Contains("N/A", File.ReadAllText(System.IO.Path.Combine(evalPath, "evaluation.md")));
        var insideGold = System.IO.Path.Combine(inference.Path, "gold.json");
        await JsonFiles.WriteAsync(insideGold, new[] { new GoldLabel("toy", "behavior_change", true, "test") });
        Assert.Equal(2, await CliApplication.RunAsync(["evaluate", "--run", runFolder, "--gold", insideGold, "--output", evalPath, "--inference-root", inference.Path], output, default));
    }
}
