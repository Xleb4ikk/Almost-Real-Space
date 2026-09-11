using System;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// T90: единый модуль оптики атмосферы — реальные β (порядок каналов,
    /// масштаб по плотности), transmittance к Солнцу и обе CPU-LUT без NaN и
    /// отрицательных значений. Плюс проверяем, что прозрачность к Солнцу
    /// согласована с GPU-параметризацией (ниже горизонта = 0).
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

        // Реальные β Рэлея: синий > зелёный > красный; положительны.
        bool betaOrder = c.RayleighScattering.Z > c.RayleighScattering.Y
            && c.RayleighScattering.Y > c.RayleighScattering.X
            && c.RayleighScattering.X > 0d;

        // Масштаб по плотности: 0.5·ρ0 даёт вдвое меньшее β.
        AtmosphereOptics.Coefficients half = AtmosphereOptics.FromProfile(1.225d * 0.5d, 8500d, 1200d, 0.8d, true);
        bool densityScale = Math.Abs((half.RayleighScattering.Z / c.RayleighScattering.Z) - 0.5d) < 1e-9d;

        // Turbidity: AerosolScale множит только β_Mie, не трогая γ (Рэлей).
        AtmosphereOptics.Coefficients hazy = AtmosphereOptics.FromProfile(1.225d, 8500d, 1200d, 0.8d, true, 2d);
        bool aerosolScale = Math.Abs((hazy.MieScattering.Z / c.MieScattering.Z) - 2d) < 1e-9d
            && Math.Abs(hazy.RayleighScattering.Z - c.RayleighScattering.Z) < 1e-18d;

        // Прозрачность к Солнцу в зените у поверхности: синий гасится сильнее
        // красного, всё в (0,1]; ниже горизонта — ровно 0 (планета закрывает).
        Vector3d zenith = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, 1d, 16);
        Vector3d belowHorizon = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, -1d, 16);
        bool transmittanceOrder = zenith.Z < zenith.Y && zenith.Y < zenith.X
            && zenith.X <= 1d && zenith.X > 0d && zenith.Z > 0d;
        bool horizonBlocked = belowHorizon.X == 0d && belowHorizon.Y == 0d && belowHorizon.Z == 0d;

        // Transmittance LUT: все пиксели конечны и в [0,1].
        AtmosphereOptics.Lut tLut = AtmosphereOptics.BuildTransmittanceLut(c, R, Ra, 8);
        bool tLutOk = tLut.Pixels.Length == 64;
        for (int i = 0; i < tLut.Pixels.Length && tLutOk; i++)
        {
            Vector3d v = tLut.Pixels[i];
            tLutOk = v.IsFinite && v.X >= 0d && v.X <= 1.0001d
                && v.Y >= 0d && v.Y <= 1.0001d && v.Z >= 0d && v.Z <= 1.0001d;
        }

        // Multi-Scattering LUT: конечна, неотрицательна, в дневной части ненулевая.
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
}
