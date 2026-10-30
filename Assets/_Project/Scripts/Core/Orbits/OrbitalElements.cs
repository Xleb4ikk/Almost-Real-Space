// ============================================================================
// ORBITAL ELEMENTS
// ============================================================================
// Keplerian orbital elements structure.

using System;

namespace Galilego.Core
{
    /// <summary>
    /// Keplerian orbital elements describing a celestial body's orbit.
    /// </summary>
    public readonly struct OrbitalElements
    {
        public readonly bool IsValid;
        public readonly bool IsBound;
        public readonly double SemiMajorAxis;
        public readonly double Eccentricity;
        public readonly Vector3d EccentricityVector;
        public readonly double InclinationDegrees;
        public readonly double LongitudeOfAscendingNodeDegrees;
        public readonly double ArgumentOfPeriapsisDegrees;
        public readonly double TrueAnomalyDegrees;
        public readonly double MeanAnomalyDegrees;
        public readonly double PeriapsisDistance;
        public readonly double ApoapsisDistance;
        public readonly double OrbitalPeriodSeconds;
        public readonly double SpecificOrbitalEnergy;
        public readonly double SpecificAngularMomentum;

        private OrbitalElements(
            bool isValid,
            bool isBound,
            double semiMajorAxis,
            double eccentricity,
            Vector3d eccentricityVector,
            double inclinationDegrees,
            double longitudeOfAscendingNodeDegrees,
            double argumentOfPeriapsisDegrees,
            double trueAnomalyDegrees,
            double meanAnomalyDegrees,
            double periapsisDistance,
            double apoapsisDistance,
            double orbitalPeriodSeconds,
            double specificOrbitalEnergy,
            double specificAngularMomentum)
        {
            IsValid = isValid;
            IsBound = isBound;
            SemiMajorAxis = semiMajorAxis;
            Eccentricity = eccentricity;
            EccentricityVector = eccentricityVector;
            InclinationDegrees = inclinationDegrees;
            LongitudeOfAscendingNodeDegrees = longitudeOfAscendingNodeDegrees;
            ArgumentOfPeriapsisDegrees = argumentOfPeriapsisDegrees;
            TrueAnomalyDegrees = trueAnomalyDegrees;
            MeanAnomalyDegrees = meanAnomalyDegrees;
            PeriapsisDistance = periapsisDistance;
            ApoapsisDistance = apoapsisDistance;
            OrbitalPeriodSeconds = orbitalPeriodSeconds;
            SpecificOrbitalEnergy = specificOrbitalEnergy;
            SpecificAngularMomentum = specificAngularMomentum;
        }

        public static OrbitalElements Invalid => new OrbitalElements(
            false,
            false,
            double.NaN,
            double.NaN,
            Vector3d.Zero,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN);

        /// <summary>
        /// Computes orbital elements from state vectors.
        /// </summary>
        /// <param name="relativePosition">Position relative to the central body in the ASTRODYNAMIC frame (Z-up)</param>
        /// <param name="relativeVelocity">Velocity relative to the central body in the ASTRODYNAMIC frame (Z-up)</param>
        /// <param name="standardGravitationalParameter">Standard gravitational parameter μ = G·M of the central body (m³/s²)</param>
        /// <returns>Orbital elements, or Invalid if the computation is impossible</returns>
        /// <remarks>
        /// ⚠️ IMPORTANT: ASTRODYNAMIC FRAME CONTRACT
        /// 
        /// This method expects both vectors in the ASTRODYNAMIC frame, where +Z ALWAYS
        /// points to the north of the reference plane (ecliptic/equator).
        /// 
        /// If your vectors are in Unity's simulation frame (Y-up), you MUST convert
        /// them via the AstroFrame bridge before calling:
        ///
        /// <code>
        /// // ✅ CORRECT:
        /// var astroPos = AstroFrame.ToAstro(simPos);
        /// var astroVel = AstroFrame.ToAstro(simVel);
        /// var elements = OrbitalElements.FromState(astroPos, astroVel, mu);
        /// 
        /// // ❌ WRONG (yields incorrect inclination, LAN, argument of periapsis):
        /// var elements = OrbitalElements.FromState(simPos, simVel, mu);
        /// </code>
        /// 
        /// Orbital mechanics formulas:
        /// - Angular momentum: h = r × v
        /// - Eccentricity vector: e = (v × h)/μ - r/|r|
        /// - Node: n = k × h, where k = (0, 0, 1) in the astrodynamic frame
        /// - Inclination: i = arccos(h_z / |h|)
        /// - Longitude of ascending node: Ω = arctan2(n_y, n_x)
        /// - Argument of periapsis: ω = arccos(n · e / (|n| |e|))
        /// </remarks>
        public static OrbitalElements FromState(Vector3d relativePosition, Vector3d relativeVelocity, double standardGravitationalParameter)
        {
            const double epsilon = 1e-10d;

            double radius = relativePosition.Magnitude;
            double speedSquared = relativeVelocity.SqrMagnitude;
            if (radius <= epsilon || standardGravitationalParameter <= 0d)
            {
                return Invalid;
            }

            Vector3d angularMomentum = Vector3d.Cross(relativePosition, relativeVelocity);
            double angularMomentumMagnitude = angularMomentum.Magnitude;
            if (angularMomentumMagnitude <= epsilon)
            {
                return Invalid;
            }

#if UNITY_EDITOR
            // Astro-frame conversion is guaranteed by the AstroFrame bridge
            // (Universe/AstroFrame.cs); no heuristic guard is needed here.
#endif

            Vector3d node = Vector3d.Cross(new Vector3d(0d, 0d, 1d), angularMomentum);
            double nodeMagnitude = node.Magnitude;
            // Node test is RELATIVE: an absolute epsilon=1e-10 at |h|~1e10 would take
            // fp direction noise (sin π ≠ 0 in rotations!) for a real node.
            // e is dimensionless, so an absolute threshold is correct for it.
            bool hasAscendingNode = nodeMagnitude > 1e-9d * angularMomentumMagnitude;
            Vector3d eccentricityVector =
                (Vector3d.Cross(relativeVelocity, angularMomentum) / standardGravitationalParameter) -
                (relativePosition / radius);

            double eccentricity = eccentricityVector.Magnitude;
            double energy = (0.5d * speedSquared) - (standardGravitationalParameter / radius);
            bool isParabolic = Math.Abs(energy) <= epsilon;
            double semiMajorAxis = isParabolic
                ? double.PositiveInfinity
                : -standardGravitationalParameter / (2d * energy);

            bool isBound = !isParabolic && semiMajorAxis > 0d && eccentricity < 1d;
            double periapsisDistance = (angularMomentumMagnitude * angularMomentumMagnitude) /
                (standardGravitationalParameter * (1d + eccentricity));
            double apoapsisDistance = isBound
                ? semiMajorAxis * (1d + eccentricity)
                : double.PositiveInfinity;
            double orbitalPeriodSeconds = isBound
                ? 2d * Math.PI * Math.Sqrt((semiMajorAxis * semiMajorAxis * semiMajorAxis) / standardGravitationalParameter)
                : double.PositiveInfinity;

            double inclination = SafeAcos(angularMomentum.Z / angularMomentumMagnitude);
            double longitudeOfAscendingNode = hasAscendingNode
                ? NormalizeAngle(Math.Atan2(node.Y, node.X))
                : 0d;

            // With node≈0 (near-equatorial orbit) the node is undefined, and ω=0 is
            // correct only if the periapsis happens to lie at zero longitude. General
            // rule: ω must map the perifocal angle ν (measured from the TRUE periapsis
            // by the e-vector below) into true longitude. Prograde: ω=Λ_p,
            // retrograde (the R1(π) rotation mirrors the angle): ω=−Λ_p.
            double argumentOfPeriapsis = 0d;
            if (hasAscendingNode && eccentricity > epsilon)
            {
                argumentOfPeriapsis = SafeAcos(Vector3d.Dot(node, eccentricityVector) / (nodeMagnitude * eccentricity));
                if (eccentricityVector.Z < 0d)
                {
                    argumentOfPeriapsis = (2d * Math.PI) - argumentOfPeriapsis;
                }
            }
            else if (eccentricity > epsilon)
            {
                double periapsisLongitude = Math.Atan2(eccentricityVector.Y, eccentricityVector.X);
                argumentOfPeriapsis = angularMomentum.Z >= 0d
                    ? NormalizeAngle(periapsisLongitude)
                    : NormalizeAngle(-periapsisLongitude);
            }
            else if (angularMomentum.Z < 0d)
            {
                argumentOfPeriapsis = Math.PI;
            }

            double trueAnomaly = 0d;
            if (eccentricity > epsilon)
            {
                trueAnomaly = SafeAcos(Vector3d.Dot(eccentricityVector, relativePosition) / (eccentricity * radius));
                if (Vector3d.Dot(relativePosition, relativeVelocity) < 0d)
                {
                    trueAnomaly = (2d * Math.PI) - trueAnomaly;
                }
            }
            else if (hasAscendingNode)
            {
                trueAnomaly = SafeAcos(Vector3d.Dot(node, relativePosition) / (nodeMagnitude * radius));
                if (relativePosition.Z < 0d)
                {
                    trueAnomaly = (2d * Math.PI) - trueAnomaly;
                }
            }
            else if (angularMomentum.Z >= 0d)
            {
                trueAnomaly = NormalizeAngle(Math.Atan2(relativePosition.Y, relativePosition.X));
            }
            else
            {
                // Circular retrograde equatorial: with ω=π longitude L requires
                // ν=π−L (the R1(π) rotation mirrors the angle); otherwise mirrored.
                trueAnomaly = NormalizeAngle(Math.PI - Math.Atan2(relativePosition.Y, relativePosition.X));
            }

            double meanAnomaly = double.NaN;
            if (isBound)
            {
                double eccentricAnomaly = 2d * Math.Atan2(
                    Math.Sqrt(1d - eccentricity) * Math.Sin(trueAnomaly * 0.5d),
                    Math.Sqrt(1d + eccentricity) * Math.Cos(trueAnomaly * 0.5d));
                meanAnomaly = NormalizeAngle(eccentricAnomaly - (eccentricity * Math.Sin(eccentricAnomaly)));
            }

            return new OrbitalElements(
                true,
                isBound,
                semiMajorAxis,
                eccentricity,
                eccentricityVector,
                RadiansToDegrees(inclination),
                RadiansToDegrees(longitudeOfAscendingNode),
                RadiansToDegrees(argumentOfPeriapsis),
                RadiansToDegrees(NormalizeAngle(trueAnomaly)),
                double.IsNaN(meanAnomaly) ? double.NaN : RadiansToDegrees(meanAnomaly),
                periapsisDistance,
                apoapsisDistance,
                orbitalPeriodSeconds,
                energy,
                angularMomentumMagnitude);
        }

        /// <summary>
        /// Computes the Sphere of Influence radius using Laplace's formula.
        /// </summary>
        /// <param name="semiMajorAxis">Semi-major axis of the orbit around the parent body (m)</param>
        /// <param name="orbitingMass">Mass of the orbiting body (kg)</param>
        /// <param name="parentMass">Mass of the parent body (kg)</param>
        /// <returns>Sphere of Influence radius (m)</returns>
        public static double CalculateSphereOfInfluenceRadius(double semiMajorAxis, double orbitingMass, double parentMass)
        {
            if (semiMajorAxis <= 0d || orbitingMass <= 0d || parentMass <= 0d)
            {
                return 0d;
            }

            return semiMajorAxis * Math.Pow(orbitingMass / parentMass, 0.4d);
        }

        /// <summary>
        /// Computes the Hill sphere radius.
        /// </summary>
        /// <param name="semiMajorAxis">Semi-major axis of the orbit around the parent body (m)</param>
        /// <param name="eccentricity">Orbit eccentricity</param>
        /// <param name="orbitingMass">Mass of the orbiting body (kg)</param>
        /// <param name="parentMass">Mass of the parent body (kg)</param>
        /// <returns>Hill sphere radius (m)</returns>
        public static double CalculateHillRadius(double semiMajorAxis, double eccentricity, double orbitingMass, double parentMass)
        {
            if (semiMajorAxis <= 0d || orbitingMass <= 0d || parentMass <= 0d)
            {
                return 0d;
            }

            return semiMajorAxis * (1d - Math.Max(0d, eccentricity)) * Math.Pow(orbitingMass / (3d * parentMass), 1d / 3d);
        }

        private static double AtanhBounded(double x)
        {
            if (x >= 1d)
            {
                return 20d;
            }

            if (x <= -1d)
            {
                return -20d;
            }

            return 0.5d * Math.Log((1d + x) / (1d - x));
        }

        private static double SafeAcos(double value)
        {
            return Math.Acos(Math.Max(-1d, Math.Min(1d, value)));
        }

        private static double NormalizeAngle(double angle)
        {
            double twoPi = Math.PI * 2d;
            angle %= twoPi;
            if (angle < 0d)
            {
                angle += twoPi;
            }

            return angle;
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * (180d / Math.PI);
        }

        /// <summary>
        /// Computes the periapsis and apoapsis positions in the astrodynamic frame (Z-up).
        /// </summary>
        /// <param name="periapsisPosition">Output: periapsis position in the astrodynamic frame (m)</param>
        /// <param name="apoapsisPosition">Output: apoapsis position in the astrodynamic frame (m), or Vector3d.Zero for hyperbolic orbits</param>
        /// <returns>true if the computation succeeds; false if the orbital elements are invalid</returns>
        /// <remarks>
        /// Computes world apsis positions using:
        /// - the eccentricity vector to find the periapsis direction
        /// - r_pe = a(1-e) and r_ap = a(1+e) for the distances
        /// - the rotation matrix R(Ω, i, ω) to transform from the orbital plane into the inertial frame
        /// 
        /// For hyperbolic orbits (e >= 1.0) only the periapsis is returned and
        /// apoapsisPosition is set to Vector3d.Zero.
        /// 
        /// Returned positions are in the ASTRODYNAMIC frame (Z-up), centered on the
        /// central body. Use AstroFrame.ToSimulation() to convert to the simulation frame.
        /// </remarks>
        public bool TryGetApsisPositions(out Vector3d periapsisPosition, out Vector3d apoapsisPosition)
        {
            periapsisPosition = Vector3d.Zero;
            apoapsisPosition = Vector3d.Zero;

            // Validate orbital elements
            if (!IsValid)
            {
                return false;
            }

            const double epsilon = 1e-10d;

            // Check that the eccentricity vector is valid
            if (EccentricityVector.SqrMagnitude < epsilon)
            {
                // Circular orbit - no defined apsides
                return false;
            }

            // Periapsis distance: r_pe = a(1-e)
            // For hyperbolic orbits a < 0, but the formula still works
            double periapsisDistance = SemiMajorAxis * (1.0 - Eccentricity);

            // Periapsis direction - normalized eccentricity vector
            Vector3d periapsisDirection = EccentricityVector.Normalized;

            // Periapsis position in the astrodynamic frame
            periapsisPosition = periapsisDirection * periapsisDistance;

            // For elliptical orbits compute the apoapsis
            if (Eccentricity < 1.0)
            {
                // Apoapsis distance: r_ap = a(1+e)
                double apoapsisDistance = SemiMajorAxis * (1.0 + Eccentricity);

                // Apoapsis direction - opposite to the periapsis
                Vector3d apoapsisDirection = -periapsisDirection;

                // Apoapsis position in the astrodynamic frame
                apoapsisPosition = apoapsisDirection * apoapsisDistance;
            }
            else
            {
                // Hyperbolic or parabolic orbit - no apoapsis
                apoapsisPosition = Vector3d.Zero;
            }

            return true;
        }

        /// <summary>
        /// Computes the time to periapsis and apoapsis from the current position on the orbit.
        /// </summary>
        /// <param name="mu">Gravitational parameter μ = GM of the central body (m³/s²)</param>
        /// <param name="timeToPeriapsis">Output: time to periapsis (seconds)</param>
        /// <param name="timeToApoapsis">Output: time to apoapsis (seconds), or NaN for hyperbolic orbits</param>
        /// <returns>true if the computation succeeds; false if the orbital elements are invalid</returns>
        /// <remarks>
        /// Computes the time to the apsides using:
        /// - mean motion: n = sqrt(μ / a³)
        /// - the current mean anomaly M (already computed in FromState)
        /// - time to periapsis: Δt_pe = (2π - M) / n
        /// - time to apoapsis: Δt_ap = (π - M) / n (elliptical orbits)
        /// 
        /// Handles wraparound when M > π:
        /// - M < π (between periapsis and apoapsis): time to apoapsis = (π - M) / n
        /// - M > π (between apoapsis and periapsis): time to apoapsis = (3π - M) / n
        /// 
        /// For hyperbolic orbits (e >= 1.0) timeToApoapsis is set to NaN.
        /// 
        /// The returned time is relative (from the current moment); add the current
        /// simulation time to get absolute time.
        /// </remarks>
        public bool TryGetTimeToApsides(double mu, out double timeToPeriapsis, out double timeToApoapsis)
        {
            timeToPeriapsis = double.NaN;
            timeToApoapsis = double.NaN;

            // Validate orbital elements
            if (!IsValid)
            {
                return false;
            }

            // Hyperbolic branch: one periapsis, passed once. H0 from the true anomaly
            // (no blow-up at the asymptote: |ν| < ν∞ for finite r; AtanhBounded clamps
            // only literal infinity). Mh0 < 0 — heading to the periapsis: Δt = −Mh0/n.
            // Mh0 ≥ 0 — the periapsis is behind (exactly 0 — currently in it): no future
            // one. A hyperbola never has an apoapsis; near-parabolic orbits (|e−1|≈0, n→0)
            // are unsupported, as in the predictor (NotSupportedException there).
            if (!IsBound)
            {
                timeToApoapsis = double.NaN;
                if (Eccentricity <= 1d + 1e-9d)
                {
                    return false;
                }

                if (mu <= 1e-10d)
                {
                    return false;
                }

                double alpha = -SemiMajorAxis;
                if (!(alpha > 1e-10d))
                {
                    return false;
                }

                double nuRad = TrueAnomalyDegrees * (Math.PI / 180.0);
                if (nuRad > Math.PI)
                {
                    nuRad -= 2d * Math.PI;
                }

                double halfFactor = Math.Sqrt((Eccentricity - 1d) / (Eccentricity + 1d)) * Math.Tan(0.5d * nuRad);
                double h0 = 2d * AtanhBounded(halfFactor);
                double meanHyperbolic = (Eccentricity * Math.Sinh(h0)) - h0;
                if (meanHyperbolic > 0d)
                {
                    timeToPeriapsis = double.NaN;
                    return true;
                }

                if (meanHyperbolic == 0d)
                {
                    timeToPeriapsis = 0d;
                    return true;
                }

                double meanMotionHyp = Math.Sqrt(mu / (alpha * alpha * alpha));
                timeToPeriapsis = -meanHyperbolic / meanMotionHyp;
                return true;
            }

            const double epsilon = 1e-10d;

            // Validate parameters
            if (mu <= epsilon || SemiMajorAxis <= epsilon)
            {
                return false;
            }

            // Mean motion: n = sqrt(μ / a³)
            double n = Math.Sqrt(mu / (SemiMajorAxis * SemiMajorAxis * SemiMajorAxis));

            // Current mean anomaly M (already computed in FromState, in degrees)
            if (double.IsNaN(MeanAnomalyDegrees))
            {
                return false;
            }

            // Convert mean anomaly from degrees to radians
            double M = MeanAnomalyDegrees * (Math.PI / 180.0);

            // Normalize M into [0, 2π)
            M = NormalizeAngle(M);

            // Time to periapsis: Δt_pe = (2π - M) / n
            // M = 0 — at the periapsis, time = 0
            // M = 2π — at the periapsis again, time = 0
            // Exactly at the periapsis (M≈0) the answer is 0, not a full period.
            timeToPeriapsis = M <= 1e-12d ? 0d : (2.0 * Math.PI - M) / n;

            // If the time exceeds the orbital period, subtract one period
            if (timeToPeriapsis > OrbitalPeriodSeconds)
            {
                timeToPeriapsis -= OrbitalPeriodSeconds;
            }

            // Time to apoapsis (elliptical orbits only)
            if (Eccentricity < 1.0)
            {
                // Apoapsis occurs at M = π
                // M < π (between periapsis and apoapsis): time = (π - M) / n
                // M > π (between apoapsis and periapsis): time = (3π - M) / n = (2π + π - M) / n
                // Boundary inclusive: exactly at the apoapsis (M = π) the time is 0,
                // not a full period.
                if (M <= Math.PI)
                {
                    timeToApoapsis = (Math.PI - M) / n;
                }
                else
                {
                    timeToApoapsis = (3.0 * Math.PI - M) / n;
                }
            }
            else
            {
                // Hyperbolic or parabolic orbit - no apoapsis
                timeToApoapsis = double.NaN;
            }

            return true;
        }
    }
}
