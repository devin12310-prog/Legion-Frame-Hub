using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace FrameHub;

internal static class ToolCatalog
{
    internal const string OptiRepo = "optiscaler/OptiScaler", MagpieRepo = "Blinue/Magpie";
    const long MaxArchive = 350_000_000, MaxExtracted = 1_500_000_000;
    static readonly HttpClient Http = CreateClient();
    internal sealed record Asset(string Tag, string Name, string Url, string Digest, long Size);
    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LegionFrameHub/0.3");
        return client;
    }
    static bool AllowedRepo(string repo) => repo is OptiRepo or MagpieRepo;
    static bool AllowedAsset(string repo, string name) => repo == OptiRepo
        ? Regex.IsMatch(name, @"^OptiScaler[._-][A-Za-z0-9._-]+\.(7z|zip)$", RegexOptions.IgnoreCase)
        : Regex.IsMatch(name, @"^Magpie-v[A-Za-z0-9._-]+-x64\.zip$", RegexOptions.IgnoreCase);
    internal static Asset Parse(string repo, string input)
    {
        if (!AllowedRepo(repo)) throw new IOException("Unknown package publisher.");
        using var json = JsonDocument.Parse(input);
        var release = json.RootElement;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v[0-9][A-Za-z0-9._-]{1,40}$") ||
            release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            throw new IOException("GitHub did not return a recognized stable release.");
        var matches = new List<Asset>();
        foreach (var item in release.GetProperty("assets").EnumerateArray())
        {
            string name = item.GetProperty("name").GetString() ?? "";
            if (!AllowedAsset(repo, name)) continue;
            string digest = item.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
            string url = item.GetProperty("browser_download_url").GetString() ?? "";
            long size = item.GetProperty("size").GetInt64();
            if (!string.Equals(url, $"https://github.com/{repo}/releases/download/{tag}/{name}", StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(digest, @"^sha256:[A-Fa-f0-9]{64}$") || size <= 0 || size > MaxArchive) continue;
            matches.Add(new Asset(tag, name, url, digest[7..].ToUpperInvariant(), size));
        }
        return matches.Count == 1 ? matches[0] : throw new IOException("No unambiguous Windows asset with an official GitHub checksum was found. Use the vendor releases page instead.");
    }

    internal static async Task<Asset> Latest(string repo)
    {
        if (!AllowedRepo(repo)) throw new IOException("Unknown package publisher.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + repo + "/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return Parse(repo, await response.Content.ReadAsStringAsync());
    }
    internal sealed class Stage : IDisposable
    {
        internal string Root { get; }
        internal string Payload { get; }
        internal Stage(string root, string payload) { Root = root; Payload = payload; }
        public void Dispose() => Clean(Root);
    }
    static void Clean(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    internal static async Task<Stage> Fetch(Asset asset, string expectedExecutable, Action<string> progress)
    {
        string root = Path.Combine(GameShelf.DataFolder, "tool-staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, asset.Name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            progress($"Downloading {asset.Tag} from GitHub…");
            using var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is not { Scheme: "https", Host: "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github.com" })
                throw new IOException("GitHub redirected to an unexpected download host.");
            if (response.Content.Headers.ContentLength is long n && n != asset.Size) throw new IOException("The GitHub asset size changed.");
            await using (var read = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var write = File.Create(file))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[128 * 1024];
                long count = 0;
                int length;
                while ((length = await read.ReadAsync(buffer, timeout.Token)) != 0)
                {
                    count += length;
                    if (count > asset.Size) throw new IOException("Download exceeds the release asset size.");
                    hash.AppendData(buffer, 0, length);
                    await write.WriteAsync(buffer.AsMemory(0, length), timeout.Token);
                }
                if (count != asset.Size || Convert.ToHexString(hash.GetHashAndReset()) != asset.Digest)
                    throw new IOException("The release did not match GitHub’s SHA-256 digest. Nothing was installed.");
            }
            progress("Download verified. Preparing files…");
            string unpack = Directory.CreateDirectory(Path.Combine(root, "unpacked")).FullName;
            var payload = await Task.Run(() => Unpack(file, unpack, expectedExecutable));
            return new Stage(root, payload);
        }
        catch { Clean(root); throw; }
    }
    internal static string Unpack(string file, string root, string expectedExecutable)
    {
        using var archive = ArchiveFactory.Open(file);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        string? executable = null;
        var bytes = new byte[128 * 1024];
        void Extract(IEntry entry, Func<Stream> open)
        {
            var key = (entry.Key ?? "").Replace('\\', '/').TrimEnd('/');
            if (entry.IsDirectory && key is "." or "") return;
            string target = ArchivePath(root, key);
            if (!paths.Add(target) || paths.Count > 4096) throw new IOException("Package contains duplicate paths or too many entries.");
            if (entry.IsDirectory) { Directory.CreateDirectory(target); return; }
            if (entry.Size < 0 || entry.Size > MaxExtracted - total) throw new IOException("Package exceeds the extraction limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var read = open();
            using var write = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            long count = 0;
            int amount;
            while ((amount = read.Read(bytes)) != 0)
            {
                count += amount;
                total += amount;
                if (count > entry.Size || total > MaxExtracted) throw new IOException("Package exceeds the declared entry size.");
                write.Write(bytes, 0, amount);
            }
            if (count != entry.Size) throw new IOException("Incomplete package entry.");
            if (string.Equals(Path.GetFileName(target), expectedExecutable, StringComparison.OrdinalIgnoreCase))
            {
                if (executable is not null) throw new IOException("Package contains multiple primary files.");
                executable = target;
            }
        }
        if (archive.Type == ArchiveType.SevenZip)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry()) Extract(reader.Entry, reader.OpenEntryStream);
        }
        else if (archive.Type == ArchiveType.Zip)
        {
            foreach (var entry in archive.Entries) Extract(entry, entry.OpenEntryStream);
        }
        else throw new IOException("Expected a ZIP or a 7z release archive.");
        return Path.GetDirectoryName(executable ?? throw new IOException($"Package does not contain {expectedExecutable}."))!;
    }
    internal static string ArchivePath(string root, string key)
    {
        var parts = key.Split('/');
        foreach (string part in parts)
        {
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || "<>:\"\\|?*".Contains(c)) ||
                Regex.IsMatch(part.Split('.')[0].TrimEnd(), @"^(CON|CONIN\$|CONOUT\$|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])$", RegexOptions.IgnoreCase))
                throw new IOException("An archive entry has an unsafe path.");
        }
        return Path.Combine(root, Path.Combine(parts));
    }

    static string ToolsFolder => Path.Combine(GameShelf.DataFolder, "tools");
    static string MagpieFolder => Path.Combine(ToolsFolder, "magpie");
    const string MagpieReceipt = ".framehub-magpie-version";
    internal static string? MagpieInstalled()
    {
        string receipt = Path.Combine(MagpieFolder, MagpieReceipt);
        return File.Exists(receipt) && File.Exists(Path.Combine(MagpieFolder, "Magpie.exe")) ? File.ReadAllText(receipt).Trim() : null;
    }
    static void EnsureMagpieClosed()
    {
        var matches = Process.GetProcessesByName("Magpie");
        foreach (var process in matches) process.Dispose();
        if (matches.Length != 0) throw new IOException("Exit Magpie from its tray menu before updating or removing it.");
    }
    internal static void InstallMagpie(string source, string tag)
    {
        EnsureMagpieClosed();
        Directory.CreateDirectory(ToolsFolder);
        if (Directory.Exists(MagpieFolder) && MagpieInstalled() is null)
            throw new IOException("An unrecognized Magpie folder exists. The app won’t overwrite it.");
        if (Directory.Exists(Path.Combine(MagpieFolder, "config")))
            throw new IOException("Magpie portable settings were found. Back up and move the portable config folder before replacing Magpie.");
        File.WriteAllText(Path.Combine(source, MagpieReceipt), tag);
        string backup = Path.Combine(ToolsFolder, "magpie-previous-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(MagpieFolder)) Directory.Move(MagpieFolder, backup);
        try { Directory.Move(source, MagpieFolder); }
        catch
        {
            if (Directory.Exists(backup)) Directory.Move(backup, MagpieFolder);
            throw;
        }
        Clean(backup);
    }
    internal static void RemoveMagpie()
    {
        EnsureMagpieClosed();
        if (MagpieInstalled() is null) throw new IOException("No Hub-managed Magpie installation was found.");
        if (Directory.Exists(Path.Combine(MagpieFolder, "config")))
            throw new IOException("Magpie portable settings were found. Back up and move them before removing the package.");
        Directory.Delete(MagpieFolder, true);
    }
    internal static void LaunchMagpie()
    {
        if (MagpieInstalled() is null) throw new IOException("Install Magpie in the Scalers tab first.");
        string file = Path.Combine(MagpieFolder, "Magpie.exe");
        using var process = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, WorkingDirectory = MagpieFolder });
    }
}
