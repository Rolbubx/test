using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SolciteEngine.Utils;

/// <summary>A newer release found on GitHub.</summary>
public sealed record UpdateInfo(string Tag, Version Version, string AssetName, string DownloadUrl, long Size, string Notes);

/// <summary>
/// Native self-updater: asks the GitHub Releases API for the latest release, and when it is newer than the running
/// build downloads the .zip asset for this platform, unpacks it beside the game and swaps the files in on restart.
/// Only BCL types (HttpClient, JsonDocument, ZipFile): no extra package, and safe for Native AOT / trimming.
/// </summary>
public static class Updater
{
    /// <summary>"owner/repo" whose releases are checked.</summary>
    public const string Repository = "Rolbubx/test";

    private static readonly HttpClient Http = CreateClient();

    private static string AppDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static string StagingDir => Path.Combine(AppDir, "update-staging");

    /// <summary>Version of the running build (csproj &lt;Version&gt;).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var text = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return TryParseVersion(text, out var v) ? v : new Version(0, 0, 0);
        }
    }

    /// <summary>The latest release if it is newer than this build, otherwise null (also null when offline).</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warn($"Update check failed: HTTP {(int)response.StatusCode}.");
                return null;
            }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!TryParseVersion(tag, out var latest) || latest <= CurrentVersion) return null;

            var assets = new List<(string Name, string Url, long Size)>();
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    assets.Add((name, a.GetProperty("browser_download_url").GetString() ?? "", a.GetProperty("size").GetInt64()));
            }
            var pick = PickAsset(assets);
            if (pick.Url.Length == 0)
            {
                Log.Warn($"Release {tag} has no .zip asset for this platform.");
                return null;
            }
            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            return new UpdateInfo(tag, latest, pick.Name, pick.Url, pick.Size, notes);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            Log.Warn($"Update check failed: {e.Message}");
            return null;
        }
    }

    /// <summary>Downloads the release zip and unpacks it into update-staging next to the game.</summary>
    public static async Task DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"solcite-update-{Guid.NewGuid():N}.zip");
        try
        {
            using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? info.Size;
                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = File.Create(zipPath);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }

            if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, true);
            Directory.CreateDirectory(StagingDir);
            ExtractSafely(zipPath, StagingDir);
            Log.Info($"Update {info.Tag} downloaded to {StagingDir}.");
        }
        finally
        {
            try { File.Delete(zipPath); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Starts a small script that waits for this process to exit, copies update-staging over the game folder and
    /// relaunches it, then returns. The caller must close the game right after (the exe is locked while it runs).
    /// </summary>
    public static void ApplyAndRestart()
    {
        if (!Directory.Exists(StagingDir)) throw new InvalidOperationException("No downloaded update to apply.");
        int pid = Environment.ProcessId;
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        string args = string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Where(a => a != "--update").Select(a => $"\"{a}\""));
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), "solcite-update.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                $":wait\r\ntasklist /FI \"PID eq {pid}\" 2>NUL | find \"{pid}\" >NUL\r\nif not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto wait)\r\n" +
                $"xcopy /E /Y /Q \"{StagingDir}\\*\" \"{AppDir}\\\" >NUL\r\n" +
                $"rmdir /S /Q \"{StagingDir}\"\r\n" +
                $"start \"\" \"{exe}\" {args}\r\n" +
                "(goto) 2>NUL & del \"%~f0\"\r\n");
            psi = new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false };
        }
        else
        {
            var script = Path.Combine(Path.GetTempPath(), "solcite-update.sh");
            File.WriteAllText(script,
                "#!/bin/sh\n" +
                $"while kill -0 {pid} 2>/dev/null; do sleep 1; done\n" +
                $"cp -rf '{StagingDir}'/. '{AppDir}'/\n" +
                $"rm -rf '{StagingDir}'\n" +
                $"chmod +x '{exe}'\n" +
                $"nohup '{exe}' {args} >/dev/null 2>&1 &\n" +
                "rm -- \"$0\"\n");
            psi = new ProcessStartInfo("/bin/sh", $"\"{script}\"") { UseShellExecute = false };
        }
        Process.Start(psi);
        Log.Info("Updater started; closing the game to install the update.");
    }

    /// <summary>Check, download and prepare in one go (the "--update" option). Returns true when the game must now close.</summary>
    public static bool CheckAndInstall()
    {
        var info = CheckAsync().GetAwaiter().GetResult();
        if (info == null) { Log.Info($"SolciteEngine {CurrentVersion} is up to date."); return false; }
        Log.Info($"New version {info.Tag} available (running {CurrentVersion}): {info.AssetName}, {info.Size / 1048576.0:F1} MB.");
        int last = -1;
        var progress = new Progress<double>(p =>
        {
            int pct = (int)(p * 10) * 10;
            if (pct != last) { last = pct; Log.Info($"Downloading... {pct}%"); }
        });
        try { DownloadAsync(info, progress).GetAwaiter().GetResult(); }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            Log.Error($"Update failed: {e.Message}");
            return false;
        }
        ApplyAndRestart();
        return true;
    }

    // The editor build updates from the "Editor" zip, the game build from the "Game" zip, for this OS and architecture
    // (the names the release workflow makes: SolciteEngine-Editor-win-x64.zip, SolciteEngine-Game-linux-x64.zip, ...).
#if EDITOR
    private const string Kind = "editor";
#else
    private const string Kind = "game";
#endif

    private static (string Name, string Url, long Size) PickAsset(List<(string Name, string Url, long Size)> assets)
    {
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        foreach (var a in assets)
        {
            var n = a.Name.ToLowerInvariant();
            if (n.Contains(Kind) && n.Contains(os) && n.Contains(arch)) return a;
        }
        return default;
    }

    // Like ZipFile.ExtractToDirectory, but refuses entries that would land outside the target folder.
    private static void ExtractSafely(string zipPath, string target)
    {
        string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            string dest = Path.GetFullPath(Path.Combine(target, entry.FullName));
            if (!dest.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException($"Unsafe path in update: {entry.FullName}");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
    }

    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().TrimStart('v', 'V');
        int cut = text.IndexOfAny(new[] { '+', '-' });
        if (cut >= 0) text = text[..cut];
        return Version.TryParse(text, out version!);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SolciteEngine-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
