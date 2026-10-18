using System;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// T90: unified atmosphere-optics module — real β (channel order, density
    /// scaling), transmittance to the Sun, and both CPU LUTs free of NaN and
    /// negative values. Also checks that transmittance to the Sun is consistent
    /// with the GPU parameterization (below the horizon = 0).
    /// </summary>
    private static int Test90_AtmosphereOptics()
    {
        const double R = 1143000d;
        const double Ra = 1243000d;

        var profile = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.225d,
            ScaleHeightMeters = 8500d,
            OzoneEnabled = true,
        };

        AtmosphereOptics.Coefficients c = profile.ToOptics();

        // Real Rayleigh β: blue > green > red; all positive.
        bool betaOrder = c.RayleighScattering.Z > c.RayleighScattering.Y
            && c.RayleighScattering.Y > c.RayleighScattering.X
            && c.RayleighScattering.X > 0d;

        // Density scaling: 0.5·ρ0 gives half the β.
        AtmosphereOptics.Coefficients half = AtmosphereOptics.FromProfile(1.225d * 0.5d, 8500d, 1200d, 0.8d, true);
        bool densityScale = Math.Abs((half.RayleighScattering.Z / c.RayleighScattering.Z) - 0.5d) < 1e-9d;

        // Turbidity: AerosolScale multiplies only β_Mie, leaving γ (Rayleigh) untouched.
        AtmosphereOptics.Coefficients hazy = AtmosphereOptics.FromProfile(1.225d, 8500d, 1200d, 0.8d, true, 2d);
        bool aerosolScale = Math.Abs((hazy.MieScattering.Z / c.MieScattering.Z) - 2d) < 1e-9d
            && Math.Abs(hazy.RayleighScattering.Z - c.RayleighScattering.Z) < 1e-18d;

        // Transmittance to the Sun at zenith near the surface: blue is attenuated
        // more than red, everything in (0,1]; below the horizon exactly 0 (planet blocks).
        Vector3d zenith = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, 1d, 16);
        Vector3d belowHorizon = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, -1d, 16);
        bool transmittanceOrder = zenith.Z < zenith.Y && zenith.Y < zenith.X
            && zenith.X <= 1d && zenith.X > 0d && zenith.Z > 0d;
        bool horizonBlocked = belowHorizon.X == 0d && belowHorizon.Y == 0d && belowHorizon.Z == 0d;

        // Transmittance LUT: every pixel is finite and in [0,1].
        AtmosphereOptics.Lut tLut = AtmosphereOptics.BuildTransmittanceLut(c, R, Ra, 8);
        bool tLutOk = tLut.Pixels.Length == 64;
        for (int i = 0; i < tLut.Pixels.Length && tLutOk; i++)
        {
            Vector3d v = tLut.Pixels[i];
            tLutOk = v.IsFinite && v.X >= 0d && v.X <= 1.0001d
                && v.Y >= 0d && v.Y <= 1.0001d && v.Z >= 0d && v.Z <= 1.0001d;
        }

        // Multi-Scattering LUT: finite, non-negative, non-zero in the daytime part.
        AtmosphereOptics.Lut mLut = AtmosphereOptics.BuildMultiScatteringLut(c, R, Ra, 8, 8);
        bool mLutOk = mLut.Pixels.Length == 64;
        bool anyPositive = false;
        for (int i = 0; i < mLut.Pixels.Length && mLutOk; i++)
        {
            Vector3d v = mLut.Pixels[i];
            mLutOk = v.IsFinite && v.X >= 0d && v.Y >= 0d && v.Z >= 0d;
            anyPositive |= v.X + v.Y + v.Z > 0d;
        }

        Check(betaOrder, "T90 atmosphere-optics", "β Рэлея: B>G>R, положительны");
        Check(densityScale, "T90 atmosphere-optics", "β линейны по плотности");
        Check(aerosolScale, "T90 atmosphere-optics", "AerosolScale множит только β_Mie");
        Check(transmittanceOrder, "T90 atmosphere-optics", "T(зенит): B<G<R, в (0,1]");
        Check(horizonBlocked, "T90 atmosphere-optics", "ниже горизонта T=0");
        Check(tLutOk, "T90 atmosphere-optics", "transmittance LUT: конечна, [0,1]");
        Check(mLutOk && anyPositive, "T90 atmosphere-optics", "MS LUT: конечна, неотрицательна, ненулевая");
        return 0;
    }

    /// <summary>
    /// T96: unified light transmittance (disc/surface light/sky) —
    /// AtmosphereOptics.TransmittanceToSpace against the actual geometry of Terra.
    /// Checks: warm white at zenith, red at the horizon, exactly (1,1,1) above the
    /// atmosphere, monotonic and smooth with observer altitude, no T>1 for any Sun
    /// altitude, and bit-exact consistency with the transmittance LUT that the GPU
    /// sky samples.
    /// </summary>
    private static int Test96_SunLightTransmittance()
    {
        const double R = 1143000d;
        const double Top = 100000d;
        const double Ra = R + Top;

        var profile = new AtmosphereProfile
        {
            TopAltitudeMeters = Top,
            SeaLevelDensityKgPerCubicMeter = 1.225d,
            ScaleHeightMeters = 8500d,
            OzoneEnabled = true,
        };

        AtmosphereOptics.Coefficients c = profile.ToOptics();

        // Zenith at ground: warm white — the blue channel fades more than red.
        Vector3d zenith = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, 1d, 64);
        bool zenithWarm = zenith.IsFinite && zenith.X > zenith.Y && zenith.Y > zenith.Z
            && zenith.X <= 1d && zenith.X > 0.88d && zenith.Z > 0.6d;

        // Horizon at ground: red dominates, blue is almost extinguished.
        Vector3d horizon = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, 0d, 512);
        bool horizonRed = horizon.IsFinite && horizon.X > horizon.Y && horizon.Y > horizon.Z
            && horizon.X > 3d * horizon.Z && horizon.Z < 0.15d;

        // A ray below the geometric horizon hits the planet → exactly 0.
        Vector3d below = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, -0.05d, 32);
        bool blocked = below.X == 0d && below.Y == 0d && below.Z == 0d;

        // Transmittance grows with observer altitude and changes smoothly; above
        // the atmosphere it is exactly 1 (light is white in space).
        bool altitudeOk = true;
        bool altitudeSmooth = true;
        Vector3d previous = default;
        for (int i = 0; i <= 120; i++)
        {
            double height = i * 1000d;
            Vector3d t = height >= Top
                ? new Vector3d(1d, 1d, 1d)
                : AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R + height, 1d, 16);
            altitudeOk &= t.IsFinite && t.X >= 0d && t.X <= 1d
                && t.Y >= 0d && t.Y <= 1d && t.Z >= 0d && t.Z <= 1d;
            if (i > 0)
            {
                altitudeOk &= t.X >= previous.X - 1e-9d
                    && t.Y >= previous.Y - 1e-9d && t.Z >= previous.Z - 1e-9d;
                altitudeSmooth &= Math.Abs(t.X - previous.X) < 0.1d
                    && Math.Abs(t.Y - previous.Y) < 0.1d
                    && Math.Abs(t.Z - previous.Z) < 0.1d;
            }

            previous = t;
        }

        // No light gain: T ≤ 1 for any Sun and observer altitude.
        bool noGain = true;
        foreach (double altitude in new[] { 0d, 10000d, 50000d, 100000d })
        {
            for (double sinEl = -1d; sinEl <= 1.0001d; sinEl += 0.02d)
            {
                Vector3d t = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R + altitude, sinEl, 16);
                noGain &= t.IsFinite && t.X <= 1d + 1e-9d && t.Y <= 1d + 1e-9d && t.Z <= 1d + 1e-9d
                    && t.X >= 0d && t.Y >= 0d && t.Z >= 0d;
            }
        }

        // Above the atmosphere (r > R_atm) it is exactly (1,1,1): disc and light are white.
        Vector3d space = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R + (2d * Top), 1d, 16);
        bool spaceWhite = space.X == 1d && space.Y == 1d && space.Z == 1d;

        // CPU light and GPU sky must read the same function: the LUT pixel at the
        // texel centre is bit-for-bit equal to the direct computation.
        const int size = 16;
        AtmosphereOptics.Lut lut = AtmosphereOptics.BuildTransmittanceLut(c, R, Ra, size);
        int x = 10;
        int y = 2;
        double depth = Ra - R;
        double texelHeight = depth * (y + 0.5d) / size;
        double texelCos = ((x + 0.5d) / size) * 2d - 1d;
        Vector3d direct = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R + texelHeight, texelCos, 8);
        Vector3d pixel = lut.Pixels[(y * size) + x];
        bool lutMatch = Math.Abs(direct.X - pixel.X) < 1e-12d
            && Math.Abs(direct.Y - pixel.Y) < 1e-12d
            && Math.Abs(direct.Z - pixel.Z) < 1e-12d;

        Console.WriteLine(string.Format(
            "T96 values: zenith=({0:F3},{1:F3},{2:F3}) horizon=({3:F3},{4:F3},{5:F3})",
            zenith.X, zenith.Y, zenith.Z, horizon.X, horizon.Y, horizon.Z));

        Check(zenithWarm, "T96 sun-transmittance", "зенит: тёплый белый, B<G<R");
        Check(horizonRed, "T96 sun-transmittance", "горизонт: R>G>B, красный доминирует");
        Check(blocked, "T96 sun-transmittance", "ниже горизонта T = 0");
        Check(altitudeOk && altitudeSmooth, "T96 sun-transmittance", "по высоте 0→120 км: рост, [0,1], без скачков");
        Check(noGain, "T96 sun-transmittance", "нет T > 1 ни при какой высоте Солнца");
        Check(spaceWhite, "T96 sun-transmittance", "выше атмосферы T = (1,1,1): свет белый");
        Check(lutMatch, "T96 sun-transmittance", "CPU-свет == пиксель transmittance-LUT бит-в-бит");
        return 0;
    }

    /// <summary>
    /// T97: sky brightness estimate for ambient (SkyAmbientRadiance) — mean
    /// radiance over the upper hemisphere using the same integrator as the MS LUT.
    /// By day it is blue and bright, at sunset warm and weaker, at night ≈ 0, and
    /// above the atmosphere exactly 0. Deterministic bit-for-bit.
    /// </summary>
    private static int Test97_SkyAmbient()
    {
        const double R = 1143000d;
        const double Top = 100000d;
        const double Ra = R + Top;

        var profile = new AtmosphereProfile
        {
            TopAltitudeMeters = Top,
            SeaLevelDensityKgPerCubicMeter = 1.225d,
            ScaleHeightMeters = 8500d,
            OzoneEnabled = true,
        };

        AtmosphereOptics.Coefficients c = profile.ToOptics();

        Vector3d day = AtmosphereOptics.SkyAmbientRadiance(c, R, Ra, R, 1d);
        Vector3d sunset = AtmosphereOptics.SkyAmbientRadiance(c, R, Ra, R, 0.05d);
        Vector3d night = AtmosphereOptics.SkyAmbientRadiance(c, R, Ra, R, -0.4d);
        Vector3d space = AtmosphereOptics.SkyAmbientRadiance(c, R, Ra, Ra + 1000d, 1d);
        Vector3d dayAgain = AtmosphereOptics.SkyAmbientRadiance(c, R, Ra, R, 1d);

        double dayLum = Test97Luminance(day);
        double sunsetLum = Test97Luminance(sunset);
        double nightLum = Test97Luminance(night);

        bool dayOk = day.IsFinite && day.X >= 0d && day.Y >= 0d && day.Z >= 0d
            && dayLum > 0d && day.Z > day.Y && day.Y > day.X;
        bool sunsetOk = sunset.IsFinite && sunset.X >= 0d && sunset.Y >= 0d && sunset.Z >= 0d
            && sunset.X > sunset.Z && sunsetLum > 0d;
        bool twilightGlow = sunsetLum > nightLum && sunsetLum < dayLum;
        bool nightDark = night.IsFinite && nightLum < 0.01d * dayLum;
        bool spaceZero = space.X == 0d && space.Y == 0d && space.Z == 0d;
        bool deterministic = dayAgain.X == day.X && dayAgain.Y == day.Y && dayAgain.Z == day.Z;

        Console.WriteLine(string.Format(
            "T97 values: day=({0:F4},{1:F4},{2:F4}) sunset=({3:F4},{4:F4},{5:F4}) nightLum={6:E2}",
            day.X, day.Y, day.Z, sunset.X, sunset.Y, sunset.Z, nightLum));

        Check(dayOk, "T97 sky-ambient", "день: конечна, неотрицательна, B>G>R");
        Check(sunsetOk && twilightGlow, "T97 sky-ambient", "закат: R max, слабее дня, ярче ночи");
        Check(nightDark, "T97 sky-ambient", "ночь: < 1% от дня");
        Check(spaceZero, "T97 sky-ambient", "выше атмосферы: ровно 0");
        Check(deterministic, "T97 sky-ambient", "детерминирована бит-в-бит");
        return 0;
    }

    private static double Test97Luminance(Vector3d v) =>
        (0.2126d * v.X) + (0.7152d * v.Y) + (0.0722d * v.Z);
}
