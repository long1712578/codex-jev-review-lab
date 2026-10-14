using Xunit;

namespace JevReview.Tests;

public sealed class CoreTests
{
    public static readonly ReviewRubric Rubric = new("test-v1", "Review behavior", new()
    {
        ["refactor_only"] = "Structure only",
        ["behavior_change"] = "Behavior",
        ["mixed"] = "Both",
        ["insufficient_context"] = "Missing context"
    });

    public static ReviewItem Item(string id = "a", string path = "src/A.cs", string diff = "-x\n+y") =>
        new(id, "Refactor", path, diff, "Source context");

    public static JudgeResult Result(string label = "refactor_only", double probability = .95, double confidence = .99) =>
        new(label, Rubric.Labels.Keys.ToDictionary(x => x, x => x == label ? probability : (1 - probability) / 3), confidence, "fake-v1");

    [Fact]
    public void ChoiceUsesMatchingQuestionAndProbabilityNotConfidence()
    {
        var result = JevResponseParser.Parse(Response("a", "refactor_only", .55), "a", Rubric);
        Assert.Equal(.55, result.Probabilities["refactor_only"]);
        Assert.Equal("jev-1.13.0", result.Model);
        Assert.Contains("low_probability", new ReviewPolicy(new()).Reasons(Item(), result));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown_label")]
    [InlineData("bad_sum")]
    [InlineData("negative")]
    [InlineData("wrong_type")]
    [InlineData("missing_probability")]
    public void InvalidAnswersRemainUnjudged(string failure)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Response(failure == "missing" ? "other" : "a", failure == "unknown_label" ? "safe" : "refactor_only", .7))!;
        if (failure != "missing")
        {
            var answer = node["answers"]!["a"]!;
            if (failure == "bad_sum") answer["probabilities"]!["refactor_only"] = .8;
            if (failure == "negative") answer["probabilities"]!["refactor_only"] = -.7;
            if (failure == "wrong_type") answer["type"] = "score";
            if (failure == "missing_probability") answer["probabilities"]!.AsObject().Remove("mixed");
        }
        var error = Assert.Throws<JudgeException>(() => JevResponseParser.Parse(node.ToJsonString(), "a", Rubric));
        Assert.False(error.Retryable);
        Assert.Equal("invalid_response", error.Code);
    }

    [Theory]
    [InlineData("behavior_change", "behavior_change")]
    [InlineData("mixed", "mixed")]
    [InlineData("insufficient_context", "insufficient_context")]
    public void RiskyLabelsAreFlagged(string label, string reason) =>
        Assert.Contains(reason, new ReviewPolicy(new()).Reasons(Item(), Result(label)));

    [Fact]
    public void SensitivePathsAreReviewedEvenWithStrongRefactorPrediction() =>
        Assert.Contains("always_review_path", new ReviewPolicy(new()).Reasons(Item(path: "src/Authorization/Guard.cs"), Result()));

    [Fact]
    public void SamplingIsDeterministicAndCanSelectAllOrNone()
    {
        Assert.Empty(new ReviewPolicy(new() { SampleRate = 0 }).Reasons(Item(), Result()));
        Assert.Contains("quality_sample", new ReviewPolicy(new() { SampleRate = 1 }).Reasons(Item(), Result()));
        var policy = new ReviewPolicy(new() { SampleRate = .5 });
        Assert.Equal(policy.Reasons(Item("stable"), Result()), policy.Reasons(Item("stable"), Result()));
    }

    public static string Response(string id, string label, double probability) => System.Text.Json.JsonSerializer.Serialize(new
    {
        model = "jev-1.13.0",
        answers = new Dictionary<string, object>
        {
            [id] = new
            {
                type = "choice",
                choice = label,
                probabilities = Rubric.Labels.Keys.ToDictionary(x => x, x => x == "refactor_only" ? probability : Math.Round((1 - probability) / 3, 8)),
                confidence = .99
            }
        },
        usage = new { input_tokens = 50, output_tokens = 5 }
    });
}
