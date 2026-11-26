namespace SolciteEngine.Tests;

/// <summary>
/// Tests marked <c>[Trait(Critical.Category, Critical.Value)]</c> must all pass for CI to publish a release
/// (see .github/scripts/test_gate.py). Use it for start-up, loading and saving: anything that would make a
/// shipped build unusable.
/// </summary>
public static class Critical
{
    public const string Category = "Category";
    public const string Value = "Critical";
}
