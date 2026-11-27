using FrameHub;
using System.Text.Json;

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
}
finally { Directory.Delete(root, true); }
