using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Engine.Editor;

namespace Engine.Logic
{
    /// <summary>
    /// Process-wide crash handler shared by the standalone engine exe and Anvil.
    ///
    /// <see cref="Install"/> hooks unhandled managed exceptions (any thread), unobserved task
    /// exceptions and WinForms window-proc exceptions. A fatal crash writes a text report
    /// (exception, system info, recent <see cref="EditorBridge"/> log lines) plus a small
    /// minidump to <c>%LOCALAPPDATA%/Obsidian/Crashes</c> (temp fallback), then shows a native
    /// error dialog that can open the folder. Non-fatal reports (<see cref="ReportNonFatal"/>)
    /// write the text report only, once per distinct exception.
    ///
    /// Set <c>OBSIDIAN_NO_CRASH_DIALOG=1</c> or pass <c>--no-crash-dialog</c> to skip the dialog
    /// (automation, CI).
    /// </summary>
    public static class CrashHandler
    {
        public static readonly string CrashDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Obsidian", "Crashes");

        public static readonly string FallbackCrashDirectory = Path.Combine(
            Path.GetTempPath(), "Obsidian", "Crashes");

        /// <summary>Oldest reports beyond this count are deleted when a new one is written.</summary>
        public const int MaxKeptReports = 20;
        /// <summary>Cap on non-fatal reports per session so a per-frame error can't flood the disk.</summary>
        public const int MaxNonFatalReports = 10;
        const int LogLinesInReport = 200;

        static string _appName = "Obsidian";
        static bool _installed;
        static bool _showDialog = true;
        static int _fatalHandled;
        static Exception _lastFatal;
        static readonly object Gate = new();
        static readonly HashSet<string> NonFatalSignatures = new();
        static readonly DateTime StartTime = DateTime.Now;

        public static string AppName => _appName;

        /// <summary>
        /// Hooks the global exception events. Call first thing in Main, before any window exists
        /// (the WinForms exception mode can only be set before the first control is created).
        /// </summary>
        public static void Install(string appName, string[] args = null)
        {
            if (_installed) return;
            _installed = true;
            _appName = string.IsNullOrWhiteSpace(appName) ? "Obsidian" : appName;

            string env = Environment.GetEnvironmentVariable("OBSIDIAN_NO_CRASH_DIALOG");
            bool argOff = args != null && args.Any(a => string.Equals(a, "--no-crash-dialog", StringComparison.OrdinalIgnoreCase));
            _showDialog = Environment.UserInteractive && !argOff && !(env == "1" || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase));

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Route WinForms window-proc exceptions (MonoGame's window) to UnhandledException
            // instead of WinForms' "continue/quit" dialog, which would leave the game half-dead.
            try { System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.ThrowException); }
            catch (Exception ex) { Debug.WriteLine("CrashHandler: SetUnhandledExceptionMode failed: " + ex.Message); }
        }

        /// <summary>
        /// Reports a crash that ends the process: writes report + minidump and shows the dialog.
        /// Safe to call from a catch in Main and then rethrow; the same exception is not reported twice.
        /// Returns the report path, or null if nothing could be written.
        /// </summary>
        public static string ReportFatal(Exception ex, string context = null)
        {
            if (ex == null) return null;
            lock (Gate)
            {
                if (ReferenceEquals(ex, _lastFatal)) return null;
                _lastFatal = ex;
            }
            // Only the first fatal crash gets a dialog; a second thread dying at the same time just logs.
            bool first = Interlocked.Exchange(ref _fatalHandled, 1) == 0;

            string report = WriteReport(ex, context, fatal: true, out string dumpPath);
            SafeLog($"CrashHandler: FATAL {ex.GetType().Name} in {context ?? "unknown"} -> {report ?? "(report not written)"}");

            if (first && _showDialog) ShowDialog(ex, report, dumpPath);
            return report;
        }

