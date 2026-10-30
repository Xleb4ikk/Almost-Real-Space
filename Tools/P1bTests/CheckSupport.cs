using System;
using System.Threading;

internal static partial class P1bTests
{
    private static int failures;

    private static void Check(bool cond, string name, string detail)
    {
        Console.WriteLine((cond ? "PASS " : "FAIL ") + name + " | " + detail);
        if (!cond)
        {
            Interlocked.Increment(ref failures);
        }
    }
}
