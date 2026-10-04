using System.Numerics;

namespace RetroFilter;

/// <summary>
/// Palettes et table des mélanges pour Shaders/dither.hlsl. Reprend les calculs de
/// cga-v2.hlsl (OKLab, distance, pénalité « douceur ») pour les faire une fois par
/// changement de réglage au lieu d'une fois par pixel.
/// </summary>
static class DitherTable
{
    // Les 16 couleurs CGA (RGBI), niveaux 0x00 / 0x55 / 0xAA / 0xFF. Ce sont aussi les
    // 16 couleurs par défaut de l'EGA (et les seules possibles en 320x200 et 640x200).
    const float L0 = 0.0f, L1 = 0.33333f, L2 = 0.66667f, L3 = 1.0f;
    static readonly Vector3[] Cga16 =
    [
        new(L0, L0, L0), new(L0, L0, L2), new(L0, L2, L0), new(L0, L2, L2), //  0 noir, bleu, vert, cyan
        new(L2, L0, L0), new(L2, L0, L2), new(L2, L1, L0), new(L2, L2, L2), //  4 rouge, magenta, marron, gris clair
        new(L1, L1, L1), new(L1, L1, L3), new(L1, L3, L1), new(L1, L3, L3), //  8 gris foncé, bleu, vert, cyan clairs
        new(L3, L1, L1), new(L3, L1, L3), new(L3, L3, L1), new(L3, L3, L3), // 12 rouge, magenta clairs, jaune, blanc
    ];

    // Palettes 4 couleurs du mode 320x200, en index dans Cga16 (même ordre que dans cga-v2.hlsl).
    static readonly int[][] Pal4 =
    [
        [0, 11, 13, 15], // 0: palette 1 haute intensité (cyan / magenta / blanc)
        [0, 3, 5, 7],    // 1: palette 1 basse intensité
        [0, 10, 12, 14], // 2: palette 0 haute intensité (vert / rouge / jaune)
        [0, 2, 4, 6],    // 3: palette 0 basse intensité
        [0, 11, 12, 15], // 4: mode 5 (cyan / rouge / blanc)
        [0, 8, 7, 15],   // 5 + luminosité: niveaux de gris CGA
    ];

    /// <summary>Couleurs de la palette choisie (même règle que cga-v2.hlsl).</summary>
    public static Vector3[] Colors(CgaSettings settings)
    {
        int pal = Math.Clamp(settings.Palette, 0, 5);
        // La palette 5 donne les 16 couleurs, ou les 4 gris en mode luminosité.
        if (pal == 5 && !settings.MatchLuminance) return Cga16;
        return Pal4[pal].Select(i => Cga16[i]).ToArray();
    }

    /// <summary>
    /// Pour chaque paire (i, j >= i) dans l'ordre des boucles du shader, puis chaque proportion
    /// k / niveaux (k de 0 à niveaux) : mélange en OKLab (xyz) et pénalité (w).
    /// </summary>
    public static Vector4[] Mixes(Vector3[] colors, CgaSettings settings)
    {
        int levels = Math.Max(settings.DitherLevels, 1);
        float chroma = settings.MatchLuminance ? 0f : 1f;
        var labs = colors.Select(Oklab).ToArray();
        var table = new List<Vector4>(colors.Length * (colors.Length + 1) / 2 * (levels + 1));
        for (int i = 0; i < colors.Length; i++)
        {
            for (int j = i; j < colors.Length; j++)
            {
                for (int k = 0; k <= levels; k++)
                {
                    float r = (float)k / levels;
                    var lab = Oklab(Vector3.Lerp(colors[i], colors[j], r));
                    // Pénalise les mélanges de couleurs très éloignées (noir + blanc
                    // pour faire du gris, par exemple), qui donnent des motifs criards.
                    float penalty = k > 0 && k < levels
                        ? settings.MixPenalty * LabDist(labs[i], labs[j], chroma) * (MathF.Abs(r - 0.5f) + 0.5f)
                        : 0f;
                    table.Add(new Vector4(lab, penalty));
                }
            }
        }
        return table.ToArray();
    }

    static float ToLinear(float c) => c >= 0.04045f ? MathF.Pow((c + 0.055f) / 1.055f, 2.4f) : c / 12.92f;

    /// <summary>OKLab à partir d'une couleur sRGB.</summary>
    static Vector3 Oklab(Vector3 srgb)
    {
        var c = new Vector3(ToLinear(srgb.X), ToLinear(srgb.Y), ToLinear(srgb.Z));
        float l = MathF.Cbrt(Math.Max(Vector3.Dot(c, new(0.4122214708f, 0.5363325363f, 0.0514459929f)), 0f));
        float m = MathF.Cbrt(Math.Max(Vector3.Dot(c, new(0.2119034982f, 0.6806995451f, 0.1073969566f)), 0f));
        float s = MathF.Cbrt(Math.Max(Vector3.Dot(c, new(0.0883024619f, 0.2817188376f, 0.6299787005f)), 0f));
        var lms = new Vector3(l, m, s);
        return new Vector3(
            Vector3.Dot(lms, new(0.2104542553f, 0.7936177850f, -0.0040720468f)),
            Vector3.Dot(lms, new(1.9779984951f, -2.4285922050f, 0.4505937099f)),
            Vector3.Dot(lms, new(0.0259040371f, 0.7827717662f, -0.8086757660f)));
    }

    /// <summary>Distance OKLab au carré ; chroma = 0 ne compare que la clarté.</summary>
    static float LabDist(Vector3 a, Vector3 b, float chroma)
    {
        var d = a - b;
        return d.X * d.X + chroma * (d.Y * d.Y + d.Z * d.Z);
    }
}
