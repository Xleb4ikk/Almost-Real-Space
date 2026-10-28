using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

internal static partial class P1bTests
{
    private static int Test534b_MoonsAsBodies()
    {
        Console.WriteLine("--- (b+1PN) vs center+moons+1PN, 10 years ---");
        Console.WriteLine("expectation: if Jupiter falls below 18.75 km and Saturn below 11.20 km,");
        Console.WriteLine("the unmodelled moons explain the tail; otherwise the next step is full EIH.");
        Console.WriteLine("EIH should shift Jupiter/Saturn by metres to tens of metres, not kilometres.");
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] barycenter =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        string[] centersAndMoons =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "JupiterCenter", "Io", "Europa", "Ganymede", "Callisto",
            "SaturnCenter", "Mimas", "Enceladus", "Tethys", "Dione",
            "Rhea", "Titan", "Hyperion", "Iapetus",
            "Uranus", "Neptune"
        };
        StudyNBody b = JplNBody(jpl, barycenter, true, true);
        StudyNBody c = JplNBody(jpl, centersAndMoons, true, true);
        var rb = JplDop853(b, 3650d, 1e-6d, 1e-9d, 1e-13d, true);
        var rc = JplDop853(c, 3650d, 1e-6d, 1e-9d, 1e-13d, true);
        var rbEih = JplDop853(b, 3650d, 1e-6d, 1e-9d, 1e-13d, true, true);
        double jb = JplBodyError(jpl, b, rb.p, 3650d, "Jupiter", false);
        double sb = JplBodyError(jpl, b, rb.p, 3650d, "Saturn", false);
        double jc = JplBodyError(jpl, c, rc.p, 3650d, "JupiterCenter", false);
        double sc = JplBodyError(jpl, c, rc.p, 3650d, "SaturnCenter", false);
        double jbEih = JplBodyError(jpl, b, rbEih.p, 3650d, "Jupiter", false);
        double sbEih = JplBodyError(jpl, b, rbEih.p, 3650d, "Saturn", false);
        Console.WriteLine("model                  Jupiter(km)   Saturn(km)");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "barycenter + 1PN       {0,11:F2}   {1,10:F2}", jb / 1000d, sb / 1000d));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "barycenter + EIH       {0,11:F2}   {1,10:F2}", jbEih / 1000d, sbEih / 1000d));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "centers+moons + 1PN    {0,11:F2}   {1,10:F2}", jc / 1000d, sc / 1000d));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "change                 {0,11:F2}   {1,10:F2}", (jc - jb) / 1000d, (sc - sb) / 1000d));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "EIH shift (b)          {0,11:F2}   {1,10:F2}", (jbEih - jb) / 1000d, (sbEih - sb) / 1000d));
        Check(double.IsFinite(jc) && double.IsFinite(sc), "centers+moons finite",
            jc.ToString("E2", CultureInfo.InvariantCulture));
        Check(double.IsFinite(jbEih) && double.IsFinite(sbEih), "barycenter EIH finite",
            jbEih.ToString("E2", CultureInfo.InvariantCulture));
        double eihShiftJ = Math.Abs(jbEih - jb);
        double eihShiftS = Math.Abs(sbEih - sb);
        Check(eihShiftJ < 1000d && eihShiftS < 1000d, "EIH shift is sub-km",
            (eihShiftJ / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " / "
            + (eihShiftS / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " km");
        Console.WriteLine("--- Moons-as-bodies probe complete ---");
        return 0;
    }
}
