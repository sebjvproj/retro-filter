/*
    Tramage rétro, pour n'importe quelle palette (CGA 4 couleurs, EGA 16 couleurs...).
    Même calcul que cga-v2.hlsl (la v2 de référence), mais la palette et les mélanges
    viennent de tables calculées par le programme (DitherTable.cs) :

      Colors : les couleurs de la palette, en sRGB
      Mixes  : pour chaque paire (i <= j) et chaque proportion k / niveaux, la couleur
               du mélange en OKLab (xyz) et la pénalité « douceur » déjà calculée (w)

    Avec 16 couleurs, il y a 136 paires : calculer chaque mélange en OKLab pour chaque
    pixel coûtait environ 10 ms par image en 1920x1080. Avec les tables, il ne reste
    qu'une projection et une lecture par paire.

    Pour chaque gros pixel :
      1. moyenne SAMPLES x SAMPLES échantillons de l'image source, en lumière linéaire
      2. ajustement luminosité / contraste / saturation (en sRGB)
      3. tramage de Yliluoma : meilleure paire de couleurs et proportion du mélange,
         erreur mesurée en OKLab, mélange calculé en sRGB
      4. la matrice de Bayer décide laquelle des deux couleurs afficher
*/

cbuffer Params : register(b0)
{
    float4 OutputSize;   // largeur, hauteur, 1/largeur, 1/hauteur
    float Palette;       // utilisé seulement par cga-v2.hlsl
    float MatchMode;     // 0 = couleur, 1 = luminosité
    float DitherLevels;  // 1 = aucun tramage
    float MixPenalty;    // déjà intégrée dans Mixes.w
    float Brightness;
    float Contrast;
    float Saturation;
    float ColorCount;    // nombre de couleurs dans Colors
};

// Lectures par côté de gros pixel pour la moyenne (8 dans la v2 d'origine).
#ifndef SAMPLES
#define SAMPLES 8
#endif

Texture2D Source : register(t0);
StructuredBuffer<float4> Colors : register(t1);
StructuredBuffer<float4> Mixes : register(t2);
SamplerState LinearSampler : register(s0);

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

    int count = (int)ColorCount;
    float chroma = MatchMode > 0.5 ? 0.0 : 1.0;
    float levels = max(floor(DitherLevels + 0.5), 1.0);
    int stride = (int)levels + 1;

    float3 target = oklab(c);
    float4 x = projSpace(c, chroma);

    int bestA = 0;
    int bestB = 0;
    float bestRatio = 0.0;
    float bestErr = 1e9;
    int entry = 0;

    [loop]
    for (int i = 0; i < count; i++)
    {
        float4 pa = projSpace(Colors[i].rgb, chroma);
        [loop]
        for (int j = i; j < count; j++)
        {
            float4 d = projSpace(Colors[j].rgb, chroma) - pa;
            float dd = dot(d, d);
            float r = dd > 1e-8 ? saturate(dot(x - pa, d) / max(dd, 1e-8)) : 0.0;
            float k = floor(r * levels + 0.5);

            float4 mix = Mixes[entry + (int)k];
            float err = labDist(target, mix.xyz, chroma) + mix.w;

            if (err < bestErr)
            {
                bestErr = err;
                bestA = i;
                bestB = j;
                bestRatio = k / levels;
            }
            entry += stride;
        }
    }

    uint2 b = (uint2)cell & 3;
    float threshold = (BAYER4[b.y * 4 + b.x] + 0.5) / 16.0;

    return float4(Colors[bestRatio > threshold ? bestB : bestA].rgb, 1.0);
}
