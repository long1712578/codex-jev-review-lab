using System.Collections.Concurrent;
using System.Text.Json;

namespace JevReview;

public sealed class McpServer(string workspace, bool allowLive = false)
{
    private readonly WorkspacePathGuard paths = new(workspace);
    private readonly ConcurrentDictionary<string, Job> jobs = new();
    private InitializationState initializationState;

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
            if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("jsonrpc", out var rpc) || rpc.ValueKind != JsonValueKind.String || rpc.GetString() != "2.0" ||
                !request.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
                return Error(null, -32600, "invalid_request");
            if (!request.TryGetProperty("id", out var requestId))
            {
                if (method.GetString() == "notifications/initialized" && initializationState == InitializationState.AwaitingReady)
                    initializationState = InitializationState.Ready;
                return null;
            }
            if (requestId.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
                return Error(null, -32600, "invalid_id");
            id = requestId.Clone();
            object result;
            switch (method.GetString())
            {
                case "initialize":
                    ValidateInitialization(request.GetProperty("params"));
                    if (initializationState != InitializationState.New) return Error(id, -32600, "already_initialized");
                    initializationState = InitializationState.AwaitingReady;
                    result = new
                    {
                        protocolVersion = "2025-06-18",
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "jev-review", version = "1.0.0" }
                    };
                    break;
                case "ping": result = new { }; break;
                case "tools/list":
                    if (initializationState != InitializationState.Ready) return Error(id, -32000, "not_initialized");
                    result = new { tools = Tools() };
                    break;
                case "tools/call":
                    if (initializationState != InitializationState.Ready) return Error(id, -32000, "not_initialized");
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
                var runId = "mcp-" + Guid.NewGuid().ToString("N");
                var request = McpReviewRequestParser.Parse(arguments, paths, runId, allowLive);
                var job = new Job();
                jobs[runId] = job;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var run = await ReviewService.StartAsync(request, cancellationToken,
                            request.Options.Mode == "live" ? CliEnvironment.RequireApiKey() : null);
                        job.Manifest = run.Manifest;
                        job.Status = cancellationToken.IsCancellationRequested ? "cancelled" : "completed";
                    }
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
                var manifest = job != null ? job.Manifest : File.Exists(manifestPath) ? JsonFiles.Read<RunManifest>(manifestPath) : null;
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

    private string SafePath(string path) => paths.Resolve(path);

    private static void ValidateInitialization(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("protocolVersion", out var version) || version.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(version.GetString()) ||
            !parameters.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("clientInfo", out var client) || client.ValueKind != JsonValueKind.Object ||
            !client.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) ||
            !client.TryGetProperty("version", out var clientVersion) || clientVersion.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(clientVersion.GetString()))
            throw new ArgumentException("invalid_initialize_parameters");
    }

    private enum InitializationState { New, AwaitingReady, Ready }

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
        public RunManifest? Manifest;
    }
}
