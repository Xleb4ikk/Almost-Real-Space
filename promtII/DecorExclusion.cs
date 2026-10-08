using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{

    /// <summary>
    /// Таблица зон исключения. Зоны берутся из всех SurfaceGrounded под SurfaceSite
    /// (то есть из всего, что ты ставишь на место): габарит — SiteBox, если он есть,
    /// иначе границы рендереров. К габариту добавляется MarginMeters.
    /// Живёт в HeightfieldTerrain.DecorExclusions → TerrainNoiseParams → джобы декора.
    /// </summary>
    public static partial class DecorExclusionTable
    {
        public const float MarginMeters = 20f;
        private const float ScanIntervalSeconds = 0.5f;
        private const int RetireFrames = 600;


        private static readonly List<DecorExclusionData> scratch = new List<DecorExclusionData>();
        private static readonly List<Renderer> renderers = new List<Renderer>();
        private static readonly List<KeyValuePair<NativeArray<DecorExclusionData>, int>> retired =
            new List<KeyValuePair<NativeArray<DecorExclusionData>, int>>();
        private static float nextScan;


        // ---------- сборка (main thread) ----------

        /// <summary>Зовётся из PlanetSurfaceRenderer.Update. Дёшево: скан раз в 0.5 с.</summary>
        public static void Apply(HeightfieldTerrain terrain, double bodyRadius, string bodyName)
        {
            FlushRetired(false);
            if (terrain == null || bodyRadius <= 0d)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < nextScan)
            {
                return;
            }

            nextScan = now + ScanIntervalSeconds;
            scratch.Clear();

            SurfaceGrounded[] all = UnityEngine.Object.FindObjectsByType<SurfaceGrounded>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                SurfaceGrounded g = all[i];
                if (g == null || !g.isActiveAndEnabled)
                {
                    continue;
                }

                SurfaceSite site = g.GetComponentInParent<SurfaceSite>();
                if (site == null || !site.isActiveAndEnabled)
                {
                    continue;
                }

                string siteBody = site.Body != null ? site.Body.gameObject.name : site.BodyName;
                if (siteBody != bodyName)
                {
                    continue;
                }

                if (TryBuild(g, site, bodyRadius, out DecorExclusionData zone))
                {
                    scratch.Add(zone);
                }
            }

            NativeArray<DecorExclusionData> current = terrain.DecorExclusions;
            if (SameAsScratch(current))
            {
                return;
            }

            // Старую не освобождаем сразу: на неё могут ссылаться джобы в полёте.
            NativeArray<DecorExclusionData> built = scratch.Count == 0
                ? Empty
                : new NativeArray<DecorExclusionData>(scratch.ToArray(), Allocator.Persistent);
            terrain.DecorExclusions = built;
            if (current.IsCreated && current.Length > 0)
            {
                retired.Add(new KeyValuePair<NativeArray<DecorExclusionData>, int>(current, Time.frameCount));
            }
        }


        static partial void AfterRelease()
        {
            FlushRetired(true);
        }

        private static void FlushRetired(bool all)
        {
            for (int i = retired.Count - 1; i >= 0; i--)
            {
                if (all || Time.frameCount - retired[i].Value > RetireFrames)
                {
                    retired[i].Key.Dispose();
                    retired.RemoveAt(i);
                }
            }
        }

        private static bool SameAsScratch(NativeArray<DecorExclusionData> table)
        {
            int n = table.IsCreated ? table.Length : 0;
            if (n != scratch.Count)
            {
                return false;
            }

            for (int i = 0; i < n; i++)
            {
                if (!Same(table[i], scratch[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private struct Range
        {
            public float UMin, UMax, VMin, VMax;
            public bool Any;
        }

        private static void Grow(ref Range r, Vector3 sitePoint, Vector2 ax, Vector2 az)
        {
            // Оси места: x — север, z — восток.
            float u = (sitePoint.x * ax.x) + (sitePoint.z * ax.y);
            float v = (sitePoint.x * az.x) + (sitePoint.z * az.y);
            if (!r.Any)
            {
                r.UMin = r.UMax = u;
                r.VMin = r.VMax = v;
                r.Any = true;
                return;
            }

            r.UMin = Mathf.Min(r.UMin, u);
            r.UMax = Mathf.Max(r.UMax, u);
            r.VMin = Mathf.Min(r.VMin, v);
            r.VMax = Mathf.Max(r.VMax, v);
        }

        private static void GrowBox(
            ref Range r, Transform source, Transform site, Vector3 center, Vector3 half, Vector2 ax, Vector2 az)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = center + new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);
                Grow(ref r, site.InverseTransformPoint(source.TransformPoint(c)), ax, az);
            }
        }

        private static double Q(double v)
        {
            // Квантование 0.25 м: дрожание float не должно пересобирать чанки каждые полсекунды.
            return Math.Round(v * 4d) / 4d;
        }

        private static double3 D3(Galilego.Core.Vector3d v)
        {
            return new double3(v.X, v.Y, v.Z);
        }

        private static bool TryBuild(SurfaceGrounded g, SurfaceSite site, double radius, out DecorExclusionData zone)
        {
            zone = default;
            Transform gt = g.transform;
            Transform st = site.transform;

            // Горизонтальные оси объекта в осях места (n, e). Угол квантуем до 0.1°.
            Vector3 right = st.InverseTransformDirection(gt.right);
            Vector2 axRaw = new Vector2(right.x, right.z);
            double yaw = axRaw.sqrMagnitude < 1e-6f ? 0d : Math.Atan2(axRaw.y, axRaw.x);
            yaw = Math.Round(yaw * (180d / Math.PI) * 10d) / 10d * (Math.PI / 180d);
            Vector2 ax = new Vector2((float)Math.Cos(yaw), (float)Math.Sin(yaw));
            Vector2 az = new Vector2(-ax.y, ax.x);

            var range = new Range();
            SiteBox box = g.GetComponent<SiteBox>();
            if (box != null && box.isActiveAndEnabled)
            {
                GrowBox(ref range, gt, st, box.Center, box.Size * 0.5f, ax, az);
            }
            else
            {
                // enabled у рендерера НЕ проверяем: LODGroup гасит рендереры, и зона мигала бы.
                g.GetComponentsInChildren(false, renderers);
                for (int i = 0; i < renderers.Count; i++)
                {
                    Renderer r = renderers[i];
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                    {
                        continue;
                    }

                    Bounds b = r.localBounds;
                    GrowBox(ref range, r.transform, st, b.center, b.extents, ax, az);
                }
            }

            if (!range.Any)
            {
                return false;
            }

            double uc = Q((range.UMin + range.UMax) * 0.5d);
            double vc = Q((range.VMin + range.VMax) * 0.5d);
            double halfU = Q(((range.UMax - range.UMin) * 0.5d) + MarginMeters);
            double halfV = Q(((range.VMax - range.VMin) * 0.5d) + MarginMeters);

            double lat = site.LatitudeDegrees * (Math.PI / 180d);
            double lon = site.LongitudeDegrees * (Math.PI / 180d);
            double3 up = D3(SurfaceFrameMath.Up(lat, lon));
            double3 north = D3(SurfaceFrameMath.North(lat, lon));
            double3 east = D3(SurfaceFrameMath.East(lat, lon));

            double cn = (ax.x * uc) + (az.x * vc);
            double ce = (ax.y * uc) + (az.y * vc);
            double3 center = math.normalize(up + (((north * cn) + (east * ce)) / radius));

            double3 gx = (north * ax.x) + (east * ax.y);
            gx = math.normalize(gx - (center * math.dot(gx, center)));
            double3 gz = (north * az.x) + (east * az.y);
            gz = math.normalize(gz - (center * math.dot(gz, center)));

            zone = new DecorExclusionData
            {
                Center = center,
                AxisX = gx,
                AxisZ = gz,
                HalfX = halfU / radius,
                HalfZ = halfV / radius
            };
            return true;
        }
    }
}
