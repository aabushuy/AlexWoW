namespace AlexWoW.MapExtractor;

public readonly record struct Vec3(float X, float Y, float Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

/// <summary>3×3 матрица поворота (соглашения G3D, как в vmap CMaNGOS).</summary>
public readonly struct Matrix3
{
    private readonly float _m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22;

    private Matrix3(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22)
    {
        _m00 = m00; _m01 = m01; _m02 = m02;
        _m10 = m10; _m11 = m11; _m12 = m12;
        _m20 = m20; _m21 = m21; _m22 = m22;
    }

    public Vec3 Mul(Vec3 v) => new(
        _m00 * v.X + _m01 * v.Y + _m02 * v.Z,
        _m10 * v.X + _m11 * v.Y + _m12 * v.Z,
        _m20 * v.X + _m21 * v.Y + _m22 * v.Z);

    public static Matrix3 operator *(Matrix3 a, Matrix3 b) => new(
        a._m00 * b._m00 + a._m01 * b._m10 + a._m02 * b._m20,
        a._m00 * b._m01 + a._m01 * b._m11 + a._m02 * b._m21,
        a._m00 * b._m02 + a._m01 * b._m12 + a._m02 * b._m22,
        a._m10 * b._m00 + a._m11 * b._m10 + a._m12 * b._m20,
        a._m10 * b._m01 + a._m11 * b._m11 + a._m12 * b._m21,
        a._m10 * b._m02 + a._m11 * b._m12 + a._m12 * b._m22,
        a._m20 * b._m00 + a._m21 * b._m10 + a._m22 * b._m20,
        a._m20 * b._m01 + a._m21 * b._m11 + a._m22 * b._m21,
        a._m20 * b._m02 + a._m21 * b._m12 + a._m22 * b._m22);

    private static Matrix3 RotZ(float a) { var c = MathF.Cos(a); var s = MathF.Sin(a); return new(c, -s, 0, s, c, 0, 0, 0, 1); }
    private static Matrix3 RotY(float a) { var c = MathF.Cos(a); var s = MathF.Sin(a); return new(c, 0, s, 0, 1, 0, -s, 0, c); }
    private static Matrix3 RotX(float a) { var c = MathF.Cos(a); var s = MathF.Sin(a); return new(1, 0, 0, 0, c, -s, 0, s, c); }

    /// <summary>G3D fromEulerAnglesZYX(z,y,x) = Rz(z)·Ry(y)·Rx(x).</summary>
    public static Matrix3 FromEulerAnglesZYX(float z, float y, float x) => RotZ(z) * (RotY(y) * RotX(x));
}
