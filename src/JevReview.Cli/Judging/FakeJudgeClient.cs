using System.Collections.Concurrent;

namespace JevReview;
public sealed class FakeJudgeClient(Dictionary<string, FakeAnswer> answers) : IJudgeClient
{
    private readonly ConcurrentDictionary<string, int> calls = new();

    public async Task<JudgeResult> JudgeAsync(ReviewItem item, ReviewRubric rubric, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fixture = answers.GetValueOrDefault(JsonFiles.Hash(JsonFiles.Serialize(item)))
            ?? answers.GetValueOrDefault(item.Id) ?? new FakeAnswer("insufficient_context");
        var attempt = calls.AddOrUpdate(item.Id, 1, (_, count) => count + 1);
        if (fixture.DelayMilliseconds > 0) await Task.Delay(fixture.DelayMilliseconds, cancellationToken);
        if (attempt <= fixture.TransientFailures) throw new JudgeException("http_429", true);
        if (fixture.Error is { } error)
            throw new JudgeException(error, error is "http_429" or "http_500" or "http_529" or "timeout" or "network_error");
        var label = fixture.Label ?? "insufficient_context";
        var probabilities = fixture.Probabilities ?? rubric.Labels.Keys.ToDictionary(x => x, x => x == label ? .97 : .01);
        var result = new JudgeResult(label, probabilities, fixture.Confidence, "fake-v1");
        JevResponseParser.Validate(result, rubric);
        return result;
    }
}
