using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Anvil.Services;

/// <summary>
/// Opens text assets (shaders, UI XML/CSS, ...) in an external editor. Prefers
/// Visual Studio Code and falls back to Notepad.
/// </summary>
public static class ExternalTextEditor
{
    private static string? _vsCodePath;
    private static bool _vsCodeSearched;

    /// <summary>
    /// Launches the editor on <paramref name="filePath"/>. Returns false (with a reason)
    /// if the file is missing or no editor could be started.
    /// </summary>
    public static bool TryOpen(string filePath, out string error)
    {
        error = string.Empty;
        if (!File.Exists(filePath))
        {
            error = "File not found: " + filePath;
            return false;
        }

        string? code = FindVsCode();
        if (code != null && TryStart(code, filePath)) return true;
        if (TryStart("notepad.exe", filePath)) return true;

        error = "Could not start Visual Studio Code or Notepad for " + filePath;
        return false;
    }

    private static bool TryStart(string exe, string filePath)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(filePath);
            return Process.Start(psi) != null;
        }
        catch
        {
            return false;
        }
    }

    // Code.exe rather than the "code" shim on PATH: code.cmd needs a console, which
    // would flash a window.
    private static string? FindVsCode()
    {
        if (_vsCodeSearched) return _vsCodePath;
        _vsCodeSearched = true;

        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft VS Code", "Code.exe"),
        };

        // A PATH entry like "...\Microsoft VS Code\bin" sits next to Code.exe.
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(dir, "code.cmd")))
                    candidates.Add(Path.Combine(Path.GetDirectoryName(dir.TrimEnd('\\', '/')) ?? dir, "Code.exe"));
            }
            catch { /* malformed PATH entry */ }
        }

        foreach (string c in candidates)
        {
            if (File.Exists(c)) { _vsCodePath = c; break; }
        }
        return _vsCodePath;
    }
}
