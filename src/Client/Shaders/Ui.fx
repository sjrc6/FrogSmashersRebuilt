float4x4 MatrixTransform;
float TextAaWidth = 1;
Texture2D SpriteTexture;
sampler2D SpriteSampler = sampler_state
{
    Texture = <SpriteTexture>;
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
    float GlyphRange : TEXCOORD1;
};

VertexOutput VertexShaderFunction(VertexInput input)
{
    VertexOutput output;
    output.GlyphRange = input.Position.z;
    input.Position.z = 0;
    output.Position = mul(input.Position, MatrixTransform);
    output.Color = input.Color;
    output.UV = input.UV;
    return output;
}

float Median(float3 value)
{
    return max(min(value.r, value.g), min(max(value.r, value.g), value.b));
}

float4 PixelShaderFunction(VertexOutput input) : COLOR0
{
    float4 sample = tex2D(SpriteSampler, input.UV);
    float2 screenTextureSize = 1.0 / max(fwidth(input.UV), float2(0.0000001, 0.0000001));
    float screenRange = max(0.5 * dot(input.GlyphRange.xx, screenTextureSize), 1.0);
    if (input.GlyphRange > 0)
    {
        float distance = Median(sample.rgb) - 0.5;
        float coverage = TextAaWidth <= 0
            ? step(0, distance)
            : saturate(screenRange * distance / max(TextAaWidth, 0.00001) + 0.5);
        return float4(input.Color.rgb, input.Color.a * coverage);
    }

    return sample * input.Color;
}

technique Ui
{
    pass P0
    {
        VertexShader = compile vs_3_0 VertexShaderFunction();
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
