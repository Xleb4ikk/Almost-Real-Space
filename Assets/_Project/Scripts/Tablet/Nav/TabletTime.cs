using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Galilego.Tablet
{
    /// <summary>
    /// Time formatting and parsing for the navigation tablet (invariant culture, no UnityEngine).
    /// Field format: "003д 04:08:12.0" (days, h:m:s, tenths). Input accepts "1д 2ч", "90 мин", "1:30:00", "5400".
    /// </summary>
    public static class TabletTime
    {
        public const double Minute = 60d;
        public const double Hour = 3600d;
        public const double Day = 86400d;
        public const double Year = 31557600d;

        private static readonly Regex Hms = new Regex(@"(\d+):(\d+)(?::([\d.,]+))?", RegexOptions.Compiled);
        private static readonly Regex Unit = new Regex(@"([\d.,]+)\s*(г|y|д|d|ч|h|мин|м|m|с|s)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Editable field text: "003д 04:08:12.0". Rounds to tenths first, so 59.97 s never shows as "60.0".</summary>
        public static string Field(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) { return TabletFormat.Dash; }
            long t = (long)Math.Round(Math.Max(0d, seconds) * 10d);
            long d = t / 864000L;
            long h = t % 864000L / 36000L;
            long m = t % 36000L / 600L;
            long s = t % 600L;
            return d.ToString("000", CultureInfo.InvariantCulture) + "д "
                + h.ToString("00", CultureInfo.InvariantCulture) + ":"
                + m.ToString("00", CultureInfo.InvariantCulture) + ":"
                + (s / 10L).ToString("00", CultureInfo.InvariantCulture) + "." + (s % 10L).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Short countdown: "12д 04:08:12" (days omitted when zero).</summary>
        public static string Countdown(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) { return TabletFormat.Dash; }
            long t = (long)Math.Round(Math.Max(0d, seconds));
            long d = t / 86400L;
            return (d > 0 ? d.ToString(CultureInfo.InvariantCulture) + "д " : string.Empty)
                + (t % 86400L / 3600L).ToString("00", CultureInfo.InvariantCulture) + ":"
                + (t % 3600L / 60L).ToString("00", CultureInfo.InvariantCulture) + ":"
                + (t % 60L).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Mission clock: "T+ 00:04:39", "T+ 12 д 01:01:40", "T+ 1 г 34 д 01:01:40".</summary>
        public static string Mission(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) { return "T+ " + TabletFormat.Dash; }
            long t = (long)Math.Floor(Math.Max(0d, seconds));
            long y = t / (long)Year;
            long d = t % (long)Year / 86400L;
            string head = y > 0 ? y.ToString(CultureInfo.InvariantCulture) + " г " : string.Empty;
            if (y > 0 || d > 0) { head += d.ToString(CultureInfo.InvariantCulture) + " д "; }
            return "T+ " + head
                + (t % 86400L / 3600L).ToString("00", CultureInfo.InvariantCulture) + ":"
                + (t % 3600L / 60L).ToString("00", CultureInfo.InvariantCulture) + ":"
                + (t % 60L).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// In-world date "dd.MM.yyyy HH:mm:ss" from universe seconds. The game has no calendar,
        /// so the start date is a setting (see NavState.WorldEpochUtc).
        /// </summary>
        public static string WorldDate(double universeSeconds, DateTime epochUtc)
        {
            try
            {
                return epochUtc.AddSeconds(universeSeconds).ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return TabletFormat.Dash;
            }
        }

        /// <summary>Parses user input into seconds. Returns false for empty / negative / non-finite values.</summary>
        public static bool TryParse(string text, out double seconds)
        {
            seconds = 0d;
            if (string.IsNullOrWhiteSpace(text)) { return false; }
            string x = text.Trim().ToLowerInvariant();
            if (x.StartsWith("-", StringComparison.Ordinal)) { return false; }
            double total = 0d;
            bool found = false;

            Match hms = Hms.Match(x);
            if (hms.Success)
            {
                found = true;
                total += double.Parse(hms.Groups[1].Value, CultureInfo.InvariantCulture) * Hour
                    + double.Parse(hms.Groups[2].Value, CultureInfo.InvariantCulture) * Minute;
                if (hms.Groups[3].Success)
                {
                    double sec;
                    if (!Num(hms.Groups[3].Value, out sec)) { return false; }
                    total += sec;
                }

                x = x.Remove(hms.Index, hms.Length).Insert(hms.Index, " ");
            }

            foreach (Match m in Unit.Matches(x))
            {
                double n;
                if (!Num(m.Groups[1].Value, out n)) { return false; }
                found = true;
                total += n * Multiplier(m.Groups[2].Value);
            }

            if (!found || double.IsNaN(total) || double.IsInfinity(total) || total < 0d) { return false; }
            seconds = total;
            return true;
        }

        private static bool Num(string s, out double v)
        {
            return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static double Multiplier(string unit)
        {
            switch (unit)
            {
                case "г": case "y": return Year;
                case "д": case "d": return Day;
                case "ч": case "h": return Hour;
                case "мин": case "м": case "m": return Minute;
                default: return 1d;
            }
        }
    }
}
