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
        /// Пересекаются ли цилиндры (интервалы по Y и круги в плане).
        /// Касание узлов (интервалы соприкасаются) пересечением не считается.
        /// </summary>
        public static bool CylindersOverlap(
            Vector3 centerA, PartMetrics a,
            Vector3 centerB, PartMetrics b)
        {
            float aMin = centerA.y + a.Bottom;
            float aMax = centerA.y + a.Top;
            float bMin = centerB.y + b.Bottom;
            float bMax = centerB.y + b.Top;
            float yOverlap = Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin);
            if (yOverlap <= ContactSkin)
            {
                return false;
            }

            float dx = centerA.x - centerB.x;
            float dz = centerA.z - centerB.z;
            float radiusSum = a.Radius + b.Radius;
            return ((dx * dx) + (dz * dz)) < ((radiusSum - ContactSkin) * (radiusSum - ContactSkin));
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
                float reach = Mathf.Max(0.05f, Mathf.Min(ghost.Radius, p.Metrics.Radius) * 0.6f);
                if (lateral > reach * reach)
                {
                    continue;
                }

                if (!p.TopOccupied)
                {
                    float node = p.Pivot.y + p.Metrics.Top;
                    float gap = ghostBottom - node;
                    if (gap >= -0.2f && gap <= 1.0f && Mathf.Abs(gap) < Mathf.Abs(bestGap))
                    {
                        bestGap = gap;
                        hit = new SnapHit(i, true, new Vector3(p.Pivot.x, node - ghost.Bottom, p.Pivot.z));
                        found = true;
                    }
                }

                if (!p.BottomOccupied)
                {
                    float node = p.Pivot.y + p.Metrics.Bottom;
                    float gap = node - ghostTop;
                    if (gap >= -0.2f && gap <= 1.0f && Mathf.Abs(gap) < Mathf.Abs(bestGap))
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
        /// Ближайшая деталь под лучом: пересечение с вертикальными цилиндрами
        /// (детали не вращаются, поэтому цилиндр всегда стоит по Y). Чистая
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
                if (RayCylinder(rayOrigin, rayDirection, baseCenter, p.Metrics.Radius, p.Metrics.Height, out float t)
                    && t < distance)
                {
                    distance = t;
                    index = i;
                }
            }

            return index >= 0;
        }

        /// <summary>Пересечение луча с вертикальным цилиндром (бок + торцы).</summary>
        public static bool RayCylinder(
            Vector3 rayOrigin, Vector3 rayDirection,
            Vector3 baseCenter, float radius, float height,
            out float distance)
        {
            distance = 0f;
            float ox = rayOrigin.x - baseCenter.x;
            float oz = rayOrigin.z - baseCenter.z;
            float dx = rayDirection.x;
            float dz = rayDirection.z;
            float a = (dx * dx) + (dz * dz);
            float b = 2f * ((ox * dx) + (oz * dz));
            float c = (ox * ox) + (oz * oz) - (radius * radius);

            float best = -1f;

            // Бок: квадратное уравнение по XZ.
            if (a > 1e-8f)
            {
                float disc = (b * b) - (4f * a * c);
                if (disc >= 0f)
                {
                    float sq = Mathf.Sqrt(disc);
                    float t0 = (-b - sq) / (2f * a);
                    float t1 = (-b + sq) / (2f * a);
                    for (int k = 0; k < 2; k++)
                    {
                        float t = k == 0 ? t0 : t1;
                        if (t < 0f)
                        {
                            continue;
                        }

                        float y = (rayOrigin.y + (rayDirection.y * t)) - baseCenter.y;
                        if (y >= 0f && y <= height)
                        {
                            best = t;
                            break;
                        }
                    }
                }
            }
            else if (c <= 0f)
            {
                // Луч вдоль оси внутри радиуса: считаем от основания.
                best = 0f;
            }

            // Торцы (крышки): нужны, чтобы смотреть на деталь сверху/снизу.
            if (Mathf.Abs(rayDirection.y) > 1e-6f)
            {
                for (int cap = 0; cap < 2; cap++)
                {
                    float capY = baseCenter.y + (cap == 0 ? 0f : height);
                    float t = (capY - rayOrigin.y) / rayDirection.y;
                    if (t < 0f)
                    {
                        continue;
                    }

                    float x = rayOrigin.x + (rayDirection.x * t) - baseCenter.x;
                    float z = rayOrigin.z + (rayDirection.z * t) - baseCenter.z;
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
        /// ParentIndex — по снапу. Форма — цилиндр (оболочка).
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
                defs[i] = new PartDefinition
                {
                    Name = PartCatalog.DisplayName(p.Kind),
                    MassKg = p.Metrics.MassKg,
                    JointStrengthNewtons = p.Metrics.JointStrengthNewtons,
                    BreakTorqueNm = p.Metrics.BreakTorqueNm,
                    Offset = new Vector3(p.Pivot.x - bottom.x, centerY, p.Pivot.z - bottom.z),
                    Shape = PartShape.Cylinder,
                    Dimensions = new Vector3(p.Metrics.Radius, p.Metrics.Height, 0f),
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
