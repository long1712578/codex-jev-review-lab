namespace JevReview;

public sealed record ReviewItem(string Id, string Task, string Path, string Diff, string Context);
public sealed record ReviewRubric(string Version, string Instructions, Dictionary<string, string> Labels);
public sealed record JudgeResult(string Label, Dictionary<string, double> Probabilities, double Confidence,
    string Model, long? InputTokens = null, long? OutputTokens = null);
public sealed record PolicyOptions
{
    public double MinProbability { get; init; } = .8;
    public double SampleRate { get; init; } = .1;
    public string[] AlwaysReviewPaths { get; init; } = ["authorization", "authentication", "transaction"];
}
public sealed class JudgeException(string code, bool retryable = false, TimeSpan? retryAfter = null) : Exception(code)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public interface IJudgeClient
{
    Task<JudgeResult> JudgeAsync(ReviewItem item, ReviewRubric rubric, CancellationToken cancellationToken);
}

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
public sealed record FakeAnswer(string? Label = null, Dictionary<string, double>? Probabilities = null,
    double Confidence = .95, string? Error = null, int TransientFailures = 0, int DelayMilliseconds = 0);
public sealed record Evidence(string Path, int Line, string Description);
public sealed record Verification(string Id, string Status, string Summary, Evidence[] Evidence, string? FindingLabel = null);
public sealed record GoldLabel(string Id, string ExpectedLabel, bool RequiresReview, string Reason);
public sealed record EvaluationMetrics(int Total, int Classified, int Unjudged, int Excluded, int Missing,
    int TruePositive, int FalsePositive, int FalseNegative, int TrueNegative,
    double? Recall, double? Precision, double? EscalationRate, double? ErrorRate, double? Coverage,
    Dictionary<string, Dictionary<string, int>> ConfusionMatrix, string[] FalseNegativeIds,
    double? P50Milliseconds, double? P95Milliseconds, long? InputTokens, long? OutputTokens);
