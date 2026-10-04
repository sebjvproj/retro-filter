#version 450

/*
    CGA retro filter, passe 2 : palette, tramage et contours, a la
    resolution virtuelle (l'image arrive deja moyennee par cga-prepare).

      1. contours : un detecteur de bords (Sobel) sur la clarte ; les
         pixels du cote sombre d'un bord fort prennent la couleur la plus
         sombre de la palette
      2. tramage de Yliluoma : on cherche la paire de couleurs de la palette
         et la proportion de melange qui imitent le mieux le pixel, l'erreur
         etant mesuree dans l'espace OKLab
      3. la matrice de Bayer decide laquelle des deux couleurs afficher

    Le melange est calcule en sRGB et non en lumiere lineaire : avec des
    pixels aussi gros, l'oeil voit les points au lieu de les fusionner, et
    la densite percue suit l'espace sRGB (les zones sombres restent pleines).
*/

layout(push_constant) uniform Push
{
    vec4 SourceSize;
    vec4 OriginalSize;
    vec4 OutputSize;
    uint FrameCount;
    float PALETTE;
    float BG_COLOR;
    float LUMA_WEIGHT;
    float DITHER_LEVELS;
    float MIX_PENALTY;
    float OUTLINE;
} params;

#pragma parameter PALETTE "Palette (0-7, voir README)" 0.0 0.0 7.0 1.0
#pragma parameter BG_COLOR "Couleur de fond (0-15, CGA)" 0.0 0.0 15.0 1.0
#pragma parameter LUMA_WEIGHT "Couleur <-> luminosite" 0.0 0.0 1.0 0.05
#pragma parameter DITHER_LEVELS "Niveaux de tramage (1 = aucun)" 16.0 1.0 16.0 1.0
#pragma parameter MIX_PENALTY "Douceur du tramage" 0.1 0.0 0.5 0.01
#pragma parameter OUTLINE "Contours" 0.0 0.0 1.0 0.05

layout(std140, set = 0, binding = 0) uniform UBO
{
    mat4 MVP;
} global;

#pragma stage vertex
layout(location = 0) in vec4 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 0) out vec2 vTexCoord;

void main()
{
    gl_Position = global.MVP * Position;
    vTexCoord = TexCoord;
}

#pragma stage fragment
layout(location = 0) in vec2 vTexCoord;
layout(location = 0) out vec4 FragColor;
layout(set = 0, binding = 2) uniform sampler2D Source;

// Les 16 couleurs CGA (RGBI), niveaux 0x00 / 0x55 / 0xAA / 0xFF
#define L0 0.0
#define L1 0.33333
#define L2 0.66667
#define L3 1.0
const vec3 CGA16[16] = vec3[](
    vec3(L0, L0, L0), //  0 noir
    vec3(L0, L0, L2), //  1 bleu
    vec3(L0, L2, L0), //  2 vert
    vec3(L0, L2, L2), //  3 cyan
    vec3(L2, L0, L0), //  4 rouge
    vec3(L2, L0, L2), //  5 magenta
    vec3(L2, L1, L0), //  6 marron
    vec3(L2, L2, L2), //  7 gris clair
    vec3(L1, L1, L1), //  8 gris fonce
    vec3(L1, L1, L3), //  9 bleu clair
    vec3(L1, L3, L1), // 10 vert clair
    vec3(L1, L3, L3), // 11 cyan clair
    vec3(L3, L1, L1), // 12 rouge clair
    vec3(L3, L1, L3), // 13 magenta clair
    vec3(L3, L3, L1), // 14 jaune
    vec3(L3, L3, L3)  // 15 blanc
);

// Palettes 4 couleurs, en index dans CGA16. La premiere couleur est le
// fond : elle est remplacee par BG_COLOR, comme sur une vraie carte CGA.
const int PAL4[28] = int[](
     0, 11, 13, 15, // 0: palette 1 haute intensite (cyan / magenta / blanc)
     0,  3,  5,  7, // 1: palette 1 basse intensite
     0, 10, 12, 14, // 2: palette 0 haute intensite (vert / rouge / jaune)
     0,  2,  4,  6, // 3: palette 0 basse intensite
     0, 11, 12, 15, // 4: mode 5 (cyan / rouge / blanc)
     0,  0,  0,  0, // 5: les 16 couleurs (pas de table)
     0,  8,  7, 15  // 6: niveaux de gris
);

const float BAYER4[16] = float[](
     0.0,  8.0,  2.0, 10.0,
    12.0,  4.0, 14.0,  6.0,
     3.0, 11.0,  1.0,  9.0,
    15.0,  7.0, 13.0,  5.0
);

