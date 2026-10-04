using System.Numerics;
using System.Runtime.InteropServices;

namespace RetroFilter;

/// <summary>Réglages du filtre CGA v2. Valeurs par défaut = preset cga-moderne.</summary>
sealed class CgaSettings
{
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 200;
    public int Palette { get; set; } = 0;
    public bool MatchLuminance { get; set; } = false;
    public int DitherLevels { get; set; } = 16;
    public float MixPenalty { get; set; } = 0.1f;
    public float Brightness { get; set; } = 0.0f;
    public float Contrast { get; set; } = 1.0f;
    public float Saturation { get; set; } = 1.0f;

    internal CgaParams ToParams() => new()
    {
        OutputSize = new Vector4(Width, Height, 1.0f / Width, 1.0f / Height),
        Palette = Palette,
        MatchMode = MatchLuminance ? 1 : 0,
        DitherLevels = DitherLevels,
        MixPenalty = MixPenalty,
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation,
    };
}

/// <summary>Tampon de constantes de Shaders/cga-v2.hlsl (même ordre, 48 octets).</summary>
[StructLayout(LayoutKind.Sequential)]
struct CgaParams
{
    public Vector4 OutputSize;
    public float Palette;
    public float MatchMode;
    public float DitherLevels;
    public float MixPenalty;
    public float Brightness;
    public float Contrast;
    public float Saturation;
    public float Padding;
}
