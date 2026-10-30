using System;
using System.Numerics;

namespace GalilegoPhysicsTests.Support
{
    /// <summary>
    /// Exact binary rational built from a double's IEEE-754 bits. Used to check
    /// Butcher-tableau identities and order conditions without the rounding of
    /// double accumulation. Not a general-purpose rational type.
    /// </summary>
    public readonly struct ExactRational
    {
        public readonly BigInteger Numerator;
        public readonly BigInteger Denominator;

        public ExactRational(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero)
            {
                throw new DivideByZeroException();
            }

            if (denominator.Sign < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            BigInteger gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            if (!gcd.IsZero && gcd > BigInteger.One)
            {
                numerator /= gcd;
                denominator /= gcd;
            }

            Numerator = numerator;
            Denominator = denominator;
        }

        public static ExactRational FromDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value == 0d)
            {
                return new ExactRational(BigInteger.Zero, BigInteger.One);
            }

            long bits = BitConverter.DoubleToInt64Bits(value);
            bool negative = bits < 0;
            int exponent = (int)((bits >> 52) & 0x7FF);
            long fraction = bits & 0xFFFFFFFFFFFFFL;
            BigInteger mantissa;
            int power;
            if (exponent == 0)
            {
                mantissa = fraction;
                power = -1074;
            }
            else
            {
                mantissa = fraction | (1L << 52);
                power = exponent - 1075;
            }

            if (negative)
            {
                mantissa = -mantissa;
            }

            return power >= 0
                ? new ExactRational(mantissa << power, BigInteger.One)
                : new ExactRational(mantissa, BigInteger.One << -power);
        }

        public static ExactRational Zero => new ExactRational(BigInteger.Zero, BigInteger.One);
        public static ExactRational One => new ExactRational(BigInteger.One, BigInteger.One);

        public static ExactRational operator +(ExactRational a, ExactRational b) =>
            new ExactRational(
                (a.Numerator * b.Denominator) + (b.Numerator * a.Denominator),
                a.Denominator * b.Denominator);

        public static ExactRational operator -(ExactRational a, ExactRational b) =>
            new ExactRational(
                (a.Numerator * b.Denominator) - (b.Numerator * a.Denominator),
                a.Denominator * b.Denominator);

        public static ExactRational operator -(ExactRational a) =>
            new ExactRational(-a.Numerator, a.Denominator);

        public static ExactRational operator *(ExactRational a, ExactRational b) =>
            new ExactRational(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

        public static ExactRational operator /(ExactRational a, ExactRational b) =>
            new ExactRational(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

        public double ToDouble()
        {
            return (double)Numerator / (double)Denominator;
        }

        public double AbsToDouble()
        {
            return Math.Abs(ToDouble());
        }

        public override string ToString()
        {
            return Numerator + "/" + Denominator;
        }
    }
}
