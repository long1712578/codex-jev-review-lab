using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace JevReview.Tests;

public sealed class GitAndMcpTests
{
    [Fact]
    public async Task GitRangeResolvesShasAndPreservesSpacedFilenames()
    {
        using var temp = new TempDirectory();
        await Git(temp.Path, "init", "-q");
        await File.WriteAllTextAsync(System.IO.Path.Combine(temp.Path, "Order Validator.cs"), "var quantity = 1;\n");
        await Git(temp.Path, "add", ".");
        await Git(temp.Path, "-c", "user.name=Test", "-c", "user.email=test@example.test", "commit", "-qm", "base");
        await File.WriteAllTextAsync(System.IO.Path.Combine(temp.Path, "Order Validator.cs"), "var quantity = 2;\n");
        await File.WriteAllBytesAsync(System.IO.Path.Combine(temp.Path, "image.bin"), [0, 1, 0, 2]);
        await Git(temp.Path, "add", ".");
        await Git(temp.Path, "-c", "user.name=Test", "-c", "user.email=test@example.test", "commit", "-qm", "head");
        var result = await GitCollector.CollectAsync(temp.Path, "HEAD~1", "HEAD", "Refactor", false, default);
        Assert.Equal(2, result.Items.Length);
        Assert.Equal(40, result.Source["baseSha"].Length);
        Assert.Equal(40, result.Source["headSha"].Length);
        Assert.Equal("endpoints", result.Source["semantics"]);
        var source = result.Items.Single(x => x.Path == "Order Validator.cs");
        Assert.Contains("+var quantity = 2;", source.Diff);
        Assert.Contains("Binary files", result.Items.Single(x => x.Path == "image.bin").Diff);
        var merge = await GitCollector.CollectAsync(temp.Path, "HEAD~1", "HEAD", "Refactor", true, default);
        Assert.Equal("merge-base", merge.Source["semantics"]);
        Assert.Equal(result.Source["baseSha"], merge.Source["diffBaseSha"]);
        await Assert.ThrowsAsync<ArgumentException>(() => GitCollector.CollectAsync(temp.Path, "--output=oops", "HEAD", "Refactor", false, default));
    }

    [Fact]
    public async Task GitSubfolderCannotImplicitlyCollectAncestorRepository()
    {
        using var temp = new TempDirectory();
        await Git(temp.Path, "init", "-q");
        await File.WriteAllTextAsync(System.IO.Path.Combine(temp.Path, "parent.cs"), "before\n");
        await Git(temp.Path, "add", ".");
        await Git(temp.Path, "-c", "user.name=Test", "-c", "user.email=test@example.test", "commit", "-qm", "base");
        await File.WriteAllTextAsync(System.IO.Path.Combine(temp.Path, "parent.cs"), "after\n");
        await Git(temp.Path, "add", ".");
        await Git(temp.Path, "-c", "user.name=Test", "-c", "user.email=test@example.test", "commit", "-qm", "head");
        var folder = System.IO.Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(folder);
        await Assert.ThrowsAsync<ArgumentException>(() => GitCollector.CollectAsync(folder, "HEAD~1", "HEAD", "refactor", false, default));
    }

