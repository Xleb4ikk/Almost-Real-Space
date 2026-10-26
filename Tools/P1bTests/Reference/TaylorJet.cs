using System;

// Truncated Taylor series (a "jet") in one variable tau:
//   f(tau) = C[0] + C[1]*tau + ... + C[Order]*tau^Order.
// Jet arithmetic is the automatic differentiation used by the Taylor integrator.
internal sealed class TaylorJet
{
    public readonly int Order;
    public readonly double[] C;

    public TaylorJet(int order)
    {
        Order = order;
        C = new double[order + 1];
    }

    public static TaylorJet Constant(int order, double value)
    {
        var j = new TaylorJet(order);
        j.C[0] = value;
        return j;
    }

    public static TaylorJet Variable(int order, double value, double slope)
    {
        var j = new TaylorJet(order);
        j.C[0] = value;
        if (order >= 1)
        {
            j.C[1] = slope;
        }

        return j;
    }

    public static TaylorJet FromSeries(int order, double[] values)
    {
        var j = new TaylorJet(order);
        int n = Math.Min(order, values.Length);
        for (int i = 0; i < n; i++)
        {
            j.C[i] = values[i];
        }

        return j;
    }

    public TaylorJet Clone()
    {
        var j = new TaylorJet(Order);
        Array.Copy(C, j.C, C.Length);
        return j;
    }

    public static TaylorJet Add(TaylorJet a, TaylorJet b)
    {
        var r = new TaylorJet(a.Order);
        for (int i = 0; i <= a.Order; i++)
        {
            r.C[i] = a.C[i] + b.C[i];
        }

        return r;
    }

    public static TaylorJet Sub(TaylorJet a, TaylorJet b)
    {
        var r = new TaylorJet(a.Order);
        for (int i = 0; i <= a.Order; i++)
        {
            r.C[i] = a.C[i] - b.C[i];
        }

        return r;
    }

    public static TaylorJet Neg(TaylorJet a)
    {
        var r = new TaylorJet(a.Order);
        for (int i = 0; i <= a.Order; i++)
        {
            r.C[i] = -a.C[i];
        }

        return r;
    }

    public static TaylorJet Mul(TaylorJet a, TaylorJet b)
    {
        int order = a.Order;
        var r = new TaylorJet(order);
        for (int n = 0; n <= order; n++)
        {
            double sum = 0d;
            for (int k = 0; k <= n; k++)
            {
                sum += a.C[k] * b.C[n - k];
            }

            r.C[n] = sum;
        }

        return r;
    }

    public static TaylorJet Scale(TaylorJet a, double s)
    {
        var r = new TaylorJet(a.Order);
        for (int i = 0; i <= a.Order; i++)
        {
            r.C[i] = a.C[i] * s;
        }

        return r;
    }

    public static TaylorJet Div(TaylorJet a, TaylorJet b)
    {
        int order = a.Order;
        var r = new TaylorJet(order);
        r.C[0] = a.C[0] / b.C[0];
        for (int n = 1; n <= order; n++)
        {
            double sum = a.C[n];
            for (int k = 1; k <= n; k++)
            {
                sum -= b.C[k] * r.C[n - k];
            }

            r.C[n] = sum / b.C[0];
        }

        return r;
    }

    public static TaylorJet Sqrt(TaylorJet a)
    {
        int order = a.Order;
        var r = new TaylorJet(order);
        r.C[0] = Math.Sqrt(a.C[0]);
        double denom = 2d * r.C[0];
        for (int n = 1; n <= order; n++)
        {
            double sum = a.C[n];
            for (int k = 1; k < n; k++)
            {
                sum -= r.C[k] * r.C[n - k];
            }

            r.C[n] = sum / denom;
        }

        return r;
    }

    public TaylorJet Derivative()
    {
        var r = new TaylorJet(Order);
        for (int n = 0; n < Order; n++)
        {
            r.C[n] = (n + 1d) * C[n + 1];
        }

        return r;
    }

    public TaylorJet Integral()
    {
        var r = new TaylorJet(Order);
        for (int n = 1; n <= Order; n++)
        {
            r.C[n] = C[n - 1] / n;
        }

        return r;
    }

    public double Eval(double tau)
    {
        double value = 0d;
        for (int n = Order; n >= 0; n--)
        {
            value = (value * tau) + C[n];
        }

        return value;
    }

    public static TaylorJet operator +(TaylorJet a, TaylorJet b) => Add(a, b);
    public static TaylorJet operator -(TaylorJet a, TaylorJet b) => Sub(a, b);
    public static TaylorJet operator -(TaylorJet a) => Neg(a);
    public static TaylorJet operator *(TaylorJet a, TaylorJet b) => Mul(a, b);
    public static TaylorJet operator *(TaylorJet a, double s) => Scale(a, s);
    public static TaylorJet operator *(double s, TaylorJet a) => Scale(a, s);
    public static TaylorJet operator /(TaylorJet a, TaylorJet b) => Div(a, b);
}
