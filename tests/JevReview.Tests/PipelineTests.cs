using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace JevReview.Tests;

public sealed class PipelineTests
{
    [Fact]
    public async Task LiveRequestUsesChoiceCriteriaAndDoesNotSendGold()
    {
        string? payload = null;
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            payload = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, CoreTests.Response("a", "refactor_only", .7));
        }));
        var result = await new JevJudgeClient(http, "secret-value", new() { Model = "jev-latest" })
            .JudgeAsync(CoreTests.Item(), CoreTests.Rubric, default);
        using var doc = JsonDocument.Parse(payload!);
        Assert.Equal("choice", doc.RootElement.GetProperty("questions").GetProperty("a").GetProperty("type").GetString());
        Assert.Equal(4, doc.RootElement.GetProperty("questions").GetProperty("a").GetProperty("criteria").EnumerateObject().Count());
        Assert.Equal("-x\n+y", doc.RootElement.GetProperty("state").GetProperty("diff").GetString());
        Assert.DoesNotContain("expectedLabel", payload!);
        Assert.Equal("refactor_only", result.Label);
    }

    [Theory]
    [InlineData(401, false)]
    [InlineData(422, false)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(529, true)]
    public async Task RemoteErrorsAreSanitizedAndOnlyTransientErrorsRetry(int status, bool retryable)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json((HttpStatusCode)status, "secret-value raw-diff"))));
        var error = await Assert.ThrowsAsync<JudgeException>(() => new JevJudgeClient(http, "secret-value", new())
            .JudgeAsync(CoreTests.Item(), CoreTests.Rubric, default));
        Assert.Equal(retryable, error.Retryable);
        Assert.DoesNotContain("secret-value", error.ToString());
        Assert.DoesNotContain("raw-diff", error.ToString());
    }

    [Fact]
    public async Task BrokenJsonDoesNotBecomeSuccess()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{broken"))));
        var error = await Assert.ThrowsAsync<JudgeException>(() => new JevJudgeClient(http, "key", new())
            .JudgeAsync(CoreTests.Item(), CoreTests.Rubric, default));
        Assert.Equal("invalid_response", error.Code);
    }

    [Fact]
    public async Task AllItemsAreAccountedIncludingFailureAndOversize()
    {
        using var temp = new TempDirectory();
        var fake = new FakeJudgeClient(new() { ["bad"] = new(Error: "invalid_response") });
        var items = new[] { CoreTests.Item("ok"), CoreTests.Item("bad"), CoreTests.Item("large", diff: new string('x', 5000)) };
        var run = await new ReviewRunner(fake).RunAsync(items, CoreTests.Rubric, new() { MaxInputBytes = 3000 }, temp.Path, default);
        Assert.Equal(new RunCounts(3, 1, 1, 1, 3), run.Manifest.Counts);
        Assert.Equal("input_too_large", run.Verdicts.Single(x => x.Id == "large").ErrorCode);
        Assert.Single(JsonFiles.ReadLines<Verdict>(System.IO.Path.Combine(temp.Path, "errors.jsonl")), x => x.Id == "bad");
        Assert.DoesNotContain("-x", await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "report.md")));
    }

    [Fact]
    public async Task RetryIsBoundedAndCanRecover()
    {
        using var temp = new TempDirectory();
        var fake = new FakeJudgeClient(new() { ["a"] = new("refactor_only", TransientFailures: 2), ["b"] = new(Error: "http_429") });
        var run = await new ReviewRunner(fake).RunAsync([CoreTests.Item("a"), CoreTests.Item("b")], CoreTests.Rubric,
            new() { MaxRetries = 2, RetryDelayMilliseconds = 0 }, temp.Path, default);
        Assert.Equal("classified", run.Verdicts[0].Status);
        Assert.Equal(3, run.Verdicts[0].Attempts);
        Assert.Equal("unjudged", run.Verdicts[1].Status);
        Assert.Equal(3, run.Verdicts[1].Attempts);
    }

    [Fact]
    public async Task TimeoutAndCancellationRetainQueuedItems()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource(50);
        var fake = new FakeJudgeClient(new() { ["a"] = new("refactor_only", DelayMilliseconds: 5000) });
        var run = await new ReviewRunner(fake).RunAsync([CoreTests.Item("a"), CoreTests.Item("b")], CoreTests.Rubric,
            new(), temp.Path, cancellation.Token);
        Assert.Equal(2, run.Manifest.Counts.FailedOrUnjudged);
        Assert.All(run.Verdicts, v => Assert.Equal("cancelled", v.ErrorCode));
        Assert.True(File.Exists(System.IO.Path.Combine(temp.Path, "manifest.json")));
    }

    [Fact]
    public async Task PerAttemptTimeoutDoesNotHangOrLoseItem()
    {
        using var temp = new TempDirectory();
        var fake = new FakeJudgeClient(new() { ["a"] = new("refactor_only", DelayMilliseconds: 5000) });
        var run = await new ReviewRunner(fake).RunAsync([CoreTests.Item()], CoreTests.Rubric,
            new() { TimeoutSeconds = 1, MaxRetries = 0 }, temp.Path, default);
        Assert.Equal("timeout", run.Verdicts[0].ErrorCode);
        Assert.Equal(1, run.Manifest.Counts.FailedOrUnjudged);
    }

    [Fact]
    public async Task ResumeRequiresSameInputRubricAndConfiguration()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();
        var runner = new ReviewRunner(new FakeJudgeClient(new() { ["a"] = new("refactor_only") }));
        await runner.RunAsync([CoreTests.Item()], CoreTests.Rubric, new(), first.Path, default);
        var reused = await runner.RunAsync([CoreTests.Item()], CoreTests.Rubric, new(), second.Path, default, first.Path);
        Assert.True(reused.Verdicts[0].Reused);
        Assert.Equal(0, reused.Verdicts[0].Attempts);
        var changed = await runner.RunAsync([CoreTests.Item(diff: "changed")], CoreTests.Rubric, new(), second.Path, default, first.Path);
        Assert.False(changed.Verdicts[0].Reused);
        var rubric = await runner.RunAsync([CoreTests.Item()], CoreTests.Rubric with { Version = "v2" }, new(), second.Path, default, first.Path);
        Assert.False(rubric.Verdicts[0].Reused);
        var model = await runner.RunAsync([CoreTests.Item()], CoreTests.Rubric, new() { Model = "fake-v2" }, second.Path, default, first.Path);
        Assert.False(model.Verdicts[0].Reused);
    }

    [Fact]
    public async Task CollectorRejectsDuplicateIdsAndGoldFields()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(System.IO.Path.Combine(temp.Path, "items.json"), "[{\"id\":\"a\",\"expectedLabel\":\"refactor_only\"}]");
        Assert.Throws<ArgumentException>(() => DatasetCollector.Load(temp.Path));
        await JsonFiles.WriteAsync(System.IO.Path.Combine(temp.Path, "items.json"), new[] { CoreTests.Item(), CoreTests.Item() });
        Assert.Throws<ArgumentException>(() => DatasetCollector.Load(temp.Path));
    }

    internal static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task InterruptedResponseBodyRetriesAndStillAccountsForQueuedItem()
    {
        using var temp = new TempDirectory();
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(new BrokenResponseStream()) })));
        var client = new JevJudgeClient(http, "secret", new() { Model = "jev-latest" });
        var run = await new ReviewRunner(client).RunAsync([CoreTests.Item("a"), CoreTests.Item("b")], CoreTests.Rubric,
            new() { MaxRetries = 1, RetryDelayMilliseconds = 0 }, temp.Path, default);
        Assert.Equal(2, run.Manifest.Counts.FailedOrUnjudged);
        Assert.All(run.Verdicts, v => { Assert.Equal("network_error", v.ErrorCode); Assert.Equal(2, v.Attempts); });
        Assert.Equal(2, JsonFiles.ReadLines<Verdict>(System.IO.Path.Combine(temp.Path, "errors.jsonl")).Length);
    }

    private sealed class BrokenResponseStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Remote body included secret-value"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidCachedPredictionIsRejudgedWithoutLosingRun(bool nullProbabilities)
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();
        var runner = new ReviewRunner(new FakeJudgeClient(new() { ["a"] = new("refactor_only") }));
        await runner.RunAsync([CoreTests.Item()], CoreTests.Rubric, new(), first.Path, default);
        var path = System.IO.Path.Combine(first.Path, "verdicts.jsonl");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        if (nullProbabilities) node["prediction"]!["probabilities"] = null;
        else node["prediction"]!["probabilities"]!["refactor_only"] = -1;
        await File.WriteAllTextAsync(path, node.ToJsonString() + "\n");
        var run = await runner.RunAsync([CoreTests.Item()], CoreTests.Rubric, new(), second.Path, default, first.Path);
        Assert.Equal("classified", run.Verdicts[0].Status);
        Assert.False(run.Verdicts[0].Reused);
        Assert.Equal(1, run.Verdicts[0].Attempts);
    }

    [Fact]
    public async Task InvalidCredentialIsRejectedWithoutEchoingItsValue()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, CoreTests.Response("a", "refactor_only", .7)))));
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new JevJudgeClient(http, "secret-value\r\n", new())
            .JudgeAsync(CoreTests.Item(), CoreTests.Rubric, default));
        Assert.DoesNotContain("secret-value", error.ToString());
    }
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jev-review-tests-" + Guid.NewGuid().ToString("N"));
    public TempDirectory() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        var full = System.IO.Path.GetFullPath(Path);
        var tempRoot = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()));
        if (!full.StartsWith(tempRoot + System.IO.Path.DirectorySeparatorChar + "jev-review-tests-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test cleanup path escaped temporary directory");
        // Git object files carry the read-only attribute on Windows.
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(full, true);
    }
}
