using System;
using System.Collections.Generic;
using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
        /// <summary>
        /// Celestial body on a Keplerian "rail" around its parent.
        /// The same structure describes a planet around a star and a moon
        /// around a planet, at any nesting depth. The tree is built via
        /// Parent/Children links; the tree root (a star) is a body without a Parent.
        /// </summary>
    [Serializable]
    public sealed class OrbitingBody : IBodyStateProvider
    {
        public string Name = "Body";

        [Header("Физика")]
        public double Mass = 1d;
        public double StandardGravitationalParameter = 1d;
        public double Radius = 1d;

        /// <summary>
        /// Impact speed against the surface (m/s) tolerated by the Touchdown reaction:
        /// below it a landing, above it a breakup. Currently one threshold per body.
        /// </summary>
        public double CrashToleranceMps = 5d;

        /// <summary>
        /// Surface friction for SurfaceMotion (Coulomb): sticks while
        /// |a_t| ≤ μ_s·|a_n|, sliding is braked by μ_k·|a_n| against motion.
        /// </summary>
        public double SurfaceStaticFrictionMu = 0.7d;

        public double SurfaceKineticFrictionMu = 0.5d;

        /// <summary>
        /// Second zonal harmonic (oblateness) of the gravity field. 0 = point mass (default; old assets unchanged).
        /// Earth 1.08263e-3, Mars 1.96e-3, Moon 2.03e-4, Jupiter 1.4736e-2, Saturn 1.6298e-2.
        /// Acts on the ship only (the rails/baked ephemeris do not feel it). Spin axis = SpinAxis.
        /// </summary>
        public double J2;

        /// <summary>Equatorial radius (m) that J2 is normalized to. 0 = use Radius.</summary>
        public double J2ReferenceRadius;

        /// <summary>Spin axis (same normalization and fallback as in GetSurfaceState).</summary>
        public Vector3d SpinAxis
        {
            get
            {
                Vector3d axis = NorthPoleDirection.Normalized;
                if (axis.SqrMagnitude < 0.5d)
                {
                    axis = new Vector3d(0d, 0d, 1d);
                }

                return axis;
            }
        }

        /// <summary>Angular spin speed (rad/s), 0 if not rotating.</summary>
        public double SpinAngularSpeed
        {
            get { return RotationPeriodSeconds > 0d ? (2d * Math.PI) / RotationPeriodSeconds : 0d; }
        }

        [Header("Кеплеровы элементы (относительно Parent; для корня не используются)")]
        public double SemiMajorAxis = 1d;
        public double Eccentricity;
        public double InclinationDegrees;
        public double LongitudeOfAscendingNodeDegrees;
        public double ArgumentOfPeriapsisDegrees;
        public double MeanAnomalyAtEpochDegrees;
        public double EpochTimeSeconds;

        /// <summary>
        /// Parent the body orbits via the Keplerian elements above.
        /// Null means the tree root (a star); its position is set directly
        /// through RootPosition/RootVelocity rather than elements.
        /// </summary>
        public OrbitingBody Parent;
        public readonly List<OrbitingBody> Children = new List<OrbitingBody>();

        [Header("Только для корня дерева (звезды)")]
        // The world frame is "root fixed at RootPosition" — a NON-inertial frame (the real star is
        // pulled by its planets). Baked ephemeris positions are heliocentric offsets from an n-body
        // run in which the star moves, so the ship's gravity MUST include the indirect term
        // −Σ μ_i·(r_i − r_root)/|r_i − r_root|³ (StarSystem.cachedRootAccel); without it the ship gets
        // a spurious ~2e-7 m/s² push that the bodies themselves do not feel.
        public Vector3d RootPosition = Vector3d.Zero;
        public Vector3d RootVelocity = Vector3d.Zero;

        [Header("Вращение вокруг своей оси (нужно для старта с поверхности)")]
        public double RotationPeriodSeconds; // 0 = rotation ignored
        public double PrimeMeridianOffsetDegrees;
        public Vector3d NorthPoleDirection = new Vector3d(0d, 0d, 1d);

        [Header("Атмосфера (null = нет атмосферы; см. AtmosphereProfile)")]
        public AtmosphereProfile Atmosphere;

        [Header("Облака (null = нет облаков; см. CloudProfile)")]
        public CloudProfile Clouds;

        /// <summary>
        /// Surface model (SphericalTerrain by default). null means consumers
        /// (touchdown detector, SurfaceMotion) must treat the body as a sphere.
        /// </summary>
        public ITerrainModel Terrain;

        public double SphereOfInfluenceRadius;
        public double HillSphereRadius;

        public BakedEphemeris Baked;

        public void SyncMassFromGravitationalParameter()
        {
            if (StandardGravitationalParameter > 0d)
            {
                Mass = PhysicsSolver.StandardGravitationalParameterToMass(StandardGravitationalParameter);
            }
        }

        public double ResolveStandardGravitationalParameter()
        {
            return StandardGravitationalParameter > 0d
                ? StandardGravitationalParameter
                : PhysicsSolver.MassToStandardGravitationalParameter(Mass);
        }

        public double ResolveSubtreeStandardGravitationalParameter()
        {
            double total = ResolveStandardGravitationalParameter();
            for (int i = 0; i < Children.Count; i++)
            {
                total += Children[i].ResolveSubtreeStandardGravitationalParameter();
            }

            return total;
        }

        /// <summary>Recomputes SOI/Hill radii relative to Parent. Skipped for the root.</summary>
        public void UpdateInfluenceRadii()
        {
            if (Parent == null)
            {
                return;
            }

            double subMu = ResolveSubtreeStandardGravitationalParameter();
            double subMass = PhysicsSolver.StandardGravitationalParameterToMass(subMu);
            double parentOwnMass = PhysicsSolver.StandardGravitationalParameterToMass(
                Parent.ResolveStandardGravitationalParameter());

            SphereOfInfluenceRadius = OrbitalElements.CalculateSphereOfInfluenceRadius(
                SemiMajorAxis, subMass, parentOwnMass);

            HillSphereRadius = OrbitalElements.CalculateHillRadius(
                SemiMajorAxis, Eccentricity, subMass, parentOwnMass);
        }

        /// <summary>
        /// World position/velocity of the body at time t (astrodynamics frame,
        /// i.e. without Unity-specific axis mapping — that is the render layer's job).
        /// Fixed for the root. For others, recursive: the parent's state
        /// plus the Keplerian offset relative to it.
        /// </summary>
        /// <summary>
        /// Cache-test counters (section 6 P1b): how many times world states and
        /// local offsets were actually evaluated. Instrumentation only, no effect on physics.
        /// </summary>
        public static long WorldStateEvaluationCount;
        public static long LocalOffsetEvaluationCount;

        public static void ResetEvaluationCounters()
        {
            WorldStateEvaluationCount = 0;
            LocalOffsetEvaluationCount = 0;
        }

        public void EvaluateWorldState(double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            WorldStateEvaluationCount++;
            if (Parent == null)
            {
                position = RootPosition;
                velocity = RootVelocity;
                return;
            }

            Parent.EvaluateWorldState(timeSeconds, out Vector3d parentPosition, out Vector3d parentVelocity);
            EvaluateLocalOffset(timeSeconds, out Vector3d localPosition, out Vector3d localVelocity);

            position = parentPosition + localPosition;
            velocity = parentVelocity + localVelocity;
        }

        /// <summary>
        /// Local Keplerian offset relative to the parent, without recursion upwards.
        /// Public wrapper over the private core; the StarSystem cache uses it to
        /// gather world positions in one parents-first pass instead of
        /// N recursive EvaluateWorldState calls. The math is bit-for-bit the same.
        /// </summary>
        public void ComputeLocalOffset(double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            EvaluateLocalOffset(timeSeconds, out position, out velocity);
        }

        /// <summary>
        /// Local offset (position only). On the baked path it skips the velocity polynomial; the result
        /// is bit-for-bit the position of ComputeLocalOffset. Used by the gravity position cache.
        /// </summary>
        public Vector3d ComputeLocalPosition(double timeSeconds)
        {
            if (Baked != null && Baked.TryEvaluatePosition(timeSeconds, out Vector3d bakedPosition))
            {
                LocalOffsetEvaluationCount++;
                return bakedPosition;
            }

            // Kepler rail, continuation past the end, or a failure with a loud exception: the full path.
            EvaluateLocalOffset(timeSeconds, out Vector3d position, out _);
            return position;
        }

        /// <summary>
        /// Position/velocity RELATIVE to the parent (Keplerian offset), without recursion upwards.
        /// Jacobi approximation: the outer orbit is the motion of the subsystem's barycentre,
        /// μ = μ_parent_own + μ_this_subtree (own + all descendants recursively).
        /// </summary>
        private void EvaluateLocalOffset(double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            LocalOffsetEvaluationCount++;
            if (Baked != null)
            {
                // The range verdict comes only from TryEvaluate (it has fp tolerance
                // at the edges: EndSeconds is recomputed with rounding). A segment
                // that cannot be read still fails loudly.
                if (Baked.TryEvaluate(timeSeconds, out Vector3d bakedPosition, out Vector3d bakedVelocity))
                {
                    position = bakedPosition;
                    velocity = bakedVelocity;
                    return;
                }

                // Fallback past the end of the rail: Keplerian continuation taken from
                // the integrated state at EndSeconds — the seam is continuous.
                // Orbits continue Keplerian rather than throwing.
                double muLocal = Parent.ResolveStandardGravitationalParameter() + ResolveSubtreeStandardGravitationalParameter();
                if (Baked.TryEvaluateContinuation(timeSeconds, muLocal, out Vector3d contPosition, out Vector3d contVelocity))
                {
                    position = contPosition;
                    velocity = contVelocity;
                    return;
                }

                // Fail reason comes from GetFailReason rather than a generic "out of range".
                BakedEphemeris.FailReason reason = Baked.GetFailReason(timeSeconds);
                string reasonText = reason switch
                {
                    BakedEphemeris.FailReason.OutOfRange => "время вне испечённого интервала",
                    BakedEphemeris.FailReason.NoData => "данные эфемериды отсутствуют (Coeffs пусты)",
                    BakedEphemeris.FailReason.Corrupt => "структура эфемериды повреждена (Degree/сегменты вне диапазонов)",
                    _ => "нет продолжения за концом рельсы (невалидный/отсутствующий фиттинг) или данные внутри спана нечитаемы"
                };
                throw new EphemerisRangeException(
                    "Тело " + Name + ": " + reasonText + " (время " + timeSeconds +
                    ", интервал [" + Baked.T0Seconds + ", " + Baked.EndSeconds + "]).",
                    timeSeconds, Name);
            }
            EvaluateKeplerOffset(timeSeconds, out position, out velocity);
        }

        /// <summary>
        /// Pure Keplerian rail offset relative to the parent — ignores Baked. Public so a loaded ephemeris can be
        /// sanity-checked against the rails it was baked from (StarSystem.DescribeEphemerisRailMismatch).
        /// </summary>
        public void EvaluateKeplerOffset(double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            // The rail must be a closed Keplerian orbit. No silent clamping:
            // a hyperbolic orbit quietly turned into an ellipse would mask a bug.
            if (!(SemiMajorAxis > 0d) || !(Eccentricity >= 0d) || !(Eccentricity < 1d))
            {
                throw new ArgumentOutOfRangeException(nameof(Eccentricity),
                    "Тело " + Name + ": рельса обязана быть эллипсом (SemiMajorAxis=" +
                    SemiMajorAxis + ", Eccentricity=" + Eccentricity + "). " +
                    "Гиперболические/вырожденные элементы для кеплеровой рельсы недопустимы.");
            }

            double semiMajorAxis = SemiMajorAxis;
            double eccentricity = Eccentricity;
            double inclination = KeplerMath.DegreesToRadians(InclinationDegrees);
            double ascendingNode = KeplerMath.DegreesToRadians(LongitudeOfAscendingNodeDegrees);
            double periapsis = KeplerMath.DegreesToRadians(ArgumentOfPeriapsisDegrees);
            double meanAnomalyAtEpoch = KeplerMath.DegreesToRadians(MeanAnomalyAtEpochDegrees);

            double mu = Parent.ResolveStandardGravitationalParameter() + ResolveSubtreeStandardGravitationalParameter();

            double meanMotion = Math.Sqrt(mu / (semiMajorAxis * semiMajorAxis * semiMajorAxis));
            double meanAnomaly = KeplerMath.NormalizeAngle(
                meanAnomalyAtEpoch + (meanMotion * (timeSeconds - EpochTimeSeconds)));
            double eccentricAnomaly = KeplerMath.SolveEccentricAnomaly(meanAnomaly, eccentricity);

            double cosE = Math.Cos(eccentricAnomaly);
            double sinE = Math.Sin(eccentricAnomaly);
            double radius = semiMajorAxis * (1d - (eccentricity * cosE));
            double orbitalYScale = Math.Sqrt(1d - (eccentricity * eccentricity));

            Vector3d orbitalPosition = new Vector3d(
                semiMajorAxis * (cosE - eccentricity),
                semiMajorAxis * orbitalYScale * sinE,
                0d);

            double velocityFactor = Math.Sqrt(mu * semiMajorAxis) / radius;
            Vector3d orbitalVelocity = new Vector3d(
                -velocityFactor * sinE,
                velocityFactor * orbitalYScale * cosE,
                0d);

            position = KeplerMath.RotateOrbitalToWorld(orbitalPosition, ascendingNode, inclination, periapsis);
            velocity = KeplerMath.RotateOrbitalToWorld(orbitalVelocity, ascendingNode, inclination, periapsis);
        }

        public void ApplyPeriapsisAndApoapsis(double periapsisDistance, double apoapsisDistance)
        {
            // Element mutation with a live ephemeris would be silently ignored
            // (the rail takes priority in EvaluateLocalOffset), so fail loudly.
            if (Baked != null)
            {
                throw new InvalidOperationException(
                    "Тело " + Name + " использует испечённую эфемериду — правка кеплеровых элементов молча не подействует. Переиспеките систему или уберите Baked.");
            }

            // apoapsis ≤ periapsis gives e<0 / a≤0; reject here too, at config build time.
            if (!(apoapsisDistance > periapsisDistance) || !(periapsisDistance > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(periapsisDistance),
                    "Апоапсис (" + apoapsisDistance + ") обязан быть больше периапсиса (" + periapsisDistance + "), оба положительны.");
            }

            SemiMajorAxis = 0.5d * (periapsisDistance + apoapsisDistance);
            Eccentricity = (apoapsisDistance - periapsisDistance) / (apoapsisDistance + periapsisDistance);
        }

        /// <summary>Angle of the prime meridian at time t (radians) — the body's own rotation.</summary>
        public double RotationAngleAtTime(double timeSeconds)
        {
            double offset = KeplerMath.DegreesToRadians(PrimeMeridianOffsetDegrees);
            if (RotationPeriodSeconds <= 0d)
            {
                return offset;
            }

            double angularSpeed = (2d * Math.PI) / RotationPeriodSeconds;
            return KeplerMath.NormalizeAngle(offset + (angularSpeed * (timeSeconds - EpochTimeSeconds)));
        }

        /// <summary>
        /// Body orientation at time t: body-to-world quaternion (QuaternionD
        /// convention — v_world = q·v_body·q⁻¹). Same path as GetSurfaceState:
        /// first the own spin around ẑ by the prime-meridian angle, then the axis
        /// tilt by the same Rodrigues rotation. Body frame is fixed: +Z is the
        /// north pole (spin axis), +X the prime meridian on the equator, +Y 90°E.
        /// The render layer takes the finished orientation from here.
        /// </summary>
        public QuaternionD GetVisualOrientation(double timeSeconds)
        {
            GetTiltParams(out Vector3d axis, out Vector3d k, out double cosAngle);
            QuaternionD tilt;
            if (cosAngle < -1d + 1e-12d)
            {
                // Same antipodal case as GetSurfaceState: (x,−y,−z), a π rotation about x̂.
                tilt = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), Math.PI);
            }
            else if (k.SqrMagnitude < 1e-300d)
            {
                tilt = QuaternionD.Identity;
            }
            else
            {
                tilt = QuaternionD.FromAxisAngle(k, Math.Acos(cosAngle));
            }

            QuaternionD spin = QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), RotationAngleAtTime(timeSeconds));
            return (tilt * spin).Normalized;
        }

        /// <summary>
        /// World position/velocity of a point on the body's surface (latitude/longitude in
        /// degrees, altitude above Radius in metres) at time t. Accounts for both the body's
        /// current orbital position and its own rotation, so the game can start from a fixed
        /// point on a planet's surface.
        ///
        /// The spin axis is honoured: the equatorial offset is rotated to
        /// NorthPoleDirection via the Rodrigues formula (identity bit-for-bit
        /// when the axis is (0,0,1)).
        /// </summary>
        public void GetSurfaceState(
            double latitudeDegrees, double longitudeDegrees, double altitudeMeters, double timeSeconds,
            out Vector3d worldPosition, out Vector3d worldVelocity)
        {
            EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);

            double lat = KeplerMath.DegreesToRadians(latitudeDegrees);
            double lon = KeplerMath.DegreesToRadians(longitudeDegrees) + RotationAngleAtTime(timeSeconds);
            double r = Radius + altitudeMeters;

            Vector3d equatorial = new Vector3d(
                r * Math.Cos(lat) * Math.Cos(lon),
                r * Math.Cos(lat) * Math.Sin(lon),
                r * Math.Sin(lat));

            GetTiltParams(out Vector3d axis, out Vector3d k, out double cosAngle);

            Vector3d localOffset;
            if (cosAngle < -1d + 1e-12d)
            {
                localOffset = new Vector3d(equatorial.X, -equatorial.Y, -equatorial.Z);
            }
            else
            {
                localOffset = ApplyAxisTilt(equatorial, k, cosAngle);
            }

            worldPosition = bodyPosition + localOffset;

            double angularSpeed = RotationPeriodSeconds > 0d ? (2d * Math.PI) / RotationPeriodSeconds : 0d;
            Vector3d angularVelocity = axis * angularSpeed;
            Vector3d rotationVelocity = Vector3d.Cross(angularVelocity, localOffset);

            worldVelocity = bodyVelocity + rotationVelocity;
        }

        /// <summary>
        /// Inverse of GetSurfaceState: world position → lat/lon.
        /// The basis uses the same Rodrigues mapping as the forward path
        /// (u=R(x̂), v=R(ŷ)) so the two transforms don't differ by ulp noise.
        /// Edge cases: poles (|equatorial projection|≈0) give lon=0 deterministically;
        /// longitude is normalized to [0,2π).
        /// </summary>
        public void SurfaceLatLonAt(Vector3d worldPosition, double timeSeconds, out double latitudeDegrees, out double longitudeDegrees)
        {
            EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            Vector3d offset = worldPosition - bodyPosition;

            GetTiltParams(out Vector3d axis, out Vector3d k, out double cosAngle);

            Vector3d u;
            Vector3d v;
            if (cosAngle < -1d + 1e-12d)
            {
                u = new Vector3d(1d, 0d, 0d);
                v = new Vector3d(0d, -1d, 0d);
            }
            else
            {
                u = ApplyAxisTilt(new Vector3d(1d, 0d, 0d), k, cosAngle);
                v = ApplyAxisTilt(new Vector3d(0d, 1d, 0d), k, cosAngle);
            }

            double axial = Vector3d.Dot(offset, axis);
            Vector3d equatorialProjection = offset - (axis * axial);
            double projectionLength = equatorialProjection.Magnitude;

            double latitude = Math.Atan2(axial, projectionLength);
            double longitude;
            if (projectionLength <= 1e-9d * Math.Max(Radius, 1d))
            {
                longitude = 0d;
            }
            else
            {
                double rotated = Math.Atan2(Vector3d.Dot(equatorialProjection, v), Vector3d.Dot(equatorialProjection, u));
                longitude = KeplerMath.NormalizeAngle(rotated - RotationAngleAtTime(timeSeconds));
            }

            latitudeDegrees = latitude * (180d / Math.PI);
            longitudeDegrees = longitude * (180d / Math.PI);
        }

        /// <summary>Axis-tilt parameters: normalized axis (default on degeneracy), k = ẑ×axis and its cosine. Single source for forward and inverse.</summary>
        private void GetTiltParams(out Vector3d axis, out Vector3d k, out double cosAngle)
        {
            axis = NorthPoleDirection.Normalized;
            if (axis.SqrMagnitude < 0.5d)
            {
                axis = new Vector3d(0d, 0d, 1d);
            }

            cosAngle = Vector3d.Dot(new Vector3d(0d, 0d, 1d), axis);
            k = Vector3d.Cross(new Vector3d(0d, 0d, 1d), axis);
        }

        /// <summary>
        /// Rodrigues rotation of an equatorial vector onto the axis: v + k×v + k×(k×v)/(1+c).
        /// With axis (0,0,1) the correction is exact zero and the result is bit-for-bit the input.
        /// </summary>
        private static Vector3d ApplyAxisTilt(Vector3d equatorial, Vector3d k, double cosAngle)
        {
            Vector3d kCross = Vector3d.Cross(k, equatorial);
            return equatorial + kCross + (Vector3d.Cross(k, kCross) / (1d + cosAngle));
        }

        /// <summary>
        /// 1:1 tidal lock. Period = the body's orbital period using the REDUCED mass
        /// μ_local = μ_parent(own) + μ_own_subtree — the same used by EvaluateLocalOffset.
        /// At the epoch, longitude 0 faces the parent (same body-equatorial basis
        /// as SurfaceLatLonAt). Phase is tied to MEAN anomaly. Only 1:1 is modelled.
        /// Retrograde lock (h·axis ≤ 0) is rejected loudly.
        /// </summary>
        public void ApplyTidalLock()
        {
            if (Parent == null)
            {
                throw new InvalidOperationException("Приливный захват требует родителя: у корня дерева (звезды) его нет.");
            }

            if (!(SemiMajorAxis > 0d))
            {
                throw new InvalidOperationException("Приливный захват \"" + Name + "\": SemiMajorAxis обязан быть положительным.");
            }

            double muLocal = Parent.ResolveStandardGravitationalParameter() + ResolveSubtreeStandardGravitationalParameter();
            if (!(muLocal > 0d))
            {
                throw new InvalidOperationException("Приливный захват \"" + Name + "\": μ_local (родитель + поддерево) обязана быть положительной.");
            }

            EvaluateWorldState(EpochTimeSeconds, out Vector3d bodyP0, out Vector3d bodyV0);
            Parent.EvaluateWorldState(EpochTimeSeconds, out Vector3d parentP0, out Vector3d parentV0);
            Vector3d relative = bodyP0 - parentP0;
            Vector3d relativeVelocity = bodyV0 - parentV0;
            GetTiltParams(out Vector3d axis, out Vector3d k, out double cosAngle);

            Vector3d orbitAngularMomentum = Vector3d.Cross(relative, relativeVelocity);
            if (Vector3d.Dot(orbitAngularMomentum, axis) <= 0d)
            {
                throw new InvalidOperationException(
                    "Приливный захват \"" + Name + "\": поддержан только для проградных орбит (h·axis > 0).");
            }

            RotationPeriodSeconds = 2d * Math.PI * Math.Sqrt(
                (SemiMajorAxis * SemiMajorAxis * SemiMajorAxis) / muLocal);
            double omega = 2d * Math.PI / RotationPeriodSeconds;

            // θ(Epoch) = lonFacing ⇒ SurfaceLatLonAt(sub-parent point, Epoch) = 0.
            Vector3d u = ApplyAxisTilt(new Vector3d(1d, 0d, 0d), k, cosAngle);
            Vector3d v = ApplyAxisTilt(new Vector3d(0d, 1d, 0d), k, cosAngle);
            double axial = Vector3d.Dot(relative, axis);
            Vector3d equatorialProjection = relative - (axis * axial);
            double lonFacing = Math.Atan2(Vector3d.Dot(equatorialProjection, v), Vector3d.Dot(equatorialProjection, u));
            PrimeMeridianOffsetDegrees = KeplerMath.NormalizeAngle(lonFacing) * (180d / Math.PI);
        }
    }
}
