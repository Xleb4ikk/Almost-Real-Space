using System;
using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// One ring of the fairing profile: radius and height above the TOP of the base plate, m.
    /// The shell is a polyline of such rings; every pair of neighbours is a truncated cone
    /// (equal radii — a cylinder, radius 0 — the nose point).
    /// </summary>
    public struct FairingRing
    {
        public float Radius;
        public float Y;

        public FairingRing(float radius, float y)
        {
            Radius = radius;
            Y = y;
        }
    }

    /// <summary>How the shell ends at the top.</summary>
    public enum FairingEnd
    {
        /// <summary>Building was stopped by the player, the top rim stays open.</summary>
        Open = 0,

        /// <summary>Closed in a point: a nose cone.</summary>
        Apex = 1,

        /// <summary>Closed on a part edge (the bottom ring of the tank above, a capsule rim, ...).</summary>
        Docked = 2,
    }

    /// <summary>
    /// Pure data and math of a procedural fairing shell (no MonoBehaviour, testable without Play).
    /// The shell is built from <see cref="Petals"/> identical petals (2/3/4/6/8) that separate
    /// like orange peel. All heights are relative to the top of the base plate; the axis is local Y.
    /// </summary>
    public sealed class FairingShell
    {
        /// <summary>Petal counts the player cycles with X (more) and Z (fewer).</summary>
        public static readonly int[] PetalCounts = { 2, 3, 4, 6, 8 };

        public const int DefaultPetals = 4;

        /// <summary>Areal density of a composite sandwich shell, kg/m² (Falcon-class fairings are ~9–10).</summary>
        public const float ArealDensityKgPerM2 = 9f;

        /// <summary>Separation system, hinges, seals: added on top of the bare shell mass.</summary>
        public const float HardwareFactor = 1.08f;

        /// <summary>Visual wall thickness, m (the mesh has a real inner surface).</summary>
        public const float ShellThicknessM = 0.012f;

        /// <summary>
        /// Gap between neighbouring petals, degrees. Zero: the petals butt at the seam, so the
        /// closed shell is light-tight (a positive gap shows the payload inside as a dark slot).
        /// </summary>
        public const float SeamGapDegrees = 0f;

        public const float MinSegmentMeters = 0.25f;
        public const float MaxRadiusM = 8f;
        public const float MaxHeightM = 40f;

        /// <summary>Ring limit (the convex collider hull of a petal stays under 255 vertices).</summary>
        public const int MaxRings = 17;

        private const double PlateDensityKgPerM3 = 2700d;
        private const double PlateFill = 0.30d;

        /// <summary>Rings bottom-up. Rings[0] is always (base radius, 0).</summary>
        public readonly List<FairingRing> Rings = new List<FairingRing>();

        public int Petals = DefaultPetals;
        public FairingEnd End = FairingEnd.Open;

        /// <summary>Runtime flag: the petals were jettisoned in flight (never saved into the draft).</summary>
        public bool Jettisoned;

        public bool HasShell
        {
            get { return Rings.Count > 1; }
        }

        public bool IsClosed
        {
            get { return End != FairingEnd.Open; }
        }

        public FairingRing Last
        {
            get { return Rings[Rings.Count - 1]; }
        }

        public float BaseRadius
        {
            get { return Rings.Count > 0 ? Rings[0].Radius : 0f; }
        }

        /// <summary>Vertical extent of the shell ("length" shown to the player), m.</summary>
        public float Height
        {
            get { return Rings.Count > 1 ? Rings[Rings.Count - 1].Y - Rings[0].Y : 0f; }
        }

        /// <summary>Lateral area of the shell, m² (sum of truncated-cone sides).</summary>
        public float SurfaceAreaM2()
        {
            float area = 0f;
            for (int i = 0; i + 1 < Rings.Count; i++)
            {
                area += SegmentArea(Rings[i], Rings[i + 1]);
            }

            return area;
        }

        /// <summary>Mass of the whole shell (all petals), kg.</summary>
        public double MassKg
        {
            get { return MassFromArea(SurfaceAreaM2()); }
        }

        /// <summary>Mass of the shell if <paramref name="candidate"/> were added on top, kg.</summary>
        public double MassWith(FairingRing candidate)
        {
            return Rings.Count > 0
                ? MassFromArea(SurfaceAreaM2() + SegmentArea(Last, candidate))
                : 0d;
        }

        /// <summary>Height of the shell if <paramref name="candidate"/> were added on top, m.</summary>
        public float HeightWith(FairingRing candidate)
        {
            return Rings.Count > 0 ? Mathf.Max(Last.Y, candidate.Y) - Rings[0].Y : 0f;
        }

        public static double MassFromArea(float areaM2)
        {
            return areaM2 * ArealDensityKgPerM2 * HardwareFactor;
        }

        public static float SegmentArea(FairingRing a, FairingRing b)
        {
            float dr = b.Radius - a.Radius;
            float dy = b.Y - a.Y;
            return Mathf.PI * (a.Radius + b.Radius) * Mathf.Sqrt((dr * dr) + (dy * dy));
        }

        /// <summary>Mass of the base plate: a ribbed aluminium disc (same thickness as the separator), kg.</summary>
        public static double PlateMassKg(float radius)
        {
            return Math.PI * radius * radius * ProceduralSeparator.Thickness * PlateDensityKgPerM3 * PlateFill;
        }

        /// <summary>Shell radius at a height above the plate top (clamped to the shell extent), m.</summary>
        public float RadiusAt(float y)
        {
            if (Rings.Count == 0)
            {
                return 0f;
            }

            if (y <= Rings[0].Y)
            {
                return Rings[0].Radius;
            }

            for (int i = 0; i + 1 < Rings.Count; i++)
            {
                FairingRing a = Rings[i];
                FairingRing b = Rings[i + 1];
                if (y <= b.Y)
                {
                    float span = b.Y - a.Y;
                    return span < 1e-5f ? b.Radius : Mathf.Lerp(a.Radius, b.Radius, (y - a.Y) / span);
                }
            }

            return Rings[Rings.Count - 1].Radius;
        }

        /// <summary>
        /// Distance from a point of the (radius, height) half-plane to the shell profile, m.
        /// Used for "the player came close — open the petals".
        /// </summary>
        public float DistanceTo(float radius, float y)
        {
            if (Rings.Count == 0)
            {
                return float.MaxValue;
            }

            if (Rings.Count == 1)
            {
                return Vector2.Distance(new Vector2(radius, y), new Vector2(Rings[0].Radius, Rings[0].Y));
            }

            float best = float.MaxValue;
            var p = new Vector2(radius, y);
            for (int i = 0; i + 1 < Rings.Count; i++)
            {
                var a = new Vector2(Rings[i].Radius, Rings[i].Y);
                var b = new Vector2(Rings[i + 1].Radius, Rings[i + 1].Y);
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                best = Mathf.Min(best, Vector2.Distance(p, a + (ab * t)));
            }

            return best;
        }

        /// <summary>Remove the last ring (undo); the base ring stays. Reopens a closed shell.</summary>
        public bool PopRing()
        {
            if (Rings.Count <= 1)
            {
                return false;
            }

            Rings.RemoveAt(Rings.Count - 1);
            End = FairingEnd.Open;
            return true;
        }

        public FairingShell Clone()
        {
            var copy = new FairingShell { Petals = Petals, End = End };
            copy.Rings.AddRange(Rings);
            return copy;
        }

        /// <summary>Ogive-like demo profile for the inventory icon/preview (radius r, height h).</summary>
        public static FairingShell CreateDemo(float radius, float height)
        {
            var shell = new FairingShell { Petals = DefaultPetals, End = FairingEnd.Apex };
            shell.Rings.Add(new FairingRing(radius, 0f));
            shell.Rings.Add(new FairingRing(radius, height * 0.48f));
            shell.Rings.Add(new FairingRing(radius * 0.72f, height * 0.72f));
            shell.Rings.Add(new FairingRing(radius * 0.36f, height * 0.90f));
            shell.Rings.Add(new FairingRing(0f, height));
            return shell;
        }

        // ─── Petal count cycle ───────────────────────────────────────────────

        public static int ClampPetals(int count)
        {
            if (count <= 0)
            {
                return DefaultPetals;
            }

            int best = PetalCounts[0];
            int bestDelta = int.MaxValue;
            for (int i = 0; i < PetalCounts.Length; i++)
            {
                int delta = Math.Abs(PetalCounts[i] - count);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = PetalCounts[i];
                }
            }

            return best;
        }

        /// <summary>Next/previous petal count in the cycle (direction +1 or −1).</summary>
        public static int StepPetals(int count, int direction)
        {
            int current = ClampPetals(count);
            int index = 0;
            for (int i = 0; i < PetalCounts.Length; i++)
            {
                if (PetalCounts[i] == current)
                {
                    index = i;
                    break;
                }
            }

            index = (((index + (direction > 0 ? 1 : -1)) % PetalCounts.Length) + PetalCounts.Length) % PetalCounts.Length;
            return PetalCounts[index];
        }

        /// <summary>Russian noun form for the HUD: "4 дольки", "6 долек".</summary>
        public static string PetalsLabel(int count)
        {
            if (count == 1)
            {
                return "1 долька";
            }

            return count >= 2 && count <= 4 ? count + " дольки" : count + " долек";
        }
    }
}