vec3 toLinear(vec3 c)
{
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

// OKLab a partir d'une couleur sRGB
vec3 oklab(vec3 srgb)
{
    vec3 c = toLinear(srgb);
    vec3 lms = vec3(
        dot(c, vec3(0.4122214708, 0.5363325363, 0.0514459929)),
        dot(c, vec3(0.2119034982, 0.6806995451, 0.1073969566)),
        dot(c, vec3(0.0883024619, 0.2817188376, 0.6299787005)));
    lms = pow(max(lms, vec3(0.0)), vec3(1.0 / 3.0));
    return vec3(
        dot(lms, vec3(0.2104542553, 0.7936177850, -0.0040720468)),
        dot(lms, vec3(1.9779984951, -2.4285922050, 0.4505937099)),
        dot(lms, vec3(0.0259040371, 0.7827717662, -0.8086757660)));
}

float luma(vec3 c)
{
    return dot(c, vec3(0.299, 0.587, 0.114));
}

// Distance OKLab au carre ; chroma = 0 ne compare que la clarte
float labDist(vec3 a, vec3 b, float chroma)
{
    vec3 d = a - b;
    return d.x * d.x + chroma * dot(d.yz, d.yz);
}

// Espace ou l'on projette le pixel sur le segment entre deux couleurs :
// RGB (pondere par chroma) + luma
vec4 projSpace(vec3 c, float chroma)
{
    return vec4(c * sqrt(chroma), luma(c));
}

float lumaAt(vec2 cell, vec2 offset)
{
    vec2 p = clamp(cell + offset, vec2(0.0), params.SourceSize.xy - 1.0);
    return luma(texture(Source, (p + 0.5) * params.SourceSize.zw).rgb);
}

void main()
{
    vec2 cell = floor(vTexCoord * params.SourceSize.xy);
    vec3 c = texture(Source, (cell + 0.5) * params.SourceSize.zw).rgb;

    int pal = min(int(params.PALETTE + 0.5), 7);
    int count = pal == 5 ? 16 : (pal == 7 ? 2 : 4);
    float chroma = 1.0 - params.LUMA_WEIGHT;
    float levels = max(floor(params.DITHER_LEVELS + 0.5), 1.0);

    vec3 colors[16];
    vec3 labs[16];
    int darkest = 0;
    for (int i = 0; i < count; i++)
    {
        if (pal == 5)
            colors[i] = CGA16[i];
        else if (i == 0)
            colors[i] = CGA16[int(params.BG_COLOR + 0.5)];
        else
            colors[i] = CGA16[pal == 7 ? 15 : PAL4[pal * 4 + i]];
        labs[i] = oklab(colors[i]);
        if (labs[i].x < labs[darkest].x)
            darkest = i;
    }

    // Contours : Sobel sur la luma des gros pixels voisins
    if (params.OUTLINE > 0.0)
    {
        float tl = lumaAt(cell, vec2(-1.0, -1.0));
        float t  = lumaAt(cell, vec2( 0.0, -1.0));
        float tr = lumaAt(cell, vec2( 1.0, -1.0));
        float l  = lumaAt(cell, vec2(-1.0,  0.0));
        float r  = lumaAt(cell, vec2( 1.0,  0.0));
        float bl = lumaAt(cell, vec2(-1.0,  1.0));
        float b  = lumaAt(cell, vec2( 0.0,  1.0));
        float br = lumaAt(cell, vec2( 1.0,  1.0));
        float gx = (tr + 2.0 * r + br) - (tl + 2.0 * l + bl);
        float gy = (bl + 2.0 * b + br) - (tl + 2.0 * t + tr);
        float edge = length(vec2(gx, gy)) * 0.25;
        float localMean = (tl + t + tr + l + r + bl + b + br) * 0.125;
        // Seul le cote sombre du bord est trace : traits fins, pas doubles
        float threshold = 0.4 - 0.35 * params.OUTLINE;
        if (edge > threshold && luma(c) < localMean)
        {
            FragColor = vec4(colors[darkest], 1.0);
            return;
        }
    }

    vec3 target = oklab(c);
    vec4 x = projSpace(c, chroma);

    vec3 bestA = vec3(0.0);
    vec3 bestB = vec3(0.0);
    float bestRatio = 0.0;
    float bestErr = 1e9;

    for (int i = 0; i < count; i++)
    {
        vec4 pa = projSpace(colors[i], chroma);
        for (int j = i; j < count; j++)
        {
            vec4 d = projSpace(colors[j], chroma) - pa;
            float dd = dot(d, d);
            float r = dd > 1e-8 ? clamp(dot(x - pa, d) / dd, 0.0, 1.0) : 0.0;
            r = floor(r * levels + 0.5) / levels;

            float err = labDist(target, oklab(mix(colors[i], colors[j], r)), chroma);
            // Penalise les melanges de couleurs tres eloignees (noir + blanc
            // pour faire du gris, par exemple), qui donnent des motifs criards
            if (r > 0.0 && r < 1.0)
                err += params.MIX_PENALTY * labDist(labs[i], labs[j], chroma) * (abs(r - 0.5) + 0.5);

            if (err < bestErr)
            {
                bestErr = err;
                bestA = colors[i];
                bestB = colors[j];
                bestRatio = r;
            }
        }
    }

    ivec2 bayer = ivec2(mod(cell, 4.0));
    float threshold = (BAYER4[bayer.y * 4 + bayer.x] + 0.5) / 16.0;

    FragColor = vec4(bestRatio > threshold ? bestB : bestA, 1.0);
}
