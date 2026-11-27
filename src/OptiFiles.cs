using System.Security.Cryptography;
using System.Text.Json;

namespace FrameHub;

public static class OptiFiles
{
    public sealed record Receipt(string Version, Dictionary<string, string> Files);
    public const string Config = "OptiScaler.ini";
    public const string Journal = ".framehub-optiscaler-transaction";
    const string Manifest = ".framehub-optiscaler.json";
    static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    static readonly string[] Loaders = ["dxgi.dll", "winmm.dll", "d3d12.dll", "dbghelp.dll", "version.dll", "wininet.dll", "winhttp.dll"];
    public static IReadOnlyList<string> LoaderNames => Array.AsReadOnly(Loaders);

    static string SafePath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new IOException($"Invalid relative path: {relative}");
        string[] segments = relative.Split(['/', '\\']);
        if (segments.Any(x => x is "" or "." or ".." || x.EndsWith('.') || x.EndsWith(' ') ||
            x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new IOException($"Invalid path segment: {relative}");
        string path = root;
        foreach (string part in segments)
        {
            path = Path.Combine(path, part);
            CheckNotLink(path);
        }
        return path;
    }
    static void CheckNotLink(string path)
    {
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"Symbolic links and junctions are not supported here: {path}");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
    static string Root(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists) throw new IOException($"Folder no longer exists: {directory.FullName}");
        for (DirectoryInfo? part = directory; part != null; part = part.Parent) CheckNotLink(part.FullName);
        return directory.FullName;
    }
    static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    static bool IsIni(string path) => Names.Equals(path, Config);
    static bool Allowed(string path) => !path.Split(['/', '\\']).Any(x => x.StartsWith(".framehub", StringComparison.OrdinalIgnoreCase)) &&
        !new[] { ".exe", ".com", ".bat", ".cmd", ".ps1", ".msi", ".vbs", ".js", ".lnk", ".url", ".reg", ".scr", ".sys" }.Contains(Path.GetExtension(path), Names);

