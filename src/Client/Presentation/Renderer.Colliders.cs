using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

public sealed partial class Renderer
{
    private void DrawColliders(World world)
    {
        canvas.Begin(-1);
        foreach (var box in world.Map.Collision)
            ColliderBox(
                new((float)box.X, (float)box.Y),
                new((float)box.Width / 2, (float)box.Height / 2),
                box.OneWay ? Color.Cyan : Color.Lime
            );

        foreach (var player in world.Players.Where(player => player.Alive))
        {
            ColliderBox(ColliderPoint(player.Center), Vector2.One, Color.Yellow);
            if (
                player.Mode == CharacterMode.Tongue
                && player.TonguePhase is not (TonguePhase.Stunned or TonguePhase.Burping)
            )
            {
                var tip = ColliderPoint(player.TongueTip);
                bool extending = player.TonguePhase == TonguePhase.Extending && player.TongueDelayLeft <= 0;
                float opacity = extending ? 1 : .4f;
                ColliderLine(ColliderPoint(player.TongueOrigin), tip, Color.Magenta * .4f);
                ColliderArc(tip, .5f, 0, MathHelper.TwoPi, Color.Magenta * opacity);
                ColliderArc(tip, .75f, 0, MathHelper.TwoPi, Color.Violet * opacity);
                ColliderArc(tip, 1, 0, MathHelper.TwoPi, Color.Red * opacity);
            }

            if (player.Mode == CharacterMode.Attacking && player.AttackPhase != AttackPhase.Idle)
            {
                var (start, end, radius) = world.GetBatHitbox(player);
                var color = player.AttackPhase == AttackPhase.Charging ? Color.Orange * .5f : Color.Orange;
                ColliderCapsule(ColliderPoint(start), ColliderPoint(end), radius.ToFloat(), color);
            }
        }

        if (world.Rules.Lobby && world.BeachBall.Active)
            ColliderArc(
                ColliderPoint(world.BeachBall.Position),
                BeachBallState.Radius.ToFloat(),
                0,
                MathHelper.TwoPi,
                Color.White
            );
        if (world.Fly.Active)
            ColliderBox(ColliderPoint(world.Fly.Position), new(.75f), Color.DeepSkyBlue);
        canvas.End();
    }

    private static Vector2 ColliderPoint(FixedVector point) => new(point.X.ToFloat(), point.Y.ToFloat());

    private void ColliderBox(Vector2 center, Vector2 halfSize, Color color)
    {
        var topLeft = center + new Vector2(-halfSize.X, halfSize.Y);
        var topRight = center + halfSize;
        var bottomLeft = center - halfSize;
        var bottomRight = center + new Vector2(halfSize.X, -halfSize.Y);
        ColliderLine(topLeft, topRight, color);
        ColliderLine(topRight, bottomRight, color);
        ColliderLine(bottomRight, bottomLeft, color);
        ColliderLine(bottomLeft, topLeft, color);
    }

    private void ColliderCapsule(Vector2 start, Vector2 end, float radius, Color color)
    {
        float angle = MathF.Atan2(end.Y - start.Y, end.X - start.X);
        var side = new Vector2(-MathF.Sin(angle), MathF.Cos(angle)) * radius;
        ColliderLine(start + side, end + side, color);
        ColliderLine(start - side, end - side, color);
        ColliderArc(start, radius, angle + MathHelper.PiOver2, MathHelper.Pi, color);
        ColliderArc(end, radius, angle - MathHelper.PiOver2, MathHelper.Pi, color);
    }

    private void ColliderArc(Vector2 center, float radius, float start, float sweep, Color color)
    {
        int segments = (int)MathF.Ceiling(sweep / MathHelper.TwoPi * 48);
        var previous = center + new Vector2(MathF.Cos(start), MathF.Sin(start)) * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = start + sweep * i / segments;
            var next = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            ColliderLine(previous, next, color);
            previous = next;
        }
    }

    private void ColliderLine(Vector2 start, Vector2 end, Color color)
    {
        var position = Screen(start);
        var delta = Screen(end) - position;
        Batch.Draw(
            assets.White,
            position,
            null,
            color,
            MathF.Atan2(delta.Y, delta.X),
            new Vector2(0, .5f),
            new Vector2(delta.Length(), 2),
            SpriteEffects.None,
            0
        );
    }
}
