using FrameHub;
using System.Text.Json;
using System.IO.Compression;

var root = Path.Combine(Path.GetTempPath(), "framehub-test-" + Guid.NewGuid().ToString("N"));
var game = Directory.CreateDirectory(Path.Combine(root, "game")).FullName;
var package = Directory.CreateDirectory(Path.Combine(root, "release")).FullName;
string InGame(string name) => Path.Combine(game, name);
string InPackage(string name) => Path.Combine(package, name);
void Expect(bool value, string description)
{
    if (!value) throw new Exception(description);
    Console.WriteLine("PASS " + description);
}
void Reject(Action act, string fragment, string description)
{
    try { act(); }
    catch (IOException e) when (e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("PASS " + description);
        return;
    }
    throw new Exception("Expected rejection: " + description);
}
try
{
    GameShelf.Entry[] shelf = [new("Neon Circuit", "1", true), new("Cobalt Dunes", "local.exe", false), new("Darkwater", "2", true)];
    Expect(GameShelf.Filter(shelf, "", 0).Select(x => x.Name).SequenceEqual(new[] { "Cobalt Dunes", "Darkwater", "Neon Circuit" }),
        "library cards are sorted by game name");
    Expect(GameShelf.Filter(shelf, " CIRCUIT ", 1).Single().Name == "Neon Circuit", "game search and Steam tag work together");
    Expect(GameShelf.Filter(shelf, "", 2).Single().Name == "Cobalt Dunes" &&
        GameShelf.Filter(shelf, "Circuit", 2).Length == 0, "manual tag excludes Steam cards");
    var digest = new string('a', 64);
    var current = new Version(0, 2, 0, 0);
    var metadata = $$"""{"tag_name":"v0.3.0","assets":[{"name":"LegionFrameHub.exe","size":159000000,"digest":"sha256:{{digest}}"}]}""";
    Expect(HubUpdate.ParseRelease(metadata, current)?.Digest == digest.ToUpperInvariant(), "accepts newer release with verified EXE metadata");
    Expect(HubUpdate.ParseRelease(metadata.Replace("v0.3.0", "v0.2.0"), current) is null, "no update for current release");
    Expect(HubUpdate.ParseRelease(metadata.Replace("v0.3.0", "v0.1.0"), current) is null, "older release cannot trigger a downgrade");
    Reject(() => HubUpdate.ParseRelease(metadata.Replace("sha256:", ""), current), "verifiable", "rejects missing trusted release digest");
    Reject(() => HubUpdate.ParseRelease(metadata.Replace("LegionFrameHub.exe", "unexpected.exe"), current), "verifiable", "rejects unexpected release asset");
    var toolMetadata = $$"""{"tag_name":"v0.13.0","draft":false,"prerelease":false,"assets":[{"name":"Magpie-v0.13.0-x64.zip","size":1200,"digest":"sha256:{{digest}}","browser_download_url":"https://github.com/Blinue/Magpie/releases/download/v0.13.0/Magpie-v0.13.0-x64.zip"}]}""";
    Expect(ToolCatalog.Parse(ToolCatalog.MagpieRepo, toolMetadata).Tag == "v0.13.0", "accepts official stable x64 Magpie release");
    Reject(() => ToolCatalog.Parse(ToolCatalog.MagpieRepo, toolMetadata.Replace("github.com", "mirror.invalid")), "official", "rejects download URL outside official repo");
    Reject(() => ToolCatalog.Parse(ToolCatalog.MagpieRepo, toolMetadata.Replace("x64", "ARM64")), "official", "rejects wrong package architecture");
    Reject(() => ToolCatalog.Parse(ToolCatalog.MagpieRepo, toolMetadata.Replace("\"prerelease\":false", "\"prerelease\":true")), "stable", "rejects prerelease package");
    Reject(() => ToolCatalog.ArchivePath(root, "../escaped.dll"), "unsafe path", "archive path traversal rejected");
    Reject(() => ToolCatalog.ArchivePath(root, "folder/NUL.txt"), "unsafe path", "Windows device names rejected");
    Reject(() => ToolCatalog.ArchivePath(root, "file.txt:payload"), "unsafe path", "NTFS alternate data streams rejected");
    string zip = Path.Combine(root, "known.zip");
    using (var payload = ZipFile.Open(zip, ZipArchiveMode.Create))
    using (var output = new StreamWriter(payload.CreateEntry("payload/Magpie.exe").Open()))
        output.Write("test exe");
    string unpacked = Directory.CreateDirectory(Path.Combine(root, "zip-stage")).FullName;
    Expect(File.ReadAllText(Path.Combine(ToolCatalog.Unpack(zip, unpacked, "Magpie.exe"), "Magpie.exe")) == "test exe", "extracts and identifies payload folder");
    File.WriteAllText(InGame("game.exe"), "original game");
    File.WriteAllText(InGame("dxgi.dll"), "another mod");
    File.WriteAllText(InPackage("OptiScaler.dll"), "v1 test payload");
    File.WriteAllText(InPackage("OptiScaler.ini"), "upstream defaults");
    File.WriteAllText(InPackage("setup.bat"), "do not import");
    Directory.CreateDirectory(InPackage("extras"));
    File.WriteAllText(InPackage(Path.Combine("extras", "dependency.dll")), "v1 extra");
    Reject(() => OptiFiles.Change(game, package, "dxgi.dll", "v1"), "unmanaged", "refuses unmanaged loader collision");
    Expect(File.ReadAllText(InGame("dxgi.dll")) == "another mod" && OptiFiles.Read(game) is null, "collision changes nothing");
    File.Delete(InGame("dxgi.dll"));
    var receipt = OptiFiles.Change(game, package, "dxgi.dll", "v1");
    Expect(receipt?.Files.Count == 3 && OptiFiles.Read(game)?.Version == "v1", "install records only managed files");
    Expect(File.ReadAllText(InGame("dxgi.dll")) == "v1 test payload" && !File.Exists(InGame("setup.bat")), "loader renamed and scripts skipped");
    Expect(!Directory.Exists(InGame(OptiFiles.Journal)), "successful install clears staging");
    File.WriteAllText(InGame("OptiScaler.ini"), "custom settings");
    File.WriteAllText(InPackage("OptiScaler.dll"), "v2 test payload");
    File.Delete(InPackage(Path.Combine("extras", "dependency.dll")));
    OptiFiles.Change(game, package, "winmm.dll", "v2");
    Expect(OptiFiles.Read(game)?.Version == "v2" && File.ReadAllText(InGame("winmm.dll")) == "v2 test payload" &&
        !File.Exists(InGame("dxgi.dll")) && !File.Exists(InGame(Path.Combine("extras", "dependency.dll"))), "update swaps loader and removes obsolete managed files");
    Expect(File.ReadAllText(InGame("OptiScaler.ini")) == "custom settings", "update preserves edited config");
    File.WriteAllText(InGame("winmm.dll"), "changed externally");
    Reject(() => OptiFiles.Change(game, null, "", ""), "changed outside", "modified managed binary is protected");
    Expect(File.ReadAllText(InGame("winmm.dll")) == "changed externally" && OptiFiles.Read(game)?.Version == "v2", "blocked removal changes nothing");
    File.WriteAllText(InGame("winmm.dll"), "v2 test payload");
    Expect(OptiFiles.Change(game, null, "", "") is null && OptiFiles.Read(game) is null && !File.Exists(InGame("winmm.dll")), "removal clears only managed payload");
    Expect(File.ReadAllText(InGame("game.exe")) == "original game" && File.ReadAllText(InGame("OptiScaler.ini")) == "custom settings", "game and settings are kept");
    OptiFiles.Change(game, package, "dxgi.dll", "v2");
    Expect(File.ReadAllText(InGame("OptiScaler.ini")) == "custom settings", "reinstall reuses settings without overwriting them");
    Directory.CreateDirectory(InGame(OptiFiles.Journal));
    Reject(() => OptiFiles.Change(game, null, "", ""), "earlier transaction", "unfinished transaction blocks changes");
    Directory.Delete(InGame(OptiFiles.Journal));
    File.WriteAllText(InGame(".framehub-optiscaler.json"), JsonSerializer.Serialize(new OptiFiles.Receipt("tampered", new() { ["../outside.dll"] = new string('A', 64) })));
    Reject(() => OptiFiles.Read(game), "invalid path", "receipt cannot traverse outside the game directory");
    if (!OperatingSystem.IsWindows())
    {
        File.CreateSymbolicLink(InPackage("linked.dll"), InGame("game.exe"));
        string cleanGame = Directory.CreateDirectory(Path.Combine(root, "clean")).FullName;
        Reject(() => OptiFiles.Change(cleanGame, package, "dxgi.dll", "v2"), "symbolic links", "package symbolic links are rejected");
    }
    if (args.Length == 2)
    {
        string optiTree = Directory.CreateDirectory(Path.Combine(root, "real-opti")).FullName;
        string magpieTree = Directory.CreateDirectory(Path.Combine(root, "real-magpie")).FullName;
        string optiPayload = ToolCatalog.Unpack(args[0], optiTree, "OptiScaler.dll");
        string magpiePayload = ToolCatalog.Unpack(args[1], magpieTree, "Magpie.exe");
        Expect(File.Exists(Path.Combine(magpiePayload, "Magpie.exe")), "extracts official Magpie x64 ZIP");
        var demoGame = Directory.CreateDirectory(Path.Combine(root, "fixture-game")).FullName;
        var optiReceipt = OptiFiles.Change(demoGame, optiPayload, "dxgi.dll", "fixture");
        Expect(optiReceipt?.Files.Count > 5 && File.Exists(Path.Combine(demoGame, "dxgi.dll")), "manages official OptiScaler 7z in a disposable game directory");
    }
    if (args is ["--live-tool"])
    {
        var official = await ToolCatalog.Latest(ToolCatalog.MagpieRepo);
        using var fixture = await ToolCatalog.Fetch(official, "Magpie.exe", Console.WriteLine);
        Expect(File.Exists(Path.Combine(fixture.Payload, "Magpie.exe")), "downloads, verifies and stages latest public Magpie release");
    }
}
finally { Directory.Delete(root, true); }
