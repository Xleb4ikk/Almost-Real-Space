using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

internal static partial class P1bTests
{
    private static int Test533_Asteroids()
    {
        Console.WriteLine("--- 10 vs 13 bodies: Ceres/Pallas/Vesta effect (10 years, barycentric) ---");
        Console.WriteLine("expectation: the effect is km-scale; its sign depends on the baseline model.");
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] ten =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        string[] thirteen =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune",
            "Ceres", "Pallas", "Vesta"
        };
        StudyNBody nb10 = JplNBody(jpl, ten, true, true);
        StudyNBody nb13 = JplNBody(jpl, thirteen, true, true);
        var r10 = JplDop853(nb10, 3650d, 1e-6d, 1e-9d, 1e-13d, false);
        var r13 = JplDop853(nb13, 3650d, 1e-6d, 1e-9d, 1e-13d, false);
        string[] bodies = { "Jupiter", "Saturn", "Uranus", "Neptune", "Earth" };
        Console.WriteLine("body        10-body(km)   13-body(km)   change(km)");
        for (int i = 0; i < bodies.Length; i++)
        {
            string body = bodies[i];
            double dp10 = JplBodyError(jpl, nb10, r10.p, 3650d, body, false);
            double dp13 = JplBodyError(jpl, nb13, r13.p, 3650d, body, false);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-11} {1,11:F2}   {2,11:F2}   {3,10:F2}",
                body, dp10 / 1000d, dp13 / 1000d, (dp13 - dp10) / 1000d));
            Check(double.IsFinite(dp13), "asteroids " + body + " finite",
                dp13.ToString("E2", CultureInfo.InvariantCulture));
        }

        double jupiter10 = JplBodyError(jpl, nb10, r10.p, 3650d, "Jupiter", false);
        double jupiter13 = JplBodyError(jpl, nb13, r13.p, 3650d, "Jupiter", false);
        double saturn10 = JplBodyError(jpl, nb10, r10.p, 3650d, "Saturn", false);
        double saturn13 = JplBodyError(jpl, nb13, r13.p, 3650d, "Saturn", false);
        Check(Math.Abs(jupiter13 - jupiter10) < 20000d,
            "asteroid Jupiter effect is km-scale",
            ((jupiter13 - jupiter10) / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " km");
        Check(Math.Abs(saturn13 - saturn10) < 20000d,
            "asteroid Saturn effect is km-scale",
            ((saturn13 - saturn10) / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " km");
        Console.WriteLine("--- Asteroid probe complete ---");
        return 0;
    }
}
