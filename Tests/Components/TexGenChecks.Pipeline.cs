using Engine.Components;
using Engine.Editor;
using Engine.Recources;

/// <summary>Phase B of Docs/Research/AiTextures/PLAN.md: ids, protocol, settings, runner, importer and the bridge.</summary>
internal static partial class TexGenChecks
{
    // ---- B1: ids, protocol, settings ----

    private static void RunIds()
    {
        var p = new TexGenParams { Prompt = "mossy cobblestone", Seed = 1234 };
        Check(MaterialGenerationId.Canonical(p) == "v1|fake|source2|auto|1024|1234|0||mossy cobblestone|",
            "the canonical id string follows PLAN.md 2.4");
        Check(MaterialGenerationId.Compute(p) == "9f5ffcc47f1a39a9", "id of 'mossy cobblestone' seed 1234 matches the Python SHA-256 vector");
        var other = new TexGenParams
        {
            Prompt = "  rusty steel plate ", NegativePrompt = "blurry", Seed = -7, Resolution = 512, Backend = "classic",
            Family = "metal", ForceDielectric = true, InputRgbSha256 = string.Concat(Enumerable.Repeat("ab", 32)),
        };
        Check(MaterialGenerationId.Compute(other) == "3daaf8e94a2cb2cc", "id with every field set (trimmed prompt, negative seed, hash) matches the Python vector");
        Check(MaterialGenerationId.Compute(p, "2") == "facd687b99f01bb6", "another pipeline version gives another id");
        Check(MaterialGenerationId.Compute(new TexGenParams { Prompt = "mossy cobblestone ", Seed = 1234 }) == MaterialGenerationId.Compute(p),
            "whitespace around the prompt does not change the id");
        Check(MaterialGenerationId.Compute(new TexGenParams { Prompt = "mossy cobblestone", Seed = 1235 }) != MaterialGenerationId.Compute(p),
            "a different seed changes the id");
        Check(MaterialGenerationId.Compute(new TexGenParams { Prompt = "pedra \u00e1spera", Seed = 1 }) == "9a8bd8e2613652e3", "non-ASCII prompts hash as UTF-8");
        Check(MaterialGenerationId.Slug("Mossy  Cobblestone!") == "mossy_cobblestone" && MaterialGenerationId.Slug("!!!") == "material" &&
              MaterialGenerationId.Slug(new string('a', 80)).Length == 32, "slugs follow texgen's rule");
    }

    private static void RunProtocol()
    {
        var progress = TexGenProtocol.Parse("{\"event\":\"progress\",\"stage\":\"diffusion\",\"progress\":0.35}");
        Check(progress.Kind == TexGenEventKind.Progress && progress.Stage == "diffusion" && Math.Abs(progress.Progress - 0.35) < 1e-9, "a progress line parses");
        var warning = TexGenProtocol.Parse("{\"event\":\"warning\",\"code\":\"albedo_too_dark\",\"message\":\"dark\"}");
        Check(warning.Kind == TexGenEventKind.Warning && warning.Code == "albedo_too_dark" && warning.Message == "dark", "a warning line parses");
        var done = TexGenProtocol.Parse("{\"event\":\"done\",\"out\":\"C:/x/3fa1\"}");
        Check(done.Kind == TexGenEventKind.Done && done.Out == "C:/x/3fa1", "a done line parses");
        var error = TexGenProtocol.Parse("{\"event\":\"error\",\"code\":\"model_missing\",\"message\":\"no model\"}");
        Check(error.Kind == TexGenEventKind.Error && error.Code == "model_missing", "an error line parses");
        foreach (string junk in new[] { "", "   ", "Traceback (most recent call last):", "{\"event\":\"mystery\"}", "{\"no\":1}", "[1,2]", "{\"event\":", "{\"event\":5}" })
            Check(TexGenProtocol.Parse(junk).Kind == TexGenEventKind.Unknown, "junk line is Unknown: [" + junk + "]");
    }

