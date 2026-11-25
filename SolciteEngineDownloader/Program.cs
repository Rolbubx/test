using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

// SolciteEngineDownloader: installs (or updates) the SolciteEngine editor from the latest GitHub release.
//   SolciteEngineDownloader.exe                 installs into the default folder, then offers to start the editor
//   SolciteEngineDownloader.exe "D:\Solcite"    installs into that folder
//   --yes      no questions (does not start the editor)       --launch   start the editor when done
//   --tag v1.2.3   install that release instead of the latest one

const string Repository = "Rolbubx/test";

string? folder = null, tag = null;
bool yes = false, launch = false;
var args2 = Environment.GetCommandLineArgs();
for (int i = 1; i < args2.Length; i++)
{
    if (args2[i] == "--yes" || args2[i] == "-y") yes = true;
    else if (args2[i] == "--launch") launch = true;
    else if (args2[i] == "--tag" && i + 1 < args2.Length) tag = args2[++i];
    else if (!args2[i].StartsWith("--")) folder = args2[i];
}
folder = Path.GetFullPath(folder ?? DefaultFolder());

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("SolciteEngineDownloader");
http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

try
{
    Console.WriteLine("SolciteEngine downloader");
    Console.WriteLine($"Looking for the {(tag ?? "latest")} release on github.com/{Repository} ...");
    string url = tag == null ? $"https://api.github.com/repos/{Repository}/releases/latest" : $"https://api.github.com/repos/{Repository}/releases/tags/{tag}";
    using var response = await http.GetAsync(url);
    if (!response.IsSuccessStatusCode) return Fail($"GitHub answered HTTP {(int)response.StatusCode}. Is there a release yet, and is the repository public?");
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    string releaseTag = doc.RootElement.GetProperty("tag_name").GetString() ?? "?";
    string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
    string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    string? assetUrl = null, assetName = null;
    long size = 0;
    foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
    {
        string name = a.GetProperty("name").GetString() ?? "";
        string n = name.ToLowerInvariant();
        if (n.EndsWith(".zip") && n.Contains("editor") && n.Contains(os) && n.Contains(arch))
        {
            assetName = name;
            assetUrl = a.GetProperty("browser_download_url").GetString();
            size = a.GetProperty("size").GetInt64();
            break;
        }
    }
    if (assetUrl == null) return Fail($"Release {releaseTag} has no editor package for {os}-{arch}.");

    string versionFile = Path.Combine(folder, "installed-version.txt");
    if (File.Exists(versionFile) && File.ReadAllText(versionFile).Trim() == releaseTag)
        Console.WriteLine($"{releaseTag} is already installed in {folder}; reinstalling it.");
    Console.WriteLine($"Installing {releaseTag} ({assetName}, {size / 1048576.0:F1} MB) into {folder}");
    if (!yes && !Confirm("Continue? [Y/n] ")) return 1;

    string zipPath = Path.Combine(Path.GetTempPath(), $"solcite-{Guid.NewGuid():N}.zip");
    try
    {
        using (var download = await http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            download.EnsureSuccessStatusCode();
            long total = download.Content.Headers.ContentLength ?? size;
            await using var input = await download.Content.ReadAsStreamAsync();
            await using var output = File.Create(zipPath);
            var buffer = new byte[81920];
            long done = 0;
            int read, lastPct = -1;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                int pct = total > 0 ? (int)(done * 100 / total) : 0;
                if (pct != lastPct) { lastPct = pct; Console.Write($"\rDownloading... {pct,3}%"); }
            }
            Console.WriteLine();
        }

        Console.WriteLine("Unpacking...");
        Directory.CreateDirectory(folder);
        string root = folder + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            string dest = Path.GetFullPath(Path.Combine(folder, entry.FullName));
            if (!dest.StartsWith(root, StringComparison.Ordinal)) return Fail($"Unsafe path in the package: {entry.FullName}");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
    }
    finally
    {
        try { File.Delete(zipPath); } catch (IOException) { }
    }

    File.WriteAllText(versionFile, releaseTag);
    string exe = Path.Combine(folder, OperatingSystem.IsWindows() ? "SolciteEngine.exe" : "SolciteEngine");
    if (!OperatingSystem.IsWindows() && File.Exists(exe))
        File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    Console.WriteLine($"Done. SolciteEngine {releaseTag} is installed in {folder}");

    if (File.Exists(exe) && (launch || (!yes && Confirm("Start the editor now? [Y/n] "))))
        Process.Start(new ProcessStartInfo(exe, "--editor") { WorkingDirectory = folder, UseShellExecute = true });
    return 0;
}
catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or JsonException or InvalidDataException or KeyNotFoundException)
{
    return Fail(e.Message.Contains("being used by another process") ? "A file is in use: close SolciteEngine and try again." : e.Message);
}

static string DefaultFolder() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SolciteEngine");

static bool Confirm(string question)
{
    Console.Write(question);
    var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
    return answer is null or "" or "y" or "yes" or "o" or "oui";
}

static int Fail(string message)
{
    Console.Error.WriteLine($"Error: {message}");
    return 1;
}
