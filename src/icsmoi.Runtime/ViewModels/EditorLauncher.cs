using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace icsmoi.Runtime.ViewModels;

/// <summary>
/// Opens the profile Editor (a separate process) on a profile file. One Editor per profile: asking for a
/// profile that is already open in a running Editor brings that window forward instead of starting a
/// second editor on the same file. Editors are left running when the Runtime exits — they may hold
/// unsaved work.
/// </summary>
public sealed class EditorLauncher
{
    private const string EditorExe = "icsmoi.Editor.exe";
    private const int SwRestore = 9;

    private readonly Dictionary<string, Process> _editors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opens (or focuses) the Editor on <paramref name="profilePath"/>. Returns an error message, or <c>null</c> on success.</summary>
    public string? Open(string profilePath)
    {
        var key = Path.GetFullPath(profilePath);

        if (_editors.TryGetValue(key, out var running))
        {
            if (!running.HasExited)
            {
                Focus(running);
                return null;
            }
            _editors.Remove(key);
            running.Dispose();
        }

        var exe = FindEditor();
        if (exe is null) return $"Couldn't find {EditorExe} next to the Runtime.";

        try
        {
            var startInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            };
            startInfo.ArgumentList.Add("--profile");
            startInfo.ArgumentList.Add(key);

            var process = Process.Start(startInfo);
            if (process is not null) _editors[key] = process;
            return null;
        }
        catch (Exception ex)
        {
            return $"Couldn't start the Editor: {ex.Message}";
        }
    }

    // The build copies the Editor into <Runtime folder>\Editor. Running from a source tree without that
    // copy, fall back to the sibling project's build output (same configuration and framework folder).
    private static string? FindEditor()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var bundled = Path.Combine(baseDir, "Editor", EditorExe);
        if (File.Exists(bundled)) return bundled;

        var framework = Path.GetFileName(baseDir);
        var configuration = Path.GetFileName(Path.GetDirectoryName(baseDir) ?? "");
        var sibling = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "icsmoi.Editor", "bin", configuration, framework, EditorExe));
        return File.Exists(sibling) ? sibling : null;
    }

    private static void Focus(Process process)
    {
        process.Refresh();
        var handle = process.MainWindowHandle;
        if (handle == IntPtr.Zero) return;

        if (IsIconic(handle)) ShowWindow(handle, SwRestore);
        SetForegroundWindow(handle);
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
}
