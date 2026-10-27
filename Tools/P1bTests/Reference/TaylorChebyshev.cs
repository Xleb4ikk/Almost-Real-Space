using Galilego.Universe;
using System;

internal readonly struct TaylorLocalState
{
    public readonly TaylorJet X;
    public readonly TaylorJet Y;
    public readonly TaylorJet Z;
    public readonly TaylorJet Vx;
    public readonly TaylorJet Vy;
    public readonly TaylorJet Vz;

    public TaylorLocalState(TaylorJet x, TaylorJet y, TaylorJet z, TaylorJet vx, TaylorJet vy, TaylorJet vz)
    {
        X = x;
        Y = y;
        Z = z;
        Vx = vx;
        Vy = vy;
        Vz = vz;
    }
}

internal readonly struct TaylorLocalStateDd
{
    public readonly P1bTests.DdJet X;
    public readonly P1bTests.DdJet Y;
    public readonly P1bTests.DdJet Z;
    public readonly P1bTests.DdJet Vx;
    public readonly P1bTests.DdJet Vy;
    public readonly P1bTests.DdJet Vz;

    public TaylorLocalStateDd(
        P1bTests.DdJet x, P1bTests.DdJet y, P1bTests.DdJet z,
        P1bTests.DdJet vx, P1bTests.DdJet vy, P1bTests.DdJet vz)
    {
        X = x;
        Y = y;
        Z = z;
        Vx = vx;
        Vy = vy;
        Vz = vz;
    }
}

internal static class TaylorChebyshev
{
    public static TaylorLocalState LocalState(BakedEphemeris baked, double t, int order)
    {
        if (baked == null || baked.Coeffs == null || baked.SegmentCount <= 0 || !(baked.SegmentLengthSeconds > 0d))
        {
            return Zero(order);
        }

        int seg = (int)Math.Floor((t - baked.T0Seconds) / baked.SegmentLengthSeconds);
        seg = Math.Max(0, Math.Min(baked.SegmentCount - 1, seg));
        double segStart = baked.T0Seconds + (seg * baked.SegmentLengthSeconds);
        double u0 = (2d * (t - segStart) / baked.SegmentLengthSeconds) - 1d;
        double du = 2d / baked.SegmentLengthSeconds;

        int degree = baked.Degree;
        int stride = BakedEphemeris.AxisStride(degree);
        int basis = seg * stride;

        TaylorJet u = TaylorJet.Variable(order, u0, du);
        TaylorJet t0 = TaylorJet.Constant(order, 1d);
        TaylorJet t1 = u;
        var pos = new TaylorJet[3];
        var vel = new TaylorJet[3];
        for (int axis = 0; axis < 3; axis++)
        {
            pos[axis] = TaylorJet.Constant(order, 0d);
            vel[axis] = TaylorJet.Constant(order, 0d);
        }

        for (int k = 0; k <= degree; k++)
        {
            TaylorJet tk;
            if (k == 0)
            {
                tk = t0;
            }
            else if (k == 1)
            {
                tk = t1;
            }
            else
            {
                tk = (u * t1 * 2d) - t0;
                t0 = t1;
                t1 = tk;
            }

            for (int axis = 0; axis < 3; axis++)
            {
                double c = baked.Coeffs[basis + (axis * (degree + 1)) + k];
                pos[axis] += tk * c;
            }
        }

        for (int axis = 0; axis < 3; axis++)
        {
            vel[axis] = pos[axis].Derivative();
        }

        return new TaylorLocalState(pos[0], pos[1], pos[2], vel[0], vel[1], vel[2]);
    }

    public static TaylorLocalStateDd LocalStateDd(
        BakedEphemeris baked, P1bTests.StudyDd t, int order)
    {
        return LocalStateDd(baked, t, t.ToDouble, order);
    }

    public static TaylorLocalStateDd LocalStateDd(
        BakedEphemeris baked, P1bTests.StudyDd t, double tSelect, int order)
    {
        if (baked == null || baked.Coeffs == null || baked.SegmentCount <= 0
            || !(baked.SegmentLengthSeconds > 0d))
        {
            return ZeroDd(order);
        }

        int seg = (int)Math.Floor((tSelect - baked.T0Seconds) / baked.SegmentLengthSeconds);
        seg = Math.Max(0, Math.Min(baked.SegmentCount - 1, seg));
        P1bTests.StudyDd segStart = P1bTests.StudyDd.Add(
            P1bTests.StudyDd.From(baked.T0Seconds),
            P1bTests.StudyDd.Mul(
                P1bTests.StudyDd.From(seg),
                P1bTests.StudyDd.From(baked.SegmentLengthSeconds)));
        P1bTests.StudyDd one = P1bTests.StudyDd.From(1d);
        P1bTests.StudyDd two = P1bTests.StudyDd.From(2d);
        P1bTests.StudyDd length = P1bTests.StudyDd.From(baked.SegmentLengthSeconds);
        P1bTests.StudyDd u0 = P1bTests.StudyDd.Sub(
            P1bTests.StudyDd.Div(
                P1bTests.StudyDd.Mul(
                    two, P1bTests.StudyDd.Sub(t, segStart)),
                length),
            one);
        P1bTests.StudyDd du = P1bTests.StudyDd.Div(two, length);

        P1bTests.DdJet u = P1bTests.DdJet.Variable(order, u0, du);
        P1bTests.DdJet t0 = P1bTests.DdJet.Constant(order, one);
        P1bTests.DdJet t1 = u;
        var pos = new P1bTests.DdJet[3];
        for (int axis = 0; axis < 3; axis++)
        {
            pos[axis] = P1bTests.DdJet.Constant(order, P1bTests.StudyDd.Zero);
        }

        int degree = baked.Degree;
        int stride = BakedEphemeris.AxisStride(degree);
        int basis = seg * stride;
        for (int k = 0; k <= degree; k++)
        {
            P1bTests.DdJet tk;
            if (k == 0)
            {
                tk = t0;
            }
            else if (k == 1)
            {
                tk = t1;
            }
            else
            {
                tk = (u * t1 * P1bTests.StudyDd.From(2d)) - t0;
                t0 = t1;
                t1 = tk;
            }

            for (int axis = 0; axis < 3; axis++)
            {
                P1bTests.StudyDd c = P1bTests.StudyDd.From(
                    baked.Coeffs[basis + (axis * (degree + 1)) + k]);
                pos[axis] = pos[axis] + (tk * c);
            }
        }

        return new TaylorLocalStateDd(
            pos[0], pos[1], pos[2],
            pos[0].Derivative(), pos[1].Derivative(), pos[2].Derivative());
    }

    private static TaylorLocalStateDd ZeroDd(int order)
    {
        P1bTests.DdJet z = P1bTests.DdJet.Constant(order, P1bTests.StudyDd.Zero);
        return new TaylorLocalStateDd(z, z, z, z, z, z);
    }

    private static TaylorLocalState Zero(int order)
    {
        TaylorJet z = TaylorJet.Constant(order, 0d);
        return new TaylorLocalState(z, z, z, z, z, z);
    }
}
