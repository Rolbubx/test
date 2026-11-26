using SolciteEngine.Core;
using SolciteEngine.Editor;

namespace SolciteEngine.Tests;

/// <summary>What the editor and the game read at start-up: project folders and prefabs.</summary>
public sealed class LoadingTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "solcite-tests-" + Guid.NewGuid().ToString("N"));

    public LoadingTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SolciteEngine.sln")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
        throw new DirectoryNotFoundException("SolciteEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Fact, Trait(Critical.Category, Critical.Value)]
    public void CreatedProject_OpensAgain()
    {
        var created = ProjectPaths.Create(_temp, "My Game", out string error);
        Assert.True(created != null, error);
        Assert.True(Directory.Exists(created!.Scripts));

        var opened = ProjectPaths.Open(created.Root, out error);
        Assert.True(opened != null, error);
        Assert.Equal("My Game", opened!.Name);
        Assert.Equal(created.Scripts, opened.Scripts);
    }

    [Fact, Trait(Critical.Category, Critical.Value)]
    public void OpeningAFolderThatIsNotAProject_FailsCleanly()
    {
        Assert.Null(ProjectPaths.Open(_temp, out string error));
        Assert.NotEmpty(error);
        Assert.Null(ProjectPaths.Open(Path.Combine(_temp, "missing"), out error));
        Assert.NotEmpty(error);
    }

    [Fact, Trait(Critical.Category, Critical.Value)]
    public void ShippedPrefabs_Load()
    {
        Prefab.SearchFolders.Insert(0, RepoFile("SolciteEngine", "Prefabs"));
        foreach (var name in new[] { "Player", "Camera" })
        {
            var data = Prefab.Load(name);
            Assert.NotNull(data);
            Assert.NotNull(data!.Root);
        }
    }

    [Fact]
    public void BrokenPrefab_GivesNull()
    {
        File.WriteAllText(Path.Combine(_temp, "Broken" + Prefab.Extension), "{ not json");
        Prefab.SearchFolders.Insert(0, _temp);
        Assert.Null(Prefab.Load("Broken"));
    }

    [Fact]
    public void CleanName_KeepsOnlySafeCharacters()
    {
        Assert.Equal("MyGame_2-b", ProjectPaths.CleanName("  My/Game:_2-b?  "));
    }
}
