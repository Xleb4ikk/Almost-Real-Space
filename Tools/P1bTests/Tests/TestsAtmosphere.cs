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

    /// <summary>
    /// T96: единая прозрачность света (диск/свет поверхности/небо) —
    /// AtmosphereOptics.TransmittanceToSpace по фактической геометрии Терры.
    /// Проверяем: тёплый белый в зените и красный на горизонте, ровно (1,1,1)
    /// выше атмосферы, монотонность и плавность по высоте наблюдателя, никаких
    /// T>1 на всём диапазоне высот Солнца (старый Kasten–Young давал
    /// отрицательную воздушную массу ниже ≈−1.6°), и битовую согласованность с
    /// transmittance-LUT, который сэмплит GPU-небо.
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

        // Зенит у земли: тёплый белый — синий канал гаснет сильнее красного.
        Vector3d zenith = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, 1d, 64);
        bool zenithWarm = zenith.IsFinite && zenith.X > zenith.Y && zenith.Y > zenith.Z
            && zenith.X <= 1d && zenith.X > 0.88d && zenith.Z > 0.6d;

        // Горизонт у земли: красный доминирует, синий почти погас.
        Vector3d horizon = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, 0d, 512);
        bool horizonRed = horizon.IsFinite && horizon.X > horizon.Y && horizon.Y > horizon.Z
            && horizon.X > 3d * horizon.Z && horizon.Z < 0.15d;

        // Луч ниже геометрического горизонта упирается в планету → ровно 0.
        Vector3d below = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, -0.05d, 32);
        bool blocked = below.X == 0d && below.Y == 0d && below.Z == 0d;

        // С высотой наблюдателя прозрачность растёт и меняется плавно; выше
        // атмосферы — ровно 1 (в космосе свет белый, как и требует задача).
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

        // Никакого усиления света: T ≤ 1 при любой высоте Солнца и наблюдателя
        // (старый Kasten–Young ниже −1.6° уходил в минус и давал T > 1).
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

        // Выше атмосферы (r > R_atm) — ровно (1,1,1): диск и свет белые.
        Vector3d space = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R + (2d * Top), 1d, 16);
        bool spaceWhite = space.X == 1d && space.Y == 1d && space.Z == 1d;

        // CPU-свет и GPU-небо обязаны читать одну и ту же функцию: пиксель LUT
        // в центре текселя бит-в-бит равен прямому расчёту.
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
    /// T97: оценка яркости неба для ambient (SkyAmbientRadiance) — средний
    /// radiance по верхней полусфере тем же интегратором, что у MS LUT.
    /// Днём синяя и яркая, на закате тёплая и слабее, ночью ≈ 0, выше
    /// атмосферы — ровно 0. Детерминирована бит-в-бит.
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
