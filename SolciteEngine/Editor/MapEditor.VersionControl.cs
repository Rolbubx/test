using SolciteEngine.Gui;

namespace SolciteEngine.Editor;

// The "Version control" popup in the toolbar: a simple git + GitHub workflow for the open project.
public sealed partial class MapEditor
{
    private VersionControl? _vcs;
    private string _commitMessage = "";
    private string _remoteUrl = "";

    private VersionControl? Vcs => Paths == null || Paths.IsEngineSandbox ? null : _vcs ??= new VersionControl(Paths.Root);

    private void DrawVersionControlPopup()
    {
        var g = G;
        var vcs = Vcs;
        if (vcs == null) return;

        while (vcs.Messages.TryDequeue(out var m)) Say(m.Text, m.Error);

        if (!g.BeginPopup("##vcs", 380f)) return;
        g.Heading("Version control");

        if (!vcs.GitInstalled)
        {
            g.TextWrapped("git was not found on the PATH. Install it from https://git-scm.com, then reopen this panel.", dim: true);
            if (g.Button("Check again")) vcs.Refresh();
            g.EndPopup();
            return;
        }

        if (!vcs.IsRepo)
        {
            g.TextWrapped("This project is not under version control yet. Starting it creates a git repository here " +
                          "(cache/ and the build folders are ignored).", dim: true);
            if (g.Button("Start version control", 0f, ButtonStyle.Primary)) { vcs.Init(); _remoteUrl = vcs.Remote; }
            g.EndPopup();
            return;
        }

        string sync = vcs.Remote.Length == 0 ? "" : $"   ↑{vcs.Ahead} ↓{vcs.Behind}";
        g.TextWrapped($"Branch {vcs.Branch}   ·   {(vcs.Changes == 0 ? "no changes" : vcs.Changes + " changed file(s)")}{sync}", dim: true);
        g.Spacing(4f);

        g.SeparatorText("Commit");
        g.InputText("Message", ref _commitMessage, "what changed?");
        g.TextWrapped("Press Compile first: the map is saved only by Compile.", dim: true);
        if (g.Button("Commit all changes", 0f, vcs.Changes > 0 ? ButtonStyle.Primary : ButtonStyle.Normal) && vcs.Commit(_commitMessage)) _commitMessage = "";

        g.SeparatorText("GitHub");
        if (_remoteUrl.Length == 0 && vcs.Remote.Length > 0) _remoteUrl = vcs.Remote;
        g.InputText("Remote", ref _remoteUrl, "https://github.com/<you>/<repo>.git");
        if (g.Button("Save remote")) vcs.SetRemote(_remoteUrl);

        g.Spacing(4f);
        if (vcs.Busy) g.TextWrapped("Working...", dim: true);
        else if (vcs.Remote.Length > 0)
        {
            if (g.Button("Pull")) vcs.PullAsync();
            g.SameLine();
            if (g.Button("Push")) vcs.PushAsync();
            g.TextWrapped("Pushing needs git to be signed in to GitHub already (credential manager or SSH key). Results show in the Console.", dim: true);
        }
        g.EndPopup();
    }
}
