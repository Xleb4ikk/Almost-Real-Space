using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Physics core of the star system: the body tree (star → planets → moons)
    /// plus ship integration. Deliberately without camera, IMGUI telemetry and
    /// orbit rendering — those are separate responsibilities.
    ///
    /// Motion model is a hybrid:
    /// - All celestial bodies follow Keplerian rails (OrbitingBody), without
    ///   perturbing each other, at any depth.
    /// - The ship flies full n-body: each integrator step receives gravity
    ///   from ALL bodies of the system at once.
    /// </summary>
    public sealed class StarSystem
    {
        private readonly OrbitingBody root;
        private readonly List<OrbitingBody> allBodies = new List<OrbitingBody>();

        public OrbitingBody Root => root;
        public IReadOnlyList<OrbitingBody> AllBodies => allBodies;

        private readonly Dictionary<OrbitingBody, int> bodyIndex = new Dictionary<OrbitingBody, int>();

        private Vector3d[] cachedPositions;

        // Parent index and depth per body (allBodies is a parents-first preorder). They replace a Dictionary
        // lookup per body on every RHS call and the recursive tree walk in FindDominantBody.
        private int[] parentIndex;
        private int[] bodyDepth;

        // Root's direct children and their SUBTREE μ (planet + all its moons). The root's own acceleration (indirect
        // term) is evaluated from these lumps: a moon changes it by ≲1e-11 m/s² (its planet is already counted with
        // the moon's mass), and the loop shrinks from N−1 bodies to the number of planets. Rebuilt in Revalidate.
        private int[] rootChildIndex;
        private double[] rootChildSubtreeMu;

        /// <summary>
        /// Acceleration of the root (star) from all other bodies at the cached time: the indirect term
        /// of the "root fixed" frame. Valid together with cachedPositions.
        /// </summary>
        private Vector3d cachedRootAccel;

        private double cachedTime = double.NaN;
        private bool cacheValid;
        private long cachedStamp;

        /// <summary>Cache instrumentation (test 6): hits/misses per substep time.</summary>
        public long CacheHits;
        public long CacheMisses;

        public StarSystem(OrbitingBody rootBody)
        {
            root = rootBody ?? throw new ArgumentNullException(nameof(rootBody));
            FlattenTree(root);

            for (int i = 0; i < allBodies.Count; i++)
            {
                bodyIndex[allBodies[i]] = i;
            }

            parentIndex = new int[allBodies.Count];
            bodyDepth = new int[allBodies.Count];
            for (int i = 0; i < allBodies.Count; i++)
            {
                parentIndex[i] = allBodies[i].Parent == null ? -1 : bodyIndex[allBodies[i].Parent];
                bodyDepth[i] = parentIndex[i] < 0 ? 0 : bodyDepth[parentIndex[i]] + 1;
            }

            RebuildRootChildLumps();

            foreach (OrbitingBody body in allBodies)
            {
                body.SyncMassFromGravitationalParameter();
            }

            ValidateNamesUnique();
            ValidateHillStability();
            ValidateRadii();
            ValidateScale();
            minimumBodyRadius = ComputeMinimumBodyRadius();

            foreach (OrbitingBody body in allBodies)
            {
                body.UpdateInfluenceRadii();
            }
        }

        /// <summary>Duplicate names silently break manifest attach. Fail loudly at load.</summary>
        private void ValidateNamesUnique()
        {
            var names = new HashSet<string>(allBodies.Count, StringComparer.Ordinal);
            foreach (OrbitingBody body in allBodies)
            {
                if (!names.Add(body.Name ?? string.Empty))
                {
                    throw new InvalidOperationException(
                        "Дубликат имени тела: \"" + body.Name + "\" — attach по манифесту и диагностика становятся неоднозначными.");
                }
            }
        }

        /// <summary>
        /// Non-positive or non-finite radius is a content bug that silently gives
        /// absurd physics (division by zero in altitude detectors). Fail loudly at load.
        /// Small bodies (asteroids) are legitimate content, so no lower floor is enforced.
        /// </summary>
        private void ValidateRadii()
        {
            foreach (OrbitingBody body in allBodies)
            {
                if (!(body.Radius > 0d) || !double.IsFinite(body.Radius))
                {
                    throw new InvalidOperationException(
                        "Тело \"" + body.Name + "\": радиус обязан быть конечным и положительным, получено " + body.Radius + ".");
                }
            }
        }

        /// <summary>
        /// Upper scale bound — the double fp budget. Positions are absolute vectors
        /// from the root; local deltas (altitude, landing contacts) are computed by
        /// subtracting absolutes, so their precision equals the ulp of the scale.
        /// Table: ulp(4.5e11)=6.1e-5 m, ulp(1e13)≈2e-3 m — centimetre relief is still
        /// distinguishable. Orbits beyond MaxOrbitRadiusMeters fail loudly at load
        /// instead of silently degrading. The bound applies to EACH rail (per-body
        /// semi-major axis).
        /// </summary>
        public const double MaxOrbitRadiusMeters = 1e13d;

        private void ValidateScale()
        {
            if (root.RootPosition.Magnitude > MaxOrbitRadiusMeters)
            {
                throw new InvalidOperationException(
                    "Корень системы в " + root.RootPosition.Magnitude.ToString("E2") + " м от начала координат — выше fp-бюджета " +
                    MaxOrbitRadiusMeters.ToString("E1") + " м (ulp > 2e-3 м: рельеф и посадочные допуски теряют смысл).");
            }

            foreach (OrbitingBody body in allBodies)
            {
                if (body.Parent != null && body.SemiMajorAxis > MaxOrbitRadiusMeters)
                {
                    throw new InvalidOperationException(
                        "Тело \"" + body.Name + "\": полуось " + body.SemiMajorAxis.ToString("E2") + " м выше fp-бюджета " +
                        MaxOrbitRadiusMeters.ToString("E1") + " м (ulp > 2e-3 м: рельеф и посадочные допуски теряют смысл).");
                }
            }
        }

        private double minimumBodyRadius;

        /// <summary>
        /// Minimum radius over all bodies — conservative estimate for LOD layers
        /// (e.g. debris hop cap against tunnelling: hop ≤ k·R/v). Snapshot taken at
        /// construction/Revalidate; radii are publicly mutable, so call Revalidate after edits.
        /// </summary>
        public double MinimumBodyRadiusMeters => minimumBodyRadius;

        private double ComputeMinimumBodyRadius()
        {
            double min = double.PositiveInfinity;
            for (int i = 0; i < allBodies.Count; i++)
            {
                if (allBodies[i].Radius < min)
                {
                    min = allBodies[i].Radius;
                }
            }

            return min;
        }

        /// <summary>
        /// Loud validation of long-term moon stability (design rule): a ≤ 0.3 of the
        /// planet's Hill radius and sibling moons separated by ≥ 3 mutual R_H.
        /// The prograde stability limit is ~0.4-0.5 R_H; 0.3 is the design margin.
        /// A moon beyond it is not instantaneously unstable but chaotic with a Lyapunov
        /// time under a year, making any long ephemeris physically uncheckable.
        /// Non-conic orbits (a≤0, e≥1) are skipped — the Keplerian rail rejects them loudly.
        /// </summary>
        private void ValidateHillStability()
        {
            var violations = new List<string>();
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                OrbitingBody parent = body.Parent;
                if (parent == null || parent.Parent == null)
                {
                    continue; // root and planets (direct children of the star) are not checked
                }

                if (!(body.SemiMajorAxis > 0d) || !(body.Eccentricity >= 0d) || !(body.Eccentricity < 1d))
                {
                    continue;
                }

                double muGrandparent = parent.Parent.ResolveStandardGravitationalParameter();
                double muSubtree = parent.ResolveSubtreeStandardGravitationalParameter();
                if (!(muGrandparent > 0d) || !(muSubtree > 0d) || !(parent.SemiMajorAxis > 0d))
                {
                    continue;
                }

                // R_H = a_P · (μ_subtree(P) / (3·μ_own(G)))^{1/3}
                double hill = parent.SemiMajorAxis * Math.Pow(muSubtree / (3d * muGrandparent), 1d / 3d);
                if (!(hill > 0d))
                {
                    continue;
                }

                if (body.SemiMajorAxis > HillStabilityFraction * hill)
                {
                    violations.Add(string.Format(
                        "{0}: a={1:E2}м > {2:F2}·R_H({3})={4:E2}м",
                        body.Name, body.SemiMajorAxis, HillStabilityFraction, parent.Name, HillStabilityFraction * hill));
                }
            }

            // Sibling moons separation: Δa ≥ 3 mutual R_H.
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody parent = allBodies[i];
                if (parent.Parent == null || parent.Children.Count < 2)
                {
                    continue;
                }

                double muParent = parent.ResolveStandardGravitationalParameter();
                if (!(muParent > 0d))
                {
                    continue;
                }

                for (int a = 0; a < parent.Children.Count; a++)
                {
                    OrbitingBody c1 = parent.Children[a];
                    if (!(c1.SemiMajorAxis > 0d) || !(c1.Eccentricity < 1d))
                    {
                        continue;
                    }

                    for (int b = a + 1; b < parent.Children.Count; b++)
                    {
                        OrbitingBody c2 = parent.Children[b];
                        if (!(c2.SemiMajorAxis > 0d) || !(c2.Eccentricity < 1d))
                        {
                            continue;
                        }

                        double mu1 = c1.ResolveSubtreeStandardGravitationalParameter();
                        double mu2 = c2.ResolveSubtreeStandardGravitationalParameter();
                        double aMean = 0.5d * (c1.SemiMajorAxis + c2.SemiMajorAxis);
                        double mutual = aMean * Math.Pow((mu1 + mu2) / (3d * muParent), 1d / 3d);
                        double gap = Math.Abs(c1.SemiMajorAxis - c2.SemiMajorAxis);
                        if (!(mutual > 0d))
                        {
                            continue;
                        }

                        if (gap < MutualSeparationFactor * mutual)
                        {
                            violations.Add(string.Format(
                                "{0} и {1}: разнос a={2:E2}м < {3:F1}·взаимного R_H={4:E2}м",
                                c1.Name, c2.Name, gap, MutualSeparationFactor, MutualSeparationFactor * mutual));
                        }
                    }
                }
            }

            if (violations.Count > 0)
            {
                throw new InvalidOperationException(
                    "Система нестабильна по правилу Хилла (a ≤ " + HillStabilityFraction.ToString("F2") + " R_H, разнос лун ≥ " +
                    MutualSeparationFactor.ToString("F1") + " взаимных R_H): " + string.Join("; ", violations) +
                    ". Такая конфигурация хаотична с ляпуновским временем до года — раздвиньте/уменьшите орбиты спутников.");
            }
        }

        /// <summary>Design threshold for moon prograde stability (fraction of R_H).</summary>
        private const double HillStabilityFraction = 0.3d;

        /// <summary>Minimum separation of sibling moons in mutual Hill radii.</summary>
        private const double MutualSeparationFactor = 3d;

        public void InvalidatePositionCache()
        {
            cacheValid = false;
            cachedTime = double.NaN;
        }

        /// <summary>
        /// Recommended way to attach an ephemeris: swaps Baked and invalidates the
        /// cache atomically. Direct body.Baked = ... is valid only before the first
        /// state query; afterwards the cache would silently return stale Keplerian positions.
        /// A replaced file ephemeris is disposed (its FileStream would otherwise block rewrites).
        /// </summary>
        public void AttachEphemeris(OrbitingBody body, BakedEphemeris ephemeris)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (!bodyIndex.ContainsKey(body))
            {
                throw new InvalidOperationException(
                    "Тело " + body.Name + " не принадлежит этой системе — attach чужого тела запрещён.");
            }

            BakedEphemeris previous = body.Baked;
            body.Baked = ephemeris;
            if (previous != null && !ReferenceEquals(previous, ephemeris) && previous.FilePath != null)
            {
                previous.Dispose();
            }

            InvalidatePositionCache();
        }

        /// <summary>
        /// Re-runs the full validation cycle after config mutations (μ, elements):
        /// SyncMass → Hill → Scale → Radii → InfluenceRadii → cache.
        /// BODY SET IS NOT REFRESHED: FlattenTree/allBodies/bodyIndex are built only
        /// in the constructor; adding bodies after the ctor requires recreating the system.
        /// </summary>
        public void Revalidate()
        {
            foreach (OrbitingBody body in allBodies)
            {
                body.SyncMassFromGravitationalParameter();
            }

            ValidateNamesUnique();
            ValidateHillStability();
            ValidateRadii();
            ValidateScale();
            minimumBodyRadius = ComputeMinimumBodyRadius();

            foreach (OrbitingBody body in allBodies)
            {
                body.UpdateInfluenceRadii();
            }

            RebuildRootChildLumps();
            InvalidatePositionCache();
        }

        private void RebuildRootChildLumps()
        {
            var indices = new List<int>();
            for (int i = 1; i < allBodies.Count; i++)
            {
                if (parentIndex[i] == 0)
                {
                    indices.Add(i);
                }
            }

            rootChildIndex = indices.ToArray();
            rootChildSubtreeMu = new double[rootChildIndex.Length];
            for (int k = 0; k < rootChildIndex.Length; k++)
            {
                rootChildSubtreeMu[k] = allBodies[rootChildIndex[k]].ResolveSubtreeStandardGravitationalParameter();
            }
        }

        private void FlattenTree(OrbitingBody body)
        {
            if (!visitSet.Add(body))
            {
                // A cycle in the hierarchy (A.Parent=B, B.Parent=A) would otherwise overflow the stack.
                throw new InvalidOperationException(
                    "Цикл в иерархии тел: " + body.Name + " посещён второй раз.");
            }

            allBodies.Add(body);
            foreach (OrbitingBody child in body.Children)
            {
                // One-sided repair ("only Children was set") is allowed, but a
                // CONFLICT (child.Parent points to another body) fails loudly instead
                // of silently overwriting the hierarchy.
                if (child.Parent != null && !ReferenceEquals(child.Parent, body))
                {
                    throw new InvalidOperationException(
                        "Тело " + child.Name + " объявлено ребёнком " + body.Name +
                        ", но Parent указывает на " + child.Parent.Name + ".");
                }

                child.Parent = body;
                FlattenTree(child);
            }
        }

        private readonly HashSet<OrbitingBody> visitSet = new HashSet<OrbitingBody>();

        /// <summary>
        /// Gravitational acceleration at shipPosition at time t from ALL bodies of the
        /// system at once. This is the function passed to OrbitIntegrator.StepForward
        /// as accelerationProvider.
        /// </summary>
        public Vector3d EvaluateShipAcceleration(Vector3d shipPosition, double timeSeconds)
        {
            Vector3d totalAcceleration = Vector3d.Zero;
            Vector3d rootPosition = Vector3d.Zero;
            Vector3d rootAcceleration = Vector3d.Zero;

            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
                double bodyMu = body.ResolveStandardGravitationalParameter();

                totalAcceleration += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    shipPosition, bodyPosition, bodyMu);
                totalAcceleration += BodyJ2Acceleration(body, shipPosition, bodyPosition, bodyMu);

                if (i == 0)
                {
                    rootPosition = bodyPosition; // allBodies[0] is the root (preorder)
                }
                else
                {
                    rootAcceleration += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                        rootPosition, bodyPosition, bodyMu);
                }
            }

            // Indirect term: the "root fixed" world frame accelerates (the star is pulled by its planets).
            return totalAcceleration - rootAcceleration;
        }

        /// <summary>J2 (oblateness) term of one body; zero for point masses (J2 == 0) and beyond the cutoff radius.</summary>
        private static Vector3d BodyJ2Acceleration(OrbitingBody body, Vector3d shipPosition, Vector3d bodyPosition, double bodyMu)
        {
            if (body.J2 == 0d)
            {
                return Vector3d.Zero;
            }

            double referenceRadius = body.J2ReferenceRadius > 0d ? body.J2ReferenceRadius : body.Radius;
            return PhysicsSolver.CalculateJ2Acceleration(shipPosition - bodyPosition, body.SpinAxis, bodyMu, body.J2, referenceRadius);
        }

        /// <summary>
        /// World positions of all bodies at time t in one parents-first pass.
        /// Granularity is the specific DOPRI substage time (t, t+c₂h, …), NOT the whole
        /// step: stages must see positions at their own time or RK5 silently loses order.
        /// A parent is computed once per RHS call instead of once per descendant.
        /// Memoizes the last t: a repeated RHS at the same time (FSAL) reuses the array.
        /// </summary>
        public void EnsureCachedPositions(double timeSeconds)
        {
            // Ephemeris-set stamp: direct body.Baked = ... after state queries does
            // not call InvalidatePositionCache, so the stamp detects the change and
            // avoids silently returning stale Keplerian positions.
            long stamp = 0L;
            for (int i = 0; i < allBodies.Count; i++)
            {
                stamp = (stamp * 31) + (allBodies[i].Baked != null ? 1L : 0L) + i;
            }

            if (cacheValid && timeSeconds == cachedTime && cachedPositions != null && cachedPositions.Length == allBodies.Count && stamp == cachedStamp)
            {
                CacheHits++;
                return;
            }

            cachedStamp = stamp;

            CacheMisses++;
            if (cachedPositions == null || cachedPositions.Length != allBodies.Count)
            {
                cachedPositions = new Vector3d[allBodies.Count];
            }

            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                if (body.Parent == null)
                {
                    cachedPositions[i] = body.RootPosition;
                    continue;
                }

                cachedPositions[i] = cachedPositions[parentIndex[i]] + body.ComputeLocalPosition(timeSeconds);
            }

            // Indirect term of the root-fixed frame: the root's own acceleration from all other bodies.
            // Per-body sum (not subtree mass at the child position): the subtree form moves a moon's
            // pull to its parent's position, which is a real acceleration error in the frame
            // (23 m over one year for the Sun at 1 AU and a 7-body field).
            Vector3d rootPosition = cachedPositions[0];
            Vector3d rootAcceleration = Vector3d.Zero;
            for (int i = 1; i < allBodies.Count; i++)
            {
                rootAcceleration += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    rootPosition, cachedPositions[i], allBodies[i].ResolveStandardGravitationalParameter());
            }

            cachedRootAccel = rootAcceleration;
            cachedTime = timeSeconds;
            cacheValid = true;
        }

        /// <summary>Same μ/r³ sum as EvaluateShipAcceleration, but body positions come from the substage cache. Used in the hot RHS loop.</summary>
        /// <param name="includeOblateness">
        /// false = point masses only (plus the root-frame indirect term). Used for the WEIGHT on a spherical
        /// surface: J2 over a sphere gives the wrong equator/pole sign (the real pole is closer to the centre);
        /// orbital flight keeps J2 (default).
        /// </param>
        public Vector3d EvaluateShipAccelerationCached(Vector3d shipPosition, double timeSeconds, bool includeOblateness = true)
        {
            EnsureCachedPositions(timeSeconds);

            Vector3d total = Vector3d.Zero;
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                double bodyMu = body.ResolveStandardGravitationalParameter();
                total += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    shipPosition,
                    cachedPositions[i],
                    bodyMu);
                if (includeOblateness)
                {
                    total += BodyJ2Acceleration(body, shipPosition, cachedPositions[i], bodyMu);
                }
            }

            return total - cachedRootAccel;
        }

        /// <summary>
        /// Acceleration of the ship relative to the central body, formed from relative
        /// vectors (not by subtracting two ~1.5e11 m fields). This is the force used by
        /// the local-frame ship integrator: central point mass + J2, plus every other
        /// body's direct-minus-indirect term and its J2 differential. Translation
        /// invariant, so a LEO propagation does not lose ulp(1e11) per step.
        /// </summary>
        public Vector3d EvaluateShipAccelerationLocal(
            Vector3d relativePosition, OrbitingBody central, double timeSeconds)
        {
            EnsureCachedPositions(timeSeconds);
            int centralIndex = allBodies.IndexOf(central);
            if (centralIndex < 0)
            {
                throw new ArgumentException("central body is not part of the system", nameof(central));
            }

            Vector3d centralPosition = cachedPositions[centralIndex];
            double centralMu = central.ResolveStandardGravitationalParameter();
            Vector3d total = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                relativePosition, Vector3d.Zero, centralMu);
            total += BodyJ2Acceleration(central, relativePosition, Vector3d.Zero, centralMu);
            for (int i = 0; i < allBodies.Count; i++)
            {
                if (i == centralIndex)
                {
                    continue;
                }

                OrbitingBody body = allBodies[i];
                double bodyMu = body.ResolveStandardGravitationalParameter();
                if (bodyMu == 0d)
                {
                    continue;
                }

                Vector3d dc = centralPosition - cachedPositions[i];
                total += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    dc + relativePosition, Vector3d.Zero, bodyMu);
                total -= PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    dc, Vector3d.Zero, bodyMu);
                if (body.J2 != 0d)
                {
                    double referenceRadius = body.J2ReferenceRadius > 0d
                        ? body.J2ReferenceRadius
                        : body.Radius;
                    total += PhysicsSolver.CalculateJ2Acceleration(
                        dc + relativePosition, body.SpinAxis, bodyMu, body.J2, referenceRadius);
                    total -= PhysicsSolver.CalculateJ2Acceleration(
                        dc, body.SpinAxis, bodyMu, body.J2, referenceRadius);
                }
            }

            return total;
        }

        /// <summary>Planner check: false = the point is inside any body, so the candidate trajectory is invalid.</summary>
        public bool TryEvaluateAcceleration(Vector3d shipPosition, double timeSeconds, out Vector3d acceleration)
        {
            EnsureCachedPositions(timeSeconds);

            acceleration = Vector3d.Zero;
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                Vector3d bodyPos = cachedPositions[i];
                double sqr = (bodyPos - shipPosition).SqrMagnitude;
                if (sqr < PhysicsSolver.NumericalFloorSqrDistance
                    || PhysicsSolver.IsInsideBodySqr(sqr, body.Radius))
                {
                    acceleration = Vector3d.Zero;
                    return false;
                }

                if (!PhysicsSolver.TryCalculateAccelerationFromStandardGravitationalParameter(
                    shipPosition, bodyPos, body.ResolveStandardGravitationalParameter(), body.Radius, out Vector3d term))
                {
                    acceleration = Vector3d.Zero;
                    return false;
                }

                acceleration += term;
                acceleration += BodyJ2Acceleration(body, shipPosition, bodyPos, body.ResolveStandardGravitationalParameter());
            }

            acceleration -= cachedRootAccel;
            return acceleration.IsFinite;
        }

        /// <summary>Pure "inside body" predicate — reused by Tier1 without the full driver.</summary>
        public bool IsInsideBody(Vector3d shipPosition, OrbitingBody body, double timeSeconds)
        {
            if (body == null)
            {
                return false;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            double sqr = (bodyPosition - shipPosition).SqrMagnitude;
            return PhysicsSolver.IsInsideBodySqr(sqr, body.Radius);
        }

        /// <summary>Inside any body of the system (for periodic debris checks).</summary>
        public bool IsInsideAnyBody(Vector3d shipPosition, double timeSeconds, out OrbitingBody hitBody)
        {
            EnsureCachedPositions(timeSeconds);
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                double sqr = (cachedPositions[i] - shipPosition).SqrMagnitude;
                if (PhysicsSolver.IsInsideBodySqr(sqr, body.Radius))
                {
                    hitBody = body;
                    return true;
                }
            }

            hitBody = null;
            return false;
        }

        /// <summary>
        /// Ship inside any body's atmosphere: threshold is TopAltitudeMeters (shared
        /// with DragSource and AltitudeCrossingDetector), boundary is alt &lt; top.
        /// Source of the "no warp above ×3 in atmosphere" rule for the game frame.
        /// </summary>
        public bool IsInsideAtmosphere(Vector3d shipPosition, double timeSeconds)
        {
            EnsureCachedPositions(timeSeconds);
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                if (body.Atmosphere == null)
                {
                    continue;
                }

                double altitude = (shipPosition - cachedPositions[i]).Magnitude - body.Radius;
                if (altitude < body.Atmosphere.TopAltitudeMeters)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One ship integration step via DOPRI5, with this class's
        /// EvaluateShipAcceleration as the accelerationProvider. Bodies need no
        /// separate advance: OrbitingBody.EvaluateWorldState is evaluated on the fly.
        /// </summary>
        [Obsolete("Legacy B9 cluster: use SpacecraftPhysics (SpacecraftOrbitIntegrator). Chain: StepShip -> OrbitIntegrator -> RK4/CelestialBody.")]
#pragma warning disable 0618 // Legacy B9 cluster: internal delegation within obsolete surface.
        public IntegrationResult StepShip(CelestialBody ship, double currentTimeSeconds, double dt)
        {
            return OrbitIntegrator.StepForward(
                ship.Position,
                ship.Velocity,
                currentTimeSeconds,
                dt,
                EvaluateShipAcceleration,
                absoluteTolerance: OrbitIntegrator.DefaultAbsoluteTolerance,
                relativeTolerance: OrbitIntegrator.DefaultRelativeTolerance);
        }
#pragma warning restore 0618

        public double BakedEndSeconds()
        {
            double end = double.PositiveInfinity;
            for (int i = 0; i < allBodies.Count; i++)
            {
                BakedEphemeris b = allBodies[i].Baked;
                if (b != null && b.EndSeconds < end)
                {
                    end = b.EndSeconds;
                }
            }

            return end;
        }

        /// <summary>Position/velocity of an arbitrary body at time t — for render, camera, UI.</summary>
        public void EvaluateBodyState(OrbitingBody body, double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            body.EvaluateWorldState(timeSeconds, out position, out velocity);
        }

        /// <summary>
        /// Ready visual frame of a body at time t: position (EvaluateWorldState —
        /// rail/ephemeris), velocity and body-to-world orientation (GetVisualOrientation).
        /// The render layer converts it to a Unity transform via AstroFrame and does not
        /// touch the internals of the rotation representation. The body-frame convention
        /// is fixed in GetVisualOrientation.
        /// </summary>
        public void GetVisualFrame(OrbitingBody body, double timeSeconds, out Vector3d position, out Vector3d velocity, out QuaternionD orientation)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            EvaluateBodyState(body, timeSeconds, out position, out velocity);
            orientation = body.GetVisualOrientation(timeSeconds);
        }

        /// <summary>
        /// Body whose sphere of influence currently contains the ship — the deepest
        /// (innermost) match: if the ship is inside both a planet's SOI and its moon's
        /// SOI, the moon is returned.
        /// </summary>
        /// <summary>
        /// Sanity check of an attached ephemeris against the rails it was baked from: at the bake start each body's
        /// offset from its parent must match the Keplerian rail, except for the planet-centre-vs-barycentre
        /// displacement caused by its own moons (allowed explicitly) and a 1e-4 relative slack.
        /// A mismatch means the files were baked by an older baker (before the rigid-subtree initial-condition fix:
        /// wrong periods, millions of km of drift) or from different elements: re-bake. Returns null when consistent.
        /// </summary>
        public string DescribeEphemerisRailMismatch(double relativeTolerance = 1e-4d)
        {
            System.Text.StringBuilder report = null;
            for (int i = 1; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                BakedEphemeris baked = body.Baked;
                if (baked == null || Math.Abs(baked.T0Seconds) > 1d)
                {
                    continue;
                }

                if (!baked.TryEvaluate(baked.T0Seconds, out Vector3d bakedOffset, out _))
                {
                    continue;
                }

                body.EvaluateKeplerOffset(baked.T0Seconds, out Vector3d railOffset, out _);

                // Own moons displace the planet centre from the subsystem barycentre by up to (μ_moons/μ_subtree)·a_moon.
                double ownMu = body.ResolveStandardGravitationalParameter();
                double allowance = 0d;
                for (int c = 0; c < body.Children.Count; c++)
                {
                    OrbitingBody child = body.Children[c];
                    double childMu = child.ResolveSubtreeStandardGravitationalParameter();
                    allowance += (childMu / (ownMu + childMu)) * child.SemiMajorAxis * (1d + child.Eccentricity);
                }

                allowance += relativeTolerance * railOffset.Magnitude;
                double difference = (bakedOffset - railOffset).Magnitude;
                if (difference > allowance)
                {
                    if (report == null)
                    {
                        report = new System.Text.StringBuilder();
                    }

                    report.Append(body.Name).Append(": ").Append((difference / 1e3).ToString("N0")).Append(" км (допуск ")
                        .Append((allowance / 1e3).ToString("N0")).Append(" км); ");
                }
            }

            return report?.ToString();
        }

        public OrbitingBody FindDominantBody(Vector3d shipPosition, double timeSeconds)
        {
            // One parents-first pass over the cached positions instead of a recursive walk that re-derived
            // every ancestor of every body (O(N·depth) Kepler/Chebyshev evaluations per call). allBodies is
            // a preorder, so the iteration order and the "deepest, last wins" rule match the old recursion.
            EnsureCachedPositions(timeSeconds);

            OrbitingBody best = root;
            int bestDepth = -1;
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                bool insideSoi = body.Parent == null
                    || (shipPosition - cachedPositions[i]).Magnitude <= body.SphereOfInfluenceRadius;
                if (insideSoi && bodyDepth[i] >= bestDepth)
                {
                    bestDepth = bodyDepth[i];
                    best = body;
                }
            }

            return best;
        }
    }
}
