using System;

namespace GalilegoPhysicsTests.Support
{
    internal static class Env
    {
        public static bool IsSet(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrEmpty(value) && value != "0" && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        public static bool SlowEnabled => IsSet("GALILEGO_SLOW");
        public static bool Calibrate => IsSet("GALILEGO_CALIBRATE");
        public static bool Dump => IsSet("GALILEGO_DUMP");

        public static string SlowSkipReason =>
            "Slow-тест пропущен: задайте GALILEGO_SLOW=1 (длинные горизонты и тяжёлые golden).";
    }
}
