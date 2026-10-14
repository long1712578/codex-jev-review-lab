using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace JevReview.Tests;

public sealed class DistributionTests
{
    [Fact]
    public async Task ReleaseExcludesUnrelatedSkillLocalFilesAndRemainsInstallable()
    {
        using var temp = new TempDirectory();
        var sourceRoot = FindSourceRoot();
        var fixture = Path.Combine(temp.Path, "publisher");
        Directory.CreateDirectory(fixture);
        foreach (var directory in new[] { "src", ".agents/skills/ultrathink", ".agents/skills/orchestrate", ".agents/skills/workflowz", ".agents/skills/jevify" })
            CopyDirectory(Path.Combine(sourceRoot, directory), Path.Combine(fixture, directory));
        foreach (var file in new[]
        {
            "global.json", "Directory.Build.props", "JevReview.slnx", "rubrics/refactor-review.v1.json", "configs/policy.json",
            "fixtures/fake/dev-responses.json", "datasets/smoke/items.json", "scripts/New-CodexRelease.ps1",
            "scripts/Install-CodexJev.ps1", "scripts/CodexJev.Common.ps1", "docs/codex-v1-quickstart.md", "docs/codex-v1-release-notes.md"
        })
        {
            var destination = Path.Combine(fixture, file);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(sourceRoot, file), destination);
        }
        await GitAndMcpTests.Git(fixture, "init", "-q");
        await File.WriteAllTextAsync(Path.Combine(fixture, "seed.txt"), "synthetic publisher fixture");
        await GitAndMcpTests.Git(fixture, "add", "seed.txt");
        await GitAndMcpTests.Git(fixture, "-c", "user.name=Test", "-c", "user.email=test@example.test", "commit", "-qm", "seed");
        await File.WriteAllTextAsync(Path.Combine(fixture, ".agents/skills/jevify/local-notes.txt"), "synthetic note that must stay local");
        var release = Path.Combine(temp.Path, "sentinel-release");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var published = await PowerShell(fixture, Path.Combine(fixture, "scripts/New-CodexRelease.ps1"),
            "-OutputDirectory", release, "-Configuration", configuration, "-SkipChecks");
        Assert.True(published.ExitCode == 0, published.Output);
        using (var archive = ZipFile.OpenRead(Path.Combine(release, "codex-jev-1.0.0.zip")))
            Assert.DoesNotContain(archive.Entries, x => x.FullName.EndsWith("local-notes.txt", StringComparison.Ordinal));
        var project = Path.Combine(temp.Path, "sentinel-consumer");
        Directory.CreateDirectory(project);
        await GitAndMcpTests.Git(project, "init", "-q");
        var bundle = Path.Combine(release, "codex-jev-1.0.0");
        var installation = await PowerShell(project, Path.Combine(bundle, "scripts/Install-CodexJev.ps1"), "-ProjectPath", project);
        Assert.True(installation.ExitCode == 0, installation.Output);
        Assert.False(File.Exists(Path.Combine(project, ".agents/skills/jevify/local-notes.txt")));
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    [Fact]
    public async Task ReleaseInstallsOutsideCheckoutAndPreservesExistingFiles()
    {
        using var temp = new TempDirectory();
        var sourceRoot = FindSourceRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var release = Path.Combine(temp.Path, "release files Đơn hàng");
        var packageResult = await PowerShell(sourceRoot, Path.Combine(sourceRoot, "scripts/New-CodexRelease.ps1"),
            "-OutputDirectory", release, "-Configuration", configuration, "-SkipChecks");
        Assert.True(packageResult.ExitCode == 0, packageResult.Output);

        var bundle = Path.Combine(release, "codex-jev-1.0.0");
        var zipPath = Path.Combine(release, "codex-jev-1.0.0.zip");
        Assert.True(File.Exists(zipPath));
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(zipPath)));
        Assert.StartsWith(expectedHash, await File.ReadAllTextAsync(zipPath + ".sha256"));
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            Assert.Contains(zip.Entries, x => x.FullName == ".agents/skills/jevify/SKILL.md");
            Assert.DoesNotContain(zip.Entries, x => x.FullName.StartsWith("evaluation/", StringComparison.Ordinal)
                || x.FullName.Contains("holdout", StringComparison.Ordinal) || x.FullName.StartsWith(".git/", StringComparison.Ordinal));
        }

        var extracted = Path.Combine(temp.Path, "extracted bundle");
        ZipFile.ExtractToDirectory(zipPath, extracted);
        var project = Path.Combine(temp.Path, "Company project Đơn hàng");
        Directory.CreateDirectory(project);
        await GitAndMcpTests.Git(project, "init", "-q");
        var agents = Path.Combine(project, "AGENTS.md");
        await File.WriteAllTextAsync(agents, "existing team instructions\n");
        var configDirectory = Path.Combine(project, ".codex");
        Directory.CreateDirectory(configDirectory);
        var config = Path.Combine(configDirectory, "config.toml");
        await File.WriteAllTextAsync(config, "# existing config\n");
        var installer = Path.Combine(extracted, "scripts/Install-CodexJev.ps1");

        var preview = await PowerShell(project, installer, "-ProjectPath", project, "-PackageRoot", extracted, "-WhatIf");
        Assert.True(preview.ExitCode == 0, preview.Output);
        Assert.False(Directory.Exists(Path.Combine(project, ".jev-review")));
        Assert.False(Directory.Exists(Path.Combine(project, ".agents")));
        var installed = await PowerShell(project, installer, "-ProjectPath", project, "-PackageRoot", extracted);
        Assert.True(installed.ExitCode == 0, installed.Output);
        Assert.Equal("existing team instructions\n", await File.ReadAllTextAsync(agents));
        Assert.Equal("# existing config\n", await File.ReadAllTextAsync(config));
        foreach (var skill in new[] { "ultrathink", "orchestrate", "workflowz", "jevify" })
            Assert.True(File.Exists(Path.Combine(project, ".agents/skills", skill, "SKILL.md")));

        var setup = Path.Combine(project, ".jev-review");
        var dll = Path.Combine(setup, "runtime/jev-review.dll");
        Assert.True(File.Exists(Path.Combine(setup, "runtime/JevReview.Core.dll")));
        var doctor = await Run(project, "dotnet", dll, "doctor", "--rubric", Path.Combine(setup, "rubric.json"));
        Assert.True(doctor.ExitCode == 0, doctor.Output);
        var runDirectory = Path.Combine(project, "runs", "portable-smoke");
        var smoke = await Run(project, "dotnet", dll, "classify", "--dataset", Path.Combine(setup, "smoke"),
            "--rubric", Path.Combine(setup, "rubric.json"), "--policy", Path.Combine(setup, "policy.json"),
            "--fake-responses", Path.Combine(setup, "fake-responses.json"), "--mode", "fake", "--output", runDirectory);
        Assert.True(smoke.ExitCode == 0, smoke.Output);
        var manifest = JsonFiles.Read<RunManifest>(Path.Combine(runDirectory, "manifest.json"));
        Assert.Equal(new RunCounts(4, 4, 0, 0, 3), manifest.Counts);
        var item = JsonFiles.ReadLines<Verdict>(Path.Combine(runDirectory, "verdicts.jsonl"))[0];
        var inspection = await Run(project, "dotnet", dll, "inspect", "--run", runDirectory, "--id", item.Id);
        Assert.True(inspection.ExitCode == 0, inspection.Output);
        var records = Path.Combine(project, "records.json");
        await JsonFiles.WriteAsync(records, new[] { new Verification(item.Id, "unresolved", "Synthetic setup check; source evidence is not supplied.", []) });
        var verified = await Run(project, "dotnet", dll, "verify", "--run", runDirectory, "--file", records);
        Assert.True(verified.ExitCode == 0, verified.Output);
        Assert.True(File.Exists(Path.Combine(runDirectory, "verification.json")));
        await ExerciseMcp(project, dll);

        var originalRubric = await File.ReadAllTextAsync(Path.Combine(setup, "rubric.json"));
        var collision = await PowerShell(project, installer, "-ProjectPath", project, "-PackageRoot", extracted);
        Assert.NotEqual(0, collision.ExitCode);
        Assert.Equal(originalRubric, await File.ReadAllTextAsync(Path.Combine(setup, "rubric.json")));

        var fresh = Path.Combine(temp.Path, "collision project");
        Directory.CreateDirectory(fresh);
        await GitAndMcpTests.Git(fresh, "init", "-q");
        var existingSkill = Path.Combine(fresh, ".agents/skills/workflowz");
        Directory.CreateDirectory(existingSkill);
        await File.WriteAllTextAsync(Path.Combine(existingSkill, "SKILL.md"), "team-owned skill");
        var skillCollision = await PowerShell(fresh, installer, "-ProjectPath", fresh, "-PackageRoot", extracted);
        Assert.NotEqual(0, skillCollision.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fresh, ".jev-review")));
        Assert.False(Directory.Exists(Path.Combine(fresh, ".agents/skills/ultrathink")));
        Assert.Equal("team-owned skill", await File.ReadAllTextAsync(Path.Combine(existingSkill, "SKILL.md")));

        var tampered = Path.Combine(temp.Path, "tampered project");
        Directory.CreateDirectory(tampered);
        await GitAndMcpTests.Git(tampered, "init", "-q");
        await File.AppendAllTextAsync(Path.Combine(extracted, "configs/policy.json"), "\n");
        var integrity = await PowerShell(tampered, installer, "-ProjectPath", tampered, "-PackageRoot", extracted);
        Assert.NotEqual(0, integrity.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(tampered, ".jev-review")));
    }

    private static async Task ExerciseMcp(string project, string dll)
    {
        var start = Start(project, "dotnet", dll, "mcp", "--workspace", project);
        start.RedirectStandardInput = true;
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        async Task<JsonElement> Request(string message)
        {
            await process.StandardInput.WriteLineAsync(message);
            await process.StandardInput.FlushAsync(timeout.Token);
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            using var response = JsonDocument.Parse(line!);
            Assert.False(response.RootElement.TryGetProperty("error", out _));
            return response.RootElement.GetProperty("result").Clone();
        }
        await Request(ReleaseCorrectnessTests.InitializeRequest);
        await process.StandardInput.WriteLineAsync(ReleaseCorrectnessTests.InitializedNotification);
        var tools = await Request("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
        Assert.Equal(3, tools.GetProperty("tools").GetArrayLength());
        async Task<JsonElement> Call(string name, object arguments)
        {
            var result = await Request(JsonFiles.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name, arguments } }));
            Assert.False(result.GetProperty("isError").GetBoolean());
            return result.GetProperty("structuredContent");
        }
        var begun = await Call("start_review", new
        {
            dataset = ".jev-review/smoke",
            mode = "fake",
            rubric = ".jev-review/rubric.json",
            policy = ".jev-review/policy.json",
            fakeResponses = ".jev-review/fake-responses.json"
        });
        var runId = begun.GetProperty("runId").GetString();
        JsonElement status;
        do
        {
            await Task.Delay(20, timeout.Token);
            status = await Call("get_review_status", new { runId });
        } while (status.GetProperty("status").GetString() == "running");
        Assert.Equal("completed", status.GetProperty("status").GetString());
        var final = await Call("get_review_result", new { runId });
        Assert.Equal(4, final.GetProperty("manifest").GetProperty("counts").GetProperty("classified").GetInt32());
        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(await stderr);
    }

    private static string FindSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(current.FullName, "JevReview.slnx")))
            current = current.Parent ?? throw new InvalidOperationException("Source root missing");
        return current.FullName;
    }

    private static Task<ProcessResult> PowerShell(string cwd, string script, params string[] args) =>
        Run(cwd, "pwsh", ["-NoProfile", "-NonInteractive", "-File", script, .. args]);

    private static ProcessStartInfo Start(string cwd, string command, params string[] args)
    {
        var start = new ProcessStartInfo(command)
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        start.Environment.Remove("TYPESAFE_API_KEY");
        foreach (var argument in args) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task<ProcessResult> Run(string cwd, string command, params string[] args)
    {
        using var process = Process.Start(Start(cwd, command, args))!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }
        return new(process.ExitCode, await stdout + await stderr);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
