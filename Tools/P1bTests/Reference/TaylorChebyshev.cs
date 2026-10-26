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
                double cv = baked.Coeffs[basis + ((axis + 3) * (degree + 1)) + k];
                pos[axis] += tk * c;
                vel[axis] += tk * cv;
            }
        }

        return new TaylorLocalState(pos[0], pos[1], pos[2], vel[0], vel[1], vel[2]);
    }

    private static TaylorLocalState Zero(int order)
    {
        TaylorJet z = TaylorJet.Constant(order, 0d);
        return new TaylorLocalState(z, z, z, z, z, z);
    }
}

