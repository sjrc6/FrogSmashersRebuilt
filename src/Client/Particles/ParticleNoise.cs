using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using NumericsVector3 = System.Numerics.Vector3;

namespace FrogSmashers.Client;

public static class ParticleNoise
{
    private static readonly byte[] Permutation =
    [
        151,
        160,
        137,
        91,
        90,
        15,
        131,
        13,
        201,
        95,
        96,
        53,
        194,
        233,
        7,
        225,
        140,
        36,
        103,
        30,
        69,
        142,
        8,
        99,
        37,
        240,
        21,
        10,
        23,
        190,
        6,
        148,
        247,
        120,
        234,
        75,
        0,
        26,
        197,
        62,
        94,
        252,
        219,
        203,
        117,
        35,
        11,
        32,
        57,
        177,
        33,
        88,
        237,
        149,
        56,
        87,
        174,
        20,
        125,
        136,
        171,
        168,
        68,
        175,
        74,
        165,
        71,
        134,
        139,
        48,
        27,
        166,
        77,
        146,
        158,
        231,
        83,
        111,
        229,
        122,
        60,
        211,
        133,
        230,
        220,
        105,
        92,
        41,
        55,
        46,
        245,
        40,
        244,
        102,
        143,
        54,
        65,
        25,
        63,
        161,
        1,
        216,
        80,
        73,
        209,
        76,
        132,
        187,
        208,
        89,
        18,
        169,
        200,
        196,
        135,
        130,
        116,
        188,
        159,
        86,
        164,
        100,
        109,
        198,
        173,
        186,
        3,
        64,
        52,
        217,
        226,
        250,
        124,
        123,
        5,
        202,
        38,
        147,
        118,
        126,
        255,
        82,
        85,
        212,
        207,
        206,
        59,
        227,
        47,
        16,
        58,
        17,
        182,
        189,
        28,
        42,
        223,
        183,
        170,
        213,
        119,
        248,
        152,
        2,
        44,
        154,
        163,
        70,
        221,
        153,
        101,
        155,
        167,
        43,
        172,
        9,
        129,
        22,
        39,
        253,
        19,
        98,
        108,
        110,
        79,
        113,
        224,
        232,
        178,
        185,
        112,
        104,
        218,
        246,
        97,
        228,
        251,
        34,
        242,
        193,
        238,
        210,
        144,
        12,
        191,
        179,
        162,
        241,
        81,
        51,
        145,
        235,
        249,
        14,
        239,
        107,
        49,
        192,
        214,
        31,
        181,
        199,
        106,
        157,
        184,
        84,
        204,
        176,
        115,
        121,
        50,
        45,
        127,
        4,
        150,
        254,
        138,
        236,
        205,
        93,
        222,
        114,
        67,
        29,
        24,
        72,
        243,
        141,
        128,
        195,
        78,
        66,
        215,
        61,
        156,
        180,
    ];
    private static readonly NumericsVector3[] Gradients =
    [
        new(1, 1, 0),
        new(-1, 1, 0),
        new(1, -1, 0),
        new(-1, -1, 0),
        new(1, 0, 1),
        new(-1, 0, 1),
        new(1, 0, -1),
        new(-1, 0, -1),
        new(0, 1, 1),
        new(0, -1, 1),
        new(0, 1, -1),
        new(0, -1, -1),
        new(1, 1, 0),
        new(-1, 1, 0),
        new(0, -1, 1),
        new(0, -1, -1),
    ];

    public static Vector3 Offset(uint seed)
    {
        var random = new ParticleRandom(seed);
        return new Vector3(random.NextFloat(), random.NextFloat(), random.NextFloat()) * 100;
    }

    public static Vector3 Velocity(
        Vector3 position,
        Vector3 offset,
        float frequency,
        float strength,
        bool damping = true
    )
    {
        if (strength == 0)
        {
            return Vector3.Zero;
        }

        frequency = Math.Max(frequency, .0001f);
        var p = position + offset;
        var a = Derivative(new NumericsVector3(p.Z, p.Y, p.X) * frequency);
        var b = Derivative(new NumericsVector3(p.X + 100, p.Z, p.Y) * frequency);
        var c = Derivative(new NumericsVector3(p.Y, p.X + 100, p.Z) * frequency);
        return new Vector3(c.X - b.Y, a.X - c.Y, b.X - a.Y) * (damping ? strength : strength * frequency);
    }

    private static Vector2 Derivative(NumericsVector3 point)
    {
        int x = (int)MathF.Floor(point.X);
        int y = (int)MathF.Floor(point.Y);
        int z = (int)MathF.Floor(point.Z);
        point -= new NumericsVector3(x, y, z);
        float u = Fade(point.X);
        float v = Fade(point.Y);
        float w = Fade(point.Z);
        float du = Slope(point.X);
        float dv = Slope(point.Y);
        var a = MixX(Corner(x, y, z, point), Corner(x + 1, y, z, point - NumericsVector3.UnitX), u, du);
        var b = MixX(
            Corner(x, y + 1, z, point - NumericsVector3.UnitY),
            Corner(x + 1, y + 1, z, point - new NumericsVector3(1, 1, 0)),
            u,
            du
        );
        var c = MixX(
            Corner(x, y, z + 1, point - NumericsVector3.UnitZ),
            Corner(x + 1, y, z + 1, point - new NumericsVector3(1, 0, 1)),
            u,
            du
        );
        var d = MixX(
            Corner(x, y + 1, z + 1, point - new NumericsVector3(0, 1, 1)),
            Corner(x + 1, y + 1, z + 1, point - NumericsVector3.One),
            u,
            du
        );
        var result = NumericsVector3.Lerp(MixY(a, b, v, dv), MixY(c, d, v, dv), w);
        return new(result.Y, result.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NumericsVector3 Corner(int x, int y, int z, NumericsVector3 point)
    {
        int h = Permutation[(Permutation[(Permutation[x & 255] + y) & 255] + z) & 255] & 15;
        var g = Gradients[h];
        return new(NumericsVector3.Dot(g, point), g.X, g.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NumericsVector3 MixX(NumericsVector3 a, NumericsVector3 b, float t, float slope)
    {
        var result = NumericsVector3.Lerp(a, b, t);
        result.Y += (b.X - a.X) * slope;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NumericsVector3 MixY(NumericsVector3 a, NumericsVector3 b, float t, float slope)
    {
        var result = NumericsVector3.Lerp(a, b, t);
        result.Z += (b.X - a.X) * slope;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Slope(float t) => 30 * t * t * (t - 1) * (t - 1);
}

public struct ParticleRandom
{
    private uint x;
    private uint y;
    private uint z;
    private uint w;

    public ParticleRandom(uint seed)
    {
        x = seed;
        y = unchecked(x * 1812433253u + 1);
        z = unchecked(y * 1812433253u + 1);
        w = unchecked(z * 1812433253u + 1);
    }

    public uint Next()
    {
        uint t = x ^ (x << 11);
        x = y;
        y = z;
        z = w;
        return w = w ^ (w >> 19) ^ t ^ (t >> 8);
    }

    public float NextFloat() => (Next() & 0x7fffff) * 1.1920930376163597e-7f;
}
