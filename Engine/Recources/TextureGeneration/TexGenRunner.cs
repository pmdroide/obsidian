using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Engine.Editor;

namespace Engine.Recources
{
    public sealed class TexGenResult
    {
        public bool Success { get; init; }
        /// <summary>The finished folder (a cache entry), when successful.</summary>
        public string CacheDir { get; init; }
        public string Id { get; init; }
        /// <summary>True when the cache already had this id and Python didn't run.</summary>
        public bool FromCache { get; init; }
        public bool Cancelled { get; init; }
        public string ErrorCode { get; init; }
        public string ErrorMessage { get; init; }
        public int ExitCode { get; init; }
        /// <summary>Validation warnings of this run (or stored in the cache entry).</summary>
        public IReadOnlyList<TexGenEvent> Warnings { get; init; } = Array.Empty<TexGenEvent>();
    }

    /// <summary>
    /// Runs the local Python generator (<c>python -m texgen generate</c>) off the game thread and reads its stdout
    /// protocol. The finished folder is <c>cacheRoot/&lt;id&gt;</c>; a valid existing one is returned without running Python.
    /// </summary>
    public sealed class TexGenRunner
    {
        private readonly TexGenSettings _settings;

        public TexGenRunner(TexGenSettings settings = null) => _settings = settings ?? TexGenSettings.Load();

        public TexGenSettings Settings => _settings;

        /// <summary>The cache folder this request lands in (fills in the input hash first).</summary>
        public string CacheDirFor(TexGenParams p, out string id)
        {
            if (!string.IsNullOrEmpty(p.InputRgb) && p.InputRgbSha256 == null && File.Exists(p.InputRgb))
                p.InputRgbSha256 = MaterialGenerationId.HashFile(p.InputRgb);
            id = MaterialGenerationId.Compute(p);
            return Path.Combine(_settings.CacheRoot, id);
        }

        public async Task<TexGenResult> GenerateAsync(TexGenParams p, IProgress<TexGenEvent> progress = null,
                                                      CancellationToken cancellation = default)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            string dir = CacheDirFor(p, out string id);

            var cached = GeneratedMaterialSet.TryRead(dir, out _);
            if (cached != null)
                return new TexGenResult { Success = true, CacheDir = dir, Id = id, FromCache = true, Warnings = cached.Warnings };

            if (!File.Exists(_settings.PythonExe))
                return Fail(id, "python_missing", "Python was not found at " + _settings.PythonExe +
                    " (create Tools/TexGen/.venv, see Docs/Research/AiTextures/ENVIRONMENT.md)", 3);

            var psi = new ProcessStartInfo
            {
                FileName = _settings.PythonExe, WorkingDirectory = _settings.TexGenRoot, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string arg in Arguments(p, dir)) psi.ArgumentList.Add(arg);
            psi.Environment["HF_HUB_OFFLINE"] = "1";
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUTF8"] = "1";

