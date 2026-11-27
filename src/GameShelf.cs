using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FrameHub;

internal static class GameShelf
{
    internal sealed record Entry(string Name, string Location, bool Steam, string? InstallFolder = null)
    {
        public override string ToString() => Name + (Steam ? "  ·  Steam" : "  ·  Local");
    }
    internal static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionFrameHub");
    static string Config => Path.Combine(DataFolder, "games.json");
    static string ScalerGame => Path.Combine(DataFolder, "scaler-game.txt");
    static string SteamLinks => Path.Combine(DataFolder, "steam-game-exes.json");
    internal static string? LoadScalerGame() => File.Exists(ScalerGame) ? File.ReadAllText(ScalerGame) : null;
    internal static void SaveScalerGame(string path)
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(ScalerGame + ".tmp", path);
        File.Move(ScalerGame + ".tmp", ScalerGame, overwrite: true);
    }
    static Dictionary<string, string> LoadSteamLinks() => File.Exists(SteamLinks)
        ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SteamLinks)) ?? [] : [];
    internal static string? LinkedSteamExe(string id) => LoadSteamLinks().GetValueOrDefault(id);
    internal static void LinkSteamExe(string id, string path)
    {
        var links = LoadSteamLinks();
        links[id] = path;
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(SteamLinks + ".tmp", JsonSerializer.Serialize(links));
        File.Move(SteamLinks + ".tmp", SteamLinks, overwrite: true);
    }
    internal static List<Entry> Load() => File.Exists(Config)
        ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(Config)) ?? [] : [];
    internal static void Save(List<Entry> items)
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(Config + ".tmp", JsonSerializer.Serialize(items));
        File.Move(Config + ".tmp", Config, overwrite: true);
    }
    static IEnumerable<string> Lines(string file)
    {
        if (!File.Exists(file)) return [];
        try { return File.ReadAllLines(file); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
    static string? ReadValue(IEnumerable<string> lines, string key)
    {
        var match = lines.Select(line => Regex.Match(line, "^\\s*\"" + key + "\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase))
            .FirstOrDefault(x => x.Success);
        return match?.Groups[1].Value.Replace(@"\\", @"\").Replace("\\\"", "\"");
    }
    internal static Entry[] SteamGames()
    {
        if (!OperatingSystem.IsWindows()) return [];
        string? root = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam") is { } key ? ReadSteamRoot(key) : null;
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) return [];
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        foreach (var line in Lines(Path.Combine(root, "steamapps", "libraryfolders.vdf")))
        {
            var path = ReadValue([line], "path");
            if (path is not null && Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal)) paths.Add(path);
        }
        var games = new Dictionary<string, Entry>();
        foreach (var folder in paths.Select(path => Path.Combine(path, "steamapps")).Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "appmanifest_*.acf", new EnumerationOptions { IgnoreInaccessible = true }).Take(3000))
            {
                string id = Path.GetFileNameWithoutExtension(file)[12..];
                if (!uint.TryParse(id, out _) || id is "228980" or "993090") continue;
                var manifest = Lines(file);
                var name = ReadValue(manifest, "name");
                var dir = ReadValue(manifest, "installdir");
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(dir) && dir is not "." and not ".." &&
                    !dir.Contains('\\') && !dir.Contains('/') && !dir.Contains(':') && Directory.Exists(Path.Combine(folder, "common", dir)))
                    games.TryAdd(id, new Entry(name, id, true, Path.Combine(folder, "common", dir)));
            }
        }
        return games.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    internal static Entry[] Filter(IEnumerable<Entry> entries, string query, int tag) =>
        entries.Where(entry => (tag != 1 || entry.Steam) && (tag != 2 || !entry.Steam) &&
            entry.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static string? ReadSteamRoot(RegistryKey key) { using (key) return key.GetValue("SteamPath") as string; }
}
