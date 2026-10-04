/*
    Filtre CGA, version 2 : portage HLSL de shaders/cga-v2.slang (même calcul).
    Rendu à la résolution virtuelle (320x200) ; copy.hlsl agrandit ensuite sans lissage.

    Pour chaque gros pixel :
      1. moyenne 8x8 échantillons de l'image source, en lumière linéaire
      2. ajustement luminosité / contraste / saturation (en sRGB)
      3. tramage de Yliluoma : on cherche la paire de couleurs de la palette
         et la proportion de mélange qui imitent le mieux le pixel, l'erreur
         étant mesurée dans l'espace OKLab
      4. la matrice de Bayer décide laquelle des deux couleurs afficher

    Le mélange est calculé en sRGB et non en lumière linéaire : avec des
    pixels aussi gros, l'oeil voit les points au lieu de les fusionner, et
    la densité perçue suit l'espace sRGB (les zones sombres restent pleines).
*/

cbuffer Params : register(b0)
{
    float4 OutputSize;   // largeur, hauteur, 1/largeur, 1/hauteur
    float Palette;       // 0 à 5, voir README
    float MatchMode;     // 0 = couleur, 1 = luminosité
    float DitherLevels;  // 1 = aucun tramage
    float MixPenalty;    // douceur du tramage
    float Brightness;
    float Contrast;
    float Saturation;
    float Padding;
};

// Lectures par côté de gros pixel pour la moyenne (8 dans la v2 d'origine).
#ifndef SAMPLES
#define SAMPLES 8
#endif

Texture2D Source : register(t0);
SamplerState LinearSampler : register(s0);

// Les 16 couleurs CGA (RGBI), niveaux 0x00 / 0x55 / 0xAA / 0xFF
#define L0 0.0
#define L1 0.33333
#define L2 0.66667
#define L3 1.0
static const float3 CGA16[16] =
{
    float3(L0, L0, L0), //  0 noir
    float3(L0, L0, L2), //  1 bleu
    float3(L0, L2, L0), //  2 vert
    float3(L0, L2, L2), //  3 cyan
    float3(L2, L0, L0), //  4 rouge
    float3(L2, L0, L2), //  5 magenta
    float3(L2, L1, L0), //  6 marron
    float3(L2, L2, L2), //  7 gris clair
    float3(L1, L1, L1), //  8 gris foncé
    float3(L1, L1, L3), //  9 bleu clair
    float3(L1, L3, L1), // 10 vert clair
    float3(L1, L3, L3), // 11 cyan clair
    float3(L3, L1, L1), // 12 rouge clair
    float3(L3, L1, L3), // 13 magenta clair
    float3(L3, L3, L1), // 14 jaune
    float3(L3, L3, L3)  // 15 blanc
};

// Palettes 4 couleurs du mode 320x200, en index dans CGA16
static const int PAL4[24] =
{
     0, 11, 13, 15, // 0: palette 1 haute intensité (cyan / magenta / blanc)
     0,  3,  5,  7, // 1: palette 1 basse intensité
     0, 10, 12, 14, // 2: palette 0 haute intensité (vert / rouge / jaune)
     0,  2,  4,  6, // 3: palette 0 basse intensité
     0, 11, 12, 15, // 4: mode 5 (cyan / rouge / blanc)
     0,  8,  7, 15  // 5 + luminosité: niveaux de gris CGA
};

static const float BAYER4[16] =
{
     0.0,  8.0,  2.0, 10.0,
    12.0,  4.0, 14.0,  6.0,
     3.0, 11.0,  1.0,  9.0,
    15.0,  7.0, 13.0,  5.0
};

float3 toLinear(float3 c)
{
    return c >= 0.04045 ? pow(max((c + 0.055) / 1.055, 0.0), 2.4) : c / 12.92;
}

float3 toSrgb(float3 c)
{
    return c >= 0.0031308 ? 1.055 * pow(max(c, 0.0), 1.0 / 2.4) - 0.055 : c * 12.92;
}

