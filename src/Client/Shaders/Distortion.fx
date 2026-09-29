float4x4 MatrixTransform;
float2 Resolution;
float Time;
float4 ImpactRect;
float4 ImpactOffset;
float Shimmer;
float4 ShimmerRect;
float4 ShimmerParams;
float4 NoiseTransform;
float PackedNormal;
Texture2D SpriteTexture;
Texture2D NoiseTexture;
Texture2D MaskTexture;

sampler2D SpriteSampler = sampler_state
{
    Texture = <SpriteTexture>;
    AddressU = Clamp;
    AddressV = Clamp;
    MinFilter = Linear;
    MagFilter = Linear;
};

sampler2D NoiseSampler = sampler_state
{
    Texture = <NoiseTexture>;
    AddressU = Wrap;
    AddressV = Wrap;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
};

sampler2D MaskSampler = sampler_state
{
    Texture = <MaskTexture>;
    AddressU = Clamp;
    AddressV = Clamp;
    MinFilter = Linear;
    MagFilter = Linear;
};

struct VertexInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 UV : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 UV : TEXCOORD0;
};

VertexOutput VertexMain(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(input.Position, MatrixTransform);
    output.Color = input.Color;
    output.UV = input.UV;
    return output;
}

float4 PixelMain(VertexOutput input) : COLOR0
{
    float2 local = (input.UV - ImpactRect.xy) / max(ImpactRect.zw, float2(.00001, .00001));
    float inside = step(0, local.x) * step(0, local.y) * step(local.x, 1) * step(local.y, 1) * step(.00001, ImpactRect.z);
    float mask = tex2D(MaskSampler, local).a * inside;
    float2 offset = ImpactOffset.xy * mask * ImpactOffset.z / Resolution;
    if (Shimmer > .5)
    {
        local = (input.UV - ShimmerRect.xy) / max(ShimmerRect.zw, float2(.00001, .00001));
        inside = step(0, local.x) * step(0, local.y) * step(local.x, 1) * step(local.y, 1);
        float2 noiseUV = (float2(local.x, 1 - local.y) + Time / 20 * ShimmerParams.zw) * NoiseTransform.xy + NoiseTransform.zw;
        noiseUV.y = 1 - noiseUV.y;
        float4 packed = tex2D(NoiseSampler, noiseUV);
        float2 normal = (PackedNormal > .5 ? packed.ag : packed.rg) * 2 - 1;
        offset += normal * ShimmerParams.xy / Resolution * inside;
    }
    return tex2D(SpriteSampler, saturate(input.UV + offset)) * input.Color;
}

technique Distortion
{
    pass P0
    {
        VertexShader = compile vs_3_0 VertexMain();
        PixelShader = compile ps_3_0 PixelMain();
    }
}
