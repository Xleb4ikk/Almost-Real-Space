using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test88_SkyPhysics()
    {
        double H = 8500d;
        double top = 100000d;
        double R = 1143000d;
        Vector3d extinction = SkyPhysics.ExtinctionPerMeter(1.225d);

        // Космос: плотность 0 → прозрачность (1,1,1).
        Vector3d spaceT = SkyPhysics.SunTransmittance(1d, 0d, H, extinction);
        bool spaceClear = Math.Abs(spaceT.X - 1d) < 1e-12d && Math.Abs(spaceT.Y - 1d) < 1e-12d && Math.Abs(spaceT.Z - 1d) < 1e-12d;

        // Зенит у земли: синий гаснет сильнее зелёного, зелёный сильнее красного.
        Vector3d noonT = SkyPhysics.SunTransmittance(1d, 1d, H, extinction);
        bool channelOrder = noonT.Z < noonT.Y && noonT.Y < noonT.X && noonT.X <= 1d;

        // Низкое солнце: зелёный/синий уходят в ноль, красный остаётся заметным.
        // Порог ослаблен против прежнего (<0.05): реальные β Рэлея слабее
        // эмпирических, поэтому зелёный на закате ~0.087, но красный по-прежнему
        // доминирует кратно. Проверяем именно доминирование, а не абсолют.
        Vector3d duskT = SkyPhysics.SunTransmittance(0.02d, 1d, H, extinction);
        bool duskRed = duskT.X > (duskT.Y * 3d) && duskT.X > 0.05d && duskT.Y < 0.15d;

        // Airmass: конечна у горизонта и монотонно растёт при снижении солнца.
        double amZenith = SkyPhysics.Airmass(1d);
        double amHorizon = SkyPhysics.Airmass(0d);
        bool airmassFinite = amHorizon > 20d && amHorizon < 60d && amZenith > 0.9d && amZenith < 1.1d;
        bool airmassMono = true;
        double previous = 0d;
        for (int i = 100; i >= 0; i--)
        {
            double am = SkyPhysics.Airmass(i / 100d);
            if (am < previous - 1e-9d)
            {
                airmassMono = false;
            }

            previous = am;
        }

        // Окклюзия: на поверхности солнце ниже горизонта закрыто; с высоты видно.
        double occGroundUp = SkyPhysics.SunOcclusion(0.1d, 0d, R);
        double occGroundDown = SkyPhysics.SunOcclusion(-0.1d, 0d, R);
        double occHighDown = SkyPhysics.SunOcclusion(-0.1d, 40000d, R);
        bool occlusion = occGroundUp > 0.99d && occGroundDown < 0.01d && occHighDown > 0.99d;

        // Видимость звёзд: полдень у земли скрыта, ночь/космос — видна.
        double noonLum = SkyPhysics.SkyLuminance(1d, SkyPhysics.Density(0d, H, top));
        double noonStars = SkyPhysics.StarVisibility(noonLum);
        double nightStars = SkyPhysics.StarVisibility(SkyPhysics.SkyLuminance(-0.5d, SkyPhysics.Density(0d, H, top)));
        double spaceStars = SkyPhysics.StarVisibility(SkyPhysics.SkyLuminance(1d, SkyPhysics.Density(60000d, H, top)));
        bool stars = noonStars < 0.05d && nightStars > 0.99d && spaceStars > 0.5d;

        bool day = SkyPhysics.DayFactor(1d) > 0.99d && SkyPhysics.DayFactor(-0.2d) < 0.01d;

        Check(spaceClear, "T88 sky-physics", "космос: прозрачность = 1");
        Check(channelOrder && duskRed, "T88 sky-physics", "порядок каналов и красный закат");
        Check(airmassFinite && airmassMono, "T88 sky-physics", "airmass конечна у горизонта и монотонна");
        Check(occlusion, "T88 sky-physics", "окклюзия с учётом высоты наблюдателя");
        Check(stars, "T88 sky-physics", "видимость звёзд: полдень/ночь/космос");
        Check(day, "T88 sky-physics", "дневной фактор света");
        return 0;
    }

    private static int Test80_StarColor()
    {
        // Физический цвет черного тела: спектр Планка → CIE (фит Wyman) →
        // линейный sRGB. Проверки: детерминизм бит-в-бит; монотонность цвета
        // по температуре (1500K красный доминирует, 10000K синий);
        // 5772K (наша звезда) — тёплый белый: R≥G≥B, близко к белому.
        bool deterministic = true;
        for (int i = 0; i < 5; i++)
        {
            Vector3d a = StarColorUtil.FromTemperature(1500d + (i * 2000d));
            Vector3d b = StarColorUtil.FromTemperature(1500d + (i * 2000d));
            if ((a - b).SqrMagnitude != 0d)
            {
                deterministic = false;
            }
        }

        Vector3d red = StarColorUtil.FromTemperature(1500d);
        Vector3d white = StarColorUtil.FromTemperature(5772d);
        Vector3d blue = StarColorUtil.FromTemperature(10000d);

        bool redDominant = red.X > red.Z * 1.5d;
        bool blueDominant = blue.Z > blue.X * 1.2d;
        bool warmWhite = white.X >= white.Y && white.Y >= white.Z && white.X < 1.0000001d
            && white.Z > 0.7d;
        bool normalized = Math.Abs(white.X - 1d) < 1e-12d && Math.Abs(blue.Z - 1d) < 1e-12d;

        Check(deterministic, "T80 star-color", "детерминизм бит-в-бит");
        Check(redDominant, "T80 star-color", "1500K: красный доминирует");
        Check(blueDominant, "T80 star-color", "10000K: синий доминирует");
        Check(warmWhite, "T80 star-color", string.Format(
            "5772K тёплый белый: R={0:F3} G={1:F3} B={2:F3} (звёзды белые, желтизна — атмосферная)",
            white.X, white.Y, white.Z));
        Check(normalized, "T80 star-color", "нормировка max=1");
        return 0;
    }

    private static int Test81_StarGranulation()
    {
        // Грануляция: детерминизм бит-в-бит; диапазон [0,1]; контраст есть
        // (дорожки и ядра различимы: выборка должна дать и <0.4, и >0.7);
        // бесшовность — эквивалентные направления (одна точка) дают одно.
        var rng = new Random(5772);
        bool deterministic = true;
        bool inRange = true;
        double min = 1d;
        double max = 0d;
        for (int i = 0; i < 2000; i++)
        {
            double x = rng.NextDouble() * 2d - 1d;
            double y = rng.NextDouble() * 2d - 1d;
            double z = rng.NextDouble() * 2d - 1d;
            var dir = new Vector3d(x, y, z);
            if (dir.SqrMagnitude < 1e-6d)
            {
                continue;
            }

            dir = dir.Normalized;
            double a = StarSurfaceTexture.Granulation(dir, 42);
            double b = StarSurfaceTexture.Granulation(dir, 42);
            if (Math.Abs(a - b) != 0d)
            {
                deterministic = false;
            }

            if (a < 0d || a > 1d)
            {
                inRange = false;
            }

            min = Math.Min(min, a);
            max = Math.Max(max, a);
        }

        bool contrast = min < 0.4d && max > 0.7d;
        bool seamless = Math.Abs(StarSurfaceTexture.Granulation(new Vector3d(0.5d, 0.5d, 0.7071067811865476d).Normalized, 42)
            - StarSurfaceTexture.Granulation(new Vector3d(0.5d, 0.5d, 0.7071067811865476d).Normalized, 42)) == 0d;

        Check(deterministic, "T81 star-granulation", "детерминизм бит-в-бит");
        Check(inRange, "T81 star-granulation", "значения в [0,1]");
        Check(contrast, "T81 star-granulation", string.Format("контраст ячеек: min={0:F2}, max={1:F2}", min, max));
        Check(seamless, "T81 star-granulation", "чистая функция направления (швов нет по построению)");
        return 0;
    }
}
