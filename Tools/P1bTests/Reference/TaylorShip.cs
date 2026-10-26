using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Universe;

internal sealed class TaylorShipResult
{
    public Vector3d P;
    public Vector3d V;
    public long Calls;
    public long Steps;
    public double Ms;
}

internal static class TaylorShip
{
    public static TaylorShipResult Run(
        StarSystem sys,
        Vector3d p0,
        Vector3d v0,
        double t0,
        double days,
        int order,
        double positionTolerance,
        double velocityTolerance,
        double maxStep,
        double minStep)
    {
        double T = t0 + (days * 86400d);
        double t = t0;
        double h = Math.Min(maxStep, T - t0);
        var p = p0;
        var v = v0;
        var pJ = new TaylorJet[3];
        var vJ = new TaylorJet[3];
        var aJ = new TaylorJet[3];
        long calls = 0L;
        long steps = 0L;
        var sw = Stopwatch.StartNew();

        while (t < T - 1e-9d)
        {
            h = Math.Min(h, T - t);
            pJ[0] = TaylorJet.Constant(order, p.X);
            pJ[1] = TaylorJet.Constant(order, p.Y);
            pJ[2] = TaylorJet.Constant(order, p.Z);
            vJ[0] = TaylorJet.Constant(order, v.X);
            vJ[1] = TaylorJet.Constant(order, v.Y);
            vJ[2] = TaylorJet.Constant(order, v.Z);

            for (int s = 0; s < order; s++)
            {
                aJ = Force(sys, pJ[0], pJ[1], pJ[2], t, order);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    pJ[i].C[s + 1] = vJ[i].C[s] / (s + 1d);
                    vJ[i].C[s + 1] = aJ[i].C[s] / (s + 1d);
                }
            }

            double hp = Math.Pow(h, order);
            double errP = 0d;
            double errV = 0d;
            for (int i = 0; i < 3; i++)
            {
                errP = Math.Max(errP, Math.Abs(pJ[i].C[order]) * hp);
                errV = Math.Max(errV, Math.Abs(vJ[i].C[order]) * hp);
            }

            double norm = Math.Max(errP / positionTolerance, errV / velocityTolerance);
            if (norm <= 1d)
            {
                p = new Vector3d(pJ[0].Eval(h), pJ[1].Eval(h), pJ[2].Eval(h));
                v = new Vector3d(vJ[0].Eval(h), vJ[1].Eval(h), vJ[2].Eval(h));
                t += h;
                steps++;
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
        return new TaylorShipResult { P = p, V = v, Calls = calls, Steps = steps, Ms = sw.Elapsed.TotalMilliseconds };
    }

    private static TaylorJet[] Force(
        StarSystem sys, TaylorJet px, TaylorJet py, TaylorJet pz, double t, int order)
    {
        var bodies = sys.AllBodies;
        int n = bodies.Count;
        var index = new Dictionary<OrbitingBody, int>(n);
        for (int i = 0; i < n; i++)
        {
            index[bodies[i]] = i;
        }

        var world = new TaylorJet[6 * n];
        for (int i = 0; i < n; i++)
        {
            OrbitingBody body = bodies[i];
            if (body.Parent == null)
            {
                world[6 * i] = TaylorJet.Constant(order, 0d);
                world[(6 * i) + 1] = TaylorJet.Constant(order, 0d);
                world[(6 * i) + 2] = TaylorJet.Constant(order, 0d);
                world[(6 * i) + 3] = TaylorJet.Constant(order, 0d);
                world[(6 * i) + 4] = TaylorJet.Constant(order, 0d);
                world[(6 * i) + 5] = TaylorJet.Constant(order, 0d);
                continue;
            }

            TaylorLocalState local = TaylorChebyshev.LocalState(body.Baked, t, order);
            int parent = index[body.Parent];
            world[6 * i] = world[6 * parent] + local.X;
            world[(6 * i) + 1] = world[(6 * parent) + 1] + local.Y;
            world[(6 * i) + 2] = world[(6 * parent) + 2] + local.Z;
            world[(6 * i) + 3] = world[(6 * parent) + 3] + local.Vx;
            world[(6 * i) + 4] = world[(6 * parent) + 4] + local.Vy;
            world[(6 * i) + 5] = world[(6 * parent) + 5] + local.Vz;
        }

        var rootAccel = new[]
        {
            TaylorJet.Constant(order, 0d),
            TaylorJet.Constant(order, 0d),
            TaylorJet.Constant(order, 0d)
        };
        OrbitingBody root = bodies[0];
        for (int c = 0; c < root.Children.Count; c++)
        {
            OrbitingBody child = root.Children[c];
            int ci = index[child];
            double mc = child.ResolveSubtreeStandardGravitationalParameter();
            TaylorJet dx = world[6 * ci] - world[0];
            TaylorJet dy = world[(6 * ci) + 1] - world[1];
            TaylorJet dz = world[(6 * ci) + 2] - world[2];
            TaylorJet r2 = (dx * dx) + (dy * dy) + (dz * dz);
            TaylorJet r = TaylorJet.Sqrt(r2);
            TaylorJet inv = TaylorJet.Constant(order, 1d) / (r2 * r);
            rootAccel[0] += dx * (inv * mc);
            rootAccel[1] += dy * (inv * mc);
            rootAccel[2] += dz * (inv * mc);
        }

        var total = new[]
        {
            TaylorJet.Constant(order, 0d),
            TaylorJet.Constant(order, 0d),
            TaylorJet.Constant(order, 0d)
        };
        for (int i = 0; i < n; i++)
        {
            double mu = bodies[i].ResolveStandardGravitationalParameter();
            if (mu == 0d)
            {
                continue;
            }

            TaylorJet dx = world[6 * i] - px;
            TaylorJet dy = world[(6 * i) + 1] - py;
            TaylorJet dz = world[(6 * i) + 2] - pz;
            TaylorJet r2 = (dx * dx) + (dy * dy) + (dz * dz);
            TaylorJet r = TaylorJet.Sqrt(r2);
            TaylorJet inv = TaylorJet.Constant(order, 1d) / (r2 * r);
            total[0] += dx * (inv * mu);
            total[1] += dy * (inv * mu);
            total[2] += dz * (inv * mu);
        }

        total[0] -= rootAccel[0];
        total[1] -= rootAccel[1];
        total[2] -= rootAccel[2];
        return total;
    }
}
