using System;
using System.Globalization;

internal static partial class P1bTests
{
    private static void RunSolarSystemCase(
        string label, double[] mu, double[] p0, double[] v0,
        string[] ptStrings, string[] vtStrings, double positionGate, double velocityGate)
    {
        var input = new TaylorNBodyInput
        {
            Names = new[] { "Sun", "Earth", "Moon" },
            Mu = mu,
            P0 = p0,
            V0 = v0
        };
        var r = TaylorNBody.RunSeconds(
            input, 31557600d, 16, 1e-6d, 1e-9d, 3600d, 1e-6d, 0d);
        double[] pt = Golden(ptStrings);
        double[] vt = Golden(vtStrings);
        double dp = Distance(r.P, pt);
        double dv = Distance(r.V, vt);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-18} dp={1:E3} m, dv={2:E3} m/s, steps={3:N0}, calls={4:N0}, {5:F0} ms",
            label, dp, dv, r.Steps, r.Calls, r.Ms));
        Check(double.IsFinite(dp) && double.IsFinite(dv), label + " finite", "dp/dv finite");
        Check(dp < positionGate, label + " dp", dp.ToString("E3", CultureInfo.InvariantCulture));
        Check(dv < velocityGate, label + " dv", dv.ToString("E3", CultureInfo.InvariantCulture));
        CheckReferenceMargin(label, Math.Max(dp, dv), 1e-28d);
    }

    private static int Test509_SolarSystemGolden()
    {
        Console.WriteLine("--- Sun-Earth-Moon SI golden: mpmath 50 digits, 1 year ---");
        double[] mu = { 1.32712440018e20d, 3.986004418e14d, 4.9048695e12d };
        double[] p0 = { 0d, 0d, 0d, 149597870700d, 0d, 0d, 149982270700d, 0d, 0d };
        double[] v0Planar =
        {
            0d, -0.0905964504400131d, 0d,
            0d, 29784.691831696804d, 0d,
            0d, 30802.9952423305d, 0d
        };
        double[] v0Inclined =
        {
            0d, -0.09059140829330364d, -1.8817547698941284e-05d,
            0d, 29784.691831696804d, 0d,
            0d, 30666.568454065924d, 509.15170531684873d
        };

        RunSolarSystemCase(
            "planar",
            mu, p0, v0Planar,
            new[]
            {
                "16.70481897952951994983770164227069711899",
                "3899.900160529196467186687432661171219891",
                "0.0",
                "149596864479.355596112008579076431268711",
                "-1278109665.364031020600197694531471842307",
                "0.0",
                "149612055489.7024194045321125243211484416",
                "-1653497375.915943494181208285450520080337",
                "0.0"
            },
            new[]
            {
                "-0.0007760928396947077309155446145092945536212",
                "-0.09059312420663842541881763558014509186032",
                "0.0",
                "242.7028071511625948900782897788792375811",
                "29795.70563930674642714395837622125351881",
                "0.0",
                "1275.411767801280308988378523946487651674",
                "29817.94535946487129358312603365894578",
                "0.0"
            },
            0.5d, 1e-6d);

        RunSolarSystemCase(
            "Moon 30 deg",
            mu, p0, v0Inclined,
            new[]
            {
                "12.85715269797366217674718900102536499756",
                "3421.203986055403502792368904692326433715",
                "0.7753934203465871383480947703541186643558",
                "149597496097.4725027424698249273990078363",
                "-1121239758.270418353359875837933406115652",
                "1825444.170817074810520321545275288940502",
                "149664833636.0226009985675727044886256943",
                "-1449511710.437748179204144785221749326597",
                "-169327075.013314123384793008978858442189"
            },
            new[]
            {
                "-0.0006808686444410653740278493403752020177538",
                "-0.09058884838978158810781108041032668458187",
                "-0.00001881923463478323990739946551295869352693",
                "211.6703640919983572241966992922257892936",
                "29793.63804763061926285954495762394790255",
                "3.749922407473467354706245687884786244624",
                "1220.794659773202659903008710232707616698",
                "29870.27883488653307888579662470474568062",
                "204.4551479542310532349116860760712615358"
            },
            0.5d, 1e-6d);

        Console.WriteLine("--- Sun-Earth-Moon SI golden complete ---");
        return 0;
    }
}



