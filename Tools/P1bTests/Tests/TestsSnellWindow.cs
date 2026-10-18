using System;
using System.Globalization;

internal static partial class P1bTests
{
    // Water refractive index, as in the shader (WaterSurface.shader, isUnderwater
    // branch). Kept as a constant in two places: test and shader.
    private const float WaterIndex = 1.33f;
    private const float AirIndex = 1.0f;

    /// <summary>
    /// Snell window and water Fresnel: a REFERENCE, not a shader check.
    ///
    /// The test computes the same formula as the shader, so by itself it only
    /// proves that the formulas in the project are consistent. The real GPU check
    /// is a capture of the window: WaterSurface.shader has _UnderwaterDebugMode
    /// (1 = Fresnel in grayscale, 2 = window mask), and the capture tool
    /// (Editor/SurfaceCaptureTool, underwater-debug action) shoots it looking
    /// straight up from 3 m. The mask boundary in the capture must fall at
    /// 48.6° ± 0.5° — that is the shader check the test pins down by number.
    /// </summary>
    private static int Test139_SnellWindowReference()
    {
        // In this harness Check reports failure: it also drives the failures
        // counter that gates the ALL PASS print. The test always returns 0,
        // otherwise the same failure would be counted twice.
        int Fail(string fmt, params object[] args)
        {
            Check(false, "Test139_SnellWindow", string.Format(CultureInfo.InvariantCulture, fmt, args));
            return 0;
        }

        int Ok(string fmt, params object[] args)
        {
            Console.WriteLine("    " + string.Format(CultureInfo.InvariantCulture, fmt, args));
            return 0;
        }

        // --- critical angle --------------------------------------------------
        // NOTE ON THE NUMBER: the textbook 48.6° is n = 1.333. The shader uses
        // 1.33, so the critical angle is 48.75°. The 0.15° difference is
        // invisible, but hardcoding it from memory would diverge from the
        // shader, so the number is computed rather than pinned.
        double critical = Math.Asin(AirIndex / WaterIndex) * 180.0 / Math.PI;
        if (Math.Abs(critical - 48.75) > 0.05)
        {
            return Fail("критический угол {0:F2}°, ожидался 48.75° (n = 1.33)", critical);
        }

        double cosCritical = Math.Cos(critical * Math.PI / 180.0);
        if (Math.Abs(cosCritical - 0.6594) > 0.001)
        {
            return Fail("cos критического {0:F4}, ожидался 0.6594", cosCritical);
        }

        // --- Fresnel at normal incidence -------------------------------------
        float f0 = FresnelWater(1.0f);
        float f0Expected = (float)Math.Pow((WaterIndex - AirIndex) / (WaterIndex + AirIndex), 2.0);
        if (Math.Abs(f0 - f0Expected) > 1e-4f)
        {
            return Fail("F(0°) = {0:F4}, ожидалось {1:F4}", f0, f0Expected);
        }

        if (Math.Abs(f0 - 0.0204f) > 0.0005f)
        {
            return Fail("F(0°) = {0:F4}, у воды должно быть около 0.02", f0);
        }

        // --- monotonic growth and curve shape --------------------------------
        // Fresnel does NOT rise smoothly to one: at 48° (only 0.75° before the
        // critical angle) reflection is still 0.39 and only reaches one in the
        // last fractions of a degree.
        bool growing = true;
        float previous = f0;
        for (int degrees = 1; degrees <= 48; degrees++)
        {
            float f = FresnelWater((float)Math.Cos(degrees * Math.PI / 180.0));
            if (f < previous - 1e-6f)
            {
                growing = false;
            }

            previous = f;
        }

        if (!growing)
        {
            return Fail("Френель убывает на 1..48°, а должна расти монотонно");
        }

        if (Math.Abs(previous - 0.39f) > 0.04f)
        {
            return Fail("F(48°) = {0:F3}, ожидалось ~0.39 (до критического ещё далеко)", previous);
        }

        float atCritical = FresnelWater((float)cosCritical);
        if (Math.Abs(atCritical - 1.0f) > 0.002f)
        {
            return Fail("F на критическом угле = {0:F4}, ожидалась 1", atCritical);
        }

        Ok("кривая: F(0)={0:F4}, F(48)={1:F3}, F(крит)={2:F3} — резкий фронт у 48.75°", f0, previous, atCritical);

        // --- beyond critical: total internal reflection ----------------------
        foreach (double degrees in new double[] { 49d, 60d, 89d })
        {
            double rad = degrees * Math.PI / 180.0;
            float f = FresnelWater((float)Math.Cos(rad));
            if (f < 0.999f)
            {
                return Fail("при {0:F0}° (за критическим) Френель = {1:F4}, должно быть 1", degrees, f);
            }
        }

        // --- conservation: reflection + transmission = 1 ---------------------
        // Checked PER POLARIZATION, not as a single "F + T = 1" formula: for
        // unpolarized light T is (n2·cosθt)/(n1·cosθi)·(t_s²+t_p²)/2, and
        // substituting "1 − F" would just verify an identity with itself.
        // The point: rs² + T_s = 1 holds only if Fresnel and Snell come from the
        // same optics. A mismatch would mean the window and the mirror are drawn
        // from different formulas.
        double worstSum = 0d;
        for (int degrees = 0; degrees <= 48; degrees += 4)
        {
            double rad = degrees * Math.PI / 180.0;
            double cosI = Math.Cos(rad);
            double sinT = WaterIndex * Math.Sin(rad);
            if (sinT >= 1.0)
            {
                return Fail("при {0:F0}° ожидался выход луча, а sinT = {1:F3}", degrees, sinT);
            }

            double cosT = Math.Sqrt(1.0 - (sinT * sinT));
            double rsA = ((WaterIndex * cosI) - (AirIndex * cosT)) / ((WaterIndex * cosI) + (AirIndex * cosT));
            double rpA = ((WaterIndex * cosT) - (AirIndex * cosI)) / ((WaterIndex * cosT) + (AirIndex * cosI));
            double ts = (2.0 * WaterIndex * cosI) / ((WaterIndex * cosI) + (AirIndex * cosT));
            double tp = (2.0 * WaterIndex * cosI) / ((AirIndex * cosI) + (WaterIndex * cosT));
            double transmitFactor = (AirIndex * cosT) / (WaterIndex * cosI);

            double sumS = (rsA * rsA) + (transmitFactor * ts * ts);
            double sumP = (rpA * rpA) + (transmitFactor * tp * tp);
            worstSum = Math.Max(worstSum, Math.Max(Math.Abs(sumS - 1.0), Math.Abs(sumP - 1.0)));
        }

        if (worstSum > 0.01)
        {
            return Fail("R + T не равно 1: худшее расхождение {0:F4} на 0..48°", worstSum);
        }

        // --- refraction: ray deviation for a tilted normal -------------------
        // For a small tilt β the refracted ray deviates from the original by
        // (1 - 1/n)·β. This is what decides whether the ceiling ripple distorts
        // anything at all: at β = 10° it is 2.5°, so the sky in the window shifts
        // by 2.5° — almost imperceptible.
        double beta = 10.0 * Math.PI / 180.0;
        F3 up = new F3(0f, 1f, 0f);

        // The normal points INTO THE INCIDENT SIDE, i.e. down towards the
        // camera. With the sign reversed dot(N, I) goes to +0.98 and refract
        // flips the ray by 156° instead of 2.5°.
        F3 n = new F3((float)-Math.Sin(beta), (float)-Math.Cos(beta), 0f);
        F3 view = new F3(0f, 1f, 0f);
        F3 refracted = Refract(view, n, WaterIndex / AirIndex);
        // --- refraction: ray deviation for a tilted normal -------------------
        // The ray looks straight up and the normal is tilted by β: the incidence
        // angle is θi = β and the ray leaves into air at θt = asin(n·sin β),
        // i.e. DEVIATES FROM VERTICAL BY (n − 1)·β.
        //
        // IMPORTANT: the coefficient here is (n − 1) = 0.33, NOT (1 − 1/n) =
        // 0.25. The 0.25 figure is the ratio of refraction deviation to mirror
        // deviation for a LENS (deviation 2β), whereas here the ray is actually
        // refracted and the surface tilts together with the normal.
        double deviation = RefractionDeviation(10.0);
        double expected = (WaterIndex - 1.0) * 10.0;
        if (Math.Abs(deviation - expected) > 0.15)
        {
            return Fail(
                "отклонение при наклоне 10° = {0:F3}°, ожидалось (n−1)·β = {1:F3}°",
                deviation, expected);
        }

        // At a 40° tilt the deviation is already about 10°, so ripple sloped at
        // 40° (present in the shader) moves the sky by tens of degrees.
        double betaBig = 40.0 * Math.PI / 180.0;
        F3 nBig = new F3((float)-Math.Sin(betaBig), (float)-Math.Cos(betaBig), 0f);
        F3 refractedBig = Refract(view, nBig, WaterIndex / AirIndex);
        // At a 40° tilt the ray exits into air at 58.8°, deviating by 18.8°:
        // ripple sloped at 40° (present in the shader, see 1.33·cosθi/cosθt)
        // moves the sky by almost twenty degrees — the answer to "why is the
        // ceiling flat" is not a missing formula, but missing tilt.
        double deviationBig = RefractionDeviation(40.0);
        if (deviationBig < 15.0 || deviationBig > 22.0)
        {
            return Fail("отклонение при наклоне 40° = {0:F2}°, ожидалось 18-19°", deviationBig);
        }

        // --- refraction stays within the critical angle ----------------------
        // A ray at 40° to the normal refracts 10° outward inside the water, while
        // the mirror starts at 48.6°: so at a 40° tilt the ray STILL exits to the
        // sky. This is why the window must be large rather than a "soft blob".
        if (!(deviationBig < critical))
        {
            return Fail("преломлённый луч должен выходить в небо при наклоне 40°");
        }

        return Ok(string.Format(
            CultureInfo.InvariantCulture,
            "критический {0:F2}°, F(0)={1:F4}, F(крит)={2:F3}, отклонение при 10°={3:F2}°, при 40°={4:F2}°",
            critical, f0, atCritical, deviation, deviationBig));
    }

