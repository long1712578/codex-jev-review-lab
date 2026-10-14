namespace JevReview;

internal static class CliReviewRequestParser
{
    public static ReviewRequest Parse(CommandOptions command)
    {
        if (command.Has("dataset") == command.Has("git")) throw new ArgumentException("choose_dataset_or_git");
        ReviewSource source = command.Has("git")
            ? new GitReviewSource(command.Require("git"), command.Require("base"), command.Get("head", "HEAD"),
                command.Require("task"), command.Has("merge-base"))
            : new DatasetReviewSource(command.Require("dataset"));
        var mode = command.Get("mode", "fake");
        var options = new RunOptions
        {
            Mode = mode,
            Model = command.Get("model", mode == "live" ? "jev-latest" : "fake-v1"),
            Concurrency = command.Integer("concurrency", 1),
            TimeoutSeconds = command.Integer("timeout-seconds", 30),
            MaxRetries = command.Integer("max-retries", 2),
            MaxInputBytes = command.Integer("max-input-bytes", 24000)
        };
        ReviewRunner.ValidateOptions(options);
        var output = command.Get("output", Path.Combine("runs",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]));
        return new(source, command.Get("rubric", "rubrics/refactor-review.v1.json"), options, output,
            command.Optional("policy"), command.Get("fake-responses", "fixtures/fake/dev-responses.json"), command.Optional("resume"));
    }
}
