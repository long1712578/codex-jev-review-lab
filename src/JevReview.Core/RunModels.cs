namespace JevReview;

public sealed record RunOptions
{
    public string Mode { get; init; } = "fake";
    public string Model { get; init; } = "fake-v1";
    public string Endpoint { get; init; } = "https://api.typesafe.ai/v1/systemone";
    public int Concurrency { get; init; } = 1;
    public int MaxRetries { get; init; } = 2;
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxInputBytes { get; init; } = 24000;
    public int RetryDelayMilliseconds { get; init; } = 250;
    public string? FakeFixtureHash { get; init; }
    public PolicyOptions Policy { get; init; } = new();
}
public sealed record Verdict(string Id, string Path, string InputHash, string RubricHash, string ConfigHash,
    string Status, JudgeResult? Prediction, bool Flagged, string[] Reasons, string? ErrorCode,
    int Attempts, double LatencyMilliseconds, bool Reused = false);
public sealed record RunCounts(int Total, int Classified, int FailedOrUnjudged, int ExcludedWithReason, int Flagged);
public sealed record RunManifest(string RunId, DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
    string RubricVersion, string RubricHash, string InputHash, string ConfigHash, RunOptions Configuration,
    string[] ActualModels, RunCounts Counts, double WallClockMilliseconds, Dictionary<string, string>? Source = null);
public sealed record ReviewRun(RunManifest Manifest, Verdict[] Verdicts, string OutputDirectory);