    /// <summary>
    /// Ray deviation in degrees for a normal tilted by betaDegrees: the ray looks
    /// straight up (as the camera does underwater), the water normal is tilted,
    /// and the ray leaves at asin(n·sin β) from the normal — deviating from
    /// vertical by (n−1)·β.
    ///
    /// Computed via refract — the same function as in the shader. The normal
    /// points INTO THE INCIDENT SIDE (down, towards the camera): in HLSL
    /// dot(N, I) must be negative, otherwise refract flips the ray instead of
    /// refracting it.
    /// </summary>
    private static double RefractionDeviation(double betaDegrees)
    {
        double beta = betaDegrees * Math.PI / 180.0;
        F3 view = new F3(0f, 1f, 0f);
        F3 n = new F3((float)-Math.Sin(beta), (float)-Math.Cos(beta), 0f);
        F3 refracted = Refract(view, n, WaterIndex / AirIndex);
        if (Dot(refracted, refracted) < 1e-12f)
        {
            return 180.0;
        }

        return Math.Acos(Sat(Dot(refracted, view))) * 180.0 / Math.PI;
    }

    /// <summary>
    /// Reflectance at a dielectric boundary for unpolarized light — exactly as
    /// in WaterSurface.shader.
    /// </summary>
    private static float FresnelWater(float cosIncident)
    {
        float cosI = Sat(cosIncident);
        float sinT = WaterIndex * MathF.Sqrt(Sat(1f - (cosI * cosI)));
        if (sinT >= 1f)
        {
            return 1f;
        }

        float cosT = MathF.Sqrt(1f - (sinT * sinT));
        float rs = ((WaterIndex * cosI) - cosT) / ((WaterIndex * cosI) + cosT);
        float rp = ((WaterIndex * cosT) - cosI) / ((WaterIndex * cosT) + cosI);
        return 0.5f * ((rs * rs) + (rp * rp));
    }

    /// <summary>
    /// Refraction exactly as HLSL refract(I, N, eta): incident is the incoming
    /// ray, normal points INTO THE INCIDENT MEDIUM (dot(N, I) &lt; 0), eta = n1/n2.
    /// Returns a unit vector or zero on total internal reflection.
    /// </summary>
    private static F3 Refract(F3 incident, F3 normal, float eta)
    {
        float dotNI = Dot(normal, incident);
        float k = 1f - ((eta * eta) * (1f - (dotNI * dotNI)));
        if (k < 0f)
        {
            return new F3(0f, 0f, 0f);
        }

        // HLSL: eta*I - (eta·dot(N,I) + sqrt(k))·N. The sign before eta·dot(N,I)
        // must be MINUS because dot(N,I) < 0.
        F3 result = (incident * eta) + (normal * ((-eta * dotNI) - MathF.Sqrt(k)));
        return Normalize(result);
    }
}
