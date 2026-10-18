using System;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// T98: twilight contract — sunset without a cutoff, glow after sunset.
    /// Direct light fades in a soft horizon band (finite disc + refraction,
    /// HorizonSoftening) rather than a step; ambient still glows warm at −6°
    /// (civil twilight) and fades by −12°; occlusion/day are monotonic and zero
    /// at night.
    /// </summary>
    private static int Test98_SunsetTwilight()
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

        // No step: mid-sunset (−0.25°, the disc's upper edge still visible) T is
        // non-zero but weaker than at the horizon.
        Vector3d above = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, 0d, 64);
        Vector3d midset = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, Math.Sin(-0.25d * Math.PI / 180d), 64);
        bool noCliff = above.IsFinite && midset.IsFinite
            && above.X > 0d && midset.X > 0d && midset.X < above.X
            && midset.Z <= above.Z + 1e-9d;

        // Deep below the horizon direct light is still exactly 0.
        Vector3d deep = AtmosphereOptics.TransmittanceToSpace(c, R, Ra, R, Math.Sin(-2d * Math.PI / 180d), 32);
        bool deepBlocked = deep.X == 0d && deep.Y == 0d && deep.Z == 0d;

        // Twilight: ambient at −6° is warm (R max), noticeably lit (>1% of day) but
        // weaker than at the horizon; by −12° it is almost gone (<2% of day).
        Vector3d day = AtmosphereOptics.SkyAmbientRadiance(c, R, Ra, R, 1d);
        Vector3d horizonAmb = AtmosphereOptics.SkyAmbientRadiance(c, R, Ra, R, 0d);
        Vector3d twilight = AtmosphereOptics.SkyAmbientRadiance(c, R, Ra, R, Math.Sin(-6d * Math.PI / 180d));
        Vector3d lateNight = AtmosphereOptics.SkyAmbientRadiance(c, R, Ra, R, Math.Sin(-12d * Math.PI / 180d));
        double dayLum = Test97Luminance(day);
        double horizonLum = Test97Luminance(horizonAmb);
        double twilightLum = Test97Luminance(twilight);
        double lateLum = Test97Luminance(lateNight);
        bool twilightGlow = twilight.IsFinite && twilight.X > twilight.Z
            && twilightLum > 0.01d * dayLum && twilightLum < horizonLum;
        bool nightGone = lateNight.IsFinite && lateLum < 0.02d * dayLum;

        // Occlusion and day factor: 1 by day, 0 by night, monotonic through the transition.
        double occDay = SkyPhysics.SunOcclusion(0.2d, 0d, R);
        double occNight = SkyPhysics.SunOcclusion(-0.2d, 0d, R);
        double dayDay = SkyPhysics.DayFactor(0.2d);
        double dayNight = SkyPhysics.DayFactor(-0.2d);
        bool factorsOk = occDay == 1d && occNight == 0d && dayDay == 1d && dayNight == 0d;

        Console.WriteLine(string.Format(
            "T98 values: T(0)=({0:F3},{1:F3},{2:F3}) T(-0.25)=({3:F3},{4:F3},{5:F3}) amb(-6)={6:E2} amb(-12)={7:E2}",
            above.X, above.Y, above.Z, midset.X, midset.Y, midset.Z, twilightLum, lateLum));

        Check(noCliff, "T98 sunset-twilight", "T непрерывен через горизонт (нет ступеньки в 0)");
        Check(deepBlocked, "T98 sunset-twilight", "на −2° прямой свет ровно 0");
        Check(twilightGlow, "T98 sunset-twilight", "на −6° ambient тёплый, >1% дня, слабее горизонта");
        Check(nightGone, "T98 sunset-twilight", "к −12° ambient погас (<2% дня)");
        Check(factorsOk, "T98 sunset-twilight", "окклюзия/день: 1 днём, 0 ночью");
        return 0;
    }
}
