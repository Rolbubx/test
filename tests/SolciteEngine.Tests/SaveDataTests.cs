using OpenTK.Mathematics;
using SolciteEngine.Player.Data;

namespace SolciteEngine.Tests;

public class Progress
{
    public int Level = 1;
    public Vector3 Position;
    public List<string> Items = new();
    public Dictionary<string, int> Stats { get; set; } = new();
}

/// <summary>SaveData's folder is global: every save test lives in this one class so they never run in parallel.</summary>
public sealed class SaveDataTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "solcite-tests-" + Guid.NewGuid().ToString("N"));

    public SaveDataTests() => SaveData.Folder = _folder;

    public void Dispose()
    {
        SaveData.Folder = "";
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact, Trait(Critical.Category, Critical.Value)]
    public void SaveThenLoad_GivesBackTheSameData()
    {
        var saved = new Progress { Level = 7, Position = new Vector3(1.5f, -2f, 3.25f), Items = { "sword", "key" } };
        saved.Stats["gold"] = 120;

        SaveData.DataSave(saved, "slot1");
        var loaded = SaveData.DataLoad<Progress>("slot1");

        Assert.Equal(7, loaded.Level);
        Assert.Equal(saved.Position, loaded.Position);
        Assert.Equal(new[] { "sword", "key" }, loaded.Items);
        Assert.Equal(120, loaded.Stats["gold"]);
    }

    [Fact, Trait(Critical.Category, Critical.Value)]
    public void SaveReplacesAnExistingSave()
    {
        SaveData.DataSave(new Progress { Level = 1 }, "slot");
        SaveData.DataSave(new Progress { Level = 2 }, "slot");
        Assert.Equal(2, SaveData.DataLoad<Progress>("slot").Level);
    }

    [Fact, Trait(Critical.Category, Critical.Value)]
    public void LoadingAMissingSave_Throws_OrGivesTheDefault()
    {
        Assert.False(SaveData.DataExists("nothing"));
        Assert.Throws<SaveDataException>(() => SaveData.DataLoad<Progress>("nothing"));
        Assert.Equal(42, SaveData.DataLoad("nothing", 42));
    }

    [Fact, Trait(Critical.Category, Critical.Value)]
    public void UpdateNeedsAnExistingSave()
    {
        Assert.Throws<SaveDataException>(() => SaveData.DataUpdate("missing", new Progress()));

        SaveData.DataSave(new Progress { Level = 1 }, "slot");
        SaveData.DataUpdate("slot", new Progress { Level = 5 });
        Assert.Equal(5, SaveData.DataLoad<Progress>("slot").Level);
    }

    [Fact]
    public void DeleteAndList()
    {
        SaveData.DataSave(1, "a");
        SaveData.DataSave(2, "b");
        Assert.Equal(new[] { "a", "b" }, SaveData.DataList().OrderBy(n => n));

        Assert.True(SaveData.DataDelete("a"));
        Assert.False(SaveData.DataDelete("a"));
        Assert.Equal(new[] { "b" }, SaveData.DataList());
    }

    [Fact]
    public void NamesWithOddCharactersRoundTrip()
    {
        SaveData.DataSave("hello", "slot 1/é:*");
        Assert.True(SaveData.DataExists("slot 1/é:*"));
        Assert.Equal("hello", SaveData.DataLoad<string>("slot 1/é:*"));
        Assert.Contains("slot 1/é:*", SaveData.DataList());
    }
}