    private static void RunSettings()
    {
        string repo = NewTempFolder();
        try
        {
            var defaults = TexGenSettings.Load(repo);
            Check(defaults.DefaultBackend == "fake" && defaults.TimeoutSeconds == 600 &&
                  defaults.PythonExe == Path.Combine(repo, "Tools", "TexGen", ".venv", "Scripts", "python.exe") &&
                  defaults.CacheRoot == Path.Combine(repo, "Tools", "TexGen", "cache"), "a missing texgen.json gives the defaults");
            Directory.CreateDirectory(Path.Combine(repo, "Tools", "TexGen"));
            string file = Path.Combine(repo, "Tools", "TexGen", "texgen.json");
            File.WriteAllText(file, "{ \"pythonExe\": \"py/python.exe\", \"cacheRoot\": \"C:/abs/cache\", \"defaultBackend\": \"classic\", \"timeoutSeconds\": 5 }");
            var set = TexGenSettings.Load(repo);
            Check(set.PythonExe == Path.Combine(repo, "py", "python.exe") && set.CacheRoot == Path.GetFullPath("C:/abs/cache") &&
                  set.DefaultBackend == "classic" && set.TimeoutSeconds == 5, "texgen.json overrides, relative paths resolve against the repo root");
            File.WriteAllText(file, "{ not json");
            Check(TexGenSettings.Load(repo).DefaultBackend == "fake", "a damaged texgen.json gives the defaults");
        }
        finally { try { Directory.Delete(repo, recursive: true); } catch { /* temp folder */ } }
    }

    // ---- B3: importer ----

    // A stand-in for mgcb: "builds" every /build: entry of the manifest into bin/Windows/<name>.xnb.
    private static void FakeBuild(string contentRoot)
    {
        foreach (string line in File.ReadAllLines(Path.Combine(contentRoot, "Content.mgcb")))
        {
            if (!line.StartsWith("/build:")) continue;
            string rel = line.Substring("/build:".Length);
            string xnb = Path.Combine(contentRoot, "bin", "Windows", Path.ChangeExtension(rel, ".xnb").Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(xnb)!);
            File.WriteAllText(xnb, "xnb");
        }
    }

    // A cache entry like texgen writes it (maps are tiny PNGs, texgen.json schema 1).
    private static string FakeCacheEntry(string root, string id, bool metal, string slug = "test_stone")
    {
        string dir = Path.Combine(root, "cache", id);
        Directory.CreateDirectory(dir);
        foreach (string suffix in GeneratedMaterialSet.Suffixes)
        {
            if (suffix == "Metallic" && !metal) continue;
            WritePng(Path.Combine(dir, slug + "_" + suffix + ".png"), 2, 2, 0, new byte[] { 1, 2, 3, 4 });
        }
        File.WriteAllText(Path.Combine(dir, "texgen.json"), "{\"schema\":1,\"id\":\"" + id + "\",\"params\":{\"prompt\":\"test stone\"}," +
            "\"normal_convention\":\"directx\",\"seam_score\":0.9,\"validation\":{\"passed\":false,\"warnings\":[{\"code\":\"roughness_flat\",\"message\":\"flat\"}]}," +
            "\"suggested\":{\"tiling\":0.667,\"material_type\":\"Basic\",\"family\":\"stone\"}}");
        return dir;
    }

