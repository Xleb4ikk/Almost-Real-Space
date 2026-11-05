using System;

namespace P1bStudy.Framework
{
    /// <summary>
    /// Explicit Runge-Kutta tableau with an embedded error estimator.
    /// Stages = number of regular stages (6 for DOPRI5, 12 for DOP853). The
    /// estimator vectors have Stages + 1 entries: the last one multiplies the
    /// FSAL stage f(t+h, y_new). Row A[i] has exactly i entries (columns j &lt; i).
    ///
    /// DOP853 coefficients were generated from SciPy's dop853_coefficients
    /// (Hairer/Norsett/Wanner). StudySelfTest checks sum(A[i]) == C[i], sum(B) == 1
    /// and sum(E3) == sum(E5) == 0 so a typo cannot go unnoticed.
    /// </summary>
    internal sealed class RkTableau
    {
        public string Name;
        public int Stages;
        public double[][] A;
        public double[] B;
        public double[] C;
        public double[] E5;
        public double[] E3;          // null => classical single estimator
        public int ErrorExponent;    // controller exponent denominator (5 DOPRI5, 8 DOP853)

        public static readonly RkTableau Dopri5 = BuildDopri5();
        public static readonly RkTableau Dop853 = BuildDop853();

        private static RkTableau BuildDopri5()
        {
            var t = new RkTableau();
            t.Name = "DOPRI5";
            t.Stages = 6;
            t.A = new double[][]
            {
                new double[0],
                new[] { 1d / 5d },
                new[] { 3d / 40d, 9d / 40d },
                new[] { 44d / 45d, -56d / 15d, 32d / 9d },
                new[] { 19372d / 6561d, -25360d / 2187d, 64448d / 6561d, -212d / 729d },
                new[] { 9017d / 3168d, -355d / 33d, 46732d / 5247d, 49d / 176d, -5103d / 18656d }
            };
            t.B = new[] { 35d / 384d, 0d, 500d / 1113d, 125d / 192d, -2187d / 6784d, 11d / 84d };
            t.C = new[] { 0d, 1d / 5d, 3d / 10d, 4d / 5d, 8d / 9d, 1d };
            t.E5 = new[]
            {
                71d / 57600d, 0d, -71d / 16695d, 71d / 1920d,
                -17253d / 339200d, 22d / 525d, -1d / 40d
            };
            t.E3 = null;
            t.ErrorExponent = 5;
            return t;
        }

        private static RkTableau BuildDop853()
        {
            var t = new RkTableau();
            t.Name = "DOP853";
            t.Stages = 12;
            t.A = new double[][]
            {
                new double[] {  },
                new double[] { 0.05260015195876773d },
                new double[] { 0.0197250569845379d, 0.0591751709536137d },
                new double[] { 0.02958758547680685d, 0.0d, 0.08876275643042054d },
                new double[] { 0.2413651341592667d, 0.0d, -0.8845494793282861d, 0.924834003261792d },
                new double[] { 0.037037037037037035d, 0.0d, 0.0d, 0.17082860872947386d, 0.12546768756682242d },
                new double[] { 0.037109375d, 0.0d, 0.0d, 0.17025221101954405d, 0.06021653898045596d, -0.017578125d },
                new double[] { 0.03709200011850479d, 0.0d, 0.0d, 0.17038392571223998d, 0.10726203044637328d, -0.015319437748624402d, 0.008273789163814023d },
                new double[] { 0.6241109587160757d, 0.0d, 0.0d, -3.3608926294469414d, -0.868219346841726d, 27.59209969944671d, 20.154067550477894d, -43.48988418106996d },
                new double[] { 0.47766253643826434d, 0.0d, 0.0d, -2.4881146199716677d, -0.590290826836843d, 21.230051448181193d, 15.279233632882423d, -33.28821096898486d, -0.020331201708508627d },
                new double[] { -0.9371424300859873d, 0.0d, 0.0d, 5.186372428844064d, 1.0914373489967295d, -8.149787010746927d, -18.52006565999696d, 22.739487099350505d, 2.4936055526796523d, -3.0467644718982196d },
                new double[] { 2.273310147516538d, 0.0d, 0.0d, -10.53449546673725d, -2.0008720582248625d, -17.9589318631188d, 27.94888452941996d, -2.8589982771350235d, -8.87285693353063d, 12.360567175794303d, 0.6433927460157636d }
            };
            t.B = new double[] { 0.054293734116568765d, 0.0d, 0.0d, 0.0d, 0.0d, 4.450312892752409d, 1.8915178993145003d, -5.801203960010585d, 0.3111643669578199d, -0.1521609496625161d, 0.20136540080403034d, 0.04471061572777259d };
            t.C = new double[] { 0.0d, 0.05260015195876773d, 0.0789002279381516d, 0.1183503419072274d, 0.2816496580927726d, 0.3333333333333333d, 0.25d, 0.3076923076923077d, 0.6512820512820513d, 0.6d, 0.8571428571428571d, 1.0d };
            t.E5 = new double[] { 0.01312004499419488d, 0.0d, 0.0d, 0.0d, 0.0d, -1.2251564463762044d, -0.4957589496572502d, 1.6643771824549864d, -0.35032884874997366d, 0.3341791187130175d, 0.08192320648511571d, -0.022355307863886294d, 0.0d };
            t.E3 = new double[] { -0.18980075407240762d, 0.0d, 0.0d, 0.0d, 0.0d, 4.450312892752409d, 1.8915178993145003d, -5.801203960010585d, -0.4226823213237919d, -0.1521609496625161d, 0.20136540080403034d, 0.02265179219836082d, 0.0d };
            t.ErrorExponent = 8;
            return t;
        }
    }
}