    [Fact]
    public async Task McpInitializationAndUnknownMethodsHaveProtocolResponses()
    {
        using var temp = new TempDirectory();
        var server = new McpServer(temp.Path);
        using var initialization = JsonDocument.Parse((await server.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}", default))!);
        Assert.Equal("2025-06-18", initialization.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Null(await server.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", default));
        using var tools = JsonDocument.Parse((await server.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}", default))!);
        Assert.Equal(3, tools.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength());
        using var unknown = JsonDocument.Parse((await server.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"unknown\"}", default))!);
        Assert.Equal(-32601, unknown.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task McpRunsBackgroundReviewWithoutEchoingDiffAndRejectsTraversal()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(System.IO.Path.Combine(temp.Path, "rubrics"));
        await JsonFiles.WriteAsync(System.IO.Path.Combine(temp.Path, "rubrics/refactor-review.v1.json"), CoreTests.Rubric);
        await JsonFiles.WriteAsync(System.IO.Path.Combine(temp.Path, "items.json"), new[] { CoreTests.Item(diff: "private-diff") });
        var server = new McpServer(temp.Path);
        await ReleaseCorrectnessTests.Initialize(server);
        var start = await Call(server, "start_review", new { dataset = "items.json", mode = "fake" });
        var runId = start.GetProperty("runId").GetString()!;
        JsonElement status = default;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            status = await Call(server, "get_review_status", new { runId });
            if (status.GetProperty("status").GetString() != "running") break;
            await Task.Delay(10);
        }
        Assert.Equal("completed", status.GetProperty("status").GetString());
        var result = await Call(server, "get_review_result", new { runId });
        Assert.DoesNotContain("private-diff", result.GetRawText());
        Assert.Equal(1, result.GetProperty("manifest").GetProperty("counts").GetProperty("total").GetInt32());
        var outside = await RawCall(server, "start_review", new { dataset = "../outside.json" });
        Assert.True(outside.GetProperty("result").GetProperty("isError").GetBoolean());
        var shell = await RawCall(server, "start_review", new { dataset = "items.json", shell = "echo secret" });
        Assert.True(shell.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    private static async Task<JsonElement> Call(McpServer server, string name, object arguments)
    {
        var raw = await RawCall(server, name, arguments);
        Assert.False(raw.GetProperty("result").GetProperty("isError").GetBoolean());
        return JsonDocument.Parse(raw.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    [Fact]
    public async Task KnownFailedJobReturnsStateEvenIfPartialManifestExists()
    {
        using var temp = new TempDirectory();
        var server = new McpServer(temp.Path);
        await ReleaseCorrectnessTests.Initialize(server);
        var started = await Call(server, "start_review", new { dataset = "missing.json" });
        var runId = started.GetProperty("runId").GetString()!;
        for (var i = 0; i < 100; i++)
        {
            var status = await Call(server, "get_review_status", new { runId });
            if (status.GetProperty("status").GetString() == "failed") break;
            await Task.Delay(10);
        }
        var runFolder = System.IO.Path.Combine(temp.Path, "runs", runId);
        Directory.CreateDirectory(runFolder);
        await File.WriteAllTextAsync(System.IO.Path.Combine(runFolder, "manifest.json"), "{");
        var final = await Call(server, "get_review_status", new { runId });
        Assert.Equal("failed", final.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, final.GetProperty("counts").ValueKind);
    }
    private static async Task<JsonElement> RawCall(McpServer server, string name, object arguments) =>
        JsonDocument.Parse((await server.HandleAsync(JsonFiles.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments } }), default))!).RootElement.Clone();

    [Theory]
    [InlineData(2147483648d)]
    [InlineData(1.5)]
    [InlineData(0d)]
    public async Task InvalidMcpNumbersReturnToolErrorAndKeepServerAlive(double concurrency)
    {
        using var temp = new TempDirectory();
        var server = new McpServer(temp.Path);
        await ReleaseCorrectnessTests.Initialize(server);
        var result = await RawCall(server, "start_review", new { dataset = "items.json", concurrency });
        Assert.True(result.GetProperty("result").GetProperty("isError").GetBoolean());
        var ping = await server.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}", default);
        Assert.True(JsonDocument.Parse(ping!).RootElement.TryGetProperty("result", out _));
    }

    [UnixFact]
    public async Task DatasetDirectoryCannotHideLinkedItemsFile()
    {
        using var temp = new TempDirectory();
        using var outside = new TempDirectory();
        var folder = System.IO.Path.Combine(temp.Path, "data");
        Directory.CreateDirectory(folder);
        var outsideFile = System.IO.Path.Combine(outside.Path, "items.json");
        await JsonFiles.WriteAsync(outsideFile, new[] { CoreTests.Item() });
        var link = System.IO.Path.Combine(folder, "items.json");
        File.CreateSymbolicLink(link, outsideFile);
        try
        {
            var server = new McpServer(temp.Path);
            await ReleaseCorrectnessTests.Initialize(server);
            var result = await RawCall(server, "start_review", new { dataset = "data" });
            Assert.True(result.GetProperty("result").GetProperty("isError").GetBoolean());
        }
        finally { File.Delete(link); }
    }

    [UnixFact]
    public async Task CaseDifferentSiblingIsOutsideLinuxWorkspace()
    {
        using var temp = new TempDirectory();
        var root = System.IO.Path.Combine(temp.Path, "Repo");
        var sibling = System.IO.Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        await JsonFiles.WriteAsync(System.IO.Path.Combine(sibling, "items.json"), new[] { CoreTests.Item() });
        var server = new McpServer(root);
        await ReleaseCorrectnessTests.Initialize(server);
        var result = await RawCall(server, "start_review", new { dataset = "../repo/items.json" });
        Assert.True(result.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    internal static async Task<string> Git(string cwd, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stderr);
        return (await stdout).Trim();
    }
}

internal sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Tests Unix case-sensitive paths and file symlinks; executed by Linux CI.";
    }
}