    private static void RunImporter()
    {
        string root = NewTempFolder();
        string content = Path.Combine(root, "Content"), exe = Path.Combine(root, "exe");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "Content.mgcb"), Header);
        MaterialAsset.SourceRootOverride = content;
        try
        {
            var set = GeneratedMaterialSet.TryRead(FakeCacheEntry(root, "00aa00aa00aa00aa", metal: false), out string problem);
            Check(set != null && set.Slug == "test_stone" && set.Id == "00aa00aa00aa00aa" && !set.HasMetallic && set.Tiling == 0.667 &&
                  set.Family == "stone" && set.Warnings.Count == 1 && set.Warnings[0].Code == "roughness_flat", "a cache entry reads back: " + problem);
            string incomplete = FakeCacheEntry(root, "00bb00bb00bb00bb", metal: false);
            File.Delete(Path.Combine(incomplete, "test_stone_AO.png"));
            Check(GeneratedMaterialSet.TryRead(incomplete, out problem) == null && problem.Contains("AO"), "a cache entry without a map is refused");
            Check(GeneratedMaterialSet.TryRead(Path.Combine(root, "nothing"), out _) == null, "a missing folder is refused");

            var importer = new GeneratedMaterialImporter(content, exe, FakeBuild);
            string mat = importer.Import(FakeCacheEntry(root, "11cc11cc11cc11cc", metal: false));
            Check(mat == "Materials/Generated/test_stone_11cc11cc11cc11cc.mat" && MaterialAsset.Exists(mat), "the import writes Materials/Generated/<slug>_<id>.mat");
            string folder = Path.Combine(content, "Materials", "Generated", "test_stone_11cc11cc11cc11cc");
            Check(File.Exists(Path.Combine(folder, "test_stone_BaseColor.png")) && File.Exists(Path.Combine(folder, "texgen.json")), "the maps and texgen.json are copied under Content");
            Check(File.Exists(Path.Combine(exe, "Materials", "Generated", "test_stone_11cc11cc11cc11cc", "test_stone_Normal.xnb")), "the built .xnb files are copied next to the executable");

            string manifest = File.ReadAllText(Path.Combine(content, "Content.mgcb"));
            Check(CountOf(manifest, "#begin Materials/Generated/test_stone_11cc11cc11cc11cc/") == 5 && !manifest.Contains("texgen.json") &&
                  CountOf(manifest, "/processorParam:ColorKeyEnabled=False") == 5 && CountOf(manifest, "/processorParam:PremultiplyAlpha=False") == 5,
                "five maps are registered without colour key or premultiplied alpha (no Metallic, no json)");

            var m = MaterialAsset.Load(mat)!.CreateSettings();
            string prefix = "Materials/Generated/test_stone_11cc11cc11cc11cc/test_stone_";
            Check(m.BaseColorTexture == prefix + "BaseColor.png" && m.NormalTexture == prefix + "Normal.png" && m.RoughnessTexture == prefix + "Roughness.png" &&
                  m.AmbientOcclusionTexture == prefix + "AO.png" && m.DisplacementTexture == prefix + "Height.png", "the .mat fills the five texture slots (paths with extension)");
            Check(m.MetallicTexture == "" && m.MaskTexture == "" && m.Metallic == 0f && m.Roughness == 1f, "no Metallic map: the slot is 'none' and Metallic is 0; Mask is 'none'");
            Check(Math.Abs(m.TilingU - 0.667f) < 1e-4 && Math.Abs(m.TilingV - 0.667f) < 1e-4 && m.MaterialType == MaterialEffect.MaterialTypes.Basic, "tiling and shader type come from the suggestion");

            importer.Import(FakeCacheEntry(root, "11cc11cc11cc11cc", metal: false));
            Check(CountOf(File.ReadAllText(Path.Combine(content, "Content.mgcb")), "#begin Materials/Generated/test_stone_11cc11cc11cc11cc/") == 5, "importing the same id again does not duplicate entries");

            string metalMat = importer.Import(FakeCacheEntry(root, "22dd22dd22dd22dd", metal: true));
            var metalSettings = MaterialAsset.Load(metalMat)!.CreateSettings();
            Check(metalSettings.MetallicTexture!.EndsWith("test_stone_Metallic.png") && CountOf(File.ReadAllText(Path.Combine(content, "Content.mgcb")), "#begin Materials/Generated/test_stone_22dd22dd22dd22dd/") == 6,
                "a set with metal also registers and assigns its Metallic map");
            Check(MaterialAsset.Exists(mat) && mat != metalMat, "two seeds give two independent .mat files");

            // A failing build rolls the manifest and the copied folder back.
            string before = File.ReadAllText(Path.Combine(content, "Content.mgcb"));
            var failing = new GeneratedMaterialImporter(content, exe, _ => throw new InvalidOperationException("mgcb failed"));
            bool threw = false;
            try { failing.Import(FakeCacheEntry(root, "33ee33ee33ee33ee", metal: false)); } catch (InvalidOperationException) { threw = true; }
            Check(threw && File.ReadAllText(Path.Combine(content, "Content.mgcb")) == before &&
                  !Directory.Exists(Path.Combine(content, "Materials", "Generated", "test_stone_33ee33ee33ee33ee")) &&
                  !MaterialAsset.Exists("Materials/Generated/test_stone_33ee33ee33ee33ee.mat"), "a failed mgcb build leaves no entries, folder or material behind");
        }
        finally
        {
            MaterialAsset.SourceRootOverride = null;
            try { Directory.Delete(root, recursive: true); } catch { /* temp folder */ }
        }
    }

    // ---- B2: runner (needs the venv; skipped without it) ----

    private static void RunRunner()
    {
        var real = TexGenSettings.Load();
        if (!File.Exists(real.PythonExe))
        {
            Console.WriteLine("SKIP: texgen runner checks (no Python at " + real.PythonExe + ")");
            return;
        }
        string cache = NewTempFolder();
        try
        {
            var settings = new TexGenSettings { RepoRoot = real.RepoRoot, PythonExe = real.PythonExe, TexGenRoot = real.TexGenRoot, CacheRoot = cache, TimeoutSeconds = 120 };
            var runner = new TexGenRunner(settings);
            var p = new TexGenParams { Prompt = "test stone", Seed = 5, Resolution = 128 };
            var events = new List<TexGenEvent>();
            var result = runner.GenerateAsync(p, new SyncProgress(events.Add)).GetAwaiter().GetResult();
            Check(result.Success && !result.FromCache && result.Id == MaterialGenerationId.Compute(p) && result.CacheDir == Path.Combine(cache, result.Id),
                "the runner generates into cacheRoot/<id>: " + result.ErrorMessage);
            Check(events.Any(e => e.Kind == TexGenEventKind.Progress && e.Stage == "write") && events.Any(e => e.Kind == TexGenEventKind.Done), "progress and done events are reported");
            var set = GeneratedMaterialSet.TryRead(result.CacheDir, out string problem);
            Check(set != null && set.Id == result.Id && set.Slug == "test_stone", "the output is a complete set: " + problem);
            var again = runner.GenerateAsync(p).GetAwaiter().GetResult();
            Check(again.Success && again.FromCache, "a valid cache entry is returned without running Python");

            var bad = runner.GenerateAsync(new TexGenParams { Prompt = "x", Seed = 1, Resolution = 128, Backend = "sdxl_classic" }).GetAwaiter().GetResult();
            Check(!bad.Success && bad.ExitCode == 3 && !string.IsNullOrEmpty(bad.ErrorCode), "an unavailable backend fails with exit 3 and a code: " + bad.ErrorCode);
            var noPhoto = runner.GenerateAsync(new TexGenParams { Prompt = "x", Seed = 1, Resolution = 128, Backend = "classic" }).GetAwaiter().GetResult();
            Check(!noPhoto.Success && noPhoto.ExitCode == 2, "a classic request without a photo fails with exit 2");

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var cancelled = runner.GenerateAsync(new TexGenParams { Prompt = "never", Seed = 9, Resolution = 128 }, null, cts.Token).GetAwaiter().GetResult();
            Check(!cancelled.Success && cancelled.Cancelled, "cancelling kills the process and reports Cancelled");

            var missing = new TexGenRunner(new TexGenSettings { RepoRoot = real.RepoRoot, PythonExe = Path.Combine(cache, "nope.exe"), TexGenRoot = real.TexGenRoot, CacheRoot = cache });
            Check(missing.GenerateAsync(new TexGenParams { Prompt = "y", Seed = 2, Resolution = 128 }).GetAwaiter().GetResult().ErrorCode == "python_missing", "a missing Python gives python_missing");
        }
        finally { try { Directory.Delete(cache, recursive: true); } catch { /* temp folder */ } }
    }

    private sealed class SyncProgress : IProgress<TexGenEvent>
    {
        private readonly Action<TexGenEvent> _add;
        public SyncProgress(Action<TexGenEvent> add) => _add = add;
        public void Report(TexGenEvent value) { lock (this) _add(value); }
    }

    // ---- B4: bridge ----

    public static void RunBridge(EditorBridge bridge, Action drain)
    {
        string root = NewTempFolder();
        string content = Path.Combine(root, "Content");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "Content.mgcb"), Header);
        MaterialAsset.SourceRootOverride = content;
        var previous = bridge.GeneratedMaterialImporterFactory;
        bridge.GeneratedMaterialImporterFactory = () => new GeneratedMaterialImporter(content, Path.Combine(root, "exe"), FakeBuild);
        try
        {
            string dir = FakeCacheEntry(root, "44ff44ff44ff44ff", metal: false);
            string? path = null, error = "not called";
            bridge.EnqueueImportGeneratedMaterial(dir, (m, e) => { path = m; error = e; });
            Check(path == null && error == "not called", "the import waits for the game thread");
            drain();
            Check(path == "Materials/Generated/test_stone_44ff44ff44ff44ff.mat" && error == null && MaterialAsset.Exists(path), "the bridge imports a generated material on the game thread");
            bridge.EnqueueImportGeneratedMaterial(Path.Combine(root, "nothing"), (m, e) => { path = m; error = e; });
            drain();
            Check(path == null && !string.IsNullOrEmpty(error), "the bridge reports an error for a folder that is not a set");
        }
        finally
        {
            bridge.GeneratedMaterialImporterFactory = previous;
            MaterialAsset.SourceRootOverride = null;
            try { Directory.Delete(root, recursive: true); } catch { /* temp folder */ }
        }
    }
}
