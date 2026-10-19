using GT67.Core;
using GT67.Effects;

namespace GT67.Utils;

/// <summary>The post-processing chain the editor and every game start with (Program.cs and the generated standalone game share it).</summary>
public static class GamePostProcessing
{
    /// <summary>
    /// Adds the effects in the order they run (HDR first, then tone map, then LDR). Every effect is added in both modes,
    /// so the editor's Main Volume shows all of them. The editor starts with the distracting ones (depth of field,
    /// chromatic aberration, grain) switched off; a compiled map stores the switches.
    /// </summary>
    public static void Setup(Window window, bool editor)
    {
        var post = window.Renderer.Post;
        post.Add(new VolumetricFogEffect());
        post.Add(new DepthOfFieldEffect { FocusDistance = 14f, FocusRange = 12f, MaxBlur = 6f, Enabled = !editor });
        post.Add(new BloomEffect { Threshold = 1.0f, Intensity = 0.35f });
        post.Add(new SunRaysEffect());
        post.Add(new RainEffect());
        post.Add(new ChromaticAberrationEffect { Strength = 0.004f, Enabled = !editor });
        post.Add(new ColorCorrectionEffect { Contrast = 1.12f, Saturation = 1.12f, Vibrance = 0.3f, Temperature = 0.03f });
        post.Add(new SharpenEffect());
        post.Add(new VignetteEffect { Intensity = editor ? 0.25f : 0.5f });
        post.Add(new FilmGrainEffect { Strength = 0.03f, Enabled = !editor });

        // Every effect parameter can also be read and set by name (for scripts, config files, key bindings...).
        post.Set("Bloom", "Intensity", 0.4f);
        post.Set("ColorCorrection", "Lift", (0.01f, 0.01f, 0.03f));
    }
}
