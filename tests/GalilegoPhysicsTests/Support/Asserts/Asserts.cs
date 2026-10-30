using System;
using Galilego.Core;
using Xunit;
using Xunit.Sdk;

namespace GalilegoPhysicsTests.Support
{
    internal static class Asserts
    {
        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new XunitException(message);
            }
        }

        public static void False(bool condition, string message)
        {
            True(!condition, message);
        }

        public static void Finite(double value, string what)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new XunitException(what + ": ожидалось конечное число, получено " + value.ToString("R") + ".");
            }
        }

        public static void Finite(Vector3d value, string what)
        {
            if (!value.IsFinite)
            {
                throw new XunitException(what + ": ожидался конечный вектор, получен " + value + ".");
            }
        }

        public static void Near(double actual, double expected, double tolerance, string what)
        {
            double error = Math.Abs(actual - expected);
            if (!(error <= tolerance))
            {
                throw new XunitException(
                    what + ": измерено " + actual.ToString("R") +
                    ", ожидалось " + expected.ToString("R") +
                    " (|Δ|=" + error.ToString("R") + " > tol=" + tolerance.ToString("R") + ").");
            }
        }

        public static void Relative(double actual, double expected, double relativeTolerance, string what)
        {
            double scale = Math.Max(Math.Abs(expected), Math.Abs(actual));
            double error = Math.Abs(actual - expected);
            double relative = scale > 0d ? error / scale : error;
            if (!(relative <= relativeTolerance))
            {
                throw new XunitException(
                    what + ": измерено " + actual.ToString("R") +
                    ", ожидалось " + expected.ToString("R") +
                    " (отн. |Δ|=" + relative.ToString("R") + " > tol=" + relativeTolerance.ToString("R") + ").");
            }
        }

        public static void VectorNear(Vector3d actual, Vector3d expected, double tolerance, string what)
        {
            double error = (actual - expected).Magnitude;
            if (!(error <= tolerance))
            {
                throw new XunitException(
                    what + ": |Δ|=" + error.ToString("R") + " м > tol=" + tolerance.ToString("R") +
                    "; получено " + actual + ", ожидалось " + expected + ".");
            }
        }

        public static void Throws<TException>(Action action, string what) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception exception)
            {
                throw new XunitException(
                    what + ": ожидалось " + typeof(TException).Name + ", получено " +
                    exception.GetType().Name + ": " + exception.Message);
            }

            throw new XunitException(what + ": ожидалось " + typeof(TException).Name + ", исключения не было.");
        }

        public static void Same(object expected, object actual, string what)
        {
            if (!ReferenceEquals(expected, actual))
            {
                throw new XunitException(what + ": ссылки не совпадают.");
            }
        }
    }
}
