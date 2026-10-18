using System;
using System.Globalization;
using System.Text;

namespace Galilego.Tablet
{
    /// <summary>Screen number formatting (invariant culture, space as thousands separator).</summary>
    public static class TabletFormat
    {
        public const string Dash = "---";

        public static string Num(double v, int decimals)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return Dash;
            }

            string s = Math.Abs(v).ToString("F" + decimals, CultureInfo.InvariantCulture);
            int dot = s.IndexOf('.');
            string intPart = dot >= 0 ? s.Substring(0, dot) : s;
            string frac = dot >= 0 ? s.Substring(dot) : string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < intPart.Length; i++)
            {
                if (i > 0 && (intPart.Length - i) % 3 == 0)
                {
                    sb.Append(' ');
                }

                sb.Append(intPart[i]);
            }

            bool negative = v < 0d && s.Trim('0', '.').Length > 0;
            return (negative ? "-" : string.Empty) + sb + frac;
        }

        public static string Signed(double v, int decimals)
        {
            string s = Num(v, decimals);
            return v >= 0d && s != Dash ? "+" + s : s;
        }

        /// <summary>Distance with an auto unit: m → km (to the metre) → km without fraction.</summary>
        public static string Dist(double m)
        {
            if (double.IsNaN(m) || double.IsInfinity(m))
            {
                return Dash;
            }

            double a = Math.Abs(m);
            if (a < 1000d)
            {
                return Num(m, 1) + " М";
            }

            if (a < 1e6d)
            {
                return Num(m / 1000d, 3) + " КМ";
            }

            return Num(m / 1000d, 0) + " КМ";
        }

        public static string Speed(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms))
            {
                return Dash;
            }

            return Math.Abs(ms) < 100000d ? Num(ms, 1) : Num(ms, 0);
        }

        public static string Time(double s)
        {
            if (double.IsNaN(s) || double.IsInfinity(s) || s < 0d)
            {
                return Dash;
            }

            long t = (long)s;
            long d = t / 86400;
            long h = (t / 3600) % 24;
            long m = (t / 60) % 60;
            long sec = t % 60;
            if (d > 0)
            {
                return d + "Д " + h.ToString("00") + ":" + m.ToString("00") + ":" + sec.ToString("00");
            }

            return h.ToString("00") + ":" + m.ToString("00") + ":" + sec.ToString("00");
        }

        public static string Mass(double kg)
        {
            if (double.IsNaN(kg) || double.IsInfinity(kg))
            {
                return Dash;
            }

            return Math.Abs(kg) < 1000d ? Num(kg, 1) + " КГ" : Num(kg / 1000d, 2) + " Т";
        }

        public static string Pressure(double pa)
        {
            if (double.IsNaN(pa) || pa < 0.01d)
            {
                return "ВАКУУМ";
            }

            if (pa < 1000d)
            {
                return Num(pa, 2) + " ПА";
            }

            return Num(pa / 1000d, 3) + " КПА";
        }

        public static string Atm(double pa)
        {
            if (double.IsNaN(pa) || pa < 0.01d)
            {
                return "0.000";
            }

            return Num(pa / 101325d, 3);
        }

        /// <summary>Clips text to a pixel width, appending "…".</summary>
        public static string Clip(string s, int widthPx)
        {
            if (string.IsNullOrEmpty(s) || PixelCanvas.TextWidth(s) <= widthPx)
            {
                return s ?? string.Empty;
            }

            while (s.Length > 1 && PixelCanvas.TextWidth(s + "\u2026") > widthPx)
            {
                s = s.Substring(0, s.Length - 1);
            }

            return s + "\u2026";
        }
    }
}
