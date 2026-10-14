using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace JevReview.Tests;

public sealed class ReleaseCorrectnessTests
{
    [Fact]
    public async Task GitPathsAreLiteralAndEachItemContainsOnlyItsOwnDiff()
    {
        using var temp = new TempDirectory();
        await GitAndMcpTests.Git(temp.Path, "init", "-q");
        string[] names = ["[ab].cs", "a.cs", "Đơn hàng.cs", "Order Validator.cs"];
        for (var i = 0; i < names.Length; i++)
            await File.WriteAllTextAsync(Path.Combine(temp.Path, names[i]), $"before-file-{i}\n");
        await Commit(temp.Path, "base");
        for (var i = 0; i < names.Length; i++)
            await File.WriteAllTextAsync(Path.Combine(temp.Path, names[i]), $"after-file-{i}\n");
        await Commit(temp.Path, "head");

        var collected = await GitCollector.CollectAsync(temp.Path, "HEAD~1", "HEAD", "Refactor", false, default);
        Assert.Equal(names.Length, collected.Items.Length);
        for (var i = 0; i < names.Length; i++)
        {
            Assert.Contains(names[i], collected.Items.Select(x => x.Path));
            var item = collected.Items.Single(x => x.Path == names[i]);
            Assert.Single(item.Diff.Split('\n'), x => x.StartsWith("diff --git ", StringComparison.Ordinal));
            Assert.Contains($"+after-file-{i}", item.Diff);
            for (var other = 0; other < names.Length; other++)
                if (other != i) Assert.DoesNotContain($"+after-file-{other}", item.Diff);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McpRejectsLinkedWorkspaceRootAndAncestor(bool useChild)
    {
        using var temp = new TempDirectory();
        var outside = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "child"));
        var link = Path.Combine(temp.Path, "link");
        await CreateDirectoryLink(link, outside);
        try
        {
            Assert.Throws<ArgumentException>(() => new McpServer(useChild ? Path.Combine(link, "child") : link));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task McpRechecksRootWhenItIsReplacedByALink()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "root");
        var outside = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var server = new McpServer(root);
        await Initialize(server);
        Directory.Move(root, Path.Combine(temp.Path, "old-root"));
        await CreateDirectoryLink(root, outside);
        try
        {
            var response = await server.HandleAsync(JsonFiles.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new { name = "start_review", arguments = new { dataset = "items.json" } }
            }), default);
            using var document = JsonDocument.Parse(response!);
            Assert.True(document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.False(Directory.Exists(Path.Combine(outside, "runs")));
        }
        finally { Directory.Delete(root); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"protocolVersion\":\"2025-06-18\"}")]
    [InlineData("{\"protocolVersion\":\"2025-06-18\",\"capabilities\":3,\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}")]
    [InlineData("{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"\",\"version\":\"1\"}}")]
    public async Task McpRejectsInvalidInitializeParametersAndCanRecover(string parameters)
    {
        using var temp = new TempDirectory();
        var server = new McpServer(temp.Path);
        using var response = JsonDocument.Parse((await server.HandleAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" + parameters + "}", default))!);
        Assert.Equal(-32602, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        await Initialize(server);
        using var ready = JsonDocument.Parse((await server.HandleAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}", default))!);
        Assert.Equal(3, ready.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength());
    }

    [Fact]
    public async Task McpToolsWaitForInitializedNotification()
    {
        using var temp = new TempDirectory();
        var server = new McpServer(temp.Path);
        await server.HandleAsync(InitializeRequest, default);
        using var before = JsonDocument.Parse((await server.HandleAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}", default))!);
        Assert.Equal("not_initialized", before.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Null(await server.HandleAsync(InitializedNotification, default));
        using var after = JsonDocument.Parse((await server.HandleAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}", default))!);
        Assert.Equal(3, after.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength());
    }

    internal const string InitializeRequest = "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}";
    internal const string InitializedNotification = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";

    internal static async Task Initialize(McpServer server)
    {
        await server.HandleAsync(InitializeRequest, default);
        await server.HandleAsync(InitializedNotification, default);
    }

    [Theory]
    [InlineData("{\"dataset\":\"items.json\",\"head\":7}")]
    [InlineData("{\"dataset\":\"items.json\",\"mergeBase\":\"yes\"}")]
    public async Task McpRejectsMalformedArgumentsEvenForTheOtherSourceKind(string arguments)
    {
        using var temp = new TempDirectory();
        var server = new McpServer(temp.Path);
        await Initialize(server);
        using var parsed = JsonDocument.Parse(arguments);
        using var response = JsonDocument.Parse((await server.HandleAsync(JsonFiles.Serialize(new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "tools/call",
            @params = new { name = "start_review", arguments = parsed.RootElement }
        }), default))!);
        Assert.True(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    private static async Task Commit(string root, string message)
    {
        await GitAndMcpTests.Git(root, "add", ".");
        await GitAndMcpTests.Git(root, "-c", "user.name=Test", "-c", "user.email=test@example.test", "commit", "-qm", message);
    }

    private static async Task CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"New-Item -ItemType Junction -Path '{link.Replace("'", "''")}' -Target '{target.Replace("'", "''")}' | Out-Null");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await stdout;
        Assert.Equal(0, process.ExitCode);
        await stderr;
    }
}
