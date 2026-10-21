using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Engine.Components;
using Engine.Editor;

namespace Engine.Recources
{
    /// <summary>
    /// Turns a texgen cache folder into a normal material asset: copies the maps to
    /// <c>Content/Materials/Generated/&lt;slug&gt;_&lt;id&gt;/</c>, registers them in Content.mgcb (no colour key, no
    /// premultiplied alpha), builds them with mgcb, copies the .xnb next to the executable and writes
    /// <c>Materials/Generated/&lt;slug&gt;_&lt;id&gt;.mat</c>. The id is in every path because the ContentManager caches
    /// textures by name. Like <see cref="AssetImporter"/> it runs on the game thread and rolls the manifest back when mgcb fails.
    /// </summary>
    public sealed class GeneratedMaterialImporter
    {
        public const string Folder = "Materials/Generated";

        private readonly string _contentRoot;
        private readonly string _executableRoot;
        private readonly Action<string> _buildContent;

        /// <param name="contentRoot">Source Content folder; <see cref="MaterialAsset.SourceRoot"/> when null (the .mat goes there too).</param>
        /// <param name="executableRoot">Where built .xnb files are copied; the Content folder next to the exe when null.</param>
        /// <param name="buildContent">Runs mgcb on a Content folder; the real one when null (tests pass a fake).</param>
        public GeneratedMaterialImporter(string contentRoot = null, string executableRoot = null, Action<string> buildContent = null)
        {
            _contentRoot = Path.GetFullPath(contentRoot ?? MaterialAsset.SourceRoot);
            _executableRoot = Path.GetFullPath(executableRoot ?? Path.Combine(AppContext.BaseDirectory, "Content"));
            _buildContent = buildContent ?? RunMgcb;
        }

        /// <summary>Imports the set in <paramref name="cacheDir"/> and returns the Content-relative path of its .mat. Throws on failure.</summary>
        public string Import(string cacheDir)
        {
            var set = GeneratedMaterialSet.TryRead(cacheDir, out string problem)
                ?? throw new InvalidOperationException("Can't import " + cacheDir + ": " + problem);

            string name = set.Slug + "_" + set.Id;
            string folderRel = Folder + "/" + name;
            string matRel = folderRel + MaterialAsset.Extension;
            string folderAbs = Path.Combine(_contentRoot, folderRel.Replace('/', Path.DirectorySeparatorChar));
            string manifest = Path.Combine(_contentRoot, "Content.mgcb");
            bool folderExisted = Directory.Exists(folderAbs);

            Directory.CreateDirectory(folderAbs);
            var rels = new List<string>();
            foreach (var map in set.Maps)
            {
                string fileName = Path.GetFileName(map.Value);
                File.Copy(map.Value, Path.Combine(folderAbs, fileName), overwrite: true);
                rels.Add(folderRel + "/" + fileName);
            }
            File.Copy(Path.Combine(set.Directory, "texgen.json"), Path.Combine(folderAbs, "texgen.json"), overwrite: true);

            IReadOnlyList<string> added = Array.Empty<string>();
            try
            {
                added = ContentManifest.RegisterGeneratedTextures(rels, manifest);
                _buildContent(_contentRoot);
                foreach (string rel in rels) CopyXnb(rel);
            }
            catch
            {
                foreach (string rel in added) ContentManifest.Unregister(rel, manifest);
                if (!folderExisted) { try { Directory.Delete(folderAbs, recursive: true); } catch (IOException) { } }
                throw;
            }

            MaterialAsset.Write(matRel, Describe(set, folderRel));
            EditorBridge.Log($"GeneratedMaterialImporter: '{set.Prompt}' -> {matRel}");
            return matRel;
        }

        private static MaterialComponent Describe(GeneratedMaterialSet set, string folderRel)
        {
            string Map(string suffix) => folderRel + "/" + Path.GetFileName(set.Maps[suffix]);
            var material = new MaterialComponent
            {
                BaseColorTexture = Map("BaseColor"),
                NormalTexture = Map("Normal"),
                RoughnessTexture = Map("Roughness"),
                AmbientOcclusionTexture = Map("AO"),
                DisplacementTexture = Map("Height"),
                // "" = no map (null would follow the model); a set without metal uses the scalar.
                MetallicTexture = set.HasMetallic ? Map("Metallic") : "",
                MaskTexture = "",
                Metallic = 0f,
                Roughness = 1f,
                TilingU = (float)set.Tiling,
                TilingV = (float)set.Tiling,
            };
            if (Enum.TryParse(set.MaterialType, true, out MaterialEffect.MaterialTypes type)) material.MaterialType = type;
            return material;
        }

        private void CopyXnb(string contentRelPath)
        {
            string noExt = contentRelPath.Substring(0, contentRelPath.LastIndexOf('.'));
            string xnb = noExt.Replace('/', Path.DirectorySeparatorChar) + ".xnb";
            string from = Path.Combine(_contentRoot, "bin", "Windows", xnb);
            if (!File.Exists(from)) throw new FileNotFoundException("mgcb did not build " + contentRelPath, from);
            string to = Path.Combine(_executableRoot, xnb);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: true);
        }

        private static void RunMgcb(string contentRoot)
        {
            var (exe, prefix) = AssetImporter.LocateMgcb();
            var psi = new ProcessStartInfo
            {
                FileName = exe, Arguments = prefix + "/@:Content.mgcb /workingDir:" + AssetImporter.EscapeArg(contentRoot),
                WorkingDirectory = contentRoot, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start mgcb.");
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                EditorBridge.Log("mgcb: " + output + errors);
                throw new InvalidOperationException($"mgcb exited with code {process.ExitCode}. See the log.");
            }
        }
    }
}
