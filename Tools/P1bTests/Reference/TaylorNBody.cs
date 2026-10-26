using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

internal sealed class TaylorNBodyInput
{
    public string[] Names;
    public double[] Mu;
    public double[] P0;
    public double[] V0;
}

internal readonly struct TaylorSample
{
    public readonly double T;
    public readonly double[] P;

    public TaylorSample(double t, double[] p)
    {
        T = t;
        P = p;
    }
}

internal sealed class TaylorNBodyResult
{
    public double[] P;
    public double[] V;
    public List<TaylorSample> Samples;
    public long Calls;
    public long Steps;
    public double Ms;
}

internal static class TaylorNBody
{
    public static TaylorNBodyResult Run(
        TaylorNBodyInput input,
        double years,
        int order,
        double positionTolerance,
        double velocityTolerance,
        double maxStep,
        double minStep,
        double sampleEvery)
    {
        int n = input.Names.Length;
        int dim = 3 * n;
        var p = (double[])input.P0.Clone();
        var v = (double[])input.V0.Clone();
        double T = years * 365.25d * 86400d;
        double t = 0d;
        double h = Math.Min(maxStep, T);
        var samples = sampleEvery > 0d ? new List<TaylorSample>() : null;
        double nextSample = sampleEvery > 0d ? sampleEvery : double.PositiveInfinity;
        long calls = 0L;
        long steps = 0L;
        var sw = Stopwatch.StartNew();

        var pJ = new TaylorJet[dim];
        var vJ = new TaylorJet[dim];
        var aJ = new TaylorJet[dim];

        while (t < T - 1e-9d)
        {
            h = Math.Min(h, T - t);
            for (int i = 0; i < dim; i++)
            {
                pJ[i] = TaylorJet.Constant(order, p[i]);
                vJ[i] = TaylorJet.Constant(order, v[i]);
            }

            for (int s = 0; s < order; s++)
            {
                Accel(pJ, aJ, input.Mu, n, order);
                calls++;
                for (int i = 0; i < dim; i++)
                {
                    pJ[i].C[s + 1] = vJ[i].C[s] / (s + 1d);
                    vJ[i].C[s + 1] = aJ[i].C[s] / (s + 1d);
                }
            }

            double hp = Math.Pow(h, order);
            double errP = 0d;
            double errV = 0d;
            for (int i = 0; i < dim; i++)
            {
                errP = Math.Max(errP, Math.Abs(pJ[i].C[order]) * hp);
                errV = Math.Max(errV, Math.Abs(vJ[i].C[order]) * hp);
            }

            double norm = Math.Max(errP / positionTolerance, errV / velocityTolerance);
            if (norm <= 1d)
            {
                for (int i = 0; i < dim; i++)
                {
                    p[i] = pJ[i].Eval(h);
                    v[i] = vJ[i].Eval(h);
                }

                t += h;
                steps++;
                if (samples != null && t >= nextSample - 1e-9d)
                {
                    samples.Add(new TaylorSample(t, (double[])p.Clone()));
                    nextSample += sampleEvery;
                }

                double factor = Math.Min(4d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(norm, 1e-14d), 1d / order)));
                h = Math.Min(maxStep, Math.Max(minStep, h * factor));
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(norm, 1e-14d), 1d / order)));
                h = Math.Max(minStep, h * factor);
            }
        }

        sw.Stop();
        return new TaylorNBodyResult
        {
            P = p,
            V = v,
            Samples = samples,
            Calls = calls,
            Steps = steps,
            Ms = sw.Elapsed.TotalMilliseconds
        };
    }

    private static void Accel(TaylorJet[] p, TaylorJet[] a, double[] mu, int n, int order)
    {
        int dim = 3 * n;
        for (int i = 0; i < dim; i++)
        {
            a[i] = TaylorJet.Constant(order, 0d);
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                TaylorJet dx = p[3 * j] - p[3 * i];
                TaylorJet dy = p[(3 * j) + 1] - p[(3 * i) + 1];
                TaylorJet dz = p[(3 * j) + 2] - p[(3 * i) + 2];
                TaylorJet r2 = (dx * dx) + (dy * dy) + (dz * dz);
                TaylorJet r = TaylorJet.Sqrt(r2);
                TaylorJet inv = TaylorJet.Constant(order, 1d) / (r2 * r);
                TaylorJet fi = inv * mu[j];
                TaylorJet fj = inv * mu[i];
                a[3 * i] += dx * fi;
                a[(3 * i) + 1] += dy * fi;
                a[(3 * i) + 2] += dz * fi;
                a[3 * j] -= dx * fj;
                a[(3 * j) + 1] -= dy * fj;
                a[(3 * j) + 2] -= dz * fj;
            }
        }
    }
}
