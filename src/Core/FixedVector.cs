namespace FrogSmashers.Core;

public readonly record struct FixedVector(Fixed X, Fixed Y)
{
    public Fixed LengthSquared => X * X + Y * Y;
    public Fixed Length => Fixed.Sqrt(LengthSquared);
    public FixedVector Normalized => LengthSquared == Fixed.Zero ? default : this / Length;

    public static FixedVector operator +(FixedVector a, FixedVector b) => new(a.X + b.X, a.Y + b.Y);

    public static FixedVector operator -(FixedVector a, FixedVector b) => new(a.X - b.X, a.Y - b.Y);

    public static FixedVector operator -(FixedVector a) => new(-a.X, -a.Y);

    public static FixedVector operator *(FixedVector a, Fixed b) => new(a.X * b, a.Y * b);

    public static FixedVector operator /(FixedVector a, Fixed b) => new(a.X / b, a.Y / b);

    public static Fixed Dot(FixedVector a, FixedVector b) => a.X * b.X + a.Y * b.Y;

    public override string ToString() => $"({X}, {Y})";
}
