using System;
using System.Collections.Generic;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Чистая математика строительства (без MonoBehaviour — проверяема без Play):
    /// полигон площадки, пересечение цилиндров, поиск снапа по узлам у луча
    /// камеры, конвертация черновика в PartDefinition[] для физики.
    /// Все координаты — оси МЕСТА: x — север, y — зенит, z — восток.
    /// Радиальные детали не вращаются: их цилиндры всегда стоят по Y, поэтому
    /// пересечение — круг в плане × интервал по высоте.
    /// </summary>
    public static class BuildMath
    {
        /// <summary>Контакт по узлу не считаем пересечением: пропуск по нормали, м.</summary>
        public const float ContactSkin = 0.02f;

        /// <summary>Расстояние от узла до луча камеры, при котором он «ловится», м.</summary>
        public const float SnapNodeRadius = 1.2f;

        /// <summary>Максимальная дистанция по лучу для снапа и постановки, м.</summary>
        public const float SnapReach = 25f;

        /// <summary>Дистанция запасной точки, когда луч никуда не попал, м.</summary>
        public const float AirFallbackDistance = 6f;

        // ─── Полигон площадки ────────────────────────────────────────────────

        /// <summary>Точка внутри замкнутого полигона (x = север, y = восток).</summary>
        public static bool InsidePolygon(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if (((a.y > point.y) != (b.y > point.y))
                    && (point.x < ((b.x - a.x) * (point.y - a.y) / (b.y - a.y)) + a.x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// Всё ПЯТНО детали внутри полигона: центр и окружность основания
        /// (иначе бак свисал бы за край площадки половиной).
        /// </summary>
        public static bool FootprintInside(IReadOnlyList<Vector2> polygon, Vector2 center, float radius)
        {
            if (!InsidePolygon(polygon, center))
            {
                return false;
            }

            const int samples = 8;
            for (int i = 0; i < samples; i++)
            {
                float angle = (2f * Mathf.PI * i) / samples;
                var p = new Vector2(
                    center.x + (radius * Mathf.Cos(angle)),
                    center.y + (radius * Mathf.Sin(angle)));
                if (!InsidePolygon(polygon, p))
                {
                    return false;
                }
            }

            return true;
        }

        // ─── Пересечения ─────────────────────────────────────────────────────

        /// <summary>
        /// Пересекаются ли тела вращения (цилиндры/усечённые конусы): интервалы
        /// по Y и круги в плане. Радиус детали на высоте линейно меняется между
        /// нижним и верхним, поэтому сумма радиусов тоже линейна и её максимум
        /// на интервале перекрытия — на одном из его концов: проверяем оба.
        /// Касание узлов (интервалы соприкасаются) пересечением не считается.
        /// </summary>
        public static bool CylindersOverlap(
            Vector3 centerA, PartMetrics a,
            Vector3 centerB, PartMetrics b)
        {
            float lo = Mathf.Max(centerA.y + a.Bottom, centerB.y + b.Bottom);
            float hi = Mathf.Min(centerA.y + a.Top, centerB.y + b.Top);
            if (hi - lo <= ContactSkin)
            {
                return false;
            }

            float sumAtLo = RadiusAtHeight(centerA, a, lo) + RadiusAtHeight(centerB, b, lo);
            float sumAtHi = RadiusAtHeight(centerA, a, hi) + RadiusAtHeight(centerB, b, hi);
            float dx = centerA.x - centerB.x;
            float dz = centerA.z - centerB.z;
            float maxSum = Mathf.Max(sumAtLo, sumAtHi) - ContactSkin;
            return ((dx * dx) + (dz * dz)) < (maxSum * maxSum);
        }

        /// <summary>Радиус детали на высоте y (оси места): линейно между торцами.</summary>
        public static float RadiusAtHeight(Vector3 center, PartMetrics m, float y)
        {
            float span = m.Top - m.Bottom;
            if (span <= 1e-4f)
            {
                return m.Radius;
            }

            float t = Mathf.Clamp01((y - (center.y + m.Bottom)) / span);
            return Mathf.Lerp(m.Radius, m.TopRadius, t);
        }

        /// <summary>Пересекается ли пробная деталь с уже поставленными.</summary>
        public static bool OverlapsAny(
            IReadOnlyList<PlacedPart> parts, Vector3 pivot, PartMetrics probe, int ignoreIndex)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (i == ignoreIndex)
                {
                    continue;
                }

                if (CylindersOverlap(pivot, probe, parts[i].Pivot, parts[i].Metrics))
                {
                    return true;
                }
            }

            return false;
        }

        // ─── Снап ────────────────────────────────────────────────────────────

        /// <summary>Куда нашёлся снап: деталь-партнёр и какой её узел занят.</summary>
        public readonly struct SnapHit
        {
            public readonly int PartIndex;

            /// <summary>true — призрак встаёт низом на ВЕРХ партнёра.</summary>
            public readonly bool OnTop;

            /// <summary>Желаемый пивот призрака (оси места).</summary>
            public readonly Vector3 Pivot;

            public SnapHit(int partIndex, bool onTop, Vector3 pivot)
            {
                PartIndex = partIndex;
                OnTop = onTop;
                Pivot = pivot;
            }
        }

        /// <summary>
        /// Ближайший к лучу камеры свободный узел, к которому может прилипнуть
        /// призрак: перпендикулярное расстояние узла до луча ≤ nodeRadius,
        /// расстояние вдоль луча ≤ reach. Так высокий стек не требует подходить
        /// вплотную — достаточно посмотреть на нужный узел. Кандидаты:
        /// низ призрака на верх партнёра и верх призрака под низ партнёра.
        /// </summary>
        public static bool TryFindSnap(
            IReadOnlyList<PlacedPart> parts, PartMetrics ghost,
            Vector3 rayOrigin, Vector3 rayDirection,
            out SnapHit hit)
        {
            hit = default;
            bool found = false;
            float bestT = float.MaxValue;

            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];

                if (!p.TopOccupied)
                {
                    Vector3 node = p.Pivot + new Vector3(0f, p.Metrics.Top, 0f);
                    if (RayHitsNode(rayOrigin, rayDirection, node, out float t))
                    {
                        Vector3 pivot = node - new Vector3(0f, ghost.Bottom, 0f);
                        if (t < bestT)
                        {
                            bestT = t;
                            hit = new SnapHit(i, true, pivot);
                            found = true;
                        }
                    }
                }

                if (!p.BottomOccupied)
                {
                    Vector3 node = p.Pivot + new Vector3(0f, p.Metrics.Bottom, 0f);
                    if (RayHitsNode(rayOrigin, rayDirection, node, out float t))
                    {
                        Vector3 pivot = node - new Vector3(0f, ghost.Top, 0f);
                        if (t < bestT)
                        {
                            bestT = t;
                            hit = new SnapHit(i, false, pivot);
                            found = true;
                        }
                    }
                }
            }

            return found;
        }

        private static bool RayHitsNode(Vector3 rayOrigin, Vector3 rayDirection, Vector3 node, out float t)
        {
            t = 0f;
            Vector3 d = node - rayOrigin;
            float along = Vector3.Dot(d, rayDirection);
            if (along < 0.2f || along > SnapReach)
            {
                return false;
            }

            Vector3 lateral = d - (rayDirection * along);
            if (lateral.sqrMagnitude > SnapNodeRadius * SnapNodeRadius)
            {
                return false;
            }

            t = along;
            return true;
        }

        /// <summary>
        /// Второй проход снапа: призрак уже стоит в свободной точке (парит над
        /// стеком или рядом) — если его низ прошёл ровно над свободным ВЕРХОМ
        /// детали (или верх под её низом) и центры почти совпадают по XZ,
        /// прилипаем край-к-краю. Это гарантия «не нужно ловить миллиметры»:
        /// бак, поставленный с зазором над двигателем, всё равно сядет на него.
        /// </summary>
        public static bool TryFindProximitySnap(
            IReadOnlyList<PlacedPart> parts, PartMetrics ghost, Vector3 pivot, out SnapHit hit)
        {
            hit = default;
            bool found = false;
            float bestGap = float.MaxValue;
            float ghostBottom = pivot.y + ghost.Bottom;
            float ghostTop = pivot.y + ghost.Top;

            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                float dx = pivot.x - p.Pivot.x;
                float dz = pivot.z - p.Pivot.z;
                float lateral = (dx * dx) + (dz * dz);

                // Допуск по XZ — по СОПРИКАСАЮЩИМСЯ торцам (у конуса радиусы
                // разные): низ призрака к верху партнёра и верх к низу.
                if (!p.TopOccupied)
                {
                    float reach = Mathf.Max(0.05f, Mathf.Min(ghost.Radius, p.Metrics.TopRadius) * 0.85f);
                    float node = p.Pivot.y + p.Metrics.Top;
                    float gap = ghostBottom - node;
                    if (lateral <= reach * reach
                        && gap >= -0.25f && gap <= 1.5f && Mathf.Abs(gap) < Mathf.Abs(bestGap))
                    {
                        bestGap = gap;
                        hit = new SnapHit(i, true, new Vector3(p.Pivot.x, node - ghost.Bottom, p.Pivot.z));
                        found = true;
                    }
                }

                if (!p.BottomOccupied)
                {
                    float reach = Mathf.Max(0.05f, Mathf.Min(ghost.TopRadius, p.Metrics.Radius) * 0.85f);
                    float node = p.Pivot.y + p.Metrics.Bottom;
                    float gap = node - ghostTop;
                    if (lateral <= reach * reach
                        && gap >= -0.25f && gap <= 1.5f && Mathf.Abs(gap) < Mathf.Abs(bestGap))
                    {
                        bestGap = gap;
                        hit = new SnapHit(i, false, new Vector3(p.Pivot.x, node - ghost.Top, p.Pivot.z));
                        found = true;
                    }
                }
            }

            return found;
        }

        // ─── Попадание луча в деталь (удаление по прицелу) ──────────────────

        /// <summary>
        /// Ближайшая деталь под лучом: пересечение с вертикальными телами
        /// вращения (детали не вращаются, поэтому ось всегда по Y). Чистая
        /// математика — коллайдеры визуалам не нужны, и луч постановки они не
        /// засоряют.
        /// </summary>
        public static bool TryRaycastParts(
            IReadOnlyList<PlacedPart> parts, Vector3 rayOrigin, Vector3 rayDirection,
            float maxDistance, out int index, out float distance)
        {
            index = -1;
            distance = maxDistance;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                Vector3 baseCenter = p.Pivot + new Vector3(0f, p.Metrics.Bottom, 0f);
                if (RayFrustum(rayOrigin, rayDirection, baseCenter,
                        p.Metrics.Radius, p.Metrics.TopRadius, p.Metrics.Height, out float t)
                    && t < distance)
                {
                    distance = t;
                    index = i;
                }
            }

            return index >= 0;
        }

        /// <summary>
        /// Пересечение луча с вертикальным телом вращения: бок (усечённый конус;
        /// равные радиусы — цилиндр) + плоские торцы. Квадратное уравнение
        /// получается подстановкой образующей r(y) = r0 + k·y в x² + z² = r(y)².
        /// </summary>
        public static bool RayFrustum(
            Vector3 rayOrigin, Vector3 rayDirection,
            Vector3 baseCenter, float bottomRadius, float topRadius, float height,
            out float distance)
        {
            distance = 0f;
            float safeHeight = Mathf.Max(height, 1e-6f);
            float k = (topRadius - bottomRadius) / safeHeight;
            float ox = rayOrigin.x - baseCenter.x;
            float oz = rayOrigin.z - baseCenter.z;
            float oy = rayOrigin.y - baseCenter.y;
            float m = bottomRadius + (k * oy);          // радиус на высоте начала луча
            float dx = rayDirection.x;
            float dy = rayDirection.y;
            float dz = rayDirection.z;

            float a = (dx * dx) + (dz * dz) - (k * k * dy * dy);
            float b = 2f * (((ox * dx) + (oz * dz)) - (m * k * dy));
            float c = (ox * ox) + (oz * oz) - (m * m);

            float best = -1f;
            const float eps = 1e-8f;

            // Бок: квадратное уравнение (вырождается в линейное, когда луч
            // параллелен образующей конуса).
            if (Mathf.Abs(a) > eps)
            {
                float disc = (b * b) - (4f * a * c);
                if (disc >= 0f)
                {
                    float sq = Mathf.Sqrt(disc);
                    float t0 = (-b - sq) / (2f * a);
                    float t1 = (-b + sq) / (2f * a);
                    for (int i = 0; i < 2; i++)
                    {
                        float t = i == 0 ? t0 : t1;
                        if (t < 0f)
                        {
                            continue;
                        }

                        float y = oy + (dy * t);
                        if (y >= 0f && y <= height && (best < 0f || t < best))
                        {
                            best = t;
                        }
                    }
                }
            }
            else if (Mathf.Abs(b) > eps)
            {
                float t = -c / b;
                float y = oy + (dy * t);
                if (t >= 0f && y >= 0f && y <= height)
                {
                    best = t;
                }
            }

            // Торцы (крышки): нужны, чтобы смотреть на деталь сверху/снизу.
            if (Mathf.Abs(dy) > 1e-6f)
            {
                for (int cap = 0; cap < 2; cap++)
                {
                    float capY = baseCenter.y + (cap == 0 ? 0f : height);
                    float t = (capY - rayOrigin.y) / dy;
                    if (t < 0f)
                    {
                        continue;
                    }

                    float x = rayOrigin.x + (dx * t) - baseCenter.x;
                    float z = rayOrigin.z + (dz * t) - baseCenter.z;
                    float radius = cap == 0 ? bottomRadius : topRadius;
                    if ((x * x) + (z * z) <= radius * radius && (best < 0f || t < best))
                    {
                        best = t;
                    }
                }
            }

            if (best < 0f)
            {
                return false;
            }

            distance = best;
            return true;
        }

        // ─── Сборка для физики ───────────────────────────────────────────────

        /// <summary>Самая низкая точка сборки (низ самой нижней детали), оси места.</summary>
        public static Vector3 VesselBottom(IReadOnlyList<PlacedPart> parts)
        {
            Vector3 best = Vector3.zero;
            bool has = false;
            for (int i = 0; i < parts.Count; i++)
            {
                Vector3 pivot = parts[i].Pivot;
                float bottom = pivot.y + parts[i].Metrics.Bottom;
                if (!has || bottom < best.y)
                {
                    best = new Vector3(pivot.x, bottom, pivot.z);
                    has = true;
                }
            }

            return best;
        }

        /// <summary>Суммарная масса сборки, кг.</summary>
        public static double TotalMass(IReadOnlyList<PlacedPart> parts)
        {
            double mass = 0d;
            for (int i = 0; i < parts.Count; i++)
            {
                mass += parts[i].Metrics.MassKg;
            }

            return mass;
        }

        /// <summary>
        /// Черновик → PartDefinition[] для VesselStep/зоны контакта: Offset —
        /// центр детали от НИЗА сборки (система корабля: y — вверх места),
        /// ParentIndex — по снапу. Форма — тело вращения: цилиндр, а у бака-
        /// переходника усечённый конус (Dimensions.z — верхний радиус).
        /// </summary>
        public static PartDefinition[] ToPartDefinitions(IReadOnlyList<PlacedPart> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                return null;
            }

            Vector3 bottom = VesselBottom(parts);
            var defs = new PartDefinition[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                float centerY = p.Pivot.y + p.Metrics.Center - bottom.y;
                float bottomY = p.Pivot.y + p.Metrics.Bottom - bottom.y;
                bool frustum = p.Metrics.TopRadius > 0.01f
                    && Mathf.Abs(p.Metrics.TopRadius - p.Metrics.Radius) > 1e-3f;
                defs[i] = new PartDefinition
                {
                    Name = PartCatalog.DisplayName(p.Kind),
                    MassKg = p.Metrics.MassKg,
                    JointStrengthNewtons = p.Metrics.JointStrengthNewtons,
                    BreakTorqueNm = p.Metrics.BreakTorqueNm,
                    Offset = new Vector3(p.Pivot.x - bottom.x, centerY, p.Pivot.z - bottom.z),
                    Shape = frustum ? PartShape.Frustum : PartShape.Cylinder,
                    Dimensions = frustum
                        ? new Vector3(p.Metrics.Radius, p.Metrics.Height, p.Metrics.TopRadius)
                        : new Vector3(p.Metrics.Radius, p.Metrics.Height, 0f),
                    Orientation = new Vector4(0f, 0f, 0f, 1f),
                    ParentIndex = p.ParentIndex,
                    JointAnchor = new Vector3(0f, bottomY - centerY, 0f),
                    EngineOffset = new Vector3(0f, bottomY - centerY, 0f),
                };
            }

            return defs;
        }
    }
}
