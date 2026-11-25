using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace SolciteEngine.Editor;

/// <summary>
/// A small wrapper around the <c>git</c> command line for one project folder: status, init, commit, pull and push (GitHub or
/// any other remote). Quick commands run on the calling thread; network ones (pull, push) run in the background.
/// Messages for the editor console are queued in <see cref="Messages"/> and drained by the UI thread.
/// </summary>
public sealed class VersionControl
{
    /// <summary>Written by Init when the project has no .gitignore yet.</summary>
    public const string DefaultIgnore = "cache/\ngenerated-game/bin/\ngenerated-game/obj/\ngenerated-game.new/\ngenerated-game.old/\n";

    private readonly string _root;
    private volatile bool _busy;

    public VersionControl(string root) => _root = root;

    /// <summary>True while a pull or push is running.</summary>
    public bool Busy => _busy;
    public bool GitInstalled { get; private set; } = true;
    public bool IsRepo { get; private set; }
    public string Branch { get; private set; } = "";
    public string Remote { get; private set; } = "";
    public int Changes { get; private set; }
    public int Ahead { get; private set; }
    public int Behind { get; private set; }

    public ConcurrentQueue<(string Text, bool Error)> Messages { get; } = new();

    private bool Say(string text, bool error = false) { Messages.Enqueue((text, error)); return !error; }

    /// <summary>Reads the branch, remote and number of changed files.</summary>
    public void Refresh()
    {
        var version = Run("--version");
        GitInstalled = version.Code == 0;
        IsRepo = GitInstalled && Run("rev-parse", "--is-inside-work-tree") is { Code: 0 } inside && inside.Output.Trim() == "true"
                 && Directory.Exists(Path.Combine(_root, ".git"));
        if (!IsRepo) { Branch = Remote = ""; Changes = Ahead = Behind = 0; return; }

        Branch = Run("rev-parse", "--abbrev-ref", "HEAD").Output.Trim();
        if (Branch == "HEAD") Branch = Run("symbolic-ref", "--short", "HEAD").Output.Trim();   // no commit yet
        Remote = Run("remote", "get-url", "origin") is { Code: 0 } r ? r.Output.Trim() : "";
        Changes = Run("status", "--porcelain").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        Ahead = Behind = 0;
        if (Run("rev-list", "--left-right", "--count", "HEAD...@{upstream}") is { Code: 0 } counts)
        {
            var parts = counts.Output.Split(new[] { '\t', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) { int.TryParse(parts[0], out int a); int.TryParse(parts[1], out int b); Ahead = a; Behind = b; }
        }
    }

    public bool Init()
    {
        string ignore = Path.Combine(_root, ".gitignore");
        if (!File.Exists(ignore)) File.WriteAllText(ignore, DefaultIgnore);
        var result = Run("init", "-b", "main");
        if (result.Code != 0) result = Run("init");   // git older than 2.28 has no -b
        Refresh();
        return result.Code == 0 ? Say("Git repository created.") : Say($"git init failed: {result.Output.Trim()}", true);
    }

    public bool SetRemote(string url)
    {
        url = url.Trim();
        if (url.Length == 0) return Say("Paste the GitHub repository URL first (https://github.com/<you>/<repo>.git).", true);
        var result = Run("remote", "get-url", "origin").Code == 0 ? Run("remote", "set-url", "origin", url) : Run("remote", "add", "origin", url);
        Refresh();
        return result.Code == 0 ? Say($"Remote origin is now {url}.") : Say($"Could not set the remote: {result.Output.Trim()}", true);
    }

    /// <summary>Stages everything and commits it.</summary>
    public bool Commit(string message)
    {
        message = message.Trim();
        if (message.Length == 0) return Say("Write a commit message first.", true);

        Run("add", "-A");
        var args = new List<string>();
        // A fresh machine often has no git identity; use a neutral one rather than failing.
        if (string.IsNullOrWhiteSpace(Run("config", "user.name").Output)) args.AddRange(new[] { "-c", "user.name=SolciteEngine" });
        if (string.IsNullOrWhiteSpace(Run("config", "user.email").Output)) args.AddRange(new[] { "-c", "user.email=editor@solciteengine.local" });
        args.AddRange(new[] { "commit", "-m", message });

        var result = Run(args.ToArray());
        Refresh();
        if (result.Code == 0) return Say($"Committed: {message}");
        return Say(result.Output.Contains("nothing to commit") ? "Nothing to commit." : $"Commit failed: {result.Output.Trim()}", true);
    }

    public void PullAsync() => InBackground("Pull", () =>
    {
        var result = Run("pull", "--ff-only");
        return result.Code == 0 ? Say("Pull done: " + Last(result.Output)) : Say($"Pull failed: {result.Output.Trim()}", true);
    });

    public void PushAsync() => InBackground("Push", () =>
    {
        // -u so the first push of a new branch also sets its upstream.
        var result = Run("push", "-u", "origin", "HEAD");
        return result.Code == 0 ? Say("Push done.") : Say($"Push failed: {result.Output.Trim()}", true);
    });

    private void InBackground(string name, Func<bool> work)
    {
        if (_busy) return;
        _busy = true;
        Task.Run(() =>
        {
            try { work(); }
            catch (Exception e) { Say($"{name} failed: {e.Message}", true); }
            finally { Refresh(); _busy = false; }
        });
    }

    private static string Last(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";

    private (int Code, string Output) Run(params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo("git")
            {
                WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args) info.ArgumentList.Add(a);
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";   // fail with a message instead of waiting for a password nobody can type

            using var process = Process.Start(info)!;
            var error = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(120_000)) { try { process.Kill(true); } catch (InvalidOperationException) { } return (-1, "git timed out."); }
            return (process.ExitCode, output + error.Result);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, "git was not found on the PATH.");
        }
    }
}