        /// <summary>
        /// Reports an exception that was caught and survived (for example a frame that threw inside
        /// Anvil). Each distinct exception type + stack is written once, up to <see cref="MaxNonFatalReports"/>.
        /// </summary>
        public static string ReportNonFatal(Exception ex, string context = null)
        {
            if (ex == null) return null;
            lock (Gate)
            {
                if (NonFatalSignatures.Count >= MaxNonFatalReports) return null;
                if (!NonFatalSignatures.Add(Signature(ex))) return null;
            }
            string report = WriteReport(ex, context, fatal: false, out _);
            SafeLog($"CrashHandler: non-fatal {ex.GetType().Name} in {context ?? "unknown"} -> {report ?? "(report not written)"}");
            return report;
        }

        static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception ?? new Exception("Non-exception object thrown: " + e.ExceptionObject);
            string context = e.IsTerminating ? $"unhandled on thread '{Thread.CurrentThread.Name ?? Thread.CurrentThread.ManagedThreadId.ToString()}'" : "unhandled (non-terminating)";
            if (e.IsTerminating) ReportFatal(ex, context);
            else ReportNonFatal(ex, context);
        }

        static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            // Fire-and-forget tasks that faulted; .NET doesn't kill the process for these anymore.
            ReportNonFatal(e.Exception, "unobserved task exception");
            e.SetObserved();
        }

        static string WriteReport(Exception ex, string context, bool fatal, out string dumpPath)
        {
            dumpPath = null;
            string dir = EnsureDirectory(CrashDirectory) ?? EnsureDirectory(FallbackCrashDirectory);
            if (dir == null) return WriteLegacyCrashLog(ex);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string baseName = $"{_appName}-{(fatal ? "crash" : "error")}-{stamp}";
            string reportPath = Path.Combine(dir, baseName + ".txt");

            if (fatal)
            {
                string dmp = Path.Combine(dir, baseName + ".dmp");
                if (TryWriteMiniDump(dmp)) dumpPath = dmp;
            }

            try
            {
                File.WriteAllText(reportPath, BuildReport(ex, context, fatal, dumpPath));
            }
            catch
            {
                return WriteLegacyCrashLog(ex);
            }

            if (fatal) WriteLegacyCrashLog(ex);
            PruneOldReports(dir);
            return reportPath;
        }

        static string BuildReport(Exception ex, string context, bool fatal, string dumpPath)
        {
            var sb = new StringBuilder();
            var proc = SafeGet(Process.GetCurrentProcess);
            sb.AppendLine($"=== {_appName} {(fatal ? "crash" : "error")} report ===");
            sb.AppendLine($"Time:         {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
            sb.AppendLine($"Context:      {context ?? "unknown"}");
            sb.AppendLine($"Fatal:        {fatal}");
            sb.AppendLine($"Version:      {SafeGet(GetVersion) ?? "unknown"}");
            sb.AppendLine($"Uptime:       {DateTime.Now - StartTime:hh\\:mm\\:ss}");
            sb.AppendLine($"OS:           {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            sb.AppendLine($"Runtime:      {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})");
            sb.AppendLine($"CPU cores:    {Environment.ProcessorCount}");
            sb.AppendLine($"Working set:  {(proc != null ? SafeGet(() => proc.WorkingSet64 / (1024 * 1024)) : 0)} MB, GC heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB");
            sb.AppendLine($"Thread:       {Thread.CurrentThread.ManagedThreadId} '{Thread.CurrentThread.Name}'");
            sb.AppendLine($"PID:          {Environment.ProcessId}");
            sb.AppendLine($"Command line: {Environment.CommandLine}");
            sb.AppendLine($"Current dir:  {SafeGet(() => Environment.CurrentDirectory)}");
            sb.AppendLine($"Base dir:     {AppContext.BaseDirectory}");
            if (dumpPath != null) sb.AppendLine($"Minidump:     {dumpPath}");
            sb.AppendLine();
            sb.AppendLine("--- Exception ---");
            sb.AppendLine(ex.ToString());

            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.Flatten().InnerExceptions)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- Inner (aggregate) ---");
                    sb.AppendLine(inner.ToString());
                }
            }

            sb.AppendLine();
            sb.AppendLine($"--- Last {LogLinesInReport} log lines ---");
            try
            {
                foreach (var entry in EditorBridge.GetLogEntries().TakeLast(LogLinesInReport))
                    sb.AppendLine($"[{entry.Timestamp:HH:mm:ss.fff}] {entry.Message}");
            }
            catch (Exception logEx) { sb.AppendLine("(log unavailable: " + logEx.Message + ")"); }
            return sb.ToString();
        }

        static string GetVersion()
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(CrashHandler).Assembly;
            return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm.GetName().Version?.ToString();
        }

        static string Signature(Exception ex)
        {
            string trace = ex.StackTrace ?? string.Empty;
            return ex.GetType().FullName + "|" + trace;
        }

        static string EnsureDirectory(string dir)
        {
            try { Directory.CreateDirectory(dir); return dir; }
            catch { return null; }
        }

        // Kept from the old Program.Main behaviour: crash.log next to the exe is the first place people look.
        static string WriteLegacyCrashLog(Exception ex)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {_appName}{Environment.NewLine}{ex}");
                return path;
            }
            catch { return null; }
        }

        static void PruneOldReports(string dir)
        {
            try
            {
                var files = new DirectoryInfo(dir).GetFiles()
                    .Where(f => f.Extension == ".txt" || f.Extension == ".dmp")
                    .GroupBy(f => Path.GetFileNameWithoutExtension(f.Name))
                    .OrderByDescending(g => g.Max(f => f.LastWriteTimeUtc))
                    .Skip(MaxKeptReports);
                foreach (var group in files)
                    foreach (var f in group)
                        try { f.Delete(); } catch { }
            }
            catch { }
        }

        static void SafeLog(string msg)
        {
            try { EditorBridge.Log(msg); }
            catch { Debug.WriteLine(msg); }
        }

        static T SafeGet<T>(Func<T> get)
        {
            try { return get(); }
            catch { return default; }
        }

        static void ShowDialog(Exception ex, string reportPath, string dumpPath)
        {
            try
            {
                string text = $"{_appName} crashed and has to close.\n\n{ex.GetType().Name}: {ex.Message}\n\n";
                text += reportPath != null
                    ? $"A crash report was saved to:\n{reportPath}\n\nOpen the crash folder?"
                    : "The crash report could not be written.";
                uint flags = MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST | (reportPath != null ? MB_YESNO : MB_OK);
                int result = MessageBoxW(IntPtr.Zero, text, _appName + " crash", flags);
                if (result == IDYES && reportPath != null)
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{reportPath}\"") { UseShellExecute = true });
            }
            catch { }
        }

        static bool TryWriteMiniDump(string path)
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                // Small dump: threads, stacks, modules, handles. Enough for native call stacks without
                // dumping the whole (multi-GB) heap of a running game.
                const uint type = MiniDumpWithDataSegs | MiniDumpWithHandleData | MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules;
                bool ok = MiniDumpWriteDump(proc.Handle, (uint)proc.Id, fs.SafeFileHandle.DangerousGetHandle(), type, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (ok) return true;
            }
            catch { }
            try { File.Delete(path); } catch { }
            return false;
        }

        const uint MB_OK = 0x0, MB_YESNO = 0x4, MB_ICONERROR = 0x10, MB_SETFOREGROUND = 0x10000, MB_TOPMOST = 0x40000;
        const int IDYES = 6;
        const uint MiniDumpWithDataSegs = 0x1, MiniDumpWithHandleData = 0x4, MiniDumpWithUnloadedModules = 0x20, MiniDumpWithThreadInfo = 0x1000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("dbghelp.dll", SetLastError = true)]
        static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, IntPtr hFile, uint dumpType,
            IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
    }
}
