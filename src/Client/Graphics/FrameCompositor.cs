using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

internal sealed class FrameCompositor : IDisposable
{
    private const int Width = Renderer.Width;
    private const int Height = Renderer.Height;
    private readonly GraphicsDevice device;
    private readonly Assets assets;
    private readonly SpriteBatch batch;
    private RenderTarget2D scene;
    private RenderTarget2D output;
    private RenderTarget2D scratch;
    public Texture2D Frame => output;

    public FrameCompositor(GraphicsDevice device, Assets assets)
    {
        this.device = device;
        this.assets = assets;
        batch = new SpriteBatch(device);
        scene = NewTarget(Width, Height);
        output = NewTarget(Width, Height);
        scratch = NewTarget(Width, Height);
        assets
            .DistortionEffect.Parameters["MatrixTransform"]
            .SetValue(Matrix.CreateOrthographicOffCenter(0, Width, Height, 0, 0, 1));
        assets.DistortionEffect.Parameters["Resolution"].SetValue(new Vector2(Width, Height));
    }

    public void BeginScene(Color background)
    {
        EnsureSize();
        device.SetRenderTarget(scene);
        device.Clear(background);
    }

    public void BeginOutput()
    {
        EnsureSize();
        device.SetRenderTarget(output);
    }

    public void ComposeScene()
    {
        (scene, output) = (output, scene);
        device.SetRenderTarget(output);
        assets.DistortionEffect.Parameters["Shimmer"].SetValue(0f);
    }

    public void ApplyImpact(Texture2D mask, Vector4 region, Vector4 offset)
    {
        var effect = assets.DistortionEffect;
        effect.Parameters["MaskTexture"].SetValue(mask);
        effect.Parameters["ImpactRect"].SetValue(region);
        effect.Parameters["ImpactOffset"].SetValue(offset);
        device.SetRenderTarget(scratch);
        batch.Begin(effect: effect, samplerState: SamplerState.LinearClamp, blendState: BlendState.Opaque);
        batch.Draw(output, new Rectangle(0, 0, Width, Height), Color.White);
        batch.End();
        (output, scratch) = (scratch, output);
    }

    private RenderTarget2D NewTarget(int width, int height) =>
        new(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 4, RenderTargetUsage.DiscardContents);

    private void EnsureSize()
    {
        float scale = Math.Min(
            device.PresentationParameters.BackBufferWidth / (float)Width,
            device.PresentationParameters.BackBufferHeight / (float)Height
        );
        int width = Math.Max(1, (int)(Width * scale));
        int height = Math.Max(1, (int)(Height * scale));
        if (output.Width == width && output.Height == height)
        {
            return;
        }

        device.SetRenderTarget(null);
        scene.Dispose();
        output.Dispose();
        scratch.Dispose();
        scene = NewTarget(width, height);
        output = NewTarget(width, height);
        scratch = NewTarget(width, height);
        assets.DistortionEffect.Parameters["Resolution"].SetValue(new Vector2(width, height));
    }

    public void ApplyShimmer(MaterialData data, Vector4 rectangle, float elapsed)
    {
        var fx = assets.DistortionEffect;
        fx.Parameters["Time"].SetValue(elapsed);
        fx.Parameters["Shimmer"].SetValue(1f);
        fx.Parameters["ShimmerRect"].SetValue(rectangle);
        fx.Parameters["ShimmerParams"]
            .SetValue(
                new Vector4(
                    data.Parameters.GetValueOrDefault("_xAmount", 1),
                    data.Parameters.GetValueOrDefault("_yAmount", 1),
                    data.Parameters.GetValueOrDefault("_xSpeed", 1),
                    data.Parameters.GetValueOrDefault("_ySpeed", 1)
                )
            );
        bool hasNoise = data.Textures.TryGetValue("_Noise1", out var path);
        fx.Parameters["NoiseTexture"].SetValue(hasNoise ? assets.Texture(path!) : assets.White);
        fx.Parameters["PackedNormal"].SetValue(data.Parameters.GetValueOrDefault("PackedNormal"));
        var st = data.TextureTransforms.GetValueOrDefault("_Noise1", [1, 1, 0, 0]);
        fx.Parameters["NoiseTransform"].SetValue(new Vector4(st[0], st[1], st[2], st[3]));
        fx.Parameters["ImpactRect"].SetValue(Vector4.Zero);
        fx.Parameters["ImpactOffset"].SetValue(Vector4.Zero);
        device.SetRenderTarget(scratch);
        device.Clear(Color.Black);
        batch.Begin(effect: fx, samplerState: SamplerState.LinearClamp, blendState: BlendState.Opaque);
        batch.Draw(scene, new Rectangle(0, 0, Width, Height), Color.White);
        batch.End();
        (scene, scratch) = (scratch, scene);
    }

    public void DrawPresentation(Texture2D texture)
    {
        EnsureSize();
        device.SetRenderTarget(output);
        device.Clear(Color.Black);
        batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
        batch.Draw(texture, new Rectangle(0, 0, output.Width, output.Height), Color.White);
        batch.End();
    }

    public void Present()
    {
        device.SetRenderTarget(null);
        device.Clear(Color.Black);
        float scale = Math.Min(
            device.PresentationParameters.BackBufferWidth / (float)Width,
            device.PresentationParameters.BackBufferHeight / (float)Height
        );
        var target = new Rectangle(
            (int)((device.PresentationParameters.BackBufferWidth - Width * scale) * .5f),
            (int)((device.PresentationParameters.BackBufferHeight - Height * scale) * .5f),
            (int)(Width * scale),
            (int)(Height * scale)
        );
        batch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Opaque);
        batch.Draw(output, target, Color.White);
        batch.End();
    }

    public void Dispose()
    {
        device.SetRenderTarget(null);
        batch.Dispose();
        scene.Dispose();
        output.Dispose();
        scratch.Dispose();
    }
}
