using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Engine.Editor;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Recources
{
    /// <summary>
    /// Runtime FBX/texture importer. Mirrors the pattern from
    /// <see cref="ShaderManager"/>: append the new asset to Content.mgcb, invoke
    /// mgcb in a subprocess to build it into a .xnb, copy that .xnb next to the
    /// running executable, then load it through the live ContentManager and
    /// register it in <see cref="Assets"/> so the editor can spawn entities from it.
    ///
    /// Must be invoked on the engine/game thread — the final
    /// <c>ContentManager.Load&lt;Model&gt;</c> calls into the GraphicsDevice and
    /// uploads vertex/index buffers.
    /// </summary>
    public sealed class AssetImporter
    {
        private static readonly string[] TextureExtensions =
            { ".png", ".jpg", ".jpeg", ".tga", ".dds", ".bmp" };

        // Cross-process guard around the Content.mgcb read-modify-write.
        private static readonly Mutex MgcbEditMutex = new Mutex(false, "obsidian.mgcb.edit");

        private readonly ContentManager _content;
        private readonly GraphicsDevice _graphics;
        private readonly Assets _assets;

        private readonly string _contentSourceRoot;      // In-repo Engine/Content/
        private readonly string _contentBuiltRoot;       // Engine/Content/bin/Windows/
        private readonly string _contentExecutableRoot;  // {AppContext.BaseDirectory}/Content/
        private readonly string _mgcbExe;
        private readonly string _mgcbArgPrefix;

        public AssetImporter(ContentManager content, GraphicsDevice graphics, Assets assets)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));

            // Locate the repo's Engine/Content directory by ancestor search. We can't use
            // a simple "4 parents up" heuristic because Anvil runs from
            // Editor/Anvil/bin/... (would land at Editor/), while Engine.exe runs from
            // Engine/bin/... (lands at Engine/). Both processes need the same source
            // root — the in-repo Engine/Content/.
            _contentSourceRoot = NormalizeDir(LocateEngineContentRoot());
            _contentBuiltRoot = NormalizeDir(Path.Combine(_contentSourceRoot, "bin", "Windows"));
            _contentExecutableRoot = NormalizeDir(Path.Combine(AppContext.BaseDirectory, "Content"));

            (_mgcbExe, _mgcbArgPrefix) = LocateMgcb();

            EditorBridge.Log($"AssetImporter: src='{_contentSourceRoot}', built='{_contentBuiltRoot}', exe='{_contentExecutableRoot}', mgcb='{_mgcbExe} {_mgcbArgPrefix}'");
        }

        private static string LocateEngineContentRoot()
        {
            // Walk ancestors of the executable's base dir until we find one that contains
            // Engine/Content/Content.mgcb. That's the repo root; the Engine/Content
            // subdirectory is what mgcb operates on.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Engine", "Content", "Content.mgcb");
                if (File.Exists(candidate))
                    return Path.Combine(dir.FullName, "Engine", "Content");
                dir = dir.Parent;
            }
            // Fallback: parent-of-parent-of... of CWD, same heuristic as ShaderManager.
            // Better than throwing; the mgcb call will surface a clear error if it's wrong.
            string cwd = Directory.GetCurrentDirectory();
            string upFour = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(cwd)))) ?? cwd;
            return Path.Combine(upFour, "Content");
        }

        /// <summary>
        /// Import a model file (.fbx/.obj) and all sibling textures alongside it.
        /// Returns the registered model key (which may differ from <paramref name="requestedKey"/>
        /// when collision-deduped). The optional out list contains the content-relative
        /// paths of every texture copied.
        /// </summary>
        public string ImportFbx(string sourceModelPath, string requestedKey, out List<string> importedTextures)
        {
            importedTextures = new List<string>();

            if (string.IsNullOrEmpty(sourceModelPath) || !File.Exists(sourceModelPath))
                throw new FileNotFoundException("Source model file not found.", sourceModelPath);

            string key = SanitiseKey(string.IsNullOrEmpty(requestedKey)
                ? Path.GetFileNameWithoutExtension(sourceModelPath)
                : requestedKey);

            string ext = (Path.GetExtension(sourceModelPath) ?? ".fbx").ToLowerInvariant();
            // Treat anything other than .obj as an FBX-style model (same importer).
            string modelRelDir = $"Art/Models/{key}";
            string modelRelPath = $"{modelRelDir}/{key}{ext}";
            string modelDestAbs = Path.Combine(_contentSourceRoot, modelRelPath.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(modelDestAbs)!);

            // Copy model. If a previous import for the same key already wrote this file,
            // overwrite — we will rebuild it.
            File.Copy(sourceModelPath, modelDestAbs, overwrite: true);
            EditorBridge.Log($"AssetImporter: copied model -> '{modelDestAbs}'");

            // Texture scan: recurse the source folder and copy every image *preserving its
            // relative path*, so an embedded ref like "Gas Tank Horizontal Textures/foo.png"
            // resolves exactly where MonoGame's ModelProcessor looks (next to the model,
            // including subfolders). A flat copy would miss textures stored in subfolders
            // and break the build.
            string sourceDir = Path.GetDirectoryName(sourceModelPath) ?? string.Empty;
            var textureRelPaths = new List<string>();
            if (Directory.Exists(sourceDir))
            {
                foreach (string filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    string e = Path.GetExtension(filePath).ToLowerInvariant();
                    if (Array.IndexOf(TextureExtensions, e) < 0) continue;
                    string relFromSource = Path.GetRelativePath(sourceDir, filePath).Replace('\\', '/');
                    string texRel = $"{modelRelDir}/{relFromSource}";
                    string texDest = Path.Combine(_contentSourceRoot, texRel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(texDest)!);
                    File.Copy(filePath, texDest, overwrite: true);
                    textureRelPaths.Add(texRel);
                    importedTextures.Add(texRel);
                }
            }

            // Append .mgcb entries (under the cross-process mutex). Keep the appended text
            // so we can roll it back if the build fails.
            string mgcbPath = Path.Combine(_contentSourceRoot, "Content.mgcb");
            string appendedBlock;
            MgcbEditMutex.WaitOne();
            try
            {
                appendedBlock = AppendMgcbEntries(mgcbPath, modelRelPath, textureRelPaths, ext);
            }
            finally
            {
                MgcbEditMutex.ReleaseMutex();
            }

            // Build with mgcb. Whole-file incremental build — only the new entries get processed.
            // A failure here usually means a texture the FBX references is genuinely missing.
            // Rather than crash, roll back the .mgcb entries (so the next startup build is
            // clean), discard the copied sources, and fall back to the error model.
            try
            {
                RunMgcbBuild();
            }
            catch (Exception ex)
            {
                EditorBridge.Log($"AssetImporter: build failed for '{key}', falling back to error model: {ex.Message}");
                MgcbEditMutex.WaitOne();
                try { RemoveMgcbBlock(mgcbPath, appendedBlock); }
                finally { MgcbEditMutex.ReleaseMutex(); }
                TryDeleteDir(Path.Combine(_contentSourceRoot, modelRelDir.Replace('/', Path.DirectorySeparatorChar)));
                return RegisterErrorFallback(key);
            }

            // Copy resulting .xnb files into the executable's content directory.
            CopyXnbForAsset(modelRelPath);
            foreach (string tex in textureRelPaths) CopyXnbForAsset(tex);

            // Now load via the live ContentManager. extensionless path. If the model XNB is
            // somehow unusable, fall back to the error model instead of throwing.
            string assetPath = $"{modelRelDir}/{key}";
            try
            {
                ModelDefinition md = new ModelDefinition(_content, assetPath, _graphics, UseSDF: false);
                string registeredKey = _assets.RegisterModel(key, md);
                EditorBridge.Log($"AssetImporter: registered model key='{registeredKey}'");

                // Give the user a folder to drop convention-named textures into later.
                try
                {
                    Directory.CreateDirectory(Path.Combine(_contentSourceRoot,
                        $"Art/Models/{registeredKey}/Textures".Replace('/', Path.DirectorySeparatorChar)));
                }
                catch (Exception ex) { EditorBridge.Log($"AssetImporter: could not create Textures dir for '{registeredKey}': {ex.Message}"); }

                // If sibling textures already follow the naming convention, bind them now.
                // Otherwise the model spawns with the error material until the user drops
                // correctly-named textures onto its Textures folder.
                if (importedTextures.Count > 0)
                    ComposeMaterial(registeredKey, importedTextures);

                return registeredKey;
            }
            catch (Exception ex)
            {
                EditorBridge.Log($"AssetImporter: load failed for '{key}', falling back to error model: {ex.Message}");
                return RegisterErrorFallback(key);
            }
        }

        // Registers the error model under the requested key so the user still gets a
        // draggable placeholder. Returns the actual registered key (or null if no error
        // model is available).
        private string RegisterErrorFallback(string key)
        {
            if (_assets.ErrorModel == null)
            {
                EditorBridge.Log("AssetImporter: no error model available for fallback.");
                return null;
            }
            return _assets.RegisterModel(key, _assets.ErrorModel);
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { EditorBridge.Log($"AssetImporter: cleanup of '{dir}' failed: {ex.Message}"); }
        }

        /// <summary>
        /// Import a standalone texture. Same flow as <see cref="ImportFbx"/> minus the
        /// sibling scan. Returns the loaded Texture2D (not currently surfaced through the
        /// editor UI; kept for future material-import flow).
        /// </summary>
        public Texture2D ImportTexture(string sourcePath, string requestedKey)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Source texture file not found.", sourcePath);

            string key = SanitiseKey(string.IsNullOrEmpty(requestedKey)
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : requestedKey);
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            string relPath = $"Art/Textures/{key}{ext}";
            string destAbs = Path.Combine(_contentSourceRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
            File.Copy(sourcePath, destAbs, overwrite: true);

            string mgcbPath = Path.Combine(_contentSourceRoot, "Content.mgcb");
            MgcbEditMutex.WaitOne();
            try { AppendMgcbEntries(mgcbPath, modelRelPath: null, textureRelPaths: new List<string> { relPath }, modelExt: null); }
            finally { MgcbEditMutex.ReleaseMutex(); }

            RunMgcbBuild();
            CopyXnbForAsset(relPath);

            string assetPath = $"Art/Textures/{key}";
            return _content.Load<Texture2D>(assetPath);
        }

        // -------- Helpers --------

        private static string NormalizeDir(string path) =>
            path.Replace('\\', '/').TrimEnd('/');

        private static string SanitiseKey(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if (c == ' ' || c == '-' || c == '.') sb.Append('_');
            }
            string result = sb.ToString().Trim('_');
            return result.Length == 0 ? "model" : result;
        }

        private static (string exe, string argPrefix) LocateMgcb()
        {
            // 1. Explicit env override.
            string env = Environment.GetEnvironmentVariable("MGCB_PATH");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
                return (env, "");

            // 2. mgcb.dll in the NuGet cache. The MonoGame.Content.Builder.Task package
            //    pulls in dotnet-mgcb, but only as an MSBuild-time task — not as a
            //    globally available `dotnet mgcb` command unless a dotnet-tools manifest
            //    is restored. Running mgcb.dll directly via `dotnet exec` sidesteps the
            //    manifest entirely. Search both %NUGET_PACKAGES% and the default
            //    %USERPROFILE%\.nuget\packages location.
            string mgcbDll = FindMgcbDll();
            if (mgcbDll != null)
                return ("dotnet", "exec " + EscapeArg(mgcbDll) + " ");

            // 3. Legacy MonoGame v3.0 MSBuild install (matches the ShaderManager path).
            const string legacy = "C:/program Files (x86)/MSBuild/MonoGame/v3.0/Tools/mgcb.exe";
            if (File.Exists(legacy))
                return (legacy, "");

            // 4. Last resort: hope a `dotnet mgcb` global/manifest tool exists.
            return ("dotnet", "mgcb ");
        }

        private static string FindMgcbDll()
        {
            // NuGet looks here first when set; otherwise it falls back to ~/.nuget/packages.
            string nugetRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (string.IsNullOrEmpty(nugetRoot))
            {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                nugetRoot = Path.Combine(userHome, ".nuget", "packages");
            }

            string mgcbPkgDir = Path.Combine(nugetRoot, "dotnet-mgcb");
            if (!Directory.Exists(mgcbPkgDir)) return null;

            // Highest-version subdirectory wins (3.8.4.1, 3.8.2.1003, …). Lexicographic
            // comparison is fine for MonoGame's SemVer-shaped version segments.
            string bestVersion = null;
            string bestDll = null;
            foreach (string versionDir in Directory.EnumerateDirectories(mgcbPkgDir))
            {
                string version = Path.GetFileName(versionDir);
                // tools/<tfm>/any/mgcb.dll — tfm is net8.0 today; enumerated to be
                // tolerant of future bumps.
                string toolsRoot = Path.Combine(versionDir, "tools");
                if (!Directory.Exists(toolsRoot)) continue;
                foreach (string tfmDir in Directory.EnumerateDirectories(toolsRoot))
                {
                    string candidate = Path.Combine(tfmDir, "any", "mgcb.dll");
                    if (!File.Exists(candidate)) continue;
                    if (bestVersion == null || string.CompareOrdinal(version, bestVersion) > 0)
                    {
                        bestVersion = version;
                        bestDll = candidate;
                    }
                }
            }
            return bestDll;
        }

        private void RunMgcbBuild()
        {
            string args = _mgcbArgPrefix + "/@:" + EscapeArg("Content.mgcb")
                          + " /workingDir:" + EscapeArg(_contentSourceRoot);

            var psi = new ProcessStartInfo
            {
                FileName = _mgcbExe,
                Arguments = args,
                CreateNoWindow = true,
                WorkingDirectory = _contentSourceRoot,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            EditorBridge.Log($"AssetImporter: invoking mgcb -> '{psi.FileName} {psi.Arguments}'");
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start mgcb.");
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (!string.IsNullOrEmpty(stdout)) EditorBridge.Log("mgcb stdout: " + stdout);
            if (!string.IsNullOrEmpty(stderr)) EditorBridge.Log("mgcb stderr: " + stderr);
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"mgcb exited with code {p.ExitCode}. See log.");
        }

        private static string EscapeArg(string a) =>
            a.Contains(' ') ? "\"" + a + "\"" : a;

        private void CopyXnbForAsset(string contentRelPath)
        {
            // strip extension, append .xnb
            string noExt = StripExtension(contentRelPath);
            string xnbRel = noExt + ".xnb";
            string fromAbs = Path.Combine(_contentBuiltRoot, xnbRel.Replace('/', Path.DirectorySeparatorChar));
            string toAbs = Path.Combine(_contentExecutableRoot, xnbRel.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fromAbs))
            {
                EditorBridge.Log($"AssetImporter: built XNB missing at '{fromAbs}' (skipping copy)");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(toAbs)!);
            File.Copy(fromAbs, toAbs, overwrite: true);
            EditorBridge.Log($"AssetImporter: copied XNB -> '{toAbs}'");
        }

        private static string StripExtension(string p)
        {
            int dot = p.LastIndexOf('.');
            int slash = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
            return dot > slash ? p.Substring(0, dot) : p;
        }

        /// <summary>
        /// Appends mgcb build entries for the model and its textures (skipping any already
        /// present). Returns the exact text appended, so the caller can roll it back if the
        /// subsequent build fails — leaving a broken entry behind would break the next
        /// content build at startup.
        /// </summary>
        private static string AppendMgcbEntries(string mgcbPath, string modelRelPath,
                                              List<string> textureRelPaths, string modelExt)
        {
            string existing = File.Exists(mgcbPath) ? File.ReadAllText(mgcbPath) : "";
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(modelRelPath) && !existing.Contains($"#begin {modelRelPath}"))
            {
                sb.AppendLine();
                sb.AppendLine($"#begin {modelRelPath}");
                sb.AppendLine("/importer:FbxImporter");
                sb.AppendLine("/processor:ModelProcessor");
                sb.AppendLine("/processorParam:ColorKeyColor=0,0,0,0");
                sb.AppendLine("/processorParam:ColorKeyEnabled=True");
                sb.AppendLine("/processorParam:DefaultEffect=BasicEffect");
                sb.AppendLine("/processorParam:GenerateMipmaps=True");
                sb.AppendLine("/processorParam:GenerateTangentFrames=True");
                sb.AppendLine("/processorParam:PremultiplyTextureAlpha=True");
                sb.AppendLine("/processorParam:PremultiplyVertexColors=True");
                sb.AppendLine("/processorParam:ResizeTexturesToPowerOfTwo=False");
                sb.AppendLine("/processorParam:RotationX=0");
                sb.AppendLine("/processorParam:RotationY=0");
                sb.AppendLine("/processorParam:RotationZ=0");
                sb.AppendLine("/processorParam:Scale=1");
                sb.AppendLine("/processorParam:SwapWindingOrder=False");
                sb.AppendLine("/processorParam:TextureFormat=Compressed");
                sb.AppendLine($"/build:{modelRelPath}");
            }

            if (textureRelPaths != null)
            {
                foreach (string texRel in textureRelPaths)
                {
                    if (existing.Contains($"#begin {texRel}")) continue;
                    sb.AppendLine();
                    sb.AppendLine($"#begin {texRel}");
                    sb.AppendLine("/importer:TextureImporter");
                    sb.AppendLine("/processor:TextureProcessor");
                    sb.AppendLine("/processorParam:ColorKeyColor=255,0,255,255");
                    sb.AppendLine("/processorParam:ColorKeyEnabled=True");
                    sb.AppendLine("/processorParam:GenerateMipmaps=True");
                    sb.AppendLine("/processorParam:PremultiplyAlpha=True");
                    sb.AppendLine("/processorParam:ResizeToPowerOfTwo=False");
                    sb.AppendLine("/processorParam:MakeSquare=False");
                    sb.AppendLine("/processorParam:TextureFormat=Color");
                    sb.AppendLine($"/build:{texRel}");
                }
            }

            if (sb.Length > 0)
            {
                File.AppendAllText(mgcbPath, sb.ToString());
            }
            return sb.ToString();
        }

        /// <summary>
        /// Removes a previously-appended block (returned by <see cref="AppendMgcbEntries"/>)
        /// from Content.mgcb. Used to roll back after a failed build so the next startup
        /// content build does not choke on an entry that references missing assets.
        /// </summary>
        private static void RemoveMgcbBlock(string mgcbPath, string appendedBlock)
        {
            if (string.IsNullOrEmpty(appendedBlock) || !File.Exists(mgcbPath)) return;
            string existing = File.ReadAllText(mgcbPath);
            int idx = existing.LastIndexOf(appendedBlock, StringComparison.Ordinal);
            if (idx < 0) return;
            File.WriteAllText(mgcbPath, existing.Remove(idx, appendedBlock.Length));
        }

        // -------- Convention-based texture binding --------

        /// <summary>The material slot a texture file maps to, decided by its name suffix.</summary>
        public enum MaterialUsage { None, Albedo, Normal, Roughness, Metallic, Mask, Displacement }

        /// <summary>
        /// Classify a texture by the suffix after the last underscore in its file stem
        /// (case-insensitive). e.g. "Crate_BaseColor" → Albedo, "Crate_Normal" → Normal.
        /// </summary>
        public static MaterialUsage ClassifyTexture(string fileStem)
        {
            if (string.IsNullOrEmpty(fileStem)) return MaterialUsage.None;
            int us = fileStem.LastIndexOf('_');
            string suffix = (us >= 0 ? fileStem.Substring(us + 1) : fileStem).ToLowerInvariant();
            switch (suffix)
            {
                case "basecolor":
                case "albedo":
                case "diffuse":
                    return MaterialUsage.Albedo;
                case "normal":
                    return MaterialUsage.Normal;
                case "roughness":
                    return MaterialUsage.Roughness;
                case "metallic":
                    return MaterialUsage.Metallic;
                case "mask":
                case "opacity":
                    return MaterialUsage.Mask;
                case "height":
                case "displacement":
                    return MaterialUsage.Displacement;
                default:
                    return MaterialUsage.None;
            }
        }

        /// <summary>
        /// Loads already-built content textures (paths relative to the Content root, with
        /// extension), classifies them by name, and — if an albedo is present — builds a
        /// single <see cref="MaterialEffect"/> and registers it for <paramref name="modelKey"/>.
        /// Returns the material, or null when no albedo matched (caller keeps the error
        /// material so the model is still visibly textured).
        /// </summary>
        private MaterialEffect ComposeMaterial(string modelKey, IReadOnlyList<string> contentRelTexturePaths)
        {
            Texture2D albedo = null, normal = null, rough = null, metallic = null, mask = null, disp = null;
            foreach (string rel in contentRelTexturePaths)
            {
                MaterialUsage usage = ClassifyTexture(Path.GetFileNameWithoutExtension(rel));
                if (usage == MaterialUsage.None) continue;

                Texture2D tex;
                try { tex = _content.Load<Texture2D>(StripExtension(rel)); }
                catch (Exception ex) { EditorBridge.Log($"AssetImporter: load texture '{rel}' failed: {ex.Message}"); continue; }

                switch (usage)
                {
                    case MaterialUsage.Albedo: albedo = tex; break;
                    case MaterialUsage.Normal: normal = tex; break;
                    case MaterialUsage.Roughness: rough = tex; break;
                    case MaterialUsage.Metallic: metallic = tex; break;
                    case MaterialUsage.Mask: mask = tex; break;
                    case MaterialUsage.Displacement: disp = tex; break;
                }
            }

            if (albedo == null)
            {
                EditorBridge.Log($"AssetImporter: no albedo (_BaseColor) texture for '{modelKey}'; keeping error material.");
                return null;
            }

            MaterialEffect material = _assets.MakeMaterial(Color.White, roughness: 1f, metallic: 0f,
                albedoMap: albedo, normalMap: normal, roughnessMap: rough, metallicMap: metallic,
                mask: mask, displacementMap: disp);
            _assets.RegisterMaterial(modelKey, material);
            EditorBridge.Log($"AssetImporter: bound material for '{modelKey}' " +
                $"(albedo{(normal != null ? "+n" : "")}{(rough != null ? "+r" : "")}{(metallic != null ? "+m" : "")}{(mask != null ? "+mask" : "")}{(disp != null ? "+disp" : "")})");
            return material;
        }

        /// <summary>
        /// Copy dropped texture files into the model's <c>Art/Models/{key}/Textures/</c>
        /// folder, build them via mgcb, and bind a convention-based material to the model.
        /// Returns true when an albedo-bearing material was bound; false otherwise (caller
        /// keeps the error material). Used by the "drop textures onto a model" editor flow.
        /// </summary>
        public bool BindTextures(string modelKey, IReadOnlyList<string> sourcePaths, out MaterialEffect material)
        {
            material = null;
            if (string.IsNullOrEmpty(modelKey) || sourcePaths == null || sourcePaths.Count == 0) return false;

            string texRelDir = $"Art/Models/{modelKey}/Textures";
            string texDestDir = Path.Combine(_contentSourceRoot, texRelDir.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(texDestDir);

            var copiedRel = new List<string>();
            foreach (string src in sourcePaths)
            {
                if (string.IsNullOrEmpty(src) || !File.Exists(src)) continue;
                string ext = Path.GetExtension(src).ToLowerInvariant();
                if (Array.IndexOf(TextureExtensions, ext) < 0) continue;

                string fileName = Path.GetFileName(src);
                string texRel = $"{texRelDir}/{fileName}";
                string dest = Path.Combine(_contentSourceRoot, texRel.Replace('/', Path.DirectorySeparatorChar));
                try { File.Copy(src, dest, overwrite: true); }
                catch (Exception ex) { EditorBridge.Log($"AssetImporter: copy texture '{src}' failed: {ex.Message}"); continue; }
                copiedRel.Add(texRel);
            }
            if (copiedRel.Count == 0) return false;

            string mgcbPath = Path.Combine(_contentSourceRoot, "Content.mgcb");
            string appendedBlock;
            MgcbEditMutex.WaitOne();
            try { appendedBlock = AppendMgcbEntries(mgcbPath, modelRelPath: null, textureRelPaths: copiedRel, modelExt: null); }
            finally { MgcbEditMutex.ReleaseMutex(); }

            try { RunMgcbBuild(); }
            catch (Exception ex)
            {
                EditorBridge.Log($"AssetImporter: texture build failed for '{modelKey}': {ex.Message}");
                MgcbEditMutex.WaitOne();
                try { RemoveMgcbBlock(mgcbPath, appendedBlock); }
                finally { MgcbEditMutex.ReleaseMutex(); }
                return false;
            }

            foreach (string rel in copiedRel) CopyXnbForAsset(rel);

            material = ComposeMaterial(modelKey, copiedRel);
            return material != null;
        }

        /// <summary>
        /// Permanently remove a runtime-imported model's content: its Content.mgcb build
        /// entries (model + Textures/* under Art/Models/{key}) and the copied source/built/
        /// executable folders. Used by the Assets-panel "Delete" with disk removal.
        /// </summary>
        public void DeleteModelContent(string modelKey)
        {
            if (string.IsNullOrEmpty(modelKey)) return;
            string modelRelDir = $"Art/Models/{modelKey}";
            char sep = Path.DirectorySeparatorChar;

            string mgcbPath = Path.Combine(_contentSourceRoot, "Content.mgcb");
            MgcbEditMutex.WaitOne();
            try { RemoveMgcbEntriesUnder(mgcbPath, modelRelDir); }
            finally { MgcbEditMutex.ReleaseMutex(); }

            TryDeleteDir(Path.Combine(_contentSourceRoot, modelRelDir.Replace('/', sep)));
            TryDeleteDir(Path.Combine(_contentBuiltRoot, modelRelDir.Replace('/', sep)));
            TryDeleteDir(Path.Combine(_contentExecutableRoot, modelRelDir.Replace('/', sep)));
            EditorBridge.Log($"AssetImporter: deleted content for model '{modelKey}'");
        }

        /// <summary>
        /// Removes every <c>#begin {relDirPrefix}/...</c> block from Content.mgcb (the model
        /// file and all its textures). A block runs from its <c>#begin</c> line to the line
        /// before the next <c>#begin</c>.
        /// </summary>
        private static void RemoveMgcbEntriesUnder(string mgcbPath, string relDirPrefix)
        {
            if (!File.Exists(mgcbPath)) return;
            string prefix = "#begin " + relDirPrefix + "/";
            string[] lines = File.ReadAllText(mgcbPath).Replace("\r\n", "\n").Split('\n');

            var sb = new StringBuilder();
            bool skipping = false;
            foreach (string line in lines)
            {
                if (line.StartsWith("#begin ", StringComparison.Ordinal))
                    skipping = line.StartsWith(prefix, StringComparison.Ordinal);
                if (!skipping) sb.Append(line).Append("\r\n");
            }
            File.WriteAllText(mgcbPath, sb.ToString());
        }

        /// <summary>
        /// Lists the texture file names currently sitting in a model's
        /// <c>Art/Models/{key}/Textures/</c> source folder. Static + lightweight so the
        /// editor UI can populate the Textures tree without constructing an importer or
        /// touching the GPU. Returns an empty list when the folder doesn't exist.
        /// </summary>
        public static IReadOnlyList<string> ListModelTextures(string modelKey)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(modelKey)) return result;
            try
            {
                string dir = Path.Combine(LocateEngineContentRoot(), "Art", "Models", modelKey, "Textures");
                if (!Directory.Exists(dir)) return result;
                foreach (string f in Directory.EnumerateFiles(dir))
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(TextureExtensions, e) >= 0)
                        result.Add(Path.GetFileName(f));
                }
            }
            catch (Exception ex) { EditorBridge.Log($"AssetImporter.ListModelTextures('{modelKey}') failed: {ex.Message}"); }
            return result;
        }
    }
}
