using System.Reflection;

namespace GT67.Scripting;

/// <summary>Result of compiling the files in <c>Scripts/</c> with <see cref="ScriptCompiler"/>.</summary>
public sealed class ScriptCompileResult
{
    public bool Success { get; init; }

    /// <summary>Compiler errors as "file(line,column): message".</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>The compiled assembly (null when compilation failed).</summary>
    public Assembly? Assembly { get; init; }

    /// <summary>Every non-abstract <see cref="Script"/> subclass in the assembly.</summary>
    public IReadOnlyList<Type> ScriptTypes { get; init; } = Array.Empty<Type>();

    public TimeSpan Duration { get; init; }
}
