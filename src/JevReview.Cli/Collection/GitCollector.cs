using System.Diagnostics;

namespace JevReview;
public sealed record CollectedReview(ReviewItem[] Items, Dictionary<string, string> Source);
public static class GitCollector
{
    public static async Task<CollectedReview> CollectAsync(string repository, string baseRef, string headRef, string task,
        bool mergeBase, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task) || baseRef.StartsWith('-') || headRef.StartsWith('-'))
            throw new ArgumentException("invalid_git_revision_or_task");
        var root = Path.GetFullPath(repository);
        var topLevel = (await GitAsync(root, ["rev-parse", "--show-toplevel"], cancellationToken)).Trim();
        if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(topLevel)).Equals(Path.TrimEndingDirectorySeparator(root),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("git_repository_root_required");
        var baseSha = (await GitAsync(root, ["rev-parse", "--verify", "--end-of-options", baseRef + "^{commit}"], cancellationToken)).Trim();
        var headSha = (await GitAsync(root, ["rev-parse", "--verify", "--end-of-options", headRef + "^{commit}"], cancellationToken)).Trim();
        var diffBase = mergeBase ? (await GitAsync(root, ["merge-base", baseSha, headSha], cancellationToken)).Trim() : baseSha;
        var paths = (await GitAsync(root, ["diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--name-only", "-z", diffBase, headSha, "--"], cancellationToken))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        List<ReviewItem> items = [];
        foreach (var path in paths)
        {
            var diff = await GitAsync(root, ["diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--unified=3", diffBase, headSha, "--", path], cancellationToken);
            items.Add(new("git-" + JsonFiles.Hash(path)[..16], task, path, diff,
                "Git endpoint diff with three context lines. Full caller/implementation context is not supplied. Binary/mode-only diffs need manual inspection; do not infer unchanged behavior."));
        }
        return new(items.ToArray(), new()
        {
            ["kind"] = "git", ["repository"] = root, ["baseSha"] = baseSha, ["headSha"] = headSha,
            ["diffBaseSha"] = diffBase, ["semantics"] = mergeBase ? "merge-base" : "endpoints",
            ["workingTree"] = "excluded: staged, unstaged and untracked", ["task"] = task
        });
    }

    private static async Task<string> GitAsync(string root, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add("--no-pager");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new ArgumentException("git_unavailable");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }
        await stderr;
        if (process.ExitCode != 0) throw new ArgumentException("git_command_failed");
        return await stdout;
    }
}
