using System;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

namespace Galilego.Debris
{
    /// <summary>
    /// One debris slot. The body is pre-created in the pool constructor and never
    /// re-created with new in the update loop — Spawn only overwrites the
    /// Position/Velocity/Mass fields. Debris merging here is NOT physics: visual
    /// coarsening is a TODO for the render layer, not this file.
    /// </summary>
    public sealed class DebrisSlot
    {
        public readonly Spacecraft.Spacecraft Body;
        public bool Active;
        public double DeathTimeSeconds;
        public double NextCheckTimeSeconds;
        public long Sequence;

        public DebrisSlot()
        {
            Body = new Spacecraft.Spacecraft(Vector3d.Zero, Vector3d.Zero, 1d);
            Active = false;
            DeathTimeSeconds = double.PositiveInfinity;
            NextCheckTimeSeconds = 0d;
            Sequence = 0;
        }
    }

    /// <summary>
    /// Fixed pool of N=256 with no allocations in the hot path. A new debris
    /// takes a free slot or evicts the oldest one when full.
    /// </summary>
    public sealed class DebrisPool
    {
        public const int Capacity = 256;

        private readonly DebrisSlot[] slots = new DebrisSlot[Capacity];
        private long nextSequence = 1;

        public DebrisPool()
        {
            for (int i = 0; i < Capacity; i++)
            {
                slots[i] = new DebrisSlot();
            }
        }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Capacity; i++)
                {
                    if (slots[i].Active)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public DebrisSlot SlotAt(int index)
        {
            return slots[index];
        }

        /// <summary>
        /// Takes a slot without a single new: reuses the pre-created Body.
        /// When full, evicts the oldest (smallest Sequence).
        /// Returns the index of the taken slot.
        /// </summary>
        public int Spawn(Vector3d position, Vector3d velocity, double mass, double nowSeconds, double lifetimeSeconds)
        {
            int free = -1;
            for (int i = 0; i < Capacity; i++)
            {
                if (!slots[i].Active)
                {
                    free = i;
                    break;
                }
            }

            if (free < 0)
            {
                long oldest = long.MaxValue;
                int oldestIdx = 0;
                for (int i = 0; i < Capacity; i++)
                {
                    if (slots[i].Sequence < oldest)
                    {
                        oldest = slots[i].Sequence;
                        oldestIdx = i;
                    }
                }

                free = oldestIdx;
            }

            DebrisSlot slot = slots[free];
            slot.Body.Position = position;
            slot.Body.Velocity = velocity;
            slot.Body.Mass = mass;
            slot.Active = true;
            slot.DeathTimeSeconds = nowSeconds + lifetimeSeconds;
            slot.NextCheckTimeSeconds = nowSeconds;
            slot.Sequence = nextSequence++;
            return free;
        }

        public void Release(int index)
        {
            slots[index].Active = false;
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                slots[i].Active = false;
            }
        }
    }

    /// <summary>
    /// Tier1 debris update: ballistics only (single GravitySource, constant mass),
    /// coarse tolerances ×50 of the active ship, periodic IsInsideAnyBody predicate
    /// instead of the full event machine. Distance-based sleep: large hops (10-60 s)
    /// far from the focus, small ones nearby; accuracy is intentionally below Tier0.
    ///
    /// Anti-tunneling cap: IsInsideAnyBody is checked only at the END of a hop, so
    /// hop ≤ k·R_min/v (k=0.25) guarantees no chord with L ≥ 0.25·R_min is missed
    /// (only a grazing rim ~0.8% of the radius deep can escape — documented
    /// residual); for R/v > FarHop/k the LOD hop is already below the cap. Bodies
    /// smaller than MinHopSeconds·v/k are not detected reliably — the Tier1 limit,
    /// rejected loudly by system-scale validation (S2).
    /// </summary>
    public sealed class DebrisUpdater
    {
        public const double NearDistanceMeters = 100000d;
        public const double NearHopSeconds = 1d;
        public const double FarHopSeconds = 30d;
        public const double CoarseFactor = 50d;

        /// <summary>Radius fraction for the anti-tunneling cap: hop ≤ 0.25·R/v.</summary>
        public const double TunnelSafetyFactor = 0.25d;

        /// <summary>
        /// Body reach zone: the cap applies only to bodies closer than
        /// ReachabilityRadii·R, otherwise distant fast bodies would flatten hops
        /// across the whole system. From 100·R a debris needs hours to arrive at
        /// any reasonable speed, so the cap engages well in advance.
        /// </summary>
        public const double ReachabilityRadii = 100d;

        /// <summary>Hop floor: the cap stops working for bodies smaller than ~MinHop·v/k.</summary>
        public const double MinHopSeconds = 0.25d;

        /// <summary>
        /// Surface bounce coefficient (0 = debris is removed on entering a body).
        /// > 0 — simplified arcade bounce: the position is projected onto the body
        /// radius, the normal velocity is reflected with this coefficient, and the
        /// tangential one is damped by TangentialDampFactor.
        /// </summary>
        public double SurfaceRestitution;

        /// <summary>Factor by which tangential velocity is damped on a bounce.</summary>
        public double TangentialDampFactor = 0.5d;

        private readonly StarSystem starSystem;
        private readonly SpacecraftPhysics physics;

        public DebrisUpdater(StarSystem starSystem)
        {
            this.starSystem = starSystem ?? throw new ArgumentNullException(nameof(starSystem));
            physics = new SpacecraftPhysics
            {
                PositionAbsoluteTolerance = 1e-2d * CoarseFactor,
                VelocityAbsoluteTolerance = 1e-5d * CoarseFactor,
                MassAbsoluteTolerance = 1e-4d * CoarseFactor,
                RelativeTolerance = 1e-10d,
            };
            physics.Sources.Add(new CachedGravitySource(starSystem));
        }

        /// <summary>Hop length by LOD distance to the focus (active ship/camera).</summary>
        public double HopForDistance(double distanceMeters)
        {
            return distanceMeters < NearDistanceMeters ? NearHopSeconds : FarHopSeconds;
        }

        /// <summary>
        /// Full hop for a slot: LOD hop by distance to the focus, squeezed by the
        /// anti-tunneling cap 0.25·R/v over bodies in the reach zone (&lt; 100·R) using
        /// the RELATIVE velocity to the body (a debris near a planet has 5 km/s
        /// relative while inertial is 30), with a MinHopSeconds floor. The divisor is
        /// clamped from below at the body's parabolic speed: v_rel → 0 (a co-moving
        /// piece) would otherwise give cap → ∞ and silently disable the cap, while one
        /// hop of free fall can accelerate such a piece to parabolic speed.
        /// Body states come from a local per-time cache (one fill per unique t).
        /// </summary>
        public double HopForSlot(DebrisSlot slot, Vector3d focusPosition, double nowSeconds)
        {
            double hop = HopForDistance((slot.Body.Position - focusPosition).Magnitude);
            RefreshBodyStates(nowSeconds);
            for (int i = 0; i < starSystem.AllBodies.Count; i++)
            {
                OrbitingBody body = starSystem.AllBodies[i];
                Vector3d relativePosition = slot.Body.Position - bodyPositionsCache[i];
                if (relativePosition.Magnitude >= ReachabilityRadii * body.Radius)
                {
                    continue;
                }

                double vRel = (slot.Body.Velocity - bodyVelocitiesCache[i]).Magnitude;
                double mu = body.ResolveStandardGravitationalParameter();
                double floorSpeed = mu > 0d
                    ? Math.Sqrt(2d * mu / Math.Max(relativePosition.Magnitude, body.Radius))
                    : 0d;
                if (vRel < floorSpeed)
                {
                    vRel = floorSpeed;
                }

                double cap = TunnelSafetyFactor * body.Radius / vRel;
                if (cap < hop)
                {
                    hop = Math.Max(cap, MinHopSeconds);
                }
            }

            return hop;
        }

        private Vector3d[] bodyPositionsCache;
        private Vector3d[] bodyVelocitiesCache;
        private double bodyStateTime = double.NaN;

        /// <summary>
        /// Local body-state cache for nowSeconds. Main thread only (pool owner):
        /// mutable state without synchronization.
        /// </summary>
        private void RefreshBodyStates(double nowSeconds)
        {
            int count = starSystem.AllBodies.Count;
            if (bodyPositionsCache == null || bodyPositionsCache.Length != count)
            {
                bodyPositionsCache = new Vector3d[count];
                bodyVelocitiesCache = new Vector3d[count];
                bodyStateTime = double.NaN;
            }

            if (bodyStateTime == nowSeconds)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                starSystem.EvaluateBodyState(starSystem.AllBodies[i], nowSeconds, out bodyPositionsCache[i], out bodyVelocitiesCache[i]);
            }

            bodyStateTime = nowSeconds;
        }

        /// <summary>
        /// Advances one slot by a single LOD-sized hop. No allocations: just Step
        /// plus the predicate. Returns false if the slot was released (entered a
        /// body or its lifetime expired).
        /// </summary>
        public bool AdvanceSlot(DebrisPool pool, int index, double nowSeconds, Vector3d focusPosition)
        {
            DebrisSlot slot = pool.SlotAt(index);
            if (!slot.Active)
            {
                return false;
            }

            if (nowSeconds >= slot.DeathTimeSeconds)
            {
                pool.Release(index);
                return false;
            }

            double hop = HopForSlot(slot, focusPosition, nowSeconds);

            physics.Step(slot.Body, nowSeconds, hop);

            double checkTime = nowSeconds + hop;
            if (starSystem.IsInsideAnyBody(slot.Body.Position, checkTime, out OrbitingBody hitBody))
            {
                if (!BounceOffSurface(slot, hitBody, checkTime))
                {
                    pool.Release(index);
                    return false;
                }
            }

            if (checkTime >= slot.DeathTimeSeconds)
            {
                pool.Release(index);
                return false;
            }

            slot.NextCheckTimeSeconds = checkTime;
            return true;
        }

        /// <summary>
        /// Simplified surface bounce: projection onto the body radius (spherical
        /// model; ITerrainModel terrain is not applied here), normal reflection with
        /// restitution, tangential damping. false — bounce impossible (restitution
        /// off or degenerate geometry), so the slot is released.
        /// </summary>
        private bool BounceOffSurface(DebrisSlot slot, OrbitingBody body, double timeSeconds)
        {
            if (SurfaceRestitution <= 0d || body == null)
            {
                return false;
            }

            if (timeSeconds >= slot.DeathTimeSeconds)
            {
                return false;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            Vector3d relative = slot.Body.Position - bodyPosition;
            double distance = relative.Magnitude;
            if (distance <= 0d)
            {
                return false;
            }

            Vector3d normal = relative / distance;
            Vector3d surfaceVelocity = bodyVelocity
                + Vector3d.Cross(body.SpinAxis * body.SpinAngularSpeed, normal * body.Radius);
            Vector3d relativeVelocity = slot.Body.Velocity - surfaceVelocity;
            double normalSpeed = Vector3d.Dot(relativeVelocity, normal);
            Vector3d tangential = relativeVelocity - (normal * normalSpeed);

            slot.Body.Position = bodyPosition + (normal * (body.Radius * 1.001d));
            slot.Body.Velocity = surfaceVelocity
                + (tangential * TangentialDampFactor)
                + (normal * (Math.Abs(normalSpeed) * SurfaceRestitution));
            return true;
        }

        /// <summary>
        /// Advances all active slots (for large N, call in parts via the scheduler —
        /// this method is for tests/small N).
        /// </summary>
        public void AdvanceAll(DebrisPool pool, double nowSeconds, Vector3d focusPosition)
        {
            for (int i = 0; i < DebrisPool.Capacity; i++)
            {
                if (pool.SlotAt(i).Active)
                {
                    AdvanceSlot(pool, i, nowSeconds, focusPosition);
                }
            }
        }
    }
}
