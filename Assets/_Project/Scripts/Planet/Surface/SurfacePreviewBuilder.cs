using System;
using System.Collections.Generic;
using Galilego.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Редакторное превью рельефа: clipmap-патч вокруг точки фрейма, тем же
    /// шумом и той же Burst-задачей, что и рантайм (TerrainTileJob +
    /// TerrainNoise + HeightfieldTerrain). «Упрощённой» высоты здесь нет
    /// намеренно: если превью считает не ту функцию, автор ставит базу не туда.
    ///
    /// Геометрия — радиальная сетка (кольца) в касательной плоскости ENU:
    /// кольцо i имеет радиус по геометрической прогрессии и число сегментов,
    /// удваивающееся каждые SegmentsGrowEvery колец. Одна сетка покрывает и
    /// сантиметры под ногами, и десятки километров по горизонту, при линейном
    /// росте числа треугольников. Радиусы задаёт вызывающий (обычно —
    /// дистанция камеры SceneView): это и есть «переменный размер, зум».
    ///
    /// Вершины локальны относительно ОПОРНОЙ ТОЧКИ ФРЕЙМА (x=север,
    /// y=зенит, z=восток), а не центра планеты: координаты километровые, float
    /// не теряет сантиметры. null Terrain — гладкая сфера, высоты нулевые.
    /// </summary>
    public static class SurfacePreviewBuilder
    {
        /// <summary>Радиальная сетка: сколько колец, сегментов и какого радиуса.</summary>
        public struct GridSettings
        {
            /// <summary>Число колец (радиальных уровней детализации).</summary>
            public int Rings;

            /// <summary>Сегментов в первом кольце; дальше удваивается каждые SegmentsGrowEvery.</summary>
            public int BaseSegments;

            /// <summary>Через сколько колец число сегментов удваивается.</summary>
            public int SegmentsGrowEvery;

            /// <summary>Радиус самого внутреннего кольца, м.</summary>
            public double InnerRadiusMeters;

            /// <summary>Радиус самого внешнего кольца, м.</summary>
            public double OuterRadiusMeters;
        }

        /// <summary>
        /// Сетка под окно камеры. visibleRadiusMeters — половина видимой высоты
        /// кадра (для SceneView это её size). Кольца идут строго степенной
        /// прогрессией с шагом 2 (иначе кольцо не содержит предыдущее и LOD
        /// рассыпается), поэтому весь доступный масштаб — 2^(Rings−1), а
        /// Inner/Outer подбираются под камеру: снаружи вчетверо больше видимого
        /// (запас за горизонт), внутри — 2^(Rings−1) раз мельче.
        ///
        /// Число вершин/треугольников от зума НЕ зависит: меняются только
        /// радиусы. Это и делает зум дешёвым — перестроение стоит как один
        /// кадр, а не как пересборка всей планеты.
        /// </summary>
        public static GridSettings GridForVisibleRadius(double visibleRadiusMeters, int rings)
        {
            int ringCount = Math.Max(3, rings);
            double span = Math.Pow(2d, ringCount - 1);
            double outer = Math.Max(1e-3d, visibleRadiusMeters) * 1.25d;
            double inner = outer / span;
            if (inner < 1e-3d)
            {
                // Слишком близко: не даём внутреннему кольцу уйти в микроны —
                // там всё равно ничего не видно, а точность вершин падает.
                inner = 1e-3d;
                outer = inner * span;
            }

            return new GridSettings
            {
                Rings = ringCount,
                BaseSegments = 64,
                SegmentsGrowEvery = 2,
                InnerRadiusMeters = inner,
                OuterRadiusMeters = outer
            };
        }

        /// <summary>Топология сетки: с какого индекса начинается кольцо и сколько в нём сегментов.</summary>
        private struct Layout
        {
            public int[] Starts;
            public int[] Segments;
            public int VertexCount;

            public int Start(int ring) => Starts[ring];

            public int Seg(int ring) => Segments[ring];
        }

        private static Layout MakeLayout(in GridSettings grid)
        {
            int rings = Math.Max(1, grid.Rings);
            var layout = new Layout
            {
                Starts = new int[rings],
                Segments = new int[rings],
                VertexCount = 1
            };

            for (int ring = 0; ring < rings; ring++)
            {
                layout.Starts[ring] = layout.VertexCount;
                layout.Segments[ring] = SegmentsForRing(grid, ring);
                layout.VertexCount += layout.Segments[ring];
            }

            return layout;
        }

        /// <summary>Число вершин сетки: центр плюс сумма сегментов по кольцам.</summary>
        public static int VertexCount(in GridSettings grid) => MakeLayout(grid).VertexCount;

        /// <summary>Сегментов в кольце i (0 — самое внутреннее).</summary>
        public static int SegmentsForRing(in GridSettings grid, int ringIndex)
        {
            int growth = Math.Max(1, grid.SegmentsGrowEvery);
            int doublings = Math.Max(0, ringIndex) / growth;
            return Math.Max(3, grid.BaseSegments) << Math.Min(doublings, 12);
        }

        private static double RingRadius(in GridSettings grid, int ringIndex)
        {
            int last = Math.Max(1, grid.Rings - 1);
            double inner = Math.Max(1e-6d, grid.InnerRadiusMeters);
            double outer = Math.Max(inner, grid.OuterRadiusMeters);
            double t = Math.Max(0, Math.Min(last, ringIndex)) / (double)last;
            return inner * Math.Pow(outer / inner, t);
        }

        /// <summary>
        /// Собрать меш превью. anchorBodyFixed — body-fixed позиция ОПОРНОЙ ТОЧКИ
        /// ФРЕЙМА (SurfaceFrame.AnchorBodyFixed), lat/lon — её координаты,
        /// frameAltitudeMeters — её высота над радиусом (нужна, чтобы правильно
        /// положить уровень моря в локальные оси). Меш садится локально
        /// относительно фрейма.
        /// </summary>
        public static Mesh Build(
            OrbitingBody body, HeightfieldTerrain terrain,
            double latitudeRadians, double longitudeRadians,
            Vector3d anchorBodyFixed, double frameAltitudeMeters,
            in GridSettings grid, bool tintUnderwater)
        {
            if (body == null)
            {
                return null;
            }

            Layout layout = MakeLayout(grid);
            int count = layout.VertexCount;

            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var colors = new Color[count];
            var extra = new Vector4[count];
            var directionsLocal = new Vector3[count];
            var heightsMeters = new double[count];
            var masksCpu = new float[count];
            var detailsCpu = new float[count];
            var rawDirs = new double3[count];
            var directions = new NativeArray<double3>(count, Allocator.TempJob);

            Vector3d up = SurfaceFrameMath.Up(latitudeRadians, longitudeRadians);
            Vector3d north = SurfaceFrameMath.North(latitudeRadians, longitudeRadians);
            Vector3d east = SurfaceFrameMath.East(latitudeRadians, longitudeRadians);
            double radius = body.Radius;

            directions[0] = new double3(up.X, up.Y, up.Z);
            for (int ring = 0; ring < grid.Rings; ring++)
            {
                double r = RingRadius(grid, ring);
                int segments = layout.Seg(ring);
                int start = layout.Start(ring);
                for (int s = 0; s < segments; s++)
                {
                    // Угол отсчитываем от севера: cos → север, sin → восток.
                    double a = (s / (double)segments) * Math.PI * 2d;
                    double n = Math.Cos(a) * r;
                    double e = Math.Sin(a) * r;
                    Vector3d dir = SurfaceFrameMath.DirectionOffset(
                        latitudeRadians, longitudeRadians, radius, e, n);
                    directions[start + s] = new double3(dir.X, dir.Y, dir.Z);
                }
            }

            // Сырое направление нужно и для позиции, и для uv1 игрового
            // шейдера (он смотрит в body-fixed направление и строит по нему
            // текстурные координаты). В локальные оси фрейма его переводим сразу,
            // пока NativeArray ещё жив.
            for (int v = 0; v < count; v++)
            {
                rawDirs[v] = directions[v];
                Vector3d local = SurfaceFrameMath.ToLocal(
                    latitudeRadians, longitudeRadians,
                    new Vector3d(rawDirs[v].x, rawDirs[v].y, rawDirs[v].z));
                directionsLocal[v] = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
            }

            double amplitude = terrain != null ? Math.Max(1d, terrain.AmplitudeMeters) : 1d;
            double seaLevel = terrain != null ? terrain.SeaLevelMeters : -1e30d;
            bool hasSea = terrain != null && seaLevel > -1e29d;

            if (terrain != null)
            {
                var heights = new NativeArray<double>(count, Allocator.TempJob);
                var masks = new NativeArray<float>(count, Allocator.TempJob);
                var details = new NativeArray<float>(count, Allocator.TempJob);
                var job = new TerrainTileJob
                {
                    Params = TerrainNoiseParams.FromTerrain(terrain),
                    Directions = directions,
                    Heights = heights,
                    ColorMasks = masks,
                    ColorDetails = details
                };
                job.Schedule(count, 64, new JobHandle()).Complete();

                for (int v = 0; v < count; v++)
                {
                    double h = heights[v] * amplitude;
                    heightsMeters[v] = h;
                    masksCpu[v] = masks[v];
                    detailsCpu[v] = details[v];
                    double3 d = rawDirs[v];
                    positions[v] = ToFrameLocal(
                        new Vector3d(d.x, d.y, d.z) * (radius + h), anchorBodyFixed, north, up, east);
                    colors[v] = Shade(terrain, h, seaLevel, amplitude, masks[v], details[v],
                        latitudeRadians, tintUnderwater && hasSea);
                }

                heights.Dispose();
                masks.Dispose();
                details.Dispose();
            }
            else
            {
                for (int v = 0; v < count; v++)
                {
                    double3 d = rawDirs[v];
                    positions[v] = ToFrameLocal(
                        new Vector3d(d.x, d.y, d.z) * radius, anchorBodyFixed, north, up, east);
                    colors[v] = TerrainPalette.Grass;
                }
            }

            directions.Dispose();

            // Уровень моря в локальных осях фрейма: опорная точка на море —
            // это anchor с радиусом (R + seaLevel), её проекция на зенит минус
            // проекция anchor. Разность радиусов — ровно seaLevel − frameAltitude.
            double seaLocalY = seaLevel + TerrainPalette.SeaEpsilon(amplitude) - frameAltitudeMeters;
            if (hasSea && tintUnderwater)
            {
                FlattenBelowSea(positions, normals, seaLocalY);
            }

            RecomputeNormals(positions, normals, layout, grid);

            // Раскладка вершин — РОВНО та, что ждёт игровой
            // Galileo/PlanetSurface (его struct Attributes): uv2 = (сырая высота,
            // косинус уклона к зениту, маска цвета CPU, деталь CPU). Собственный
            // шейдер превью эти каналы не использовал; игровой использует все,
            // и именно по ним считает текстуры, пляж и воду.
            for (int v = 0; v < count; v++)
            {
                extra[v] = new Vector4(
                    (float)heightsMeters[v],
                    normals[v].y,
                    masksCpu[v],
                    detailsCpu[v]);
            }

            int[] triangles = BuildTriangles(layout, out _);

            var mesh = new Mesh
            {
                name = "SurfacePreview",
                indexFormat = count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.SetUVs(1, new List<Vector4>(extra));
            mesh.SetUVs(2, new List<Vector3>(directionsLocal));
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 ToFrameLocal(Vector3d point, Vector3d anchor, Vector3d north, Vector3d up, Vector3d east)
        {
            Vector3d d = point - anchor;
            return new Vector3(
                (float)Vector3d.Dot(d, north),
                (float)Vector3d.Dot(d, up),
                (float)Vector3d.Dot(d, east));
        }

        /// <summary>
        /// Запасной упрощённый материал на случай, если игровой шейдер рельефа
        /// недоступен. Плоский цвет по вершинам + сетка масштаба: не похоже на
        /// игру, но превью остаётся пригодным, а не чёрным.
        /// </summary>
        public static Material CreateFallbackMaterial()
        {
            Shader shader = Shader.Find("Galilego/SurfacePreview");
            if (shader == null)
            {
                return null;
            }

            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private static Color Shade(
            HeightfieldTerrain terrain, double rawHeight, double seaLevel, double amplitude,
            float mask, float detail, double latitudeRadians, bool tintUnderwater)
        {
            Color c = TerrainPalette.HeightColorEx(
                rawHeight, seaLevel, amplitude, 0d, mask,
                terrain.ColorRockSlopeTan, terrain.ColorRockSlopeWidth,
                terrain.ColorSnowSlopeTan, terrain.ColorNoiseStrength,
                detail, terrain.ColorDetailStrength, latitudeRadians,
                terrain.ColorRockHeightMin, terrain.BeachHeightMeters);

            // Под водой дно чуть синее: иначе океан неотличим от суши, а превью
            // показывает ~70% «суши» из подводного дна и вводит в заблуждение.
            // Порог — тот же SeaEpsilon, что у палитры и у шейдера.
            return tintUnderwater && rawHeight < seaLevel + TerrainPalette.SeaEpsilon(amplitude)
                ? Color.Lerp(c, TerrainPalette.Sea, 0.55f)
                : c;
        }

        private static void FlattenBelowSea(Vector3[] positions, Vector3[] normals, double seaLocalY)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i].y < seaLocalY)
                {
                    positions[i].y = (float)seaLocalY;
                    normals[i] = Vector3.up;
                }
            }
        }

        /// <summary>
        /// Нормали центральными разностями по готовой позиции: дёшево, и меш
        /// сам себе опорный (вторая выборка шума не нужна). Соседи берутся из
        /// той же топологии, что и треугольники, — индексы не расходятся.
        /// </summary>
        private static void RecomputeNormals(
            Vector3[] positions, Vector3[] normals, in Layout layout, in GridSettings grid)
        {
            for (int v = 0; v < positions.Length; v++)
            {
                normals[v] = Vector3.up;
            }

            for (int ring = 0; ring < grid.Rings; ring++)
            {
                int segments = layout.Seg(ring);
                int start = layout.Start(ring);
                for (int s = 0; s < segments; s++)
                {
                    int v = start + s;
                    int prev = start + ((s - 1 + segments) % segments);
                    int next = start + ((s + 1) % segments);
                    int outward = ring + 1 < grid.Rings
                        ? layout.Start(ring + 1) + Nearest(layout.Seg(ring + 1), segments, s)
                        : v;
                    int inward = ring == 0
                        ? 0
                        : layout.Start(ring - 1) + Nearest(layout.Seg(ring - 1), segments, s);

                    Vector3 radial = positions[outward] - positions[inward];
                    Vector3 tangential = positions[next] - positions[prev];
                    Vector3 n = Vector3.Cross(tangential, radial);
                    float len = n.magnitude;
                    Vector3 normal = len > 1e-9f ? (n / len) : Vector3.up;
                    normals[v] = normal.y < 0f ? -normal : normal;
                }
            }
        }

        /// <summary>Индекс сегмента в кольце countSeg, ближайший по доле к s/segments.</summary>
        private static int Nearest(int countSeg, int segments, int s)
        {
            int index = (int)((long)s * countSeg / segments);
            return index >= countSeg ? countSeg - 1 : index;
        }

        private static int[] BuildTriangles(in Layout layout, out int triangleCount)
        {
            int first = layout.Seg(0);
            int rings = layout.Segments.Length;

            // Верхняя граница: два треугольника на каждый шаг. Фактически
            // вырожденные пропускаем, поэтому массив ужимается в конце.
            int capacity = first;
            for (int ring = 1; ring < rings; ring++)
            {
                capacity += 2 * Math.Max(layout.Seg(ring - 1), layout.Seg(ring));
            }

            var triangles = new int[capacity * 3];
            int t = 0;

            // Веер центра в первое кольцо.
            for (int s = 0; s < first; s++)
            {
                triangles[t++] = 0;
                triangles[t++] = layout.Start(0) + ((s + 1) % first);
                triangles[t++] = layout.Start(0) + s;
            }

            // Квады между соседними кольцами. Обход против часовой стрелки
            // (сверху, +Y): cross(v1−v0, v2−v0) смотрит на наблюдателя, значит
            // грань лицевая — иначе превью не видно с Cull Back (проверено на
            // геометрических нормалях всех 24256 треугольников).
            for (int ring = 1; ring < rings; ring++)
            {
                int innerSeg = layout.Seg(ring - 1);
                int outerSeg = layout.Seg(ring);
                int innerStart = layout.Start(ring - 1);
                int outerStart = layout.Start(ring);
                int steps = Math.Max(innerSeg, outerSeg);

                for (int s = 0; s < steps; s++)
                {
                    // (s + 1) по модулю steps: иначе на последнем шаге индекс
                    // уезжает на начало СЛЕДУЮЩЕГО кольца (на 1..Seg(ring)
                    // вне диапазона) — Unity молча отбрасывает весь меш.
                    int s1 = (s + 1) % steps;
                    int a0 = innerStart + ((int)((long)s * innerSeg / steps));
                    int a1 = innerStart + ((int)((long)s1 * innerSeg / steps));
                    int b0 = outerStart + ((int)((long)s * outerSeg / steps));
                    int b1 = outerStart + ((int)((long)s1 * outerSeg / steps));

                    if (a0 == a1)
                    {
                        // Внутреннее ребро схлопнулось (сегментов снаружи вдвое
                        // больше): квад вырожден в один треугольник.
                        triangles[t++] = a0;
                        triangles[t++] = b1;
                        triangles[t++] = b0;
                    }
                    else if (b0 == b1)
                    {
                        triangles[t++] = a0;
                        triangles[t++] = a1;
                        triangles[t++] = b1;
                    }
                    else
                    {
                        triangles[t++] = a0;
                        triangles[t++] = b1;
                        triangles[t++] = b0;

                        triangles[t++] = a0;
                        triangles[t++] = a1;
                        triangles[t++] = b1;
                    }
                }
            }

            if (t != triangles.Length)
            {
                Array.Resize(ref triangles, t);
            }

            triangleCount = t / 3;
            return triangles;
        }
    }
}
