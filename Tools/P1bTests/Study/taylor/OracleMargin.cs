using System;
using System.Globalization;

internal static partial class P1bTests
{
    private static void CheckReferenceMargin(string label, double methodError, double goldenError)
    {
        double ratio = methodError / Math.Max(goldenError, 1e-300d);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0}: method={1:E2} golden={2:E2} ratio={3:E1}",
            label, methodError, goldenError, ratio));
        if (methodError <= goldenError)
        {
            Console.WriteLine("  method at/below golden resolution; 100x not applicable");
            return;
        }

        Check(methodError >= 100d * goldenError, label + " 100x",
            ratio.ToString("E1", CultureInfo.InvariantCulture));
    }
}

