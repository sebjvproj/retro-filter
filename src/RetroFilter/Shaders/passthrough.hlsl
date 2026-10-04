// Affiche la capture telle quelle (étape 1 du prototype).

Texture2D Source : register(t0);
SamplerState PointSampler : register(s0);

struct VSOut
{
    float4 pos : SV_Position;
    float2 uv  : TEXCOORD0;
};

// Triangle plein écran généré sans tampon de sommets.
VSOut VSMain(uint id : SV_VertexID)
{
    VSOut o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float4 PSMain(VSOut i) : SV_Target
{
    return float4(Source.Sample(PointSampler, i.uv).rgb, 1);
}
