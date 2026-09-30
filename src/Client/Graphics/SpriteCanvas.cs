using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

internal sealed class SpriteCanvas : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly Assets assets;
    private readonly GameCamera camera;
    private readonly SpriteBatch batch;
    private readonly SpriteMesh mesh = new();
    public SpriteBatch Batch => batch;
    public GraphicsDevice Device => device;
    public Assets Assets => assets;

    public SpriteCanvas(GraphicsDevice device, Assets assets, GameCamera camera)
    {
        this.device = device;
        this.assets = assets;
        this.camera = camera;
        batch = new SpriteBatch(device);
    }

    public Vector2 Screen(Vector2 position) => camera.ToScreen(position);

    public void BeginFont()
    {
        batch.Begin(
            blendState: BlendState.NonPremultiplied,
            samplerState: SamplerState.PointClamp,
            transformMatrix: Matrix.CreateScale(
                device.Viewport.Width / (float)Renderer.Width,
                device.Viewport.Height / (float)Renderer.Height,
                1
            )
        );
    }

    public void Begin(float mode)
    {
        assets.SpriteEffect.Parameters["Mode"].SetValue(mode);
        batch.Begin(
            SpriteSortMode.Immediate,
            mode < 0 ? BlendState.NonPremultiplied : BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            assets.SpriteEffect
        );
    }

    public void End() => batch.End();

    public void DrawSprite(string? id, Vector2 pos, Color color, Vector2 scale, float rotation, bool forceQuad = false)
    {
        if (id == null || !assets.Data.Sprites.TryGetValue(id, out var s))
        {
            return;
        }

        var tex = assets.Texture(s.Path);
        device.SamplerStates[0] = s.PointFilter ? SamplerState.PointClamp : SamplerState.LinearClamp;
        if (
            !forceQuad
            && mesh.Draw(device, assets.SpriteEffect, tex, s, Screen(pos), camera.PixelsPerUnit, scale, rotation, color)
        )
        {
            return;
        }

        int w = s.RectWidth > 0 ? s.RectWidth : s.Width;
        int h = s.RectHeight > 0 ? s.RectHeight : s.Height;
        var source = new Rectangle(s.RectX, s.RectY, w, h);
        var origin = new Vector2(w * s.PivotX, h * (1 - s.PivotY));
        var flip = SpriteEffects.None;
        if (scale.X < 0)
        {
            flip |= SpriteEffects.FlipHorizontally;
            origin.X = w - origin.X;
        }

        if (scale.Y < 0)
        {
            flip |= SpriteEffects.FlipVertically;
            origin.Y = h - origin.Y;
        }

        batch.Draw(
            tex,
            Screen(pos),
            source,
            color,
            -rotation,
            origin,
            new Vector2(MathF.Abs(scale.X), MathF.Abs(scale.Y))
                * camera.PixelsPerUnit
                / Math.Max(.01f, s.PixelsPerUnit),
            flip,
            0
        );
    }

    public bool SpriteVisible(string? id, Vector2 pos, Vector2 scale, float rotation)
    {
        if (id == null || !assets.Data.Sprites.TryGetValue(id, out var s))
        {
            return false;
        }

        if (s.Vertices.Length > 0)
        {
            Vector2 min = new(float.MaxValue);
            Vector2 max = new(float.MinValue);
            float cosine = MathF.Cos(rotation);
            float sine = MathF.Sin(rotation);
            foreach (var v in s.Vertices)
            {
                float x = v[0] * scale.X;
                float y = v[1] * scale.Y;
                var point = Screen(pos + new Vector2(x * cosine - y * sine, x * sine + y * cosine));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return max.X >= 0 && min.X <= GameCamera.Width && max.Y >= 0 && min.Y <= GameCamera.Height;
        }

        float w = s.RectWidth / s.PixelsPerUnit * MathF.Abs(scale.X);
        float h = s.RectHeight / s.PixelsPerUnit * MathF.Abs(scale.Y);
        float c = MathF.Abs(MathF.Cos(rotation));
        float sn = MathF.Abs(MathF.Sin(rotation));
        var half = new Vector2(w * c + h * sn, w * sn + h * c) * .5f * camera.PixelsPerUnit;
        var center = Screen(pos);
        return center.X + half.X >= 0
            && center.X - half.X <= GameCamera.Width
            && center.Y + half.Y >= 0
            && center.Y - half.Y <= GameCamera.Height;
    }

    public void Outline(Rectangle r, Color c, int width)
    {
        batch.Draw(assets.White, new Rectangle(r.X, r.Y, r.Width, width), c);
        batch.Draw(assets.White, new Rectangle(r.X, r.Bottom - width, r.Width, width), c);
        batch.Draw(assets.White, new Rectangle(r.X, r.Y, width, r.Height), c);
        batch.Draw(assets.White, new Rectangle(r.Right - width, r.Y, width, r.Height), c);
    }

    public void Dispose() => batch.Dispose();
}
