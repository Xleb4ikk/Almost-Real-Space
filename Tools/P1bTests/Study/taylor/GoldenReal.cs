using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

internal static partial class P1bTests
{
    private sealed class GoldCp
    {
        [JsonPropertyName("days")] public double Days { get; set; }
        [JsonPropertyName("P")] public string[] P { get; set; }
        [JsonPropertyName("V")] public string[] V { get; set; }
        [JsonPropertyName("rel_P")] public string[] RelP { get; set; }
        [JsonPropertyName("rel_V")] public string[] RelV { get; set; }
        [JsonPropertyName("golden_error_estimate")] public Dictionary<string, string> Err { get; set; }
    }

    private sealed class GoldFile
    {
        [JsonPropertyName("checkpoints")] public GoldCp[] Checkpoints { get; set; }
    }

    private static BigInteger GoldFix(string s)
    {
        int e = 0, k = s.IndexOfAny(new[] { 'e', 'E' });
        if (k >= 0)
        {
            e = int.Parse(s.Substring(k + 1), CultureInfo.InvariantCulture);
            s = s.Substring(0, k);
        }

        bool neg = s.StartsWith("-");
        if (neg || s.StartsWith("+"))
        {
            s = s.Substring(1);
        }

        int dot = s.IndexOf('.');
        string fp = dot < 0 ? "" : s.Substring(dot + 1);
        var v = BigInteger.Parse((dot < 0 ? s : s.Substring(0, dot)) + fp, CultureInfo.InvariantCulture);
        int sh = 30 + e - fp.Length;
        v = sh >= 0 ? v * BigInteger.Pow(10, sh) : v / BigInteger.Pow(10, -sh);
        return neg ? -v : v;
    }

    private static BigInteger DblFix(double d)
    {
        long b = BitConverter.DoubleToInt64Bits(d);
        bool neg = b < 0;
        int ex = (int)((b >> 52) & 0x7FF);
        long m = b & 0xFFFFFFFFFFFFFL;
        if (ex == 0)
        {
            ex++;
        }
        else
        {
            m |= 1L << 52;
        }

        int sh = ex - 1075;
        var v = new BigInteger(m) * BigInteger.Pow(10, 30);
        v = sh >= 0 ? v << sh : v >> -sh;
        return neg ? -v : v;
    }

    private static double GoldErr(BigInteger[] mine, string[] g)
    {
        double s = 0d;
        for (int i = 0; i < 3; i++)
        {
            double d = (double)(mine[i] - GoldFix(g[i])) / 1e30;
            s += d * d;
        }

        return Math.Sqrt(s);
    }
}
