using System;
using Galilego.Core;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Pure player-walking logic over supports (no UnityEngine): vertical support,
    /// step up, slope, ceiling, corners, falling, moving-platform carry.
    /// Runs in local meters of the site's tangent frame (x = east, y = north,
    /// z = up). State — the capsule's feet (Position), velocity (Velocity),
    /// Airborne. Fixed step, called on every player substep.
    ///
    /// Order within a substep: blocking is checked at the START of the substep,
    /// before movement is applied. Render/astro desync is not tolerated: the
    /// support source must work on the same pose as the query.
    ///
    /// Rules (defaults):
    ///  - step up <= StepUpMeters raises without a jump, higher — blocked;
    ///  - slope <= MaxSlopeDegrees is walkable, steeper — slide without rise;
    ///  - ceiling below CapsuleHeight blocks entry (physically, via capsule sweep);
    ///  - no support within SupportDropMeters — free fall;
    ///  - corner: slide along the wall, up to MaxSweepIterations per substep.
    /// </summary>
    public sealed class PlayerSurfaceController
    {
        /// <summary>Player capsule radius (m): shoulders ~0.5 m (no wider fits the cramped Orion cabin).</summary>
        public const double CapsuleRadius = 0.25d;

        /// <summary>Capsule height (m): player height 1.40 m plus headroom.</summary>
        public const double CapsuleHeight = 1.45d;

        /// <summary>
        /// Crouched capsule height (m). The Orion hatch opening (~1.2 m visually,
        /// ~1.0 m physically) is below full height: crouched the player passes,
        /// standing not. Crouching is automatic and works on the ground
        /// (StepGrounded) and in flight (StepAirborne); there are no buttons. It
        /// must not go below 0.7 m — that is the capsule diameter.
        /// </summary>
        public const double CapsuleHeightCrouched = 0.8d;

        /// <summary>Crouch levels from full height down to the minimum.</summary>
        private static readonly double[] StoopLevels =
            { CapsuleHeight, 1.25d, 1.05d, 0.92d, CapsuleHeightCrouched };

        /// <summary>Do not stand up for this many seconds after crouching (so height does not jitter at a wall).</summary>
        private const double StoopHoldSeconds = 0.25d;

        /// <summary>Clearance (m) around the capsule when standing: stand only if there is free room, otherwise height jitters at a wall.</summary>
        private const double GrowClearanceMeters = 0.04d;

        public const double StepUpMeters = 0.5d;
        public const double SupportDropMeters = 0.5d;
        public const double MaxSlopeDegrees = 30d;

        /// <summary>
        /// Maximum slope on which a landing from flight/fall is allowed (degrees).
        /// Matches the sweep's ignore threshold (|n·up| &gt; 0.7 ≈ 45.6°): slopes
        /// between MaxSlopeDegrees and this threshold are not seen as walls by the
        /// sweep. Steeper than MaxSlopeDegrees on the ground is already handled by
        /// sliding.
        /// </summary>
        public const double LandingMaxSlopeDegrees = 46d;

        public const int MaxSweepIterations = 4;
        public const double SlideSpeedMps = 1.5d;

        /// <summary>
        /// Sink speed when, while hovering, the passage is almost fully cut off (a
        /// hatch lintel) and slightly below the path is freer (m/s).
        /// </summary>
        public const double HoverSinkSpeed = 1d;

        /// <summary>
        /// Numerical tolerance for seams between colliders (m): a step slightly above
        /// StepUp at the joint of two meshes (bake/scale error) must not block the
        /// step. This is NOT a gameplay threshold (StepUpMeters) but a geometry epsilon.
        /// </summary>
        public const double SeamToleranceMeters = 0.02d;

        /// <summary>Capsule feet in the local site frame.</summary>
        public Vector3d Position;

        /// <summary>Velocity relative to the site (m/s).</summary>
        public Vector3d Velocity;

        /// <summary>Free fall (no support under the feet).</summary>
        public bool Airborne;

        /// <summary>
        /// Hovering in the air: no gravity, velocity eases to HoverVelocity
        /// (zero — freeze). Set by SimulationRunner in hover mode.
        /// </summary>
        public bool Hovering;

        /// <summary>Desired hover velocity in the local frame (m/s).</summary>
        public Vector3d HoverVelocity;

        /// <summary>Hover velocity response (1/s).</summary>
        public double HoverResponse = 6d;

        /// <summary>Input in the substep was blocked (wall/ceiling): position unchanged.</summary>
        public bool Blocked;

        /// <summary>Diagnostics: how much the feet were raised in the last substep (m).</summary>
        public double DebugLift;

        /// <summary>Gravity along the local vertical (m/s²).</summary>
        public double Gravity = 9.81d;

        /// <summary>Whether the player is crouched (capsule height below full).</summary>
        public bool Crouched
        {
            get { return height < CapsuleHeight - 1e-6d; }
        }

        /// <summary>
        /// Current capsule height (m), from CapsuleHeightCrouched to CapsuleHeight.
        /// The player crouches in steps (StoopLevels) just enough to pass under a
        /// sloped wall/vault/lintel, and stands as soon as there is room above the
        /// head. The camera follows this height (FirstPersonCamera).
        /// </summary>
        public double CurrentHeightMeters
        {
            get { return height; }
        }

        private double height = CapsuleHeight;

        /// <summary>Seconds left during which the player cannot stand after crouching (hysteresis).</summary>
        private double growHold;

        private double CurrentHeight
        {
            get { return height; }
        }

        /// <summary>Support under the feet (sourceId of the last Floor); -1 — none.</summary>
        public int GroundSourceId { get; private set; } = -1;

        /// <summary>Set the current support (after a teleport/spawn on a platform).</summary>
        public void SetGroundSource(int sourceId)
        {
            GroundSourceId = sourceId;
        }

        private static readonly Vector3d Up = new Vector3d(0d, 0d, 1d);

        public void Step(IPlayerSupport support, double dt, Vector3d intentVelocity, Vector3d extraAcceleration = default)
        {
            if (support == null)
            {
                throw new ArgumentNullException(nameof(support));
            }

            if (!(dt > 0d) || !double.IsFinite(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt), "Шаг игрока обязан быть конечным и положительным.");
            }

            Blocked = false;
            if (growHold > 0d)
            {
                growHold = Math.Max(0d, growHold - dt);
            }

            if (extraAcceleration.SqrMagnitude > 0d)
            {
                // Extra acceleration (jetpack): added to velocity before the step.
                Velocity += extraAcceleration * dt;
            }

            Vector3d intent = new Vector3d(intentVelocity.X, intentVelocity.Y, 0d);

            if (Airborne)
            {
                StepAirborne(support, dt);
            }
            else
            {
                StepGrounded(support, dt, intent);
            }
        }

        private void StepGrounded(IPlayerSupport support, double dt, Vector3d intent)
        {
            Vector3d saved = Position;
            Vector3d move = intent * dt;

            // Carry by a moving support (platform, ship part).
            if (GroundSourceId >= 0 && support is IMovingSupport moving
                && moving.TryGetSupportVelocity(GroundSourceId, out Vector3d supportVelocity))
            {
                move += new Vector3d(supportVelocity.X, supportVelocity.Y, 0d) * dt;
            }

            // Step up: a support under the candidate and at one radius ahead (the
            // capsule already touches the ledge while its edge is still ahead of the
            // foot point) within StepUp raises the feet, so a low ledge is not seen
            // as a wall by the sweep.
            Vector3d candidate = Position + move;
            if (!TryFindStepFloor(support, candidate, move, out double floorHeight, out Vector3d floorNormal, out int floorId))
            {
                // No support — walked off an edge: fall within this same substep.
                Airborne = true;
                GroundSourceId = -1;
                Velocity = intent;
                return;
            }

            if (SlopeAngleDegrees(floorNormal) > MaxSlopeDegrees)
            {
                // Steeper than the limit: do not rise, slide down along the surface.
                // Horizontal is cut by the wall sweep (as when walking): a wall must
                // not let the player through on a steep slope.
                Vector3d slid = SweepHorizontal(support, Position, move, CurrentHeight);
                Position = new Vector3d(slid.X, slid.Y, Position.Z);

                // Support under the resulting point (the sweep may have moved along the wall).
                Vector3d slideProbe = new Vector3d(Position.X, Position.Y, Position.Z + StepUpMeters);
                if (support.Floor(slideProbe, StepUpMeters + SupportDropMeters,
                        out double slideHeight, out _, out int slideId))
                {
                    Position = new Vector3d(Position.X, Position.Y, Math.Min(Position.Z, slideHeight));
                    GroundSourceId = slideId;
                }
                else
                {
                    GroundSourceId = floorId;
                }

                Velocity = new Vector3d(intent.X, intent.Y, -SlideSpeedMps);
                return;
            }

            Position = new Vector3d(Position.X, Position.Y, floorHeight);

            // Horizontal sweep with sliding along walls (up to 4 iterations).
            // The capsule height is chosen across levels: if crouching gives a
            // noticeably longer travel (sloped cabin wall, hatch), crouch just enough
            // to get that travel.
            Vector3d p = SweepStoop(support, Position, move);

            Position = new Vector3d(p.X, p.Y, Position.Z);

            // Exact support under the final position (and a second slope check).
            Vector3d finalProbe = new Vector3d(Position.X, Position.Y, Position.Z + StepUpMeters);
            if (!support.Floor(finalProbe, StepUpMeters + SupportDropMeters,
                    out double finalHeight, out Vector3d finalNormal, out int finalId)
                || finalHeight > Position.Z + StepUpMeters + SeamToleranceMeters
                || SlopeAngleDegrees(finalNormal) > MaxSlopeDegrees)
            {
                Airborne = true;
                GroundSourceId = -1;
                Velocity = intent;
                return;
            }

            // Ceiling: entering under a low vault is physically blocked by the sweep;
            // here — the headroom check (height is current: crouch is accounted for).
            if (support.Ceiling(new Vector3d(Position.X, Position.Y, finalHeight + CurrentHeight), StepUpMeters,
                    out double ceilingHeight)
                && ceilingHeight - finalHeight < CurrentHeight)
            {
                Position = saved;
                Blocked = true;
                return;
            }

            DebugLift = finalHeight - saved.Z;
            Position = new Vector3d(Position.X, Position.Y, finalHeight);
            GroundSourceId = finalId;
            Velocity = intent;

            // Stand up when there is full height above the head again (out of the opening).
            TryGrow(support);

            // Fully blocked entry (all sweep iterations consumed) — "entry blocked".
            if (move.Magnitude > 1e-9d && (Position - saved).Magnitude < 1e-3d * move.Magnitude)
            {
                Position = saved;
                Blocked = true;
            }
        }

        /// <summary>
        /// The highest support within a step up: under the candidate and at the capsule
        /// radius ahead along the motion. A ledge above StepUp is not raised (the sweep
        /// blocks it).
        /// </summary>
        private static bool TryFindStepFloor(IPlayerSupport support, Vector3d candidate, Vector3d move,
            out double height, out Vector3d normal, out int sourceId)
        {
            double limit = double.NegativeInfinity;
            height = 0d;
            normal = Up;
            sourceId = -1;
            bool found = false;
            double maxDrop = StepUpMeters + SupportDropMeters;

            Vector3d probeFrom = new Vector3d(candidate.X, candidate.Y, candidate.Z + StepUpMeters);
            if (support.Floor(probeFrom, maxDrop, out double h1, out Vector3d n1, out int id1)
                && h1 <= candidate.Z + StepUpMeters + SeamToleranceMeters)
            {
                height = h1;
                normal = n1;
                sourceId = id1;
                found = true;
            }

            double moveLength = move.Magnitude;
            if (moveLength > 1e-12d)
            {
                // Several samples along the radius ahead: a single probe over the full
                // radius overshoots narrow steps (narrower than the radius) and misses
                // the ledge.
                const int samples = 4;
                Vector3d direction = move * (1d / moveLength);
                for (int k = 1; k <= samples; k++)
                {
                    Vector3d ahead = candidate + (direction * (CapsuleRadius * k / samples));
                    Vector3d aheadProbe = new Vector3d(ahead.X, ahead.Y, candidate.Z + StepUpMeters);
                    if (support.Floor(aheadProbe, maxDrop, out double h2, out Vector3d n2, out int id2)
                        && h2 <= candidate.Z + StepUpMeters + SeamToleranceMeters
                        && (!found || h2 > height))
                    {
                        height = h2;
                        normal = n2;
                        sourceId = id2;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Horizontal capsule sweep with sliding along walls (up to MaxSweepIterations
        /// iterations). height — capsule height (full/crouched). Returns the resulting
        /// position; Z is unchanged.
        /// </summary>
        private static Vector3d SweepHorizontal(IPlayerSupport support, Vector3d from, Vector3d move, double height)
        {
            Vector3d p = from;
            Vector3d remaining = move;
            for (int iteration = 0; iteration < MaxSweepIterations; iteration++)
            {
                if (remaining.Magnitude < 1e-12d)
                {
                    break;
                }

                if (!support.Sweep(p, p + remaining, CapsuleRadius, height, out Vector3d wallNormal, out double fraction))
                {
                    p += remaining;
                    break;
                }

                if (!(fraction > 0d) || !(fraction < 1d))
                {
                    fraction = fraction < 0d ? 0d : (fraction > 1d ? 1d : fraction);
                }

                p += remaining * fraction;
                Vector3d left = remaining * (1d - fraction);
                // Slide along a FLAT wall: on a sloped surface the normal has a
                // Z-component, and its projection would eat part of the travel and send
                // the remainder vertically (Z drifted between iterations). The sweep
                // normal is horizontal only.
                Vector3d wallFlat = new Vector3d(wallNormal.X, wallNormal.Y, 0d);
                if (wallFlat.SqrMagnitude > 1e-12d)
                {
                    wallFlat = wallFlat.Normalized;
                    left -= wallFlat * Vector3d.Dot(left, wallFlat);
                }

                remaining = new Vector3d(left.X, left.Y, 0d);
            }

            return p;
        }

        private void StepAirborne(IPlayerSupport support, double dt)
        {
            if (Hovering)
            {
                // Hovering: no gravity, velocity eases to the desired one (input);
                // with no input — to zero: the player freezes in the air.
                double blend = Math.Min(1d, dt * HoverResponse);
                Velocity += (HoverVelocity - Velocity) * blend;
            }
            else
            {
                Velocity = new Vector3d(Velocity.X, Velocity.Y, Velocity.Z - (Gravity * dt));
            }

            Vector3d step = Velocity * dt;

            // Horizontal — sweep along walls; in flight crouching works too: at full
            // height the sweep is cut (capsule hatch), so we try crouched and, if it
            // advances further, crouch.
            Vector3d flatStep = new Vector3d(step.X, step.Y, 0d);
            Vector3d p = SweepStoop(support, Position, flatStep);

            // Stuck-help: travel is almost fully cut (head in the hatch lintel). If
            // slightly lower the sweep advances noticeably further — sink smoothly
            // until the passage opens. An ordinary wall is not "sunken" this way: it
            // triggers only when lower down the path is really freer.
            if (Hovering && flatStep.Magnitude > 1e-9d
                && (p - Position).Magnitude < 0.5d * flatStep.Magnitude)
            {
                double progress = (p - Position).Magnitude;
                double lowest = support.Floor(new Vector3d(p.X, p.Y, p.Z + StepUpMeters), 3d,
                    out double underFloor, out _, out _) ? underFloor : double.NegativeInfinity;
                for (int k = 1; k <= 2; k++)
                {
                    double targetZ = Math.Max(lowest, p.Z - (0.15d * k));
                    if (targetZ >= p.Z - 1e-6d)
                    {
                        continue;
                    }

                    Vector3d lowered = SweepHorizontal(support,
                        new Vector3d(Position.X, Position.Y, targetZ), flatStep, CapsuleHeightCrouched);
                    double loweredProgress =
                        new Vector3d(lowered.X - Position.X, lowered.Y - Position.Y, 0d).Magnitude;
                    if (loweredProgress > progress + 0.03d)
                    {
                        step = new Vector3d(step.X, step.Y, -Math.Min(HoverSinkSpeed * dt, p.Z - targetZ));
                        break;
                    }
                }
            }

            // Vertical is split into chunks: a substep can be up to 0.5 s (meters of
            // travel), and a thin plate on the path must not be skipped whole. Landing
            // is checked BEFORE moving the chunk, so a plate inside the chunk is seen.
            const double maxVerticalChunk = 0.2d;
            double remainingZ = step.Z;
            while (Math.Abs(remainingZ) > 1e-12d)
            {
                double chunk = Math.Max(-maxVerticalChunk, Math.Min(maxVerticalChunk, remainingZ));
                if (chunk < 0d)
                {
                    // Landing only while descending: a rise (jump) must not instantly
                    // "land" back on the same support. No bounce (v_z = 0). The ray
                    // starts at StepUp above the feet, with the same StepUp tolerance:
                    // the sweep ignores slopes up to ~45° (see SiteBoxSupport.Sweep), so
                    // a fall with horizontal motion can put the feet under a plate bevel
                    // or ledge. The body then steps onto the found surface instead of
                    // sinking into the mesh — the mirror of TryFindStepFloor on the
                    // ground.
                    double fall = -chunk + 0.1d;
                    Vector3d landFrom = new Vector3d(p.X, p.Y, p.Z + StepUpMeters);
                    if (support.Floor(landFrom, StepUpMeters + fall, out double floorHeight, out Vector3d floorNormal, out int floorId)
                        && floorHeight <= p.Z + StepUpMeters
                        && SlopeAngleDegrees(floorNormal) <= LandingMaxSlopeDegrees)
                    {
                        Position = new Vector3d(p.X, p.Y, floorHeight);
                        Velocity = new Vector3d(Velocity.X, Velocity.Y, 0d);
                        Airborne = false;
                        GroundSourceId = floorId;
                        TryGrow(support);
                        return;
                    }
                }

                if (chunk > 0d && TryCeilingStop(support, p, chunk, out double allowedRise))
                {
                    p = new Vector3d(p.X, p.Y, p.Z + allowedRise);
                    Velocity = new Vector3d(Velocity.X, Velocity.Y, 0d);
                    break;
                }

                p = new Vector3d(p.X, p.Y, p.Z + chunk);
                remainingZ -= chunk;
            }

            Position = p;
            TryGrow(support);
        }

        /// <summary>
        /// Sweep with capsule-height selection. If travel at the current height is cut
        /// but the lowest level passes noticeably further — the head is in the way
        /// (sloped wall, lintel): take the HIGHEST level that gives almost the same
        /// travel as the minimum. If even the minimum is no better — it is a real
        /// wall, height is untouched. The gain threshold is relative (a fraction of
        /// the step).
        /// </summary>
        private Vector3d SweepStoop(IPlayerSupport support, Vector3d from, Vector3d move)
        {
            Vector3d p = SweepHorizontal(support, from, move, height);
            double want = move.Magnitude;
            if (!(want > 1e-9d) || height <= CapsuleHeightCrouched + 1e-9d)
            {
                return p;
            }

            double got = (p - from).Magnitude;
            if (got >= want - 1e-4d)
            {
                return p;
            }

            Vector3d pMin = SweepHorizontal(support, from, move, CapsuleHeightCrouched);
            double best = (pMin - from).Magnitude;
            if (best <= got + Math.Max(1e-3d, 0.1d * want))
            {
                return p;
            }

            double tolerance = Math.Max(1e-3d, 0.05d * want);
            for (int i = 0; i < StoopLevels.Length - 1; i++)
            {
                double level = StoopLevels[i];
                if (level >= height - 1e-9d)
                {
                    continue;
                }

                Vector3d pl = SweepHorizontal(support, from, move, level);
                if ((pl - from).Magnitude >= best - tolerance)
                {
                    height = level;
                    growHold = StoopHoldSeconds;
                    return pl;
                }
            }

            height = CapsuleHeightCrouched;
            growHold = StoopHoldSeconds;
            return pMin;
        }

        /// <summary>
        /// Stand taller: the highest level (above the current one) the capsule FITS
        /// into at this spot. The check is by capsule volume (IOverlapSupport), not by
        /// upward rays: a sloped wall and lintel are invisible to a sideways ray.
        /// Works on the ground and in the air.
        /// </summary>
        private void TryGrow(IPlayerSupport support)
        {
            if (height >= CapsuleHeight - 1e-9d || growHold > 0d)
            {
                return;
            }

            for (int i = 0; i < StoopLevels.Length; i++)
            {
                double level = StoopLevels[i];
                if (level <= height + 1e-9d)
                {
                    break;
                }

                if (Fits(support, level))
                {
                    height = level;
                    return;
                }
            }
        }

        private bool Fits(IPlayerSupport support, double level)
        {
            IOverlapSupport overlap = support as IOverlapSupport;
            if (overlap != null)
            {
                return !overlap.Overlaps(Position, CapsuleRadius + GrowClearanceMeters, level);
            }

            // Support without a volume check (terrain): ceiling above the head.
            return !support.Ceiling(
                new Vector3d(Position.X, Position.Y, Position.Z + height),
                level - height + 0.05d, out _);
        }

        /// <summary>
        /// Ceiling check during a rise in the air (jump, jetpack). Returns true if the
        /// rise is cut (rise — desired, allowed — up to the ceiling).
        /// </summary>
        private bool TryCeilingStop(IPlayerSupport support, Vector3d p, double rise, out double allowed)
        {
            double headZ = p.Z + CurrentHeight;
            allowed = rise;
            if (!support.Ceiling(new Vector3d(p.X, p.Y, headZ), rise + 0.02d, out double ceilingZ)) return false;
            double room = ceilingZ - headZ - 0.01d;
            if (room >= rise) return false;
            allowed = Math.Max(0d, room);
            return true;
        }

        public static double SlopeAngleDegrees(Vector3d normal)
        {
            Vector3d n = normal.Normalized;
            double cosine = Math.Max(-1d, Math.Min(1d, Vector3d.Dot(n, Up)));
            return Math.Acos(cosine) * (180d / Math.PI);
        }
    }
}

