namespace FrogSmashers.Core;

public readonly record struct Fixed(long Raw) : IComparable<Fixed>
{
    public const long Unit = 1L << 24;
    public static readonly Fixed Zero = new(0);
    public static readonly Fixed One = new(Unit);

    public static Fixed FromDecimal(decimal value) =>
        new(decimal.ToInt64(decimal.Round(value * Unit, 0, MidpointRounding.AwayFromZero)));

    public static implicit operator Fixed(int value) => new(value * Unit);

    public static Fixed operator +(Fixed a, Fixed b) => new(checked(a.Raw + b.Raw));

    public static Fixed operator -(Fixed a, Fixed b) => new(checked(a.Raw - b.Raw));

    public static Fixed operator -(Fixed a) => new(checked(-a.Raw));

    public static Fixed operator *(Fixed a, Fixed b) => new(checked((long)((Int128)a.Raw * b.Raw / Unit)));

    public static Fixed operator /(Fixed a, Fixed b) => new(checked((long)((Int128)a.Raw * Unit / b.Raw)));

    public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;

    public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;

    public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;

    public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

    public static Fixed Abs(Fixed x) => x.Raw < 0 ? -x : x;

    public static Fixed Min(Fixed a, Fixed b) => a < b ? a : b;

    public static Fixed Max(Fixed a, Fixed b) => a > b ? a : b;

    public static Fixed Clamp(Fixed value, Fixed min, Fixed max) => Max(min, Min(max, value));

    public static Fixed Lerp(Fixed a, Fixed b, Fixed t) => a + (b - a) * Clamp(t, 0, 1);

    public static Fixed MoveTowards(Fixed value, Fixed target, Fixed amount) =>
        value < target ? Min(value + amount, target) : Max(value - amount, target);

    public static Fixed Sqrt(Fixed value)
    {
        if (value.Raw < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        UInt128 n = (UInt128)value.Raw * Unit;
        UInt128 result = 0;
        UInt128 bit = (UInt128)1 << 126;
        while (bit > n)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            if (n >= result + bit)
            {
                n -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }

            bit >>= 2;
        }

        return new((long)result);
    }

    public float ToFloat() => (float)Raw / Unit;

    public double ToDouble() => (double)Raw / Unit;

    public decimal ToDecimal() => (decimal)Raw / Unit;

    public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);

    public override string ToString() =>
        ToDecimal().ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
