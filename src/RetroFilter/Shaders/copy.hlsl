// Recopie une texture sans lissage : agrandit l'image filtrée, ou affiche la capture brute.

Texture2D Source : register(t0);
SamplerState PointSampler : register(s0);

float4 PSMain(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    return float4(Source.Sample(PointSampler, uv).rgb, 1);
}
