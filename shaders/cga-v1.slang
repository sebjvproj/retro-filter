#version 450

/*
    CGA retro filter, version 1 (une seule passe, resolution reglable)
    Pixelisation + palette CGA + tramage ordonne (Bayer 4x4), en une seule passe.

    Pour chaque pixel de sortie :
      1. on trouve la "cellule" de la grille virtuelle (320x200 par defaut)
      2. on moyenne 4x4 echantillons de l'image source dans cette cellule
      3. on ajuste luminosite / contraste / saturation
      4. on ajoute le seuil Bayer de la cellule, puis on prend la couleur
         la plus proche dans la palette choisie
*/

layout(push_constant) uniform Push
{
    vec4 SourceSize;
    vec4 OriginalSize;
    vec4 OutputSize;
    uint FrameCount;
    float RES_X;
    float RES_Y;
    float PALETTE;
    float MATCH_MODE;
    float DITHER;
    float BRIGHTNESS;
    float CONTRAST;
    float SATURATION;
} params;

#pragma parameter RES_X "Largeur virtuelle (pixels)" 320.0 80.0 1280.0 8.0
#pragma parameter RES_Y "Hauteur virtuelle (pixels)" 200.0 50.0 800.0 4.0
#pragma parameter PALETTE "Palette (0-5, voir README)" 0.0 0.0 5.0 1.0
#pragma parameter MATCH_MODE "Correspondance: 0=couleur 1=luminosite" 0.0 0.0 1.0 1.0
#pragma parameter DITHER "Force du tramage" 1.0 0.0 2.0 0.05
#pragma parameter BRIGHTNESS "Luminosite" 0.0 -0.5 0.5 0.02
#pragma parameter CONTRAST "Contraste" 1.0 0.5 2.0 0.05
#pragma parameter SATURATION "Saturation" 1.0 0.0 3.0 0.05

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

// Les 16 couleurs CGA (RGBI)
const vec3 CGA16[16] = vec3[](
    vec3(0.000, 0.000, 0.000), //  0 noir
    vec3(0.000, 0.000, 0.667), //  1 bleu
    vec3(0.000, 0.667, 0.000), //  2 vert
    vec3(0.000, 0.667, 0.667), //  3 cyan
    vec3(0.667, 0.000, 0.000), //  4 rouge
    vec3(0.667, 0.000, 0.667), //  5 magenta
    vec3(0.667, 0.333, 0.000), //  6 marron
    vec3(0.667, 0.667, 0.667), //  7 gris clair
    vec3(0.333, 0.333, 0.333), //  8 gris fonce
    vec3(0.333, 0.333, 1.000), //  9 bleu clair
    vec3(0.333, 1.000, 0.333), // 10 vert clair
    vec3(0.333, 1.000, 1.000), // 11 cyan clair
    vec3(1.000, 0.333, 0.333), // 12 rouge clair
    vec3(1.000, 0.333, 1.000), // 13 magenta clair
    vec3(1.000, 1.000, 0.333), // 14 jaune
    vec3(1.000, 1.000, 1.000)  // 15 blanc
);

// Palettes 4 couleurs du mode 320x200, en index dans CGA16
const int PAL4[24] = int[](
     0, 11, 13, 15, // 0: palette 1 haute intensite (cyan / magenta / blanc)
     0,  3,  5,  7, // 1: palette 1 basse intensite
     0, 10, 12, 14, // 2: palette 0 haute intensite (vert / rouge / jaune)
     0,  2,  4,  6, // 3: palette 0 basse intensite
     0, 11, 12, 15, // 4: mode 5 (cyan / rouge / blanc)
     0,  8,  7, 15  // 5 + luminosite: niveaux de gris CGA
);

const float BAYER4[16] = float[](
     0.0,  8.0,  2.0, 10.0,
    12.0,  4.0, 14.0,  6.0,
     3.0, 11.0,  1.0,  9.0,
    15.0,  7.0, 13.0,  5.0
);

float luma(vec3 c)
{
    return dot(c, vec3(0.299, 0.587, 0.114));
}

// Distance "redmean" : approximation perceptuelle bon marche
float colorDist(vec3 a, vec3 b)
{
    vec3 d = a - b;
    float rmean = 0.5 * (a.r + b.r);
    return (2.0 + rmean) * d.r * d.r + 4.0 * d.g * d.g + (3.0 - rmean) * d.b * d.b;
}

vec3 nearestByColor(vec3 c, int pal)
{
    vec3 best = vec3(0.0);
    float bestD = 1e9;
    if (pal >= 5)
    {
        for (int i = 0; i < 16; i++)
        {
            float d = colorDist(c, CGA16[i]);
            if (d < bestD) { bestD = d; best = CGA16[i]; }
        }
    }
    else
    {
        for (int i = 0; i < 4; i++)
        {
            vec3 p = CGA16[PAL4[pal * 4 + i]];
            float d = colorDist(c, p);
            if (d < bestD) { bestD = d; best = p; }
        }
    }
    return best;
}

vec3 nearestByLuma(float l, int pal)
{
    vec3 best = vec3(0.0);
    float bestD = 1e9;
    for (int i = 0; i < 4; i++)
    {
        vec3 p = CGA16[PAL4[pal * 4 + i]];
        float d = abs(l - luma(p));
        if (d < bestD) { bestD = d; best = p; }
    }
    return best;
}

void main()
{
    vec2 res = max(vec2(params.RES_X, params.RES_Y), vec2(1.0));
    vec2 cell = floor(vTexCoord * res);

    // Moyenne 4x4 dans la cellule (evite le scintillement d'un seul echantillon)
    vec3 c = vec3(0.0);
    for (int y = 0; y < 4; y++)
    {
        for (int x = 0; x < 4; x++)
        {
            vec2 uv = (cell + (vec2(x, y) + 0.5) * 0.25) / res;
            c += texture(Source, uv).rgb;
        }
    }
    c *= 1.0 / 16.0;

    c = (c - 0.5) * params.CONTRAST + 0.5 + params.BRIGHTNESS;
    c = mix(vec3(luma(c)), c, params.SATURATION);
    c = clamp(c, 0.0, 1.0);

    ivec2 b = ivec2(mod(cell, 4.0));
    float threshold = (BAYER4[b.y * 4 + b.x] + 0.5) / 16.0 - 0.5;

    int pal = int(params.PALETTE + 0.5);
    vec3 outColor;
    if (params.MATCH_MODE > 0.5)
    {
        // En mode luminosite, la palette 16 couleurs devient les 4 gris CGA
        outColor = nearestByLuma(luma(c) + threshold * 0.5 * params.DITHER, pal);
    }
    else
    {
        float spread = (pal >= 5 ? 0.25 : 0.5) * params.DITHER;
        outColor = nearestByColor(c + threshold * spread, pal);
    }

    FragColor = vec4(outColor, 1.0);
}
