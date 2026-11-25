using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SolciteEngine.Utils;

namespace SolciteEngine.Editor;

/// <summary>One tool of the MCP server: its name, description, JSON schema and the code that runs it (on the editor's thread).</summary>
public sealed record McpTool(string Name, string Description, JsonObject Schema, Func<JsonElement, string> Run);

/// <summary>
/// A small MCP (Model Context Protocol) server inside the editor: JSON-RPC over HTTP (<c>POST http://127.0.0.1:port/mcp</c>,
/// the "Streamable HTTP" transport without streaming). Requests are read on a background thread; every tool call is
/// queued and run by <see cref="Pump"/> from the editor's update loop, because the scene and OpenGL belong to that thread.
/// Only listens on localhost. Start it with <c>--mcp [port]</c> (default 8765).
/// </summary>
public sealed class McpServer : IDisposable
{
    public const int DefaultPort = 8765;
    private const string ProtocolVersion = "2025-03-26";

    private readonly HttpListener _listener = new();
    private readonly IReadOnlyDictionary<string, McpTool> _tools;
    private readonly ConcurrentQueue<(McpTool tool, JsonElement args, TaskCompletionSource<(string text, bool error)> done)> _queue = new();
    private readonly CancellationTokenSource _stop = new();

    public McpServer(IEnumerable<McpTool> tools, int port)
    {
        _tools = tools.ToDictionary(t => t.Name);
        Port = port;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public int Port { get; }

    public void Start()
    {
        _listener.Start();
        Log.Info($"MCP server listening on http://127.0.0.1:{Port}/mcp ({_tools.Count} tools)");
        _ = Task.Run(AcceptLoop);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Close(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Runs the queued tool calls. Called every frame by the editor (main thread).</summary>
    public void Pump()
    {
        while (_queue.TryDequeue(out var job))
        {
            try { job.done.SetResult((job.tool.Run(job.args), false)); }
            catch (Exception e) { job.done.SetResult((e.Message, true)); }
        }
    }

    // ---- transport -------------------------------------------------------------------------------

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
            _ = Task.Run(() => Handle(context));
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            if (context.Request.HttpMethod != "POST")
            {
                response.StatusCode = 405;   // no server-initiated stream: POST only
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync();

            JsonNode? reply;
            try
            {
                var request = JsonNode.Parse(body);
                reply = await DispatchOne(request);
            }
            catch (JsonException)
            {
                reply = Error(null, -32700, "Parse error");
            }

            if (reply == null)
            {
                response.StatusCode = 202;   // a notification: nothing to answer
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(reply.ToJsonString());
            response.ContentType = "application/json";
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception e)
        {
            Log.Warn($"MCP request failed: {e.Message}");
            try { response.StatusCode = 500; } catch { /* already sent */ }
        }
        finally
        {
            response.Close();
        }
    }

    private async Task<JsonNode?> DispatchOne(JsonNode? request)
    {
        if (request is not JsonObject message) return Error(null, -32600, "Invalid request");
        var id = message["id"]?.DeepClone();
        string? method = message["method"]?.GetValue<string>();
        if (id == null) return null;   // notification (e.g. notifications/initialized)

        switch (method)
        {
            case "initialize":
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "solcite-engine", ["version"] = "0.1.0" },
                });
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                return Result(id, new JsonObject
                {
                    ["tools"] = new JsonArray(_tools.Values.Select(t => (JsonNode)new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["inputSchema"] = t.Schema.DeepClone(),
                    }).ToArray()),
                });
            case "tools/call":
                var parameters = message["params"] as JsonObject;
                string? name = parameters?["name"]?.GetValue<string>();
                if (name == null || !_tools.TryGetValue(name, out var tool))
                    return Error(id, -32602, $"Unknown tool '{name}'");

                var args = JsonSerializer.SerializeToElement(parameters!["arguments"] ?? new JsonObject());
                var done = new TaskCompletionSource<(string, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
                _queue.Enqueue((tool, args, done));
                var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(60)));
                var (text, isError) = finished == done.Task ? done.Task.Result : ("Timed out: the editor did not run the call (is the window minimised or busy?).", true);
                return Result(id, new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                    ["isError"] = isError,
                });
            default:
                return Error(id, -32601, $"Method not found: {method}");
        }
    }

    private static JsonObject Result(JsonNode id, JsonNode result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}
