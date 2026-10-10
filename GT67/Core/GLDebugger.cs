using System.Runtime.InteropServices;
using GT67.Utils;
using OpenTK.Graphics.OpenGL4;

namespace GT67.Core;

/// <summary>
/// Reports OpenGL errors. Uses the KHR_debug callback when the context supports it,
/// and falls back to polling <see cref="GL.GetError"/> (macOS has no debug output).
/// </summary>
public sealed class GLDebugger
{
    private DebugProc? _callback; // must stay referenced for as long as GL may call it

    public bool UsesCallback { get; private set; }

    public void Enable()
    {
        GL.GetInteger(GetPName.ContextFlags, out int flags);
        bool debugContext = (flags & (int)ContextFlagMask.ContextFlagDebugBit) != 0;
        if (!debugContext)
        {
            Log.Debug("No GL debug context; falling back to glGetError polling.");
            return;
        }

        _callback = OnMessage;
        GL.DebugMessageCallback(_callback, IntPtr.Zero);
        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous);
        UsesCallback = true;
        Log.Debug("GL debug callback enabled.");
    }

    /// <summary>Call once per frame in debug builds when the callback is not available.</summary>
    public void Poll(string where)
    {
        if (UsesCallback) return;
        ErrorCode error;
        while ((error = GL.GetError()) != ErrorCode.NoError)
            Log.Error($"GL error {error} after {where}");
    }

    private static void OnMessage(DebugSource source, DebugType type, int id, DebugSeverity severity,
        int length, IntPtr message, IntPtr userParam)
    {
        if (severity == DebugSeverity.DebugSeverityNotification) return;
        string text = Marshal.PtrToStringAnsi(message, length) ?? "";
        string line = $"GL {type} [{source}] #{id}: {text}";
        if (type == DebugType.DebugTypeError) Log.Error(line);
        else if (type is not (DebugType.DebugTypePerformance or DebugType.DebugTypeOther)) Log.Warn(line);
    }
}
