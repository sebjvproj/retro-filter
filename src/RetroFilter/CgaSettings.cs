using System.Numerics;
using System.Runtime.InteropServices;

namespace RetroFilter;

/// <summary>Réglages du filtre CGA v2. Valeurs par défaut = « CGA moderne » (v2 tramée pixel par pixel).</summary>
sealed class CgaSettings
{
    /// <summary>Résolution virtuelle fixe, utilisée quand PixelSize vaut 0.</summary>
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 200;

    /// <summary>Taille d'un gros pixel en pixels de la capture ; 0 = résolution fixe Width x Height.</summary>
    public int PixelSize { get; set; } = 1;
    public int Palette { get; set; } = 0;
    public bool MatchLuminance { get; set; } = false;
    public int DitherLevels { get; set; } = 16;
    public float MixPenalty { get; set; } = 0.1f;
    public float Brightness { get; set; } = 0.0f;
    public float Contrast { get; set; } = 1.0f;
    public float Saturation { get; set; } = 1.0f;

    /// <summary>Résolution de la passe CGA pour une capture de cette taille.</summary>
    public (int Width, int Height) ResolutionFor(int sourceWidth, int sourceHeight) =>
        PixelSize <= 0
            ? (Width, Height)
            : (Math.Max(1, (int)Math.Round((double)sourceWidth / PixelSize)),
               Math.Max(1, (int)Math.Round((double)sourceHeight / PixelSize)));

    internal CgaParams ToParams(int width, int height) => new()
    {
        OutputSize = new Vector4(width, height, 1.0f / width, 1.0f / height),
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
