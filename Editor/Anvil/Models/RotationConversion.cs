using System;
using Microsoft.Xna.Framework;

namespace Anvil.Models;

/// <summary>
/// Matrix ↔ Euler conversion matching the engine's convention.
///
/// The engine builds rotations as <c>Rx(α) * Ry(β) * Rz(γ)</c> using XNA's
/// CreateRotationX/Y/Z, which produce row-major matrices where
/// CreateRotationX(θ) yields:
/// <code>
/// 1   0       0       0
/// 0   cos(θ)  sin(θ)  0
/// 0  -sin(θ)  cos(θ)  0
/// 0   0       0       1
/// </code>
/// The compound matrix has these informative entries:
///   M13 = -sin(β)
///   M23 = sin(α)·cos(β), M33 = cos(α)·cos(β)
///   M11 = cos(β)·cos(γ), M12 = cos(β)·sin(γ)
/// so the inverse is α = atan2(M23, M33), β = atan2(-M13, sqrt(M11²+M12²)),
/// γ = atan2(M12, M11). Using the wrong-sign formulas (the standard "ZYX
/// column-vector" derivation) flips angles by 180° on display.
/// </summary>
public static class RotationConversion
{
    public static (double XDeg, double YDeg, double ZDeg) MatrixToEuler(Matrix m)
    {
        double cosBeta = Math.Sqrt((double)m.M11 * m.M11 + (double)m.M12 * m.M12);
        bool singular = cosBeta < 1e-6;

        double x, y, z;
        if (!singular)
        {
            x = Math.Atan2(m.M23, m.M33);
            y = Math.Atan2(-m.M13, cosBeta);
            z = Math.Atan2(m.M12, m.M11);
        }
        else
        {
            // Gimbal lock: cos(β) ≈ 0. Fall back to a stable but lossy
            // decomposition that puts all roll into X.
            x = Math.Atan2(-m.M32, m.M22);
            y = Math.Atan2(-m.M13, cosBeta);
            z = 0;
        }

        return (RadToDeg(x), RadToDeg(y), RadToDeg(z));
    }

    public static Matrix EulerToMatrix(double xDeg, double yDeg, double zDeg)
    {
        float x = (float)DegToRad(xDeg);
        float y = (float)DegToRad(yDeg);
        float z = (float)DegToRad(zDeg);
        return Matrix.CreateRotationX(x) * Matrix.CreateRotationY(y) * Matrix.CreateRotationZ(z);
    }

    private static double RadToDeg(double rad) => rad * (180.0 / Math.PI);
    private static double DegToRad(double deg) => deg * (Math.PI / 180.0);
}
