using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Anvil.Mcp;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

internal static class McpChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "obsidian-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Scenes"));
        var oldHost = Input.HostBridge;
        try
        {
            var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
            var logic = new MainSceneLogic();
            logic.SceneManager.Assets = assets;
            logic.PlayMode = new PlayModeController(logic);
            var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "MCP object" };
            logic.BasicEntities.Add(entity);
            var bridge = new EditorBridge { AutomationContentRootOverride = root };
            var editor = new EditorLogic();
            typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, [logic, editor, assets]);
            var service = new EditorAutomationService();
            service.AttachBridge(bridge);
            service.Start();
            void Pump() => typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, null);
            T Complete<T>(Task<T> task)
            {
                var timer = Stopwatch.StartNew();
                while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); Thread.Sleep(1); }
                if (!task.IsCompleted) throw new Exception("MCP check timed out.");
                return task.GetAwaiter().GetResult();
            }
            EditorAutomationResult Execute(EditorAutomationRequest request) => Complete(bridge.ExecuteAutomationAsync(request));
            var state = Execute(new()).State;
            string generation = state.SceneGeneration;
            EditorAutomationRequest Move(int id, Vector3 position) => new() { Command = EditorAutomationCommand.SetTransform,
                SceneGeneration = generation, ObjectId = id, Position = position };
            Check(state.Objects.Single().Id == entity.Id && state.ModelKeys.Contains("Cube"), "fresh engine capture contains objects and available models");
            Expect<McpException>(() => Complete(service.WriteAsync("read-only", Move(entity.Id, Vector3.One), default)), "READ_ONLY");
            service.ReadOnly = false;
            var moved = Complete(service.WriteAsync("move-1", Move(entity.Id, new(1, 2, 3)), default));
            Check(entity.Position == new Vector3(1, 2, 3) && moved.Object!.Position == new EditorVector(1, 2, 3) && moved.Status.IsDirty,
                "transform result confirms applied pose and dirty state without waiting for UI snapshots");
            Check(Complete(service.WriteAsync("move-1", Move(entity.Id, new(1, 2, 3)), default)).Status.Revision == moved.Status.Revision,
                "identical command retries return the retained result");
            Expect<McpException>(() => Complete(service.WriteAsync("move-1", Move(entity.Id, Vector3.Zero), default)), "REQUEST_ID_CONFLICT");
            Expect<EditorAutomationException>(() => Execute(Move(int.MaxValue, Vector3.One)), "OBJECT_NOT_FOUND");
            Expect<EditorAutomationException>(() => Execute(Move(entity.Id, new(float.NaN, 0, 0))), "INVALID_ARGUMENT");
            Expect<EditorAutomationException>(() => Execute(Move(entity.Id, Vector3.Zero) with { Rotation = new Quaternion() }), "INVALID_ARGUMENT");
            Expect<EditorAutomationException>(() => Execute(Move(entity.Id, Vector3.Zero) with { Scale = Vector3.Zero }), "INVALID_ARGUMENT");
            var create = new EditorAutomationRequest { Command = EditorAutomationCommand.CreateObject, SceneGeneration = generation,
                ModelKey = "Cube", Name = "Created over MCP", Position = new(4, 5, 6) };
            var created = Complete(service.WriteAsync("create-1", create, default));
            var duplicate = Complete(service.WriteAsync("create-1", create, default));
            Check(created.ObjectId == duplicate.ObjectId && logic.BasicEntities.Count == 2 && created.Object!.Name == "Created over MCP",
                "create returns an ID and retries do not duplicate gameobjects");
            var add = new EditorAutomationRequest { Command = EditorAutomationCommand.AddComponent, SceneGeneration = generation,
                ObjectId = created.ObjectId, ComponentType = AudioComponent.TypeId };
            var attached = Execute(add);
            Check(attached.ComponentInstanceId != null && attached.State.Objects.Single(o => o.Id == created.ObjectId).Components.Single().InstanceId == attached.ComponentInstanceId,
                "component creation returns the actual persisted attachment ID");
            Expect<EditorAutomationException>(() => Execute(add), "COMPONENT_EXISTS");
            Expect<EditorAutomationException>(() => Execute(add with { ComponentType = "missing" }), "INVALID_ARGUMENT");
            Execute(new() { Command = EditorAutomationCommand.Select, SceneGeneration = generation, ObjectId = created.ObjectId });
            Check(editor.SelectedObject!.Id == created.ObjectId, "selection reaches the engine editor");
            Execute(new() { Command = EditorAutomationCommand.Select, SceneGeneration = generation });
            Check(editor.SelectedObject == null, "selection can be explicitly cleared");

            using (var cancelled = new CancellationTokenSource())
            {
                var pending = bridge.ExecuteAutomationAsync(Move(entity.Id, Vector3.Zero), cancelled.Token);
                cancelled.Cancel();
                Check(pending.IsCanceled, "cancellation completes queued commands before a frame is pumped");
                Pump();
                Check(entity.Position == new Vector3(1, 2, 3), "cancelled commands never mutate the scene");
            }
            var policyChange = service.WriteAsync("policy-change", Move(entity.Id, Vector3.Zero), default);
            service.ReadOnly = true;
            Expect<McpException>(() => Complete(policyChange), "READ_ONLY");
            service.ReadOnly = false;

            var queue = Enumerable.Range(0, 64).Select(_ => bridge.ExecuteAutomationAsync(new())).ToArray();
            Expect<EditorAutomationException>(() => bridge.ExecuteAutomationAsync(new()).GetAwaiter().GetResult(), "QUEUE_FULL");
            Pump();
            Check(queue.Count(t => t.IsCompleted) == 4, "automation is limited to four commands per frame");
            foreach (var task in queue) Complete(task);

            // Hold an operation after it starts. An HTTP timeout must report pending, not claim cancellation/rollback.
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                ComponentRegistry.Register<McpBlockingComponent>("mcp-test-blocking", "MCP test blocking", owner =>
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(5))) throw new Exception("MCP blocking check was not released.");
                    return new McpBlockingComponent();
                });
                var timed = new EditorAutomationService(TimeSpan.FromMilliseconds(150)) { ReadOnly = false };
                timed.AttachBridge(bridge);
                timed.Start();
                var slowRequest = new EditorAutomationRequest { Command = EditorAutomationCommand.AddComponent, SceneGeneration = generation,
                    ObjectId = entity.Id, ComponentType = "mcp-test-blocking" };
                var slow = timed.WriteAsync("slow-command", slowRequest, default);
                var drain = Task.Run(Pump);
                try
                {
                    Check(entered.Wait(TimeSpan.FromSeconds(2)), "slow operation started on the engine thread");
                    Expect<McpException>(() => slow.GetAwaiter().GetResult(), "OPERATION_PENDING");
                    Check(timed.GetOperation("slow-command").State == "pending", "timed-out running command remains queryable as pending");
                }
                finally { release.Set(); drain.GetAwaiter().GetResult(); }
                Check(timed.GetOperation("slow-command").State == "completed" && entity.Components.OfType<McpBlockingComponent>().Count() == 1,
                    "operation status recovers the actual completion after an HTTP timeout");
                Complete(timed.WriteAsync("slow-command", slowRequest, default));
                Check(entity.Components.OfType<McpBlockingComponent>().Count() == 1, "retry after timeout returns completion without repeating the command");
                timed.Stop();
            }

            string scenePath = Path.Combine(root, "Scenes", "Mcp.obsc");
            var saved = Execute(new() { Command = EditorAutomationCommand.Save, SceneGeneration = generation, Path = "Scenes/Mcp.obsc" });
            Check(File.Exists(scenePath) && !saved.State.IsDirty, "save returns only after the scene file is written and clean");
            var restored = SceneSerialization.LoadFromFile(scenePath, assets);
            Check(restored.BasicEntities.Count == 2 && restored.BasicEntities.Single(e => e.Name == "Created over MCP").Position == new Vector3(4, 5, 6),
                "saved MCP edits survive a scene reload");
            Expect<EditorAutomationException>(() => Execute(new() { Command = EditorAutomationCommand.Save, SceneGeneration = generation, Path = "../outside.obsc" }), "INVALID_ARGUMENT");
            Expect<EditorAutomationException>(() => Execute(new() { Command = EditorAutomationCommand.Save, SceneGeneration = generation, Path = "Scenes/wrong.txt" }), "INVALID_ARGUMENT");
            Expect<EditorAutomationException>(() => Execute(new() { Command = EditorAutomationCommand.Save, SceneGeneration = generation, Path = "Scenes/file:stream.obsc" }), "INVALID_ARGUMENT");
            Expect<EditorAutomationException>(() => Execute(new() { Command = EditorAutomationCommand.Save, SceneGeneration = generation, Path = "Scenes/invalid\0.obsc" }), "INVALID_ARGUMENT");
            Directory.CreateDirectory(Path.Combine(root, "Scenes", "blocked.obsc"));
            Expect<EditorAutomationException>(() => Execute(new() { Command = EditorAutomationCommand.Save, SceneGeneration = generation, Path = "Scenes/blocked.obsc" }), "SAVE_FAILED");
            Execute(new() { Command = EditorAutomationCommand.SetPlayMode, SceneGeneration = generation, Play = true });
            Expect<EditorAutomationException>(() => Execute(Move(entity.Id, Vector3.Zero)), "WRONG_MODE");
            Execute(new() { Command = EditorAutomationCommand.SetPlayMode, SceneGeneration = generation, Play = false });

            var stale = bridge.ExecuteAutomationAsync(Move(entity.Id, Vector3.Zero));
            logic.SceneManager.SetActiveScene(new Scene());
            Expect<EditorAutomationException>(() => Complete(stale), "SCENE_CHANGED");
            Check(Execute(new()).State.SceneGeneration != generation, "scene replacement changes generation even for queued commands");
            logic.SceneManager.SetActiveScene(restored);
            generation = Execute(new()).State.SceneGeneration;

            var settings = new EditorMcpSettingsStore(Path.Combine(root, "UserSettings"));
            var preferences = new EditorMcpSettings { Enabled = true, ReadOnly = false };
            settings.Save(preferences);
            Check(settings.Load() == preferences, "local settings and access token round-trip outside project Content");

            CheckHttp(service, task => Complete(task.AsTask()), generation, restored.BasicEntities[0]);
            var shutdown = bridge.ExecuteAutomationAsync(new());
            bridge.StopAutomation();
            Check(shutdown.IsCanceled, "engine shutdown cancels pending automation immediately");
            Expect<EditorAutomationException>(() => bridge.ExecuteAutomationAsync(new()).GetAwaiter().GetResult(), "EDITOR_NOT_READY");
            service.Stop(detach: true);
        }
        finally
        {
            Input.HostBridge = oldHost;
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckHttp(EditorAutomationService service,
        Func<ValueTask<CallToolResult>, CallToolResult> complete, string generation, BasicEntity entity)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        string token = EditorMcpSettings.NewToken();
        var host = new EditorMcpHost(service);
        host.StartAsync(port, token).GetAwaiter().GetResult();
        try
        {
            using var http = new HttpClient();
            var endpoint = new Uri(host.Endpoint!);
            HttpStatusCode Probe(string? credential, string? origin = null, string? hostHeader = null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                if (credential != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + credential);
                if (origin != null) request.Headers.TryAddWithoutValidation("Origin", origin);
                if (hostHeader != null) request.Headers.Host = hostHeader;
                using var response = http.Send(request);
                return response.StatusCode;
            }
            Check(Probe(null) == HttpStatusCode.Unauthorized && Probe("wrong") == HttpStatusCode.Unauthorized,
                "HTTP rejects absent and incorrect access tokens");
            Check(Probe(token, "https://untrusted.example") == HttpStatusCode.Forbidden && Probe(token, hostHeader: "untrusted.example:" + port) == HttpStatusCode.Forbidden,
                "HTTP rejects browser origins and unapproved Host values");
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
            });
            var client = McpClient.CreateAsync(transport).GetAwaiter().GetResult();
            try
            {
                var tools = client.ListToolsAsync().GetAwaiter().GetResult();
                Check(tools.Count == 12 && tools.Any(t => t.Name == "scene_set_transform") && tools.Any(t => t.Name == "editor_get_operation"),
                    "official SDK client discovers the twelve tools over Streamable HTTP");
                var status = complete(client.CallToolAsync("editor_get_status"));
                Check(status.IsError != true && Text(status).Contains(generation), "real MCP status call returns the current scene generation");
                var moveArgs = new Dictionary<string, object?> { ["requestId"] = "http-move", ["sceneGeneration"] = generation,
                    ["objectId"] = entity.Id, ["position"] = new { x = 7, y = 8, z = 9 } };
                service.ReadOnly = true;
                var denied = complete(client.CallToolAsync("scene_set_transform", moveArgs));
                Check(denied.IsError == true && Text(denied).Contains("READ_ONLY"), "MCP exposes read-only failures as tool errors");
                service.ReadOnly = false;
                var move = complete(client.CallToolAsync("scene_set_transform", moveArgs));
                Check(move.IsError != true && Text(move).Contains("http-move"), "real MCP transform call returns a completed game-thread result");
                var objects = complete(client.CallToolAsync("scene_list_objects", new Dictionary<string, object?> { ["limit"] = 1 }));
                Check(objects.IsError != true && Text(objects).Contains("nextOffset"), "MCP object listing supports bounded pagination");
                var badPage = complete(client.CallToolAsync("scene_list_objects", new Dictionary<string, object?> { ["limit"] = 101 }));
                Check(badPage.IsError == true, "out-of-range MCP pagination is rejected");
                var badTarget = complete(client.CallToolAsync("scene_get_object", new Dictionary<string, object?> { ["objectId"] = int.MaxValue }));
                Check(badTarget.IsError == true && Text(badTarget).Contains("OBJECT_NOT_FOUND"), "MCP missing objects return actionable tool errors");
                string name = entity.Name;
                try
                {
                    entity.Name = new string('X', 5 * 1024 * 1024);
                    var large = complete(client.CallToolAsync("scene_get_object", new Dictionary<string, object?> { ["objectId"] = entity.Id }));
                    Check(large.IsError == true && Text(large).Contains("RESULT_TOO_LARGE"), "oversized tool results become bounded MCP errors");
                }
                finally { entity.Name = name; }
            }
            finally { client.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        finally { host.StopAsync().GetAwaiter().GetResult(); }
        Check(!host.IsRunning, "MCP host stops independently of editor workspace tabs");
    }

    private static string Text(CallToolResult result) => JsonSerializer.Serialize(result);
    public sealed class McpBlockingComponent : Engine.Components.GameComponent { }
    private static void Expect<T>(Action action, string code) where T : Exception
    {
        try { action(); }
        catch (T ex) when ((ex is EditorAutomationException ae ? ae.Code : ex.Message).Contains(code))
        {
            Check(true, code + " is reported");
            return;
        }
        throw new Exception("Expected " + code);
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("MCP: " + description);
        Console.WriteLine("PASS: MCP " + description);
    }
}
