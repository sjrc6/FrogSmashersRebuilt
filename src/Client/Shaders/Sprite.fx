float4x4 MatrixTransform;
float Mode;
float2 PaletteReplace;
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
    float4 color = tex2D(SpriteSampler, input.UV);
    if (Mode < -.5)
    {
        return color * input.Color;
    }
    if (Mode > 2.5)
    {
        float alpha = color.a * input.Color.a;
        return float4(input.Color.rgb * alpha, alpha);
    }
    if (Mode > 1.5)
    {
        clip(color.a - .5);
        return input.Color;
    }
    if (Mode > .5)
    {
        clip(color.a - .5);
        float red = floor(color.r * 255.0 + .5);
        float2 palette = floor(PaletteReplace * 255.0 + .5);
        if (red == palette.x)
        {
            color.rgb = input.Color.rgb;
            red = floor(input.Color.r * 255.0 + .5);
        }
        if (red == palette.y)
        {
            color.rgb = input.Color.rgb * .5;
        }
        return float4(color.rgb, 1);
    }
    color *= input.Color;
    color.rgb *= color.a;
    return color;
}

technique Sprite
{
    pass P0
    {
        VertexShader = compile vs_3_0 VertexMain();
        PixelShader = compile ps_3_0 PixelMain();
    }
}