            var warnings = new List<TexGenEvent>();
            TexGenEvent done = null, error = null;
            var gate = new object();
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                var ev = TexGenProtocol.Parse(e.Data);
                lock (gate)
                {
                    if (ev.Kind == TexGenEventKind.Warning) warnings.Add(ev);
                    else if (ev.Kind == TexGenEventKind.Done) done = ev;
                    else if (ev.Kind == TexGenEventKind.Error) error = ev;
                }
                if (ev.Kind != TexGenEventKind.Unknown) progress?.Report(ev);
                else EditorBridge.Log("texgen: " + e.Data);
            };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) EditorBridge.Log("texgen: " + e.Data); };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
            try
            {
                if (!process.Start()) return Fail(id, "start_failed", "the generator did not start", 1);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                return Fail(id, "start_failed", "the generator did not start: " + e.Message, 1);
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already gone */ }
                bool userCancelled = cancellation.IsCancellationRequested;
                return new TexGenResult
                {
                    Cancelled = userCancelled, Id = id, ExitCode = -1,
                    ErrorCode = userCancelled ? "cancelled" : "timeout",
                    ErrorMessage = userCancelled ? "cancelled" : $"the generator took longer than {_settings.TimeoutSeconds} s",
                };
            }
            process.WaitForExit(); // flushes the asynchronous output handlers

            TexGenEvent finalDone, finalError;
            lock (gate) { finalDone = done; finalError = error; }
            if (process.ExitCode != 0)
                return Fail(id, finalError?.Code ?? "internal_error",
                    finalError?.Message ?? $"the generator exited with code {process.ExitCode}", process.ExitCode, warnings);

            var set = GeneratedMaterialSet.TryRead(dir, out string problem);
            if (finalDone == null || set == null)
                return Fail(id, "output_invalid", problem ?? "the generator reported no result", 1, warnings);
            return new TexGenResult { Success = true, CacheDir = dir, Id = id, Warnings = warnings };
        }

        /// <summary>Runs a short texgen command (<c>check</c>, <c>styles</c>) and returns its done/error event.</summary>
        public async Task<TexGenEvent> QueryAsync(string[] args, CancellationToken cancellation = default)
        {
            if (!File.Exists(_settings.PythonExe))
                return new TexGenEvent { Kind = TexGenEventKind.Error, Code = "python_missing", Message = "Python was not found at " + _settings.PythonExe };
            var psi = new ProcessStartInfo
            {
                FileName = _settings.PythonExe, WorkingDirectory = _settings.TexGenRoot, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("texgen");
            foreach (string arg in args) psi.ArgumentList.Add(arg);
            psi.Environment["HF_HUB_OFFLINE"] = "1";
            psi.Environment["PYTHONUTF8"] = "1";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                using var process = Process.Start(psi) ?? throw new InvalidOperationException("the generator did not start");
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    throw;
                }
                TexGenEvent last = null;
                foreach (string line in (await output.ConfigureAwait(false)).Split('\n'))
                {
                    var ev = TexGenProtocol.Parse(line.Trim());
                    if (ev.Kind == TexGenEventKind.Done || ev.Kind == TexGenEventKind.Error) last = ev;
                }
                return last ?? new TexGenEvent { Kind = TexGenEventKind.Error, Code = "internal_error", Message = "the generator printed no result" };
            }
            catch (OperationCanceledException) { return new TexGenEvent { Kind = TexGenEventKind.Error, Code = "cancelled", Message = "cancelled or timed out" }; }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                return new TexGenEvent { Kind = TexGenEventKind.Error, Code = "start_failed", Message = e.Message };
            }
        }

        /// <summary>Which of the backends can run here (<c>texgen check</c> each); the value is null when it can, else the reason.</summary>
        public async Task<Dictionary<string, string>> CheckBackendsAsync(IEnumerable<string> backends, CancellationToken cancellation = default)
        {
            var result = new Dictionary<string, string>();
            foreach (string backend in backends)
            {
                var ev = await QueryAsync(new[] { "check", "--backend", backend }, cancellation).ConfigureAwait(false);
                result[backend] = ev.Kind == TexGenEventKind.Done ? null : (ev.Code + ": " + ev.Message);
            }
            return result;
        }

        /// <summary>Style profiles and their families (<c>texgen styles</c>); empty when the generator can't run.</summary>
        public async Task<IReadOnlyList<KeyValuePair<string, string[]>>> ListStylesAsync(CancellationToken cancellation = default)
        {
            var list = new List<KeyValuePair<string, string[]>>();
            var ev = await QueryAsync(new[] { "styles" }, cancellation).ConfigureAwait(false);
            if (ev.Kind != TexGenEventKind.Done || ev.Raw == null) return list;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(ev.Raw);
                foreach (var style in doc.RootElement.GetProperty("styles").EnumerateArray())
                {
                    var families = new List<string>();
                    foreach (var f in style.GetProperty("families").EnumerateArray()) families.Add(f.GetString());
                    list.Add(new KeyValuePair<string, string[]>(style.GetProperty("name").GetString(), families.ToArray()));
                }
            }
            catch (Exception e) when (e is System.Text.Json.JsonException || e is KeyNotFoundException || e is InvalidOperationException) { }
            return list;
        }

        internal static IEnumerable<string> Arguments(TexGenParams p, string outDir)
        {
            yield return "-m";
            yield return "texgen";
            yield return "generate";
            yield return "--prompt"; yield return (p.Prompt ?? "").Trim();
            yield return "--seed"; yield return p.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            yield return "--out"; yield return outDir;
            yield return "--resolution"; yield return p.Resolution.ToString(System.Globalization.CultureInfo.InvariantCulture);
            yield return "--backend"; yield return p.Backend;
            yield return "--style"; yield return p.Style;
            yield return "--family"; yield return p.Family;
            yield return "--normal-convention"; yield return p.NormalConvention;
            yield return "--pipeline-version"; yield return MaterialGenerationId.PipelineVersion;
            if (!string.IsNullOrWhiteSpace(p.NegativePrompt)) { yield return "--negative-prompt"; yield return p.NegativePrompt.Trim(); }
            if (!string.IsNullOrEmpty(p.InputRgb)) { yield return "--input-rgb"; yield return p.InputRgb; }
            if (p.ForceDielectric) yield return "--force-dielectric";
        }

        private static TexGenResult Fail(string id, string code, string message, int exitCode, IReadOnlyList<TexGenEvent> warnings = null) =>
            new TexGenResult { Id = id, ErrorCode = code, ErrorMessage = message, ExitCode = exitCode, Warnings = warnings ?? Array.Empty<TexGenEvent>() };
    }
}
