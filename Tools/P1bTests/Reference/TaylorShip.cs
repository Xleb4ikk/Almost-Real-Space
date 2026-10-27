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
    public long SegmentClips;
    public double Ms;
}

internal sealed class TaylorShipDdResult
{
    public Vector3d P;
    public Vector3d V;
    public P1bTests.StudyDd DdPx;
    public P1bTests.StudyDd DdPy;
    public P1bTests.StudyDd DdPz;
    public P1bTests.StudyDd DdVx;
    public P1bTests.StudyDd DdVy;
    public P1bTests.StudyDd DdVz;
    public long Calls;
    public long Steps;
    public long SegmentClips;
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
        return RunSeconds(
            sys,
            p0,
            v0,
            t0,
            days * 86400d,
            order,
            positionTolerance,
            velocityTolerance,
            maxStep,
            minStep);
    }

    public static TaylorShipResult RunSeconds(
        StarSystem sys,
        Vector3d p0,
        Vector3d v0,
        double t0,
        double totalSeconds,
        int order,
        double positionTolerance,
        double velocityTolerance,
        double maxStep,
        double minStep)
    {
        double T = t0 + totalSeconds;
        double t = t0;
        double h = Math.Min(maxStep, T - t0);
        var p = p0;
        var v = v0;
        var pJ = new TaylorJet[3];
        var vJ = new TaylorJet[3];
        var aJ = new TaylorJet[3];
        long calls = 0L;
        long steps = 0L;
        long segmentClips = 0L;
        var sw = Stopwatch.StartNew();

        while (t < T)
        {
            double remaining = T - t;
            if (!(remaining > 0d))
            {
                break;
            }

            double segment = SegmentRemaining(sys, t);
            bool clipped = segment < remaining && segment <= h;
            h = Math.Min(h, Math.Min(remaining, segment));
            if (clipped)
            {
                segmentClips++;
            }
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
            double hp1 = Math.Pow(h, order - 1);
            double errP = 0d;
            double errV = 0d;
            for (int i = 0; i < 3; i++)
            {
                errP = Math.Max(errP, Math.Max(
                    Math.Abs(pJ[i].C[order]) * hp,
                    Math.Abs(pJ[i].C[order - 1]) * hp1));
                errV = Math.Max(errV, Math.Max(
                    Math.Abs(vJ[i].C[order]) * hp,
                    Math.Abs(vJ[i].C[order - 1]) * hp1));
            }

            double norm = Math.Max(errP / positionTolerance, errV / velocityTolerance);
            if (norm <= 1d)
            {
                p = new Vector3d(pJ[0].Eval(h), pJ[1].Eval(h), pJ[2].Eval(h));
                v = new Vector3d(vJ[0].Eval(h), vJ[1].Eval(h), vJ[2].Eval(h));
                bool last = h >= remaining;
                t = last ? T : t + h;
                steps++;
                double factor = Math.Min(4d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(norm, 1e-14d), 1d / order)));
                h = Math.Min(maxStep, Math.Max(minStep, h * factor));
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(norm, 1e-14d), 1d / order)));
                double next = Math.Max(minStep, h * factor);
                if (next >= h && h <= minStep)
                {
                    throw new InvalidOperationException("TaylorShip step rejected at minStep.");
                }

                h = next;
            }
        }

        sw.Stop();
        return new TaylorShipResult
        {
            P = p,
            V = v,
            Calls = calls,
            Steps = steps,
            SegmentClips = segmentClips,
            Ms = sw.Elapsed.TotalMilliseconds
        };
    }

    public static TaylorShipDdResult RunDd(
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
        return RunDdSeconds(
            sys, p0, v0, t0, days * 86400d, order,
            positionTolerance, velocityTolerance, maxStep, minStep);
    }

    public static TaylorShipDdResult RunDdSeconds(
        StarSystem sys,
        Vector3d p0,
        Vector3d v0,
        double t0,
        double totalSeconds,
        int order,
        double positionTolerance,
        double velocityTolerance,
        double maxStep,
        double minStep)
    {
        double T = t0 + totalSeconds;
        double t = t0;
        double tc = 0d;
        double h = Math.Min(maxStep, T - t0);
        P1bTests.StudyDd tDd = P1bTests.StudyDd.From(t0);
        P1bTests.StudyDd px = P1bTests.StudyDd.From(p0.X);
        P1bTests.StudyDd py = P1bTests.StudyDd.From(p0.Y);
        P1bTests.StudyDd pz = P1bTests.StudyDd.From(p0.Z);
        P1bTests.StudyDd vx = P1bTests.StudyDd.From(v0.X);
        P1bTests.StudyDd vy = P1bTests.StudyDd.From(v0.Y);
        P1bTests.StudyDd vz = P1bTests.StudyDd.From(v0.Z);
        var pJ = new P1bTests.DdJet[3];
        var vJ = new P1bTests.DdJet[3];
        P1bTests.DdJet[] aJ;
        var invs = new P1bTests.StudyDd[order];
        for (int s = 0; s < order; s++)
        {
            invs[s] = P1bTests.StudyDd.Div(
                P1bTests.StudyDd.From(1d), P1bTests.StudyDd.From(s + 1d));
        }

        long calls = 0L;
        long steps = 0L;
        long segmentClips = 0L;
        var sw = Stopwatch.StartNew();

        while (t < T)
        {
            double tNow = t + tc;
            double remaining = (T - t) - tc;
            if (!(remaining > 0d))
            {
                break;
            }

            double segment = SegmentRemaining(sys, tNow);
            bool clipped = segment < remaining && segment <= h;
            h = Math.Min(h, Math.Min(remaining, segment));
            if (clipped)
            {
                segmentClips++;
            }

            pJ[0] = P1bTests.DdJet.Constant(order, px);
            pJ[1] = P1bTests.DdJet.Constant(order, py);
            pJ[2] = P1bTests.DdJet.Constant(order, pz);
            vJ[0] = P1bTests.DdJet.Constant(order, vx);
            vJ[1] = P1bTests.DdJet.Constant(order, vy);
            vJ[2] = P1bTests.DdJet.Constant(order, vz);

            for (int s = 0; s < order; s++)
            {
                aJ = ForceDd(sys, pJ[0], pJ[1], pJ[2], tDd, tNow, order);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    pJ[i].C[s + 1] = P1bTests.StudyDd.Mul(vJ[i].C[s], invs[s]);
                    vJ[i].C[s + 1] = P1bTests.StudyDd.Mul(aJ[i].C[s], invs[s]);
                }
            }

            double hp = Math.Pow(h, order);
            double hp1 = Math.Pow(h, order - 1);
            double errP = 0d;
            double errV = 0d;
            for (int i = 0; i < 3; i++)
            {
                errP = Math.Max(errP, Math.Max(
                    Math.Abs(pJ[i].C[order].ToDouble) * hp,
                    Math.Abs(pJ[i].C[order - 1].ToDouble) * hp1));
                errV = Math.Max(errV, Math.Max(
                    Math.Abs(vJ[i].C[order].ToDouble) * hp,
                    Math.Abs(vJ[i].C[order - 1].ToDouble) * hp1));
            }

            double norm = Math.Max(errP / positionTolerance, errV / velocityTolerance);
            if (norm <= 1d)
            {
                px = pJ[0].Eval(h);
                py = pJ[1].Eval(h);
                pz = pJ[2].Eval(h);
                vx = vJ[0].Eval(h);
                vy = vJ[1].Eval(h);
                vz = vJ[2].Eval(h);
                bool last = h >= remaining;
                if (last)
                {
                    t = T;
                    tc = 0d;
                }
                else
                {
                    double s = t + h;
                    double bb = s - t;
                    tc += (t - (s - bb)) + (h - bb);
                    t = s;
                }

                tDd = last ? P1bTests.StudyDd.From(T) : P1bTests.StudyDd.Add(tDd, P1bTests.StudyDd.From(h));
                steps++;
                double factor = Math.Min(4d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(norm, 1e-30d), 1d / order)));
                h = Math.Min(maxStep, Math.Max(minStep, h * factor));
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(norm, 1e-30d), 1d / order)));
                double next = Math.Max(minStep, h * factor);
                if (next >= h && h <= minStep)
                {
                    throw new InvalidOperationException("TaylorShip dd step rejected at minStep.");
                }

                h = next;
            }
        }

        sw.Stop();
        return new TaylorShipDdResult
        {
            P = new Vector3d(px.ToDouble, py.ToDouble, pz.ToDouble),
            V = new Vector3d(vx.ToDouble, vy.ToDouble, vz.ToDouble),
            DdPx = px,
            DdPy = py,
            DdPz = pz,
            DdVx = vx,
            DdVy = vy,
            DdVz = vz,
            Calls = calls,
            Steps = steps,
            SegmentClips = segmentClips,
            Ms = sw.Elapsed.TotalMilliseconds
        };
    }

    internal static Vector3d EvaluateAcceleration(StarSystem sys, Vector3d p, double t)
    {
        TaylorJet px = TaylorJet.Constant(0, p.X);
        TaylorJet py = TaylorJet.Constant(0, p.Y);
        TaylorJet pz = TaylorJet.Constant(0, p.Z);
        TaylorJet[] a = Force(sys, px, py, pz, t, 0);
        return new Vector3d(a[0].C[0], a[1].C[0], a[2].C[0]);
    }

    private static P1bTests.DdJet[] ForceDd(
        StarSystem sys, P1bTests.DdJet px, P1bTests.DdJet py, P1bTests.DdJet pz,
        P1bTests.StudyDd t, double tSelect, int order)
    {
        var bodies = sys.AllBodies;
        int n = bodies.Count;
        var index = new Dictionary<OrbitingBody, int>(n);
        for (int i = 0; i < n; i++)
        {
            index[bodies[i]] = i;
        }

        var world = new P1bTests.DdJet[6 * n];
        var zero = P1bTests.DdJet.Constant(order, P1bTests.StudyDd.Zero);
        for (int i = 0; i < n; i++)
        {
            OrbitingBody body = bodies[i];
            if (body.Parent == null)
            {
                world[6 * i] = zero;
                world[(6 * i) + 1] = zero;
                world[(6 * i) + 2] = zero;
                world[(6 * i) + 3] = zero;
                world[(6 * i) + 4] = zero;
                world[(6 * i) + 5] = zero;
                continue;
            }

            TaylorLocalStateDd local = TaylorChebyshev.LocalStateDd(body.Baked, t, tSelect, order);
            int parent = index[body.Parent];
            world[6 * i] = world[6 * parent] + local.X;
            world[(6 * i) + 1] = world[(6 * parent) + 1] + local.Y;
            world[(6 * i) + 2] = world[(6 * parent) + 2] + local.Z;
            world[(6 * i) + 3] = world[(6 * parent) + 3] + local.Vx;
            world[(6 * i) + 4] = world[(6 * parent) + 4] + local.Vy;
            world[(6 * i) + 5] = world[(6 * parent) + 5] + local.Vz;
        }

        var one = P1bTests.DdJet.Constant(order, P1bTests.StudyDd.From(1d));
        var rootAccel = new[]
        {
            P1bTests.DdJet.Constant(order, P1bTests.StudyDd.Zero),
            P1bTests.DdJet.Constant(order, P1bTests.StudyDd.Zero),
            P1bTests.DdJet.Constant(order, P1bTests.StudyDd.Zero)
        };
        OrbitingBody root = bodies[0];
        for (int c = 0; c < root.Children.Count; c++)
        {
            OrbitingBody child = root.Children[c];
            int ci = index[child];
            P1bTests.StudyDd mc = P1bTests.StudyDd.From(
                child.ResolveSubtreeStandardGravitationalParameter());
            P1bTests.DdJet dx = world[6 * ci] - world[0];
            P1bTests.DdJet dy = world[(6 * ci) + 1] - world[1];
            P1bTests.DdJet dz = world[(6 * ci) + 2] - world[2];
            P1bTests.DdJet r2 = (dx * dx) + (dy * dy) + (dz * dz);
            P1bTests.DdJet r = P1bTests.DdJet.Sqrt(r2);
            P1bTests.DdJet inv = one / (r2 * r);
            rootAccel[0] = rootAccel[0] + (dx * (inv * mc));
            rootAccel[1] = rootAccel[1] + (dy * (inv * mc));
            rootAccel[2] = rootAccel[2] + (dz * (inv * mc));
        }

        var total = new[]
        {
            P1bTests.DdJet.Constant(order, P1bTests.StudyDd.Zero),
            P1bTests.DdJet.Constant(order, P1bTests.StudyDd.Zero),
            P1bTests.DdJet.Constant(order, P1bTests.StudyDd.Zero)
        };
        for (int i = 0; i < n; i++)
        {
            double muDouble = bodies[i].ResolveStandardGravitationalParameter();
            if (muDouble == 0d)
            {
                continue;
            }

            P1bTests.StudyDd mu = P1bTests.StudyDd.From(muDouble);
            P1bTests.DdJet dx = world[6 * i] - px;
            P1bTests.DdJet dy = world[(6 * i) + 1] - py;
            P1bTests.DdJet dz = world[(6 * i) + 2] - pz;
            P1bTests.DdJet r2 = (dx * dx) + (dy * dy) + (dz * dz);
            P1bTests.DdJet r = P1bTests.DdJet.Sqrt(r2);
            P1bTests.DdJet inv = one / (r2 * r);
            total[0] = total[0] + (dx * (inv * mu));
            total[1] = total[1] + (dy * (inv * mu));
            total[2] = total[2] + (dz * (inv * mu));
        }

        total[0] = total[0] - rootAccel[0];
        total[1] = total[1] - rootAccel[1];
        total[2] = total[2] - rootAccel[2];
        return total;
    }

    private static double SegmentRemaining(StarSystem sys, double t)
    {
        double remaining = double.PositiveInfinity;
        var bodies = sys.AllBodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            OrbitingBody body = bodies[i];
            if (body.Parent == null)
            {
                continue;
            }

            BakedEphemeris baked = body.Baked;
            if (baked == null || baked.Coeffs == null || baked.SegmentCount <= 0
                || !(baked.SegmentLengthSeconds > 0d))
            {
                continue;
            }

            double segment = Math.Floor((t - baked.T0Seconds) / baked.SegmentLengthSeconds);
            double end = baked.T0Seconds + ((segment + 1d) * baked.SegmentLengthSeconds);
            if (end > t)
            {
                remaining = Math.Min(remaining, end - t);
            }
        }

        return remaining;
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




