#version 450

/*
    CGA retro filter, passe 1 : preparation a la resolution virtuelle
    (fixee dans le preset).

      1. moyenne 8x8 echantillons de l'image du jeu dans chaque gros pixel,
         en lumiere lineaire
      2. ajustement luminosite / contraste / saturation (en sRGB)
      3. stabilite : si le gros pixel n'a presque pas change depuis l'image
         precedente, on garde l'ancienne valeur. Les petites variations
         (bruit, mouvement de quelques pixels) ne font plus scintiller le
         tramage.
*/

layout(push_constant) uniform Push
{
    vec4 SourceSize;
    vec4 OriginalSize;
    vec4 OutputSize;
    uint FrameCount;
    float BRIGHTNESS;
    float CONTRAST;
    float SATURATION;
    float STABILITY;
} params;

#pragma parameter BRIGHTNESS "Luminosite" 0.0 -0.5 0.5 0.02
#pragma parameter CONTRAST "Contraste" 1.0 0.5 2.0 0.05
#pragma parameter SATURATION "Saturation" 1.0 0.0 3.0 0.05
#pragma parameter STABILITY "Stabilite (anti-scintillement)" 0.0 0.0 0.1 0.005

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
layout(set = 0, binding = 3) uniform sampler2D PassFeedback0;

vec3 toLinear(vec3 c)
{
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

vec3 toSrgb(vec3 c)
{
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}

void main()
{
    vec2 cell = floor(vTexCoord * params.OutputSize.xy);

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

    if (params.STABILITY > 0.0)
    {
        vec3 prev = texture(PassFeedback0, (cell + 0.5) * params.OutputSize.zw).rgb;
        vec3 diff = abs(c - prev);
        if (max(diff.r, max(diff.g, diff.b)) < params.STABILITY)
            c = prev;
    }

    FragColor = vec4(c, 1.0);
}