// OKLab à partir d'une couleur sRGB
float3 oklab(float3 srgb)
{
    float3 c = toLinear(srgb);
    float3 lms = float3(
        dot(c, float3(0.4122214708, 0.5363325363, 0.0514459929)),
        dot(c, float3(0.2119034982, 0.6806995451, 0.1073969566)),
        dot(c, float3(0.0883024619, 0.2817188376, 0.6299787005)));
    lms = pow(max(lms, 0.0), 1.0 / 3.0);
    return float3(
        dot(lms, float3(0.2104542553, 0.7936177850, -0.0040720468)),
        dot(lms, float3(1.9779984951, -2.4285922050, 0.4505937099)),
        dot(lms, float3(0.0259040371, 0.7827717662, -0.8086757660)));
}

// Distance OKLab au carré ; chroma = 0 ne compare que la clarté
float labDist(float3 a, float3 b, float chroma)
{
    float3 d = a - b;
    return d.x * d.x + chroma * dot(d.yz, d.yz);
}

// Espace où l'on projette le pixel sur le segment entre deux couleurs :
// RGB (pondéré par chroma) + luma
float4 projSpace(float3 c, float chroma)
{
    return float4(c * sqrt(chroma), dot(c, float3(0.299, 0.587, 0.114)));
}

float4 PSMain(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float2 cell = floor(pos.xy);

    // Moyenne SAMPLES x SAMPLES dans la cellule, en lumière linéaire
    float3 c = 0.0;
    [loop]
    for (int sy = 0; sy < SAMPLES; sy++)
    {
        [unroll]
        for (int sx = 0; sx < SAMPLES; sx++)
        {
            float2 suv = (cell + (float2(sx, sy) + 0.5) * (1.0 / SAMPLES)) * OutputSize.zw;
            c += toLinear(Source.SampleLevel(LinearSampler, suv, 0).rgb);
        }
    }
    c = toSrgb(c * (1.0 / (SAMPLES * SAMPLES)));

    c = (c - 0.5) * Contrast + 0.5 + Brightness;
    c = lerp(dot(c, float3(0.299, 0.587, 0.114)).xxx, c, Saturation);
    c = saturate(c);

    int pal = min(int(Palette + 0.5), 5);
    float chroma = MatchMode > 0.5 ? 0.0 : 1.0;
    // En mode luminosité, la palette 16 couleurs devient les 4 gris CGA
    bool full16 = pal == 5 && chroma > 0.0;
    int count = full16 ? 16 : 4;
    float levels = max(floor(DitherLevels + 0.5), 1.0);

    float3 colors[16];
    float3 labs[16];
    [loop]
    for (int k = 0; k < count; k++)
    {
        colors[k] = CGA16[full16 ? k : PAL4[pal * 4 + k]];
        labs[k] = oklab(colors[k]);
    }

    float3 target = oklab(c);
    float4 x = projSpace(c, chroma);

    float3 bestA = 0.0;
    float3 bestB = 0.0;
    float bestRatio = 0.0;
    float bestErr = 1e9;

    [loop]
    for (int i = 0; i < count; i++)
    {
        float4 pa = projSpace(colors[i], chroma);
        [loop]
        for (int j = i; j < count; j++)
        {
            float4 d = projSpace(colors[j], chroma) - pa;
            float dd = dot(d, d);
            float r = dd > 1e-8 ? saturate(dot(x - pa, d) / max(dd, 1e-8)) : 0.0;
            r = floor(r * levels + 0.5) / levels;

            float err = labDist(target, oklab(lerp(colors[i], colors[j], r)), chroma);
            // Pénalise les mélanges de couleurs très éloignées (noir + blanc
            // pour faire du gris, par exemple), qui donnent des motifs criards
            if (r > 0.0 && r < 1.0)
                err += MixPenalty * labDist(labs[i], labs[j], chroma) * (abs(r - 0.5) + 0.5);

            if (err < bestErr)
            {
                bestErr = err;
                bestA = colors[i];
                bestB = colors[j];
                bestRatio = r;
            }
        }
    }

    uint2 b = (uint2)cell & 3;
    float threshold = (BAYER4[b.y * 4 + b.x] + 0.5) / 16.0;

    return float4(bestRatio > threshold ? bestB : bestA, 1.0);
}