    public static Receipt? Read(string gameDirectory)
    {
        string root = Root(gameDirectory);
        string path = SafePath(root, Manifest);
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path)) ?? throw new IOException("Invalid OptiScaler receipt.");
        if (receipt.Files is null || receipt.Files.Count == 0 || receipt.Files.Count > 4096)
            throw new IOException("Invalid OptiScaler receipt file list.");
        var files = new Dictionary<string, string>(Names);
        foreach (var (name, hash) in receipt.Files)
        {
            _ = SafePath(root, name);
            if (!Allowed(name) || hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit) || !files.TryAdd(name, hash))
                throw new IOException("Invalid OptiScaler receipt entry.");
        }
        return receipt with { Files = files };
    }
    static IEnumerable<string> Enumerate(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder))
        {
            CheckNotLink(file);
            yield return file;
        }
        foreach (string dir in Directory.EnumerateDirectories(folder))
        {
            CheckNotLink(dir);
            foreach (string file in Enumerate(dir)) yield return file;
        }
    }

    public static Receipt? Change(string gameDirectory, string? package, string loader, string version)
    {
        string root = Root(gameDirectory);
        string transaction = SafePath(root, Journal);
        if (Directory.Exists(transaction) || File.Exists(transaction))
            throw new IOException($"An earlier transaction needs inspection first: {transaction}");
        var old = Read(root);
        if (old is null && package is null) throw new IOException("No installation owned by FrameHub.");
        var source = new Dictionary<string, string>(Names);
        if (package is not null)
        {
            if (!Loaders.Contains(loader, Names)) throw new IOException("Choose a supported loader name.");
            string sourceRoot = Root(package);
            string gamePrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            string sourcePrefix = Path.TrimEndingDirectorySeparator(sourceRoot) + Path.DirectorySeparatorChar;
            if (sourcePrefix.StartsWith(gamePrefix, StringComparison.OrdinalIgnoreCase) ||
                gamePrefix.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Extract the package into a separate folder outside the game directory.");
            if (!File.Exists(SafePath(sourceRoot, "OptiScaler.dll")))
                throw new IOException("Choose the extracted release folder that directly contains OptiScaler.dll.");
            long bytes = 0;
            foreach (string file in Enumerate(sourceRoot))
            {
                string relative = Path.GetRelativePath(sourceRoot, file);
                if (!Allowed(relative)) continue;
                string name = Names.Equals(relative, "OptiScaler.dll") ? loader : relative;
                _ = SafePath(root, name);
                if (!source.TryAdd(name, file) || source.Count > 4096 || (bytes += new FileInfo(file).Length) > 1_500_000_000)
                    throw new IOException("Package contains duplicate paths or exceeds the safety limit.");
            }
        }

        var oldFiles = old?.Files ?? new Dictionary<string, string>(Names);
        foreach (var (name, expected) in oldFiles)
        {
            string to = SafePath(root, name);
            if (IsIni(name)) continue;
            if (Directory.Exists(to) || (File.Exists(to) && !Names.Equals(expected, Hash(to))))
                throw new IOException($"A managed file was changed outside FrameHub; no files were touched: {name}");
        }
        foreach (string name in source.Keys)
        {
            string to = SafePath(root, name);
            if (Directory.Exists(to) || (File.Exists(to) && !oldFiles.ContainsKey(name) && !IsIni(name)))
                throw new IOException($"An unmanaged file or folder already exists; no files were touched: {name}");
        }
        var hashes = new Dictionary<string, string>(Names);
        Directory.CreateDirectory(transaction);
        string stage = Path.Combine(transaction, "new");
        string backup = Path.Combine(transaction, "old");
        var moved = new List<(string From, string To)>();
        var added = new List<string>();
        string receiptPath = SafePath(root, Manifest);
        try
        {
            foreach (var (name, from) in source)
            {
                string to = SafePath(root, name);
                if (IsIni(name) && File.Exists(to))
                {
                    hashes.Add(name, Hash(to));
                    continue;
                }
                string stageFile = SafePath(stage, name);
                Directory.CreateDirectory(Path.GetDirectoryName(stageFile)!);
                File.Copy(from, stageFile);
                hashes.Add(name, Hash(stageFile));
            }
            if (oldFiles.ContainsKey(Config) && File.Exists(SafePath(root, Config)) && package is not null && !hashes.ContainsKey(Config))
                hashes.Add(Config, Hash(SafePath(root, Config)));
            foreach (string name in oldFiles.Keys.Append(Manifest))
            {
                string from = SafePath(root, name);
                if (IsIni(name) || !File.Exists(from)) continue;
                string to = SafePath(backup, name);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
                moved.Add((from, to));
            }
            foreach (string name in source.Keys)
            {
                string from = SafePath(stage, name);
                if (!File.Exists(from)) continue;
                string to = SafePath(root, name);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
                added.Add(to);
            }
            Receipt? result = null;
            if (package is not null)
            {
                result = new(version, hashes);
                string stagedReceipt = SafePath(stage, Manifest);
                Directory.CreateDirectory(stage);
                File.WriteAllText(stagedReceipt, JsonSerializer.Serialize(result));
                File.Move(stagedReceipt, receiptPath);
                added.Add(receiptPath);
            }
            TryClean(transaction);
            return result;
        }
        catch
        {
            try
            {
                foreach (string path in added.AsEnumerable().Reverse()) File.Delete(path);
                foreach (var (from, to) in moved.AsEnumerable().Reverse()) File.Move(to, from);
            }
            catch (Exception e)
            {
                throw new IOException($"Automatic rollback failed. Keep and inspect the recovery folder: {transaction}", e);
            }
            TryClean(transaction);
            throw;
        }
    }

    static void TryClean(string path)
    {
        try { Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
