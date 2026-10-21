using System.Diagnostics;
using Anvil.Mcp;
using Anvil.Mcp.Tools;
using Engine.Editor;
using Engine.Recources;
using ModelContextProtocol;

/// <summary>Phase C3 of Docs/Research/AiTextures/PLAN.md: the materials_generate tool and its polling through editor_get_operation.</summary>
internal static class TexGenMcpChecks
{
    public static void Run(EditorBridge bridge, Action drain)
    {
        var real = TexGenSettings.Load();
        if (!File.Exists(real.PythonExe))
        {
            Console.WriteLine("SKIP: materials_generate checks (no Python at " + real.PythonExe + ")");
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "obsidian-texgen-mcp-" + Guid.NewGuid().ToString("N"));
        string content = Path.Combine(root, "Content");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "Content.mgcb"), "\n/outputDir:bin/$(Platform)\n/platform:Windows\n");
        MaterialAsset.SourceRootOverride = content;
        var previous = bridge.GeneratedMaterialImporterFactory;
        bridge.GeneratedMaterialImporterFactory = () => new GeneratedMaterialImporter(content, Path.Combine(root, "exe"), FakeBuild);
        try
        {
            var settings = new TexGenSettings { RepoRoot = real.RepoRoot, PythonExe = real.PythonExe, TexGenRoot = real.TexGenRoot,
                CacheRoot = Path.Combine(root, "cache"), TimeoutSeconds = 120 };
            var service = new EditorAutomationService { MaterialRunner = new TexGenRunner(settings) };
            service.AttachBridge(bridge);
            service.Start();
            var tools = new EditorWriteTools(service);

            Expect(() => tools.GenerateMaterial("mat-ro", "test stone", resolution: 128), "READ_ONLY", "materials_generate is refused while the editor is read-only");
            service.ReadOnly = false;

            Expect(() => tools.GenerateMaterial("mat-bad", "  ", resolution: 128), "INVALID_ARGUMENT", "an empty prompt is rejected");
            Expect(() => tools.GenerateMaterial("mat-bad", "x", resolution: 100), "INVALID_ARGUMENT", "a resolution that is not a power of two is rejected");
            Expect(() => tools.GenerateMaterial("mat-bad", "x", backend: "classic", resolution: 128), "INVALID_ARGUMENT", "the photo backend is not offered over MCP");
            Expect(() => tools.GenerateMaterial("", "x", resolution: 128), "INVALID_ARGUMENT", "a request id is required");

            var started = tools.GenerateMaterial("mat-1", "mcp test stone", seed: 21, resolution: 128);
            Check(started.RequestId == "mat-1" && started.State is "pending" or "completed", "materials_generate returns at once with a pending operation");
            var done = Wait(service, "mat-1", drain);
            Check(done.State == "completed" && done.Material != null && done.Error == null, "the operation completes: " + done.Error);
            string path = done.Material!.MaterialPath;
            Check(path.StartsWith("Materials/Generated/mcp_test_stone_") && path.EndsWith(".mat") && MaterialAsset.Exists(path) && !done.Material.FromCache,
                "the result is the imported .mat: " + path);
            Check(done.Material.CacheId != null && path.Contains(done.Material.CacheId), "the result carries the cache id used in the file name");

            Check(Wait(service, "mat-1", drain).Material!.MaterialPath == path, "polling again returns the retained result");
            Check(tools.GenerateMaterial("mat-1", "mcp test stone", seed: 21, resolution: 128).State == "completed", "an identical retry rejoins the same job");
            Expect(() => tools.GenerateMaterial("mat-1", "mcp test stone", seed: 22, resolution: 128), "REQUEST_ID_CONFLICT", "reusing a request id for other arguments is a conflict");

            tools.GenerateMaterial("mat-2", "mcp test stone", seed: 21, resolution: 128);
            var again = Wait(service, "mat-2", drain);
            Check(again.State == "completed" && again.Material!.FromCache && again.Material.MaterialPath == path,
                "the same request under a new id reuses the cache and the same material");

            tools.GenerateMaterial("mat-3", "mcp test stone", seed: 22, resolution: 128);
            Check(Wait(service, "mat-3", drain).Material!.MaterialPath != path, "another seed gives another material");

            // Where the GPU backend is installed it would really generate: only check the unavailable case where it is one.
            if (service.MaterialRunner.CheckBackendsAsync(new[] { "sdxl_classic" }).GetAwaiter().GetResult()["sdxl_classic"] != null)
            {
                tools.GenerateMaterial("mat-4", "x", backend: "sdxl_classic", resolution: 128);
                var failed = Wait(service, "mat-4", drain);
                Check(failed.State == "failed" && !string.IsNullOrEmpty(failed.Error) && failed.Material == null, "an unavailable backend fails with the reason: " + failed.Error);
            }
            else Console.WriteLine("SKIP: unavailable backend check (sdxl_classic is installed here)");

            Expect(() => service.GetOperation("never-sent"), "OPERATION_NOT_FOUND", "an unknown operation is reported");
            service.Stop();
        }
        finally
        {
            bridge.GeneratedMaterialImporterFactory = previous;
            MaterialAsset.SourceRootOverride = null;
            try { Directory.Delete(root, recursive: true); } catch { /* temp folder */ }
        }
    }

    private static EditorOperationStatus Wait(EditorAutomationService service, string requestId, Action drain)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(120))
        {
            drain();
            var status = service.GetOperation(requestId);
            if (status.State != "pending") return status;
            Thread.Sleep(10);
        }
        throw new Exception("FAIL: materials_generate never finished: " + requestId);
    }

    // The same stand-in for mgcb as TexGenChecks: builds every /build: entry into bin/Windows/<name>.xnb.
    private static void FakeBuild(string contentRoot)
    {
        foreach (string line in File.ReadAllLines(Path.Combine(contentRoot, "Content.mgcb")))
        {
            if (!line.StartsWith("/build:")) continue;
            string xnb = Path.Combine(contentRoot, "bin", "Windows", Path.ChangeExtension(line.Substring(7), ".xnb").Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(xnb)!);
            File.WriteAllText(xnb, "xnb");
        }
    }

    private static void Expect(Action action, string code, string description)
    {
        try { action(); }
        catch (McpException e) when (e.Message.StartsWith(code, StringComparison.Ordinal)) { Console.WriteLine("PASS: " + description); return; }
        throw new Exception("FAIL: " + description + " (expected " + code + ")");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
