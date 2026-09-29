using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class SceneTransform(SceneNodeData source)
{
    public readonly SceneNodeData Source = source;
    public SceneTransform? Parent;
    public Vector3 Position = Vector(source.LocalPosition, Vector3.Zero);
    public Vector3 Scale = Vector(source.LocalScale, Vector3.One);
    public Vector3 WorldPosition;
    public Vector3 WorldScale;
    public float Rotation = source.LocalRotation;
    public float WorldRotation;
    public bool Active = source.Active;
    public bool WorldActive;
    public bool Resolved;

    public void Reset()
    {
        Position = Vector(Source.LocalPosition, Vector3.Zero);
        Scale = Vector(Source.LocalScale, Vector3.One);
        Rotation = Source.LocalRotation;
        Active = Source.Active;
        Resolved = false;
    }

    public static void Resolve(SceneTransform node)
    {
        if (node.Resolved)
        {
            return;
        }

        if (node.Parent == null)
        {
            node.WorldPosition = node.Position;
            node.WorldScale = node.Scale;
            node.WorldRotation = node.Rotation;
            node.WorldActive = node.Active;
        }
        else
        {
            Resolve(node.Parent);
            var p = node.Parent;
            float cos = MathF.Cos(p.WorldRotation);
            float sin = MathF.Sin(p.WorldRotation);
            float x = node.Position.X * p.WorldScale.X;
            float y = node.Position.Y * p.WorldScale.Y;
            node.WorldPosition =
                p.WorldPosition + new Vector3(x * cos - y * sin, x * sin + y * cos, node.Position.Z * p.WorldScale.Z);
            node.WorldScale = node.Scale * p.WorldScale;
            node.WorldRotation = node.Rotation + p.WorldRotation;
            node.WorldActive = node.Active && p.WorldActive;
        }

        node.Resolved = true;
    }

    private static Vector3 Vector(float[] value, Vector3 fallback) =>
        value.Length >= 3 ? new(value[0], value[1], value[2]) : fallback;
}
