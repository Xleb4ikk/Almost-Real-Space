using System;
using System.Globalization;

internal static partial class P1bTests
{
    private static double MaxJetError(TaylorJet actual, double[] expected)
    {
        double scale = 1d;
        for (int i = 0; i < expected.Length; i++)
        {
            scale = Math.Max(scale, Math.Abs(expected[i]));
        }

        double worst = 0d;
        for (int i = 0; i < expected.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(actual.C[i] - expected[i]) / scale);
        }

        return worst;
    }

    private static int Test511_JetOracle()
    {
        Console.WriteLine("--- Jet oracle: sqrt, div, mul, composite, derivative, integral ---");
        var a = TaylorJet.FromSeries(6, new[] { 4d, 0.3d, -0.2d, 0.1d, 0.05d, -0.02d, 0.01d });
        var b = TaylorJet.FromSeries(6, new[] { 2d, -0.1d, 0.04d, 0.02d, -0.01d, 0.005d, 0.002d });

        double[] sqrt = Golden(new[]
        {
            "2.0", "0.07499999999999999722444243843710864894092",
            "-0.05140625000000000267147415300428292732029",
            "0.02692773437500000141661855856956008983895",
            "0.01082955932617187560947069873557024904044",
            "-0.004713981552124023477029630043995224568296",
            "0.002773852105617523251399058174577908242234"
        });
        double[] div = Golden(new[]
        {
            "2.0", "0.25", "-0.1275000000000000056898930012039272696711",
            "0.01862500000000000161676227961038419619929",
            "0.03598125000000000184331716557295522458879",
            "-0.01104843750000000005084474508088020969046",
            "0.0002792031249999999026204303143039438632326"
        });
        double[] mul = Golden(new[]
        {
            "8.0", "0.1999999999999999555910790149937383830547",
            "-0.2700000000000000194289029309402393957838",
            "0.3120000000000000147937218031302109535302",
            "0.04800000000000000290045765183322141717516",
            "-0.02800000000000000092287288921966138732347",
            "0.03750000000000000116920362280836798972254"
        });
        double[] comp = Golden(new[]
        {
            "2.0", "0.0624999999999999930611060960927716223523",
            "-0.06140625000000000385108611666851174766854",
            "0.05433056640625000263900229793834207134539",
            "0.01101643676757812566761104023510541969983",
            "-0.006211777305603027478650792285160084667907",
            "0.006351527793407440371795173324685183637997"
        });
        double[] der = Golden(new[]
        {
            "0.2999999999999999888977697537484345957637",
            "-0.4000000000000000222044604925031308164726",
            "0.3000000000000000166533453693773481063545",
            "0.2000000000000000111022302462515654042363",
            "-0.1000000000000000020816681711721685132943",
            "0.06000000000000000124900090270330110797659",
            "0.0"
        });
        double[] integral = Golden(new[]
        {
            "0.0", "4.0", "0.1499999999999999944488848768742172978818",
            "-0.06666666666666667036741008208385513474544",
            "0.02500000000000000138777878078144567552954",
            "0.01000000000000000055511151231257827021182",
            "-0.00333333333333333340272227237240561710981"
        });

        double eSqrt = MaxJetError(TaylorJet.Sqrt(a), sqrt);
        double eDiv = MaxJetError(TaylorJet.Div(a, b), div);
        double eMul = MaxJetError(TaylorJet.Mul(a, b), mul);
        double eComp = MaxJetError((a * b) / (TaylorJet.Sqrt(a) + b), comp);
        double eDer = MaxJetError(a.Derivative(), der);
        double eInt = MaxJetError(a.Integral(), integral);

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "sqrt={0:E2} div={1:E2} mul={2:E2} comp={3:E2} der={4:E2} int={5:E2}",
            eSqrt, eDiv, eMul, eComp, eDer, eInt));
        Check(eSqrt < 1e-15d, "jet sqrt", eSqrt.ToString("E2", CultureInfo.InvariantCulture));
        Check(eDiv < 1e-15d, "jet div", eDiv.ToString("E2", CultureInfo.InvariantCulture));
        Check(eMul < 1e-15d, "jet mul", eMul.ToString("E2", CultureInfo.InvariantCulture));
        Check(eComp < 1e-15d, "jet composite", eComp.ToString("E2", CultureInfo.InvariantCulture));
        Check(eDer < 1e-15d, "jet derivative", eDer.ToString("E2", CultureInfo.InvariantCulture));
        Check(eInt < 1e-15d, "jet integral", eInt.ToString("E2", CultureInfo.InvariantCulture));
        CheckReferenceMargin("jet composite", eComp, 1e-45d);
        Console.WriteLine("--- Jet oracle complete ---");
        return 0;
    }
}

