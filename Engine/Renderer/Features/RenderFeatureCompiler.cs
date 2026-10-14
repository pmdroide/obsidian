using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Engine.Recources;

namespace Engine.Renderer.Features
{
    /// <summary>
    /// Compiles one render feature shader with mgcb (the same tool the content build uses), outside
    /// Content.mgcb: <c>RenderFeatures/X.fx</c> becomes <c>RenderFeatures/X.xnb</c> next to it, replaced only
    /// when the compile succeeds. The .xnb is a loose file the build copies next to the game, so a shader
    /// with errors never breaks the engine's content build and the game ships the last good version.
    /// Runs a process: call it off the game thread.
    /// </summary>
    internal static class RenderFeatureCompiler
    {
        public sealed class Result
        {
            public bool Success;
            /// <summary>The compiler's error lines (or the whole output when none are recognised).</summary>
            public string Error;
        }

        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

        /// <param name="sourceContentRoot">Absolute source <c>Engine/Content</c> folder.</param>
        /// <param name="shaderRelPath">Content-relative .fx path.</param>
        public static Result Compile(string sourceContentRoot, string shaderRelPath)
        {
            string sourceFile = Path.Combine(sourceContentRoot, shaderRelPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sourceFile))
                return new Result { Error = "Shader file not found: " + shaderRelPath };

            string temp = Path.Combine(Path.GetTempPath(), "obsidian-render-features", Guid.NewGuid().ToString("N"));
            try
            {
                string outputDir = Path.Combine(temp, "bin");
                string intermediateDir = Path.Combine(temp, "obj");
                var (exe, prefix) = AssetImporter.LocateMgcb();
                string args = prefix
                              + "/platform:Windows /profile:HiDef"
                              + " /workingDir:" + AssetImporter.EscapeArg(sourceContentRoot)
                              + " /outputDir:" + AssetImporter.EscapeArg(outputDir)
                              + " /intermediateDir:" + AssetImporter.EscapeArg(intermediateDir)
                              + " /importer:EffectImporter /processor:EffectProcessor /processorParam:DebugMode=Auto"
                              + " /build:" + AssetImporter.EscapeArg(shaderRelPath);

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    CreateNoWindow = true,
                    WorkingDirectory = sourceContentRoot,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

                var output = new StringBuilder();
                using var process = new Process { StartInfo = psi };
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                if (!process.Start()) return new Result { Error = "Could not start mgcb." };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
                {
                    try { process.Kill(true); } catch { }
                    return new Result { Error = "mgcb timed out." };
                }
                process.WaitForExit();

                string log;
                lock (output) log = output.ToString();
                string built = Path.Combine(outputDir, Path.ChangeExtension(shaderRelPath, ".xnb").Replace('/', Path.DirectorySeparatorChar));
                if (process.ExitCode != 0 || !File.Exists(built))
                    return new Result { Error = ExtractErrors(log, sourceContentRoot) };

                File.Copy(built, CompiledPath(sourceContentRoot, shaderRelPath), overwrite: true);
                return new Result { Success = true };
            }
            catch (Exception ex)
            {
                return new Result { Error = "mgcb failed: " + ex.Message };
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>Absolute path of the compiled .xnb for a Content-relative .fx under <paramref name="contentRoot"/>.</summary>
        public static string CompiledPath(string contentRoot, string shaderRelPath) =>
            Path.Combine(contentRoot, Path.ChangeExtension(shaderRelPath, ".xnb").Replace('/', Path.DirectorySeparatorChar));

        // "C:/.../Engine/Content/RenderFeatures/X.fx(12,5): error X3004: ..." -> "RenderFeatures/X.fx(12,5): error X3004: ..."
        private static string ExtractErrors(string log, string contentRoot)
        {
            string root = contentRoot.Replace('\\', '/').TrimEnd('/') + "/";
            var lines = log.Replace("\r", "").Split('\n')
                .Where(l => l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(l => l.Replace('\\', '/').Replace(root, "", StringComparison.OrdinalIgnoreCase).Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("Build ", StringComparison.Ordinal))
                .Distinct()
                .ToList();
            string text = lines.Count > 0 ? string.Join("\n", lines) : log.Trim();
            return string.IsNullOrEmpty(text) ? "mgcb failed without output." : text;
        }
    }
}
