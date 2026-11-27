using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FrameHub;

internal static class LegionSpace
{
    internal const string Support = "https://support.lenovo.com/us/en/solutions/ht517560";
    internal const string Companion = "https://github.com/Valkirie/HandheldCompanion/releases";
    internal const string Guide = "https://github.com/Valkirie/HandheldCompanion/wiki/Legion-Go";
    static IEnumerable<string> Roots() =>
        new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(x => Environment.GetFolderPath(x)).Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => Path.Combine(x, "Lenovo"));

    static bool IsLenovoUi(string path) =>
        Path.GetFileName(path).Equals("LegionSpace.exe", StringComparison.OrdinalIgnoreCase) &&
        Roots().Any(root => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(Path.GetRelativePath(root, path), @"^Legion\s*Space(?=$|[\\\s])", RegexOptions.IgnoreCase));

    internal static void Launch()
    {
        if (StartShortcut(@"^(Lenovo\s+)?Legion\s*Space$")) return;
        foreach (var root in Roots().Where(Directory.Exists))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            var executable = Directory.EnumerateDirectories(root, "Legion*Space*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .SelectMany(folder => Directory.EnumerateFiles(folder, "LegionSpace.exe", options))
                .Where(IsLenovoUi)
                .OrderByDescending(path => Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out var version) ? version : new Version())
                .FirstOrDefault();
            if (executable is not null)
            {
                using var started = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! });
                return;
            }
        }
        throw new FileNotFoundException("The installed Legion Space shortcut wasn’t found. Open it from Start, or use the Lenovo downloads link in Settings.");
    }

    internal static void OpenCompanion()
    {
        if (!StartShortcut(@"^Handheld\s*Companion$"))
            throw new FileNotFoundException("The Handheld Companion Start shortcut wasn’t found. Use Get Handheld Companion in Settings, or open it from Start if it’s installed.");
    }

    static bool StartShortcut(string name)
    {
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            var start = Environment.GetFolderPath(folder);
            if (!Path.IsPathFullyQualified(start) || !Directory.Exists(start)) continue;
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var link in Directory.EnumerateFiles(start, "*.lnk", options).Where(path =>
                Regex.IsMatch(Path.GetFileNameWithoutExtension(path), name, RegexOptions.IgnoreCase)))
            {
                try { using var started = Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); return true; }
                catch (Win32Exception e) when (e.NativeErrorCode is 2 or 3) { }
            }
        }
        return false;
    }

    internal static async Task<string> CloseUi()
    {
        int count = 0, inaccessible = 0;
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName("LegionSpace"))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != current.SessionId ||
                        process.MainModule?.FileName is not { } path || !IsLenovoUi(path)) continue;
                    if (process.CloseMainWindow())
                    {
                        using var timeout = new CancellationTokenSource(2500);
                        try { await process.WaitForExitAsync(timeout.Token); }
                        catch (OperationCanceledException) { }
                    }
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: false);
                        using var timeout = new CancellationTokenSource(5000);
                        try { await process.WaitForExitAsync(timeout.Token); }
                        catch (OperationCanceledException) { inaccessible++; continue; }
                    }
                    count++;
                }
                catch (Win32Exception) { inaccessible++; }
                catch (InvalidOperationException) { }
            }
        }
        return inaccessible != 0 ? $"Closed {count} window process(es). Windows blocked access to {inaccessible}; no services were changed."
            : count == 0 ? "No verified Lenovo Legion Space window process was found." :
            "Legion Space UI closed. Lenovo services are unchanged and may reopen it.";
    }
}
