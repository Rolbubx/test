using System.Diagnostics;

namespace GT67.Editor;

/// <summary>Opens script files in the user's own code editor (Visual Studio Code by default) instead of an in-game text box.</summary>
public static class ExternalEditor
{
    private static readonly string[] VsCodeLike = { "code", "code-insiders", "codium", "cursor" };

    /// <summary>
    /// Opens <paramref name="file"/> with <paramref name="command"/> (a program on the PATH such as "code"). With an empty
    /// command, or if the program cannot be found, the system's default application for the file is used.
    /// </summary>
    public static bool Open(string command, string projectRoot, string file, out string message)
    {
        command = command.Trim();
        try
        {
            if (command.Length > 0 && !command.Equals("default", StringComparison.OrdinalIgnoreCase) && FindOnPath(command) != null)
            {
                Launch(command, projectRoot, file);
                message = $"Opened {Path.GetFileName(file)} in {command}.";
                return true;
            }

            OpenWithSystem(file);
            message = command.Length > 0 && !command.Equals("default", StringComparison.OrdinalIgnoreCase)
                ? $"'{command}' was not found on the PATH; opened {Path.GetFileName(file)} with the system default."
                : $"Opened {Path.GetFileName(file)} with the system default.";
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            message = $"Could not open {Path.GetFileName(file)}: {e.Message}";
            return false;
        }
    }

    private static void Launch(string command, string projectRoot, string file)
    {
        string name = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();
        bool vsCode = VsCodeLike.Contains(name);

        // On Windows "code" is a .cmd script, which only cmd.exe can start.
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { CreateNoWindow = true }
            : new ProcessStartInfo(command);
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(command);
        }

        if (vsCode && Directory.Exists(projectRoot)) info.ArgumentList.Add(projectRoot); // opens the whole project once
        if (vsCode) info.ArgumentList.Add("-g");
        info.ArgumentList.Add(file);
        info.UseShellExecute = false;
        Process.Start(info)?.Dispose();
    }

    private static void OpenWithSystem(string file)
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
        else
            Process.Start(new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", $"\"{file}\"") { UseShellExecute = false })?.Dispose();
    }

    /// <summary>The full path of a program found in the PATH (with the usual Windows extensions), or null.</summary>
    public static string? FindOnPath(string command)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command) ? command : null;

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions.Prepend(""))
            {
                string candidate = Path.Combine(directory.Trim('"'), command + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
