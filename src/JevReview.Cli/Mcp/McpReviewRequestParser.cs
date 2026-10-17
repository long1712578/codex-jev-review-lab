using System.Text.Json;

namespace JevReview;

internal static class McpReviewRequestParser
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "dataset", "git", "base", "head", "task", "mergeBase", "rubric", "mode", "model", "policy",
        "fakeResponses", "concurrency", "timeoutSeconds", "maxRetries", "maxInputBytes"
    };

    public static ReviewRequest Parse(JsonElement arguments, WorkspacePathGuard paths, string runId, bool allowLive)
    {
        if (arguments.ValueKind != JsonValueKind.Object) throw new ArgumentException("invalid_arguments");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject())
        {
            if (!Allowed.Contains(property.Name) || !seen.Add(property.Name)) throw new ArgumentException("unknown_or_duplicate_argument");
            if (property.Name == "mergeBase") _ = Boolean(arguments, property.Name);
            else if (property.Name is "concurrency" or "timeoutSeconds" or "maxRetries" or "maxInputBytes")
                _ = Integer(arguments, property.Name, 0);
            else _ = String(arguments, property.Name);
        }

        var dataset = String(arguments, "dataset");
        var git = String(arguments, "git");
        if ((dataset == null) == (git == null)) throw new ArgumentException("choose_dataset_or_git");
        ReviewSource source;
        if (dataset != null)
        {
            var path = paths.Resolve(dataset);
            if (Directory.Exists(path)) path = paths.Resolve(Path.Combine(path, "items.json"));
            source = new DatasetReviewSource(path);
        }
        else source = new GitReviewSource(paths.Resolve(git!), Required(arguments, "base"),
            String(arguments, "head") ?? "HEAD", Required(arguments, "task"), Boolean(arguments, "mergeBase"));

        var mode = String(arguments, "mode") ?? "fake";
        if (mode == "live" && !allowLive) throw new ArgumentException("live_mode_not_enabled");
        var options = new RunOptions
        {
            Mode = mode,
            Model = String(arguments, "model") ?? (mode == "live" ? "jev-latest" : "fake-v1"),
            Concurrency = Integer(arguments, "concurrency", 1),
            TimeoutSeconds = Integer(arguments, "timeoutSeconds", 30),
            MaxRetries = Integer(arguments, "maxRetries", 2),
            MaxInputBytes = Integer(arguments, "maxInputBytes", 24000)
        };
        ReviewRunner.ValidateOptions(options);
        var policy = String(arguments, "policy");
        return new(source, paths.Resolve(String(arguments, "rubric") ?? "rubrics/refactor-review.v1.json"), options,
            paths.Resolve(Path.Combine("runs", runId)), policy == null ? null : paths.Resolve(policy),
            paths.Resolve(String(arguments, "fakeResponses") ?? "fixtures/fake/dev-responses.json"));
    }

    private static string Required(JsonElement arguments, string name) => String(arguments, name)
        ?? throw new ArgumentException("missing_required_argument");

    private static string? String(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException("invalid_string_argument");
        return value.GetString();
    }

    private static int Integer(JsonElement arguments, string name, int fallback)
    {
        if (!arguments.TryGetProperty(name, out var value)) return fallback;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number : throw new ArgumentException("invalid_numeric_argument");
    }

    private static bool Boolean(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value)) return false;
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : throw new ArgumentException("invalid_boolean_argument");
    }
}
