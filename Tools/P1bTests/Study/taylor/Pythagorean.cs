using System;
using System.Globalization;

internal static partial class P1bTests
{
    private static TaylorNBodyInput PythagoreanInput()
    {
        return new TaylorNBodyInput
        {
            Names = new[] { "A", "B", "C" },
            Mu = new[] { 3d, 4d, 5d },
            P0 = new[]
            {
                1d, 3d, 0d,
                -2d, -1d, 0d,
                1d, -1d, 0d
            },
            V0 = new double[9]
        };
    }

    private static void PythagoreanSelfCertificate()
    {
        TaylorNBodyInput input = PythagoreanInput();
        var doubleCoarse = TaylorNBody.RunSeconds(
            input, 10d, 16, 1e-12d, 1e-12d, 0.05d, 1e-12d, 0d);
        var doubleFine = TaylorNBody.RunSeconds(
            input, 10d, 20, 1e-14d, 1e-14d, 0.05d, 1e-12d, 0d);
        var ddCoarse = RunTaylorNBodyDdOptSeconds(
            input, 10d, 32, 1e-30d, 1e-30d, 0.05d, 1e-12d);
        var ddFine = RunTaylorNBodyDdOptSeconds(
            input, 10d, 36, 1e-34d, 1e-34d, 0.05d, 1e-12d);
        double doubleSelf = Distance(doubleCoarse.P, doubleFine.P);
        double ddSelf = DdNorm(ddCoarse.DdP, ddFine.DdP);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "self (K+4,tol/100) T=10: double={0:E2} dd={1:E2}",
            doubleSelf, ddSelf));
        Check(double.IsFinite(doubleSelf) && double.IsFinite(ddSelf),
            "Pythagorean self finite", "self");
        Check(doubleSelf < 1e-8d, "Pythagorean self double",
            doubleSelf.ToString("E2", CultureInfo.InvariantCulture));
        Check(ddSelf < 1e-8d, "Pythagorean self dd",
            ddSelf.ToString("E2", CultureInfo.InvariantCulture));
    }

    private static StudyDd[] GoldenDd(string[] values)
    {
        var result = new StudyDd[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = DdFromDecimal(values[i]);
        }

        return result;
    }

    private static double DdNorm(StudyDd[] a, StudyDd[] b)
    {
        StudyDd sum = StudyDd.Zero;
        for (int i = 0; i < a.Length; i++)
        {
            StudyDd d = StudyDd.Sub(a[i], b[i]);
            sum = StudyDd.Add(sum, StudyDd.Mul(d, d));
        }

        return StudyDd.Sqrt(sum).ToDouble;
    }

    private static void PythagoreanCase(double totalSeconds, string[] ptStrings, string[] vtStrings)
    {
        TaylorNBodyInput input = PythagoreanInput();
        var doubleRun = TaylorNBody.RunSeconds(
            input, totalSeconds, 16, 1e-12d, 1e-12d, 0.05d, 1e-12d, 0d);
        var ddRun = RunTaylorNBodyDdOptSeconds(
            input, totalSeconds, 32, 1e-30d, 1e-30d, 0.05d, 1e-12d);
        double[] pt = Golden(ptStrings);
        double[] vt = Golden(vtStrings);
        double doubleErr = Distance(doubleRun.P, pt);
        double ddErr = DdNorm(ddRun.DdP, GoldenDd(ptStrings));
        double ddVel = DdNorm(ddRun.DdV, GoldenDd(vtStrings));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "T={0,5:F1}: double dp={1:E2} dv={2:E2} | dd dp={3:E2} dv={4:E2} (steps {5}/{6})",
            totalSeconds, doubleErr, Distance(doubleRun.V, vt), ddErr,
            ddVel, doubleRun.Steps, ddRun.Steps));
        Check(double.IsFinite(doubleErr) && double.IsFinite(ddErr), "Pythagorean finite T=" + totalSeconds, "dp");
        CheckReferenceMargin("Pythagorean T=" + totalSeconds, Math.Max(ddErr, ddVel), totalSeconds <= 10d ? 1e-24d : 1e-28d);
    }

    private static int Test517_Pythagorean()
    {
        Console.WriteLine("--- Pythagorean three-body: double vs double-double ---");
        PythagoreanCase(2d, new[]
        {
            "0.7528060535800366027882041", "1.989475307704022701355397", "0.0",
            "-0.6727343407028099320266534", "-0.5564423959171072539005221", "0.0",
            "0.08650384041422598394840022", "-0.7485312678887278176928208", "0.0"
        }, new[]
        {
            "-0.3300994320486725275015991", "-1.180984302123757236680043", "0.0",
            "-2.180049044271738237446493", "0.6670563274478771141460267", "0.0",
            "1.942098894646594106458154", "0.1749455193159526506912042", "0.0"
        });
        PythagoreanCase(5d, new[]
        {
            "2.469169667082492226523463", "-1.227815564157012231877925", "0.0",
            "-2.278202696814356931857091", "-0.2054495387230541371468767", "0.0",
            "0.3410603572019902095715952", "0.9010489694726506488442565", "0.0"
        }, new[]
        {
            "0.3065337253427046413935291", "0.1757669344521918423082314", "0.0",
            "-0.7290185858444008487088475", "-0.01400888630191732982891148", "0.0",
            "0.3992946334698978941309605", "-0.09425305162978124152180965", "0.0"
        });
        PythagoreanCase(10d, new[]
        {
            "0.7784804101380854931090928", "0.1413923002900861686167928", "0.0",
            "-2.025092477978198586160358", "0.09721938414605839650247149", "0.0",
            "1.152985736299707573062831", "-0.1626108874908984183720529", "0.0"
        }, new[]
        {
            "1.733944362380586428641492", "3.224738369618290026065737", "0.0",
            "-0.2825554565703317399763955", "-0.3862989478437177280841755", "0.0",
            "-0.8143222521720864652037785", "-1.625803863495999833172102", "0.0"
        });
        PythagoreanCase(20d, new[]
        {
            "3.00429263669639920544772324241", "0.511925235024685865597682645346", "0.0",
            "-1.38862653751089539245552918599", "-0.470476050252722168127946150839", "0.0",
            "-0.691674352009123209304210596653", "0.0692256991873662151437473334638", "0.0"
        }, new[]
        {
            "-0.417782768059199441419091981499", "0.274583625406545989523129154299", "0.0",
            "-1.96862666379487243473902459982", "-0.233327433854997952434638968786", "0.0",
            "1.82557099187141761264267486876", "0.0219117718400707682338336824494", "0.0"
        });
        PythagoreanSelfCertificate();
        Console.WriteLine("--- Pythagorean complete ---");
        return 0;
    }
}





