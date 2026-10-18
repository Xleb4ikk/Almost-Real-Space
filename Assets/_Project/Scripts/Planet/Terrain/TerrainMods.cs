using System;
using System.Collections.Generic;
using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Level pads on a body: gizmos (yellow ring = skirt, red = level core) plus
    /// add-pad and capture-height helpers. The pad list itself lives in BodyAuthoring.TerrainPads.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BodyAuthoring))]
    public sealed class TerrainMods : MonoBehaviour
    {
        [Header("Площадка для нового места")]
        [Tooltip("Внешний радиус новой площадки, м: в ядре ровно, дальше фартук. " +
            "Ядро шире 240 м — нормаль рельефа усредняется по 114 м, и на узком " +
            "ядре здания по краям встают с наклоном.")]
        public double NewPadInnerRadiusMeters = 150d;

        [Tooltip("Внешний радиус новой площадки, м. 0 = без фартука, только ровное ядро.")]
        public double NewPadOuterRadiusMeters = 420d;

        private BodyAuthoring Body => GetComponent<BodyAuthoring>();

        private List<TerrainModifier> Pads => Body != null ? Body.TerrainPads : null;

        /// <summary>Number of active pads (disabled pads are not counted).</summary>
        public int ActiveCount
        {
            get
            {
                List<TerrainModifier> pads = Pads;
                if (pads == null)
                {
                    return 0;
                }

                int n = 0;
                for (int i = 0; i < pads.Count; i++)
                {
                    if (pads[i].IsActive)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>
        /// Capture the natural terrain height at the point as the pad target, so the
        /// pad sits flush with the terrain and does not cut into the surrounding slope.
        /// </summary>
        public void CaptureTargetHeight(int index, double latitudeDegrees, double longitudeDegrees)
        {
            List<TerrainModifier> pads = Pads;
            if (pads == null || index < 0 || index >= pads.Count)
            {
                return;
            }

            double height = SampleNaturalHeight(latitudeDegrees, longitudeDegrees);
            if (double.IsNaN(height))
            {
                return;
            }

            // Copy through an interim variable: List's indexer returns a copy (struct, not ref).
            TerrainModifier pad = pads[index];
            pad.LatitudeDegrees = latitudeDegrees;
            pad.LongitudeDegrees = longitudeDegrees;
            pad.TargetHeightMeters = height;
            pads[index] = pad;
        }

        /// <summary>
        /// Add a pad centered at the point with the height of the natural terrain.
        /// Returns the index, or -1 if the system could not be resolved.
        /// </summary>
        public int AddPad(double latitudeDegrees, double longitudeDegrees)
        {
            List<TerrainModifier> pads = Pads;
            if (pads == null)
            {
                return -1;
            }

            double height = SampleNaturalHeight(latitudeDegrees, longitudeDegrees);
            if (double.IsNaN(height))
            {
                return -1;
            }

            pads.Add(new TerrainModifier
            {
                LatitudeDegrees = latitudeDegrees,
                LongitudeDegrees = longitudeDegrees,
                InnerRadiusMeters = NewPadInnerRadiusMeters,
                OuterRadiusMeters = System.Math.Max(NewPadOuterRadiusMeters, NewPadInnerRadiusMeters),
                TargetHeightMeters = height,
                OverridesSeaLevel = height < 0d
            });
            return pads.Count - 1;
        }

        /// <summary>Remove the pad at the given index.</summary>
        public void RemovePad(int index)
        {
            List<TerrainModifier> pads = Pads;
            if (pads != null && index >= 0 && index < pads.Count)
            {
                pads.RemoveAt(index);
            }
        }

        /// <summary>
        /// Natural terrain height at the point, ignoring pads. Must be used as the pad
        /// target: regular GetHeightMeters would return the already-smoothed value there.
        /// </summary>
        private double SampleNaturalHeight(double latitudeDegrees, double longitudeDegrees)
        {
            if (!SurfaceSceneSystem.TryResolve(name, out OrbitingBody body, out _))
            {
                return double.NaN;
            }

            var terrain = body.Terrain as HeightfieldTerrain;
            if (terrain == null)
            {
                return double.NaN;
            }

            return terrain.GetRawHeightMeters(
                body,
                latitudeDegrees * (System.Math.PI / 180d),
                longitudeDegrees * (System.Math.PI / 180d));
        }

        private void OnDrawGizmosSelected()
        {
            var frame = UnityEngine.Object.FindAnyObjectByType<SurfaceFrame>();
            if (frame == null || !frame.IsUsable)
            {
                return;
            }

            List<TerrainModifier> pads = Pads;
            if (pads == null)
            {
                return;
            }

            for (int i = 0; i < pads.Count; i++)
            {
                TerrainModifier pad = pads[i];
                if (!pad.IsActive)
                {
                    continue;
                }

                // Radii are in surface meters (not degrees) so pad size can be judged by eye.
                double inner = pad.InnerRadiusMeters;
                double outer = pad.OuterRadiusMeters;
                if (outer <= 0d || outer > 1e6d)
                {
                    continue;
                }

                DrawCircle(frame, pad, outer, new Color(1f, 0.85f, 0.2f, 0.5f));
                if (inner > 0d)
                {
                    DrawCircle(frame, pad, inner, new Color(1f, 0.3f, 0.2f, 0.85f));
                }
            }
        }

        private static void DrawCircle(SurfaceFrame frame, TerrainModifier pad, double radius, Color color)
        {
            if (radius <= 0d)
            {
                return;
            }

            Vector3d offset = SurfaceFrameMath.SurfaceOffsetBetween(
                frame.LatitudeDegrees * (Math.PI / 180d),
                frame.LongitudeDegrees * (Math.PI / 180d),
                frame.AltitudeMeters,
                pad.LatitudeDegrees * (Math.PI / 180d),
                pad.LongitudeDegrees * (Math.PI / 180d),
                pad.TargetHeightMeters,
                frame.BodyRadius);

            // The offset is in frame axes (x=north, y=up, z=east); gizmos draw in
            // world space, so transform it through the frame's pose.
            var local = new Vector3((float)offset.X, (float)offset.Y, (float)offset.Z);
            Vector3 center = frame.transform.position + (frame.transform.rotation * local);

            Gizmos.color = color;
            const int segments = 48;
            Vector3 prev = center + (frame.transform.rotation * new Vector3(0f, 0f, (float)radius));
            for (int s = 1; s <= segments; s++)
            {
                double a = (s / (double)segments) * Math.PI * 2d;
                Vector3 next = center + (frame.transform.rotation * new Vector3(
                    (float)(Math.Cos(a) * radius), 0f, (float)(Math.Sin(a) * radius)));
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
