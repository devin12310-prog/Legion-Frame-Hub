using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FrameHub;

internal static class HubUpdate
{
    internal const string Repo = "devin12310-prog/Legion-Frame-Hub";
    internal const string Releases = "https://github.com/" + Repo + "/releases/latest";
    internal const string AssetName = "LegionFrameHub.exe";
    internal static Version Current => typeof(HubUpdate).Assembly.GetName().Version ?? new Version(0, 4, 0, 0);
    internal sealed record Candidate(string Tag, Version Version, string Digest, long Size);

    static string Cli()
    {
        var folders = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links")
        }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        return folders.Select(x => x.Trim().Trim('"')).Where(Path.IsPathFullyQualified)
            .Select(x => Path.Combine(x, "gh.exe")).FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("For in-app updates from this private repo, install GitHub CLI and sign in once using `gh auth login`. Or choose Open GitHub Releases to download in your browser.");
    }
    static async Task<string> GitHub(int seconds, params string[] args)
    {
        var start = new ProcessStartInfo(Cli())
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["GH_PROMPT_DISABLED"] = "1";
        start.Environment["GH_HOST"] = "github.com";
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("GitHub CLI could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException("GitHub did not respond in time. Try again or use the browser download.");
        }
        if (process.ExitCode != 0)
        {
            _ = await stderr;
            throw new IOException("GitHub CLI could not read this private release. Confirm that `gh auth login` uses the GitHub account with access, or open the release in your browser.");
        }
        return await stdout;
    }

    internal static async Task<Candidate?> Find()
    {
        return ParseRelease(await GitHub(25, "api", "--hostname", "github.com", "repos/" + Repo + "/releases/latest"), Current);
    }
    internal static Candidate? ParseRelease(string input, Version current)
    {
        using var json = JsonDocument.Parse(input);
        var release = json.RootElement;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var number) || number.Build < 0 || number.Revision != -1)
            throw new IOException("Unexpected release tag.");
        var version = new Version(number.Major, number.Minor, number.Build, 0);
        if (version <= current) return null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != AssetName) continue;
            string digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
            long bytes = asset.GetProperty("size").GetInt64();
            if (digest.StartsWith("sha256:", StringComparison.Ordinal) && digest.Length == 71 &&
                digest[7..].All(Uri.IsHexDigit) && bytes is > 0 and < 250_000_000)
                return new Candidate(tag, version, digest[7..].ToUpperInvariant(), bytes);
        }
        throw new IOException("This release has no verifiable EXE asset. Use the browser if you want to review it manually.");
    }
    internal static async Task<string> Download(Candidate candidate)
    {
        var folder = Path.Combine(GameShelf.DataFolder, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, AssetName);
        try
        {
            await GitHub(180, "release", "download", candidate.Tag, "--repo", "github.com/" + Repo, "--pattern", AssetName, "--dir", folder);
            using var stream = File.OpenRead(file);
            if (stream.Length != candidate.Size || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                throw new IOException("The downloaded file does not match the expected Windows release.");
            stream.Position = 0;
            if (Convert.ToHexString(await SHA256.HashDataAsync(stream)) != candidate.Digest)
                throw new IOException("The downloaded file did not match GitHub’s SHA-256 digest. Nothing was installed.");
            return file;
        }
        catch
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
    internal static void ReplaceWhenClosed(string source, string expected)
    {
        var target = Environment.ProcessPath ?? throw new IOException("Could not locate the running EXE.");
        if (!string.Equals(Path.GetFileName(target), AssetName, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The portable executable must be named LegionFrameHub.exe to update in place.");
        static string Encode(string value) => Convert.ToBase64String(Encoding.Unicode.GetBytes(value));
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit)) throw new IOException("Update digest is invalid.");
        var script = $$"""
            $ErrorActionPreference='Stop'
            function Decode($x) { [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($x)) }
            $src=Decode '{{Encode(source)}}'
            $dest=Decode '{{Encode(target)}}'
            $tmp=$dest+'.next-'+[Guid]::NewGuid().ToString('N')
            $bak=$dest+'.previous-'+[Guid]::NewGuid().ToString('N')
            try {
                $p=Get-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
                if($p -and !$p.WaitForExit(30000)){ throw 'Frame Hub did not exit. Start the update again.' }
                Copy-Item -LiteralPath $src -Destination $tmp
                if((Get-FileHash -LiteralPath $tmp -Algorithm SHA256).Hash -ne '{{expected}}'){ throw 'Staged download changed. Nothing was installed.' }
                [IO.File]::Replace($tmp,$dest,$bak)
                try { Start-Process -FilePath $dest -WorkingDirectory ([IO.Path]::GetDirectoryName($dest)) }
                catch { [IO.File]::Replace($bak,$dest,$null); throw }
                Remove-Item -LiteralPath ([IO.Path]::GetDirectoryName($src)) -Recurse -Force -ErrorAction SilentlyContinue
            } catch {
                $msg=$_.Exception.Message
                Add-Type -AssemblyName System.Windows.Forms
                [void][Windows.Forms.MessageBox]::Show($msg,'Legion Frame Hub update did not complete')
            } finally {
                Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
            }
            """;
        var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(ps) { CreateNoWindow = true, UseShellExecute = false };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(script) }) start.ArgumentList.Add(arg);
        using var helper = Process.Start(start) ?? throw new IOException("Windows update helper could not start.");
    }
}
