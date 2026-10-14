namespace JevReview;

public sealed record JudgeResult(string Label, Dictionary<string, double> Probabilities, double Confidence,
    string Model, long? InputTokens = null, long? OutputTokens = null);
public sealed record FakeAnswer(string? Label = null, Dictionary<string, double>? Probabilities = null,
    double Confidence = .95, string? Error = null, int TransientFailures = 0, int DelayMilliseconds = 0);
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
