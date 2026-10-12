using System.Collections.Concurrent;
using System.Text.Json;

namespace JevReview;

public sealed class McpServer(string workspace, bool allowLive = false)
{
    private readonly string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
    private readonly ConcurrentDictionary<string, Job> jobs = new();
    private bool initialized;

    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(cancellationToken);
            if (line == null) break;
            var result = await HandleAsync(line, cancellationToken);
            if (result != null) { await output.WriteLineAsync(result); await output.FlushAsync(cancellationToken); }
        }
    }

    public async Task<string?> HandleAsync(string message, CancellationToken cancellationToken)
    {
        JsonElement? id = null;
        try
        {
            if (message.Length > 1_048_576) return Error(null, -32600, "message_too_large");
            using var document = JsonDocument.Parse(message);
            var request = document.RootElement;
            if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("jsonrpc", out var rpc) || rpc.GetString() != "2.0" ||
                !request.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
                return Error(null, -32600, "invalid_request");
            if (!request.TryGetProperty("id", out var requestId)) return null;
            if (requestId.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
                return Error(null, -32600, "invalid_id");
            id = requestId.Clone();
            object result;
            switch (method.GetString())
            {
                case "initialize":
                    initialized = true;
                    result = new { protocolVersion = "2025-06-18", capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "jev-review", version = "1.0.0" } };
                    break;
                case "ping": result = new { }; break;
                case "tools/list":
                    if (!initialized) return Error(id, -32000, "not_initialized");
                    result = new { tools = Tools() };
                    break;
                case "tools/call":
                    if (!initialized) return Error(id, -32000, "not_initialized");
                    result = await CallAsync(request.GetProperty("params"), cancellationToken);
                    break;
                default: return Error(id, -32601, "method_not_found");
            }
            return JsonFiles.Serialize(new { jsonrpc = "2.0", id, result });
        }
        catch (JsonException) { return Error(id, -32700, "parse_error"); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        { return Error(id, -32602, "invalid_parameters"); }
    }

    private async Task<object> CallAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        try
        {
            var name = parameters.GetProperty("name").GetString();
            var arguments = parameters.GetProperty("arguments");
            object data;
            if (name == "start_review")
            {
                if (jobs.Values.Count(x => x.Status == "running") >= 4) throw new ArgumentException("too_many_jobs");
                List<string> command = [];
                var allowed = new[] { "dataset", "git", "base", "head", "task", "mergeBase", "rubric", "mode", "model", "policy", "fakeResponses", "concurrency", "timeoutSeconds", "maxRetries", "maxInputBytes" };
                foreach (var property in arguments.EnumerateObject())
                {
                    if (!allowed.Contains(property.Name)) throw new ArgumentException("unknown_argument");
                    var key = property.Name switch { "mergeBase" => "merge-base", "fakeResponses" => "fake-responses", "timeoutSeconds" => "timeout-seconds", "maxRetries" => "max-retries", "maxInputBytes" => "max-input-bytes", _ => property.Name };
                    if (key == "merge-base")
                    {
                        if (property.Value.GetBoolean()) command.Add("--merge-base");
                        continue;
                    }
                    string value;
                    if (key is "concurrency" or "timeout-seconds" or "max-retries" or "max-input-bytes")
                    {
                        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var number))
                            throw new ArgumentException("invalid_numeric_argument");
                        var bounds = key switch
                        {
                            "concurrency" => (1, 32), "timeout-seconds" => (1, 600), "max-retries" => (0, 5), _ => (1, 1_048_576)
                        };
                        if (number < bounds.Item1 || number > bounds.Item2) throw new ArgumentException("numeric_argument_out_of_range");
                        value = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                            throw new ArgumentException("invalid_string_argument");
                        value = property.Value.GetString()!;
                    }
                    if (key is "dataset" or "git" or "rubric" or "policy" or "fake-responses") value = SafePath(value);
                    if (key == "dataset" && Directory.Exists(value)) value = SafePath(Path.Combine(value, "items.json"));
                    command.Add("--" + key);
                    command.Add(value);
                }
                var parsed = CommandOptions.Parse(command.ToArray());
                if (parsed.Get("mode", "fake") == "live" && !allowLive) throw new ArgumentException("live_mode_not_enabled");
                // Defaults are rooted explicitly, never inherited from the MCP host's current directory.
                if (!parsed.Has("rubric")) command.AddRange(["--rubric", SafePath("rubrics/refactor-review.v1.json")]);
                if (!parsed.Has("fake-responses")) command.AddRange(["--fake-responses", SafePath("fixtures/fake/dev-responses.json")]);
                var runId = "mcp-" + Guid.NewGuid().ToString("N");
                var output = SafePath(Path.Combine("runs", runId));
                command.AddRange(["--output", output]);
                var job = new Job();
                jobs[runId] = job;
                job.Task = Task.Run(async () =>
                {
                    try { job.Run = await ReviewService.StartAsync(CommandOptions.Parse(command.ToArray()), cancellationToken); job.Status = cancellationToken.IsCancellationRequested ? "cancelled" : "completed"; }
                    catch (OperationCanceledException) { job.Status = "cancelled"; }
                    catch (Exception) { job.Status = "failed"; }
                }, CancellationToken.None);
                data = new { runId, status = "running" };
            }
            else if (name is "get_review_status" or "get_review_result")
            {
                if (arguments.EnumerateObject().Any(x => x.Name != "runId")) throw new ArgumentException("unknown_argument");
                var runId = arguments.GetProperty("runId").GetString()!;
                if (string.IsNullOrEmpty(runId) || runId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
                    throw new ArgumentException("invalid_run_id");
                jobs.TryGetValue(runId, out var job);
                var jobStatus = job?.Status;
                var folder = SafePath(Path.Combine("runs", runId));
                var manifestPath = SafePath(Path.Combine(folder, "manifest.json"));
                // A known in-process job owns its state; never read files it may still be writing.
                var manifest = job != null ? job.Run?.Manifest : File.Exists(manifestPath) ? JsonFiles.Read<RunManifest>(manifestPath) : null;
                if (job == null && manifest == null) throw new ArgumentException("unknown_run_id");
                if (name == "get_review_status") data = new { runId, status = jobStatus ?? "completed", counts = manifest?.Counts };
                else
                {
                    if (manifest == null || jobStatus == "running") throw new ArgumentException("run_not_complete");
                    var flagged = JsonDocument.Parse(await File.ReadAllTextAsync(SafePath(Path.Combine(folder, "flagged.json")), cancellationToken)).RootElement.Clone();
                    var errors = JsonFiles.ReadLines<Verdict>(SafePath(Path.Combine(folder, "errors.jsonl"))).Select(x => new { x.Id, x.ErrorCode }).ToArray();
                    data = new { manifest, flagged, errors, reportPath = Path.Combine(folder, "report.md") };
                }
            }
            else throw new ArgumentException("unknown_tool");
            return new { content = new[] { new { type = "text", text = JsonFiles.Serialize(data) } }, structuredContent = data, isError = false };
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException or JsonException)
        {
            return new { content = new[] { new { type = "text", text = "invalid_tool_input_or_run_state" } }, isError = true };
        }
    }

    private string SafePath(string path)
    {
        var full = Path.GetFullPath(path, root);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.Equals(root, comparison) && !full.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("path_outside_workspace");
        // Reject reparse points, including dangling links, on each segment. The same check applies to output paths.
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, full).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("linked_path_not_allowed");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }

    private static object[] Tools()
    {
        object String() => new { type = "string" };
        return
        [
            new { name = "start_review", description = "Start dataset or committed Git range triage. Returns a runId immediately. Does not echo diffs.",
                inputSchema = new { type = "object", additionalProperties = false, properties = new Dictionary<string, object>
                {
                    ["dataset"] = String(), ["git"] = String(), ["base"] = String(), ["head"] = String(), ["task"] = String(),
                    ["mergeBase"] = new { type = "boolean" }, ["rubric"] = String(), ["policy"] = String(), ["fakeResponses"] = String(),
                    ["mode"] = new { type = "string", @enum = new[] { "fake", "live" } }, ["model"] = String(),
                    ["concurrency"] = new { type = "integer", minimum = 1, maximum = 32 }, ["timeoutSeconds"] = new { type = "integer", minimum = 1, maximum = 600 },
                    ["maxRetries"] = new { type = "integer", minimum = 0, maximum = 5 }, ["maxInputBytes"] = new { type = "integer", minimum = 1, maximum = 1048576 }
                } } },
            new { name = "get_review_status", description = "Return job status and final counts when available.", inputSchema = new { type = "object", additionalProperties = false, properties = new { runId = String() }, required = new[] { "runId" } } },
            new { name = "get_review_result", description = "Return manifest, flagged IDs/reasons and sanitized errors after completion.", inputSchema = new { type = "object", additionalProperties = false, properties = new { runId = String() }, required = new[] { "runId" } } }
        ];
    }

    private static string Error(JsonElement? id, int code, string message) => JsonFiles.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } });
    private sealed class Job
    {
        public volatile string Status = "running";
        public ReviewRun? Run;
        public Task? Task;
    }
}
