#version 450

/*
    CGA retro filter, passe 3 : agrandit l'image a la taille de l'ecran,
    sans lissage (filter_linear2 = false dans le preset). En option, garde
    le format 4:3 des moniteurs d'epoque avec des bandes noires.
*/

layout(push_constant) uniform Push
{
    vec4 SourceSize;
    vec4 OriginalSize;
    vec4 OutputSize;
    uint FrameCount;
    float ASPECT_43;
} params;

#pragma parameter ASPECT_43 "Format 4:3 (bandes noires)" 0.0 0.0 1.0 1.0

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

void main()
{
    vec2 uv = vTexCoord;

    if (params.ASPECT_43 > 0.5)
    {
        float screen = params.OutputSize.x * params.OutputSize.w;
        vec2 size = screen > 4.0 / 3.0
            ? vec2((4.0 / 3.0) / screen, 1.0)
            : vec2(1.0, screen / (4.0 / 3.0));
        uv = (uv - 0.5) / size + 0.5;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0))))
        {
            FragColor = vec4(0.0, 0.0, 0.0, 1.0);
            return;
        }
    }

    FragColor = vec4(texture(Source, uv).rgb, 1.0);
}
