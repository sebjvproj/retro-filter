#version 450

/*
    CGA retro filter, version 2 (deux passes), conservee pour comparaison.
    Passe 1 : calcul a la resolution virtuelle (320x200, fixee dans le
    preset). La passe 2 (cga-output.slang) agrandit le resultat sans lissage.

    Pour chaque gros pixel :
      1. moyenne 8x8 echantillons de l'image source, en lumiere lineaire
      2. ajustement luminosite / contraste / saturation (en sRGB)
      3. tramage de Yliluoma : on cherche la paire de couleurs de la palette
         et la proportion de melange qui imitent le mieux le pixel, l'erreur
         etant mesuree dans l'espace OKLab
      4. la matrice de Bayer decide laquelle des deux couleurs afficher

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
    float MATCH_MODE;
    float DITHER_LEVELS;
    float MIX_PENALTY;
    float BRIGHTNESS;
    float CONTRAST;
    float SATURATION;
} params;

#pragma parameter PALETTE "Palette (0-5, voir README)" 0.0 0.0 5.0 1.0
#pragma parameter MATCH_MODE "Correspondance: 0=couleur 1=luminosite" 0.0 0.0 1.0 1.0
#pragma parameter DITHER_LEVELS "Niveaux de tramage (1 = aucun)" 16.0 1.0 16.0 1.0
#pragma parameter MIX_PENALTY "Douceur du tramage" 0.1 0.0 0.5 0.01
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

vec3 toLinear(vec3 c)
{
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

vec3 toSrgb(vec3 c)
{
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
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
    return vec4(c * sqrt(chroma), dot(c, vec3(0.299, 0.587, 0.114)));
}

void main()
{
    vec2 cell = floor(vTexCoord * params.OutputSize.xy);

    // Moyenne 8x8 dans la cellule, en lumiere lineaire
    vec3 c = vec3(0.0);
    for (int y = 0; y < 8; y++)
    {
        for (int x = 0; x < 8; x++)
        {
            vec2 uv = (cell + (vec2(x, y) + 0.5) * 0.125) * params.OutputSize.zw;
            c += toLinear(texture(Source, uv).rgb);
        }
    }
    c = toSrgb(c * (1.0 / 64.0));

    c = (c - 0.5) * params.CONTRAST + 0.5 + params.BRIGHTNESS;
    c = mix(vec3(dot(c, vec3(0.299, 0.587, 0.114))), c, params.SATURATION);
    c = clamp(c, 0.0, 1.0);

    int pal = min(int(params.PALETTE + 0.5), 5);
    float chroma = params.MATCH_MODE > 0.5 ? 0.0 : 1.0;
    // En mode luminosite, la palette 16 couleurs devient les 4 gris CGA
    bool full16 = pal == 5 && chroma > 0.0;
    int count = full16 ? 16 : 4;
    float levels = max(floor(params.DITHER_LEVELS + 0.5), 1.0);

    vec3 colors[16];
    vec3 labs[16];
    for (int i = 0; i < count; i++)
    {
        colors[i] = CGA16[full16 ? i : PAL4[pal * 4 + i]];
        labs[i] = oklab(colors[i]);
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

    ivec2 b = ivec2(mod(cell, 4.0));
    float threshold = (BAYER4[b.y * 4 + b.x] + 0.5) / 16.0;

    FragColor = vec4(bestRatio > threshold ? bestB : bestA, 1.0);
}
