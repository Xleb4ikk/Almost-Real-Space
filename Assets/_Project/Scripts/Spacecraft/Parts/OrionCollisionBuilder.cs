using System.Collections.Generic;
using Galilego.Building;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Коллайдеры капсулы Orion для ходьбы/полёта игрока.
    ///
    /// Опора игрока (MeshSupportBase) выталкивает капсулу через Physics.ComputePenetration,
    /// а он НЕ работает с non-convex MeshCollider. Раньше на КАЖДЫЙ меш модели (болты,
    /// рамы, стёкла, кресло...) вешался non-convex коллайдер: при малейшем проникновении
    /// (быстрый вход, угол рамы) стена молча игнорировалась — игрок цеплялся за мелочь
    /// или вылетал сквозь иллюминатор.
    ///
    /// Здесь вместо этого строится небольшой набор ВЫПУКЛЫХ коллайдеров по реальным
    /// мешам: оболочка корпуса режется на клинья (стёкла иллюминаторов оказываются
    /// внутри сплошной стены), вокруг люка — мелкая сетка, чтобы проём остался
    /// проёмом, а створка — один бокс, едущий вместе с дверью. Всё считается по самой
    /// модели, поэтому пересборка FBX в Blender подхватывается автоматически.
    ///
    /// ВАЖНО: вся геометрия и пороги — в МИРОВЫХ МЕТРАХ, а не в осях модели: у корня
    /// FBX масштаб ×100 (сантиметры), и в его осях пороги 0.5/1.3 м «не срабатывали» —
    /// проём не вырезался вовсе, оболочка оставалась сплошной.
    ///
    /// Нужен Read/Write у Orion_CrewModule.fbx (ставит OrionModelImport).
    /// </summary>
    public static class OrionCollisionBuilder
    {
        /// <summary>Число угловых секторов оболочки (48 → хорда ~0.3 м, прогиб ~5 мм).</summary>
        private const int SectorCount = 48;

        /// <summary>Границы высотных поясов оболочки над палубой (м): выпуклость профиля.</summary>
        private const float BandA = 0.75f;
        private const float BandB = 1.55f;

        /// <summary>Длинные треугольники режем, чтобы они не тянули выпуклую оболочку через проём.</summary>
        private const float MaxEdge = 0.10f;

        /// <summary>Шаг сетки вокруг люка: поперёк (м) и по высоте (м).</summary>
        private const float CellV = 0.12f;
        private const float CellW = 0.15f;

        /// <summary>
        /// Запас выреза проёма ВНУТРЬ кромок створки (м). Бокс створки строится по её
        /// габариту минус тот же запас, поэтому закрытая дверь перекрывает проём без
        /// щелей. Отступ «наружу» (0.10), как было раньше, давал щель слева от створки
        /// (створка стоит несимметрично оси люка).
        /// </summary>
        private const float DoorInset = 0.04f;

        /// <summary>Всё ниже палубы (м) — днище, его закрывает выпуклый колпак теплозащиты.</summary>
        private const float BelowDeck = -0.15f;

        /// <summary>Направлений для упрощения крупных выпуклых тел (хулл ≤ 2n-4 = 236 граней &lt; 255).</summary>
        private const int ExtremeDirections = 120;

        private struct Tri
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
        }

        /// <summary>Система координат капсулы: u — к люку, v — вбок, w — вверх от палубы.</summary>
        private sealed class CapsuleFrame
        {
            public Vector3 Origin;
            public Vector3 Axis;
            public Vector3 Hatch;
            public Vector3 Side;
            public float DeckTop;

            public void ToUvw(Vector3 p, out float u, out float v, out float w)
            {
                Vector3 d = p - Origin;
                u = Vector3.Dot(d, Hatch);
                v = Vector3.Dot(d, Side);
                w = Vector3.Dot(d, Axis) - DeckTop;
            }
        }

        /// <summary>
        /// Собрать коллайдеры капсулы под partRoot (модель — потомок partRoot).
        /// false — модель не подошла (нет нужных мешей/нечитаемый меш): вызывающий код
        /// возвращается к старому поведению.
        /// </summary>
        public static bool Build(Transform partRoot)
        {
            MeshFilter backshell = Find(partRoot, "Backshell");
            MeshFilter pressure = Find(partRoot, "PressureShell");
            MeshFilter hatchFrame = Find(partRoot, "Hatch_Frame");
            MeshFilter heatShield = Find(partRoot, "HeatShield");
            MeshFilter deck = Find(partRoot, "Deck_Floor");
            MeshFilter nose = Find(partRoot, "Nose_Glass");
            MeshFilter door = Find(partRoot, "Hatch_Door");
            if (backshell == null || pressure == null || hatchFrame == null || heatShield == null
                || deck == null || nose == null || door == null)
            {
                Debug.LogWarning("[Orion] Нет мешей Backshell/PressureShell/Hatch_Frame/HeatShield/Deck_Floor/Nose_Glass/Hatch_Door — "
                    + "точные коллайдеры не построены.");
                return false;
            }

            MeshFilter[] required = { backshell, pressure, hatchFrame, heatShield, deck, nose, door };
            for (int i = 0; i < required.Length; i++)
            {
                if (!required[i].sharedMesh.isReadable)
                {
                    Debug.LogError("[Orion] Меш '" + required[i].name + "' нечитаемый: включи Read/Write у Orion_CrewModule.fbx "
                        + "(Model → Read/Write) — иначе коллайдеры капсулы не построить.");
                    return false;
                }
            }

            var hostGo = new GameObject("Orion_Collision");
            hostGo.transform.SetParent(partRoot, false);
            var owner = hostGo.AddComponent<OrionCollisionMeshes>();
            // host-go хранит вершины тел в СВОИХ локальных осях (у корня модели
            // масштаб ×100 от FBX — конвертация при сохранении обязательна).
            // Вся геометрия и пороги (zoneV, clipW, MaxEdge...) — в МИРОВЫХ
            // МЕТРАХ: считать их в осях host нельзя — там сантиметры модели.
            Matrix4x4 toHost = hostGo.transform.worldToLocalMatrix;

            List<Vector3> heatPts = Points(heatShield);
            List<Vector3> nosePts = Points(nose);
            List<Vector3> deckPts = Points(deck);
            List<Vector3> framePts = Points(hatchFrame);
            List<Vector3> doorPts = Points(door);

            // Оси капсулы по данным модели (не зависят от разворота осей Blender).
            var f = new CapsuleFrame();
            Vector3 deckCentre = Mean(deckPts);
            f.Axis = (Mean(nosePts) - Mean(heatPts)).normalized;
            // Ось люка — по центру СТВОРКИ: у рамы петли с одной стороны, её
            // центр перекошен (~11°), и вырез проёма в таких осях выходил косым.
            Vector3 toHatch = Mean(doorPts) - deckCentre;
            toHatch -= f.Axis * Vector3.Dot(toHatch, f.Axis);
            f.Hatch = toHatch.normalized;
            f.Side = Vector3.Cross(f.Axis, f.Hatch);
            f.Origin = deckCentre;
            f.DeckTop = float.NegativeInfinity;
            for (int i = 0; i < deckPts.Count; i++)
            {
                f.DeckTop = Mathf.Max(f.DeckTop, Vector3.Dot(deckPts[i] - f.Origin, f.Axis));
            }

            // Размеры проёма — из створки и рамы.
            float doorMinV = float.PositiveInfinity;
            float doorMaxV = float.NegativeInfinity;
            for (int i = 0; i < doorPts.Count; i++)
            {
                f.ToUvw(doorPts[i], out _, out float v, out _);
                doorMinV = Mathf.Min(doorMinV, v);
                doorMaxV = Mathf.Max(doorMaxV, v);
            }

            float frameMinW = float.PositiveInfinity;
            float frameMaxW = float.NegativeInfinity;
            for (int i = 0; i < framePts.Count; i++)
            {
                f.ToUvw(framePts[i], out _, out _, out float w);
                frameMinW = Mathf.Min(frameMinW, w);
                frameMaxW = Mathf.Max(frameMaxW, w);
            }

            // Границы выреза — по кромкам СТВОРКИ (она стоит несимметрично оси люка,
            // симметричный вырез оставлял щель сбоку). Бокс створки шире на 2 см — щелей нет.
            float clipVMin = doorMinV + DoorInset;
            float clipVMax = doorMaxV - DoorInset;
            float zoneVMin = clipVMin - 0.30f;      // мелкая сетка вокруг люка
            float zoneVMax = clipVMax + 0.30f;
            // Вертикаль проёма. Порог люка — плоская плита до sillTop (вместо
            // срезанного скоса): вырез начинается на 12 см ниже её верха, поэтому
            // верх порога остаётся целым. Раньше вырез шёл от frameMinW+0.14 и
            // оставлял от порога скос ~31–33° — круче MaxSlopeDegrees (30°), и
            // выходящий игрок скользил по нему вниз и проваливался.
            float sillTop = frameMinW + 0.15f;   // верх порога над палубой
            float sillUIn = float.PositiveInfinity, sillUOut = float.NegativeInfinity;
            for (int i = 0; i < framePts.Count; i++)
            {
                f.ToUvw(framePts[i], out float fu, out _, out float fw);
                if (fw < frameMinW + 0.25f)
                {
                    sillUIn = Mathf.Min(sillUIn, fu);
                    sillUOut = Mathf.Max(sillUOut, fu);
                }
            }

            // Верхний запас по 4 см от кромки створки: физический просвет ~1.0 м
            // (выпуклые тела съедают ещё ~0.06 с каждой стороны) — капсула игрока
            // в приседе (0.8 м) проходит, в полный рост (1.45 м) нет.
            float clipWMin = sillTop - 0.12f;   // ниже вырез не доходит: остатки ячеек ниже плиты
            float clipWMax = frameMaxW - 0.04f;

            // Оболочка: клинья + сетка у люка.
            var cells = new Dictionary<long, List<Vector3>>();
            AddShell(backshell, f, zoneVMin, zoneVMax, clipVMin, clipVMax, clipWMin, clipWMax, cells);
            AddShell(pressure, f, zoneVMin, zoneVMax, clipVMin, clipVMax, clipWMin, clipWMax, cells);
            AddShell(hatchFrame, f, zoneVMin, zoneVMax, clipVMin, clipVMax, clipWMin, clipWMax, cells);
            foreach (KeyValuePair<long, List<Vector3>> cell in cells)
            {
                AddTriangleHull(hostGo, cell.Value, owner, toHost);
            }

            // Порог люка — сплошная плита по всей глубине рамы: фрагменты ячеек
            // оболочки под вырезом тонкие и «полом» служить не должны.
            var sillPts = new List<Vector3>(8);
            foreach (float uu in new[] { sillUIn, sillUOut })
            {
                foreach (float vv in new[] { clipVMin, clipVMax })
                {
                    foreach (float ww in new[] { -0.02f, sillTop })
                    {
                        sillPts.Add(f.Origin + f.Hatch * uu + f.Side * vv + f.Axis * (ww + f.DeckTop));
                    }
                }
            }

            AddPointsHull(hostGo, sillPts, owner, toHost);

            // Днище и палуба: колпак теплозащиты, верх срезан по палубе.
            var heatClamped = new List<Vector3>(heatPts.Count);
            for (int i = 0; i < heatPts.Count; i++)
            {
                Vector3 p = heatPts[i];
                f.ToUvw(p, out _, out _, out float w);
                heatClamped.Add(w > 0f ? p - (f.Axis * w) : p);
            }

            AddReducedHull(hostGo, heatClamped, owner, toHost);

            // Палуба (Deck_Floor) — отдельное тело-плита по визуальному полу.
            // Без него опорой внутри служил только купол теплозащиты: его верх
            // на пару сантиметров ниже плиты палубы, а у кромок к стенам тела
            // купола может не хватать — игрок «проваливался под текстуру пола».
            AddReducedHull(hostGo, deckPts, owner, toHost);

            // Стекло носа (колпак над кольцом оболочки).
            AddReducedHull(hostGo, nosePts, owner, toHost);

            // Мебель: кресло пилота, кресло и стол оператора — по одному выпуклому телу
            // (каждое на своём объекте с маркером PlayerObstacleOnly: стена, не ступенька).
            // Ячейковая нарезка и воксельные боксы проверены на реальных мешах: при радиусе
            // капсулы 0.25 м вогнутости кресел уже не пролезть, выигрыш по проходимости
            // < 0.2 м², а коллайдеров становится 300–600 вместо трёх.
            AddFurniture(partRoot, "Pilot_Seat", hostGo, owner, toHost);
            AddFurniture(partRoot, "Operator_Chair", hostGo, owner, toHost);
            AddFurniture(partRoot, "Operator_Desk", hostGo, owner, toHost);

            // Створка: бокс в локальных осях двери — едет вместе с ней при открытии.
            AddDoorBox(door);

            // Готовые коллайдеры модели (корпуса мониторов — 8 вершин, коробки): non-convex
            // не годится для ComputePenetration.
            MeshCollider[] existing = partRoot.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i].transform.IsChildOf(hostGo.transform))
                {
                    continue;
                }

                existing[i].convex = true;

                // Мониторы и планшет висят на высоте головы/груди и нужны лучам (прицел,
                // указка), но не телу: иначе игрок «цепляется» за телевизоры. Остальное
                // готовое — просто препятствие.
                string n = existing[i].name;
                if (n.IndexOf("Monitor", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Tablet", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MarkIgnored(existing[i].gameObject);
                }
                else
                {
                    MarkObstacleOnly(existing[i].gameObject);
                }
            }

            // Мониторы (BoxCollider указки) — только для лучей.
            foreach (ProceduralMonitor monitor in partRoot.GetComponentsInChildren<ProceduralMonitor>(true))
            {
                MarkIgnored(monitor.gameObject);
            }

            // Бокс интеракции кресла (E) — тоже только для лучей. Физической мебелью
            // кресла служат тела Col_*. Collider остаётся твёрдым и НЕ Trigger: прицел
            // его видит при любых настройках Physics.queriesHitTriggers.
            foreach (CockpitSeat seat in partRoot.GetComponentsInChildren<CockpitSeat>(true))
            {
                MarkIgnored(seat.gameObject);
            }

            return true;
        }

        private static void AddShell(MeshFilter source, CapsuleFrame f, float zoneVMin, float zoneVMax,
            float clipVMin, float clipVMax, float clipWMin, float clipWMax, Dictionary<long, List<Vector3>> cells)
        {
            Mesh mesh = source.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            Matrix4x4 m = source.transform.localToWorldMatrix;
            var stack = new Stack<Tri>();

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                stack.Push(new Tri
                {
                    A = m.MultiplyPoint3x4(vertices[indices[i]]),
                    B = m.MultiplyPoint3x4(vertices[indices[i + 1]]),
                    C = m.MultiplyPoint3x4(vertices[indices[i + 2]])
                });

                while (stack.Count > 0)
                {
                    Tri t = stack.Pop();
                    float ab = (t.B - t.A).sqrMagnitude;
                    float bc = (t.C - t.B).sqrMagnitude;
                    float ca = (t.A - t.C).sqrMagnitude;
                    float longest = Mathf.Max(ab, Mathf.Max(bc, ca));
                    if (longest > MaxEdge * MaxEdge)
                    {
                        // Делим самое длинное ребро пополам.
                        if (ab >= bc && ab >= ca)
                        {
                            Vector3 mid = (t.A + t.B) * 0.5f;
                            stack.Push(new Tri { A = t.A, B = mid, C = t.C });
                            stack.Push(new Tri { A = mid, B = t.B, C = t.C });
                        }
                        else if (bc >= ca)
                        {
                            Vector3 mid = (t.B + t.C) * 0.5f;
                            stack.Push(new Tri { A = t.A, B = t.B, C = mid });
                            stack.Push(new Tri { A = t.A, B = mid, C = t.C });
                        }
                        else
                        {
                            Vector3 mid = (t.C + t.A) * 0.5f;
                            stack.Push(new Tri { A = t.A, B = t.B, C = mid });
                            stack.Push(new Tri { A = mid, B = t.B, C = t.C });
                        }

                        continue;
                    }

                    Classify(t, f, zoneVMin, zoneVMax, clipVMin, clipVMax, clipWMin, clipWMax, cells);
                }
            }
        }

        private static void Classify(Tri t, CapsuleFrame f, float zoneVMin, float zoneVMax,
            float clipVMin, float clipVMax, float clipWMin, float clipWMax,
            Dictionary<long, List<Vector3>> cells)
        {
            Vector3 centre = (t.A + t.B + t.C) / 3f;
            f.ToUvw(centre, out float u, out float v, out float w);
            if (w < BelowDeck)
            {
                return;
            }

            bool zone = u > 0f && v > zoneVMin && v < zoneVMax;
            if (zone && v > clipVMin && v < clipVMax && w >= clipWMin && w <= clipWMax)
            {
                // Створ проёма: вырезаем (створка при закрытии перекрывает его боксом).
                return;
            }

            long key;
            if (zone)
            {
                int iv = Mathf.FloorToInt((v - zoneVMin) / CellV);
                // Стойки проёма (v в поясе люка, но вне выреза) в поясе высоты
                // выреза сливаем в один бакет: получится цельное выпуклое тело без
                // стыков ячеек по высоте (на стыках боковой луч Floor находил
                // «пол» в пределах шага вверх и подбрасывал игрока).
                int iw = (w >= clipWMin && w <= clipWMax) ? -100 : Mathf.FloorToInt(w / CellW);
                key = Key(1, iv, iw);
            }
            else
            {
                float angle = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
                if (angle < 0f)
                {
                    angle += 360f;
                }

                int sector = Mathf.Min(SectorCount - 1, Mathf.FloorToInt(angle / (360f / SectorCount)));
                int band = w < BandA ? 0 : (w < BandB ? 1 : 2);
                key = Key(0, sector, band);
            }

            if (!cells.TryGetValue(key, out List<Vector3> list))
            {
                list = new List<Vector3>(64);
                cells.Add(key, list);
            }

            list.Add(t.A);
            list.Add(t.B);
            list.Add(t.C);
        }

        private static long Key(int kind, int a, int b)
        {
            return ((long)kind << 44) | ((long)(a & 0x3FFFFF) << 22) | (long)(b & 0x3FFFFF);
        }

        /// <summary>Выпуклое тело по набору треугольников (вершины в мировых метрах идут тройками).</summary>
        private static void AddTriangleHull(GameObject host, List<Vector3> triangleVertices, OrionCollisionMeshes owner,
            Matrix4x4 toHost)
        {
            var map = new Dictionary<Vector3Int, int>();
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (int i = 0; i + 2 < triangleVertices.Count; i += 3)
            {
                int a = Weld(map, vertices, toHost.MultiplyPoint3x4(triangleVertices[i]));
                int b = Weld(map, vertices, toHost.MultiplyPoint3x4(triangleVertices[i + 1]));
                int c = Weld(map, vertices, toHost.MultiplyPoint3x4(triangleVertices[i + 2]));
                if (a == b || b == c || a == c)
                {
                    continue;
                }

                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }

            if (vertices.Count < 4 || indices.Count < 3)
            {
                return;
            }

            AttachHull(host, vertices, indices, owner);
        }

        /// <summary>
        /// Крупное тело: берём крайние точки по фиксированным направлениям — выпуклая оболочка
        /// гарантированно укладывается в лимит PhysX (255 граней) и не «полу-оболочка».
        /// </summary>
        private static void AddReducedHull(GameObject host, List<Vector3> points, OrionCollisionMeshes owner,
            Matrix4x4 toHost)
        {
            if (points.Count < 4)
            {
                return;
            }

            var vertices = new List<Vector3>();
            var seen = new HashSet<int>();
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int d = 0; d < ExtremeDirections; d++)
            {
                float y = 1f - (2f * (d + 0.5f) / ExtremeDirections);
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - (y * y)));
                float phi = d * golden;
                var dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);

                int best = 0;
                float bestDot = float.NegativeInfinity;
                for (int i = 0; i < points.Count; i++)
                {
                    float dot = Vector3.Dot(points[i], dir);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = i;
                    }
                }

                if (seen.Add(best))
                {
                    vertices.Add(toHost.MultiplyPoint3x4(points[best]));
                }
            }

            if (vertices.Count < 4)
            {
                return;
            }

            // Топология не важна: PhysX считает выпуклую оболочку по вершинам; индексы нужны меш-у.
            var indices = new List<int>();
            for (int i = 1; i + 1 < vertices.Count; i++)
            {
                indices.Add(0);
                indices.Add(i);
                indices.Add(i + 1);
            }

            AttachHull(host, vertices, indices, owner);
        }

        /// <summary>Выпуклое тело по готовому набору точек (плита порога — её 8 углов).</summary>
        private static void AddPointsHull(GameObject host, List<Vector3> points, OrionCollisionMeshes owner,
            Matrix4x4 toHost)
        {
            if (points.Count < 4)
            {
                return;
            }

            var vertices = new List<Vector3>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                vertices.Add(toHost.MultiplyPoint3x4(points[i]));
            }

            // Топология не важна: PhysX считает выпуклую оболочку по вершинам.
            var indices = new List<int>();
            for (int i = 1; i + 1 < vertices.Count; i++)
            {
                indices.Add(0);
                indices.Add(i);
                indices.Add(i + 1);
            }

            AttachHull(host, vertices, indices, owner);
        }

        /// <summary>
        /// Мебель — препятствие, не опора: одно выпуклое тело на предмет, на отдельном
        /// объекте с маркером PlayerObstacleOnly (маркер действует на все коллайдеры
        /// своего GameObject, а корпус и палуба на host остаются опорой). Скосы оболочки
        /// иначе работали бы как ступеньки, и игрок залезал на сиденье.
        /// </summary>
        private static void AddFurniture(Transform partRoot, string meshName, GameObject host,
            OrionCollisionMeshes owner, Matrix4x4 toHost)
        {
            MeshFilter filter = Find(partRoot, meshName);
            if (filter == null || !filter.sharedMesh.isReadable)
            {
                return;
            }

            var go = new GameObject("Col_" + meshName);
            go.transform.SetParent(host.transform, false); // нулевая локальная поза: оси те же, что у host
            go.AddComponent<PlayerObstacleOnly>();
            AddReducedHull(go, Points(filter), owner, toHost);
        }

        private static void MarkObstacleOnly(GameObject go)
        {
            if (go.GetComponent<PlayerObstacleOnly>() == null)
            {
                go.AddComponent<PlayerObstacleOnly>();
            }
        }

        private static void MarkIgnored(GameObject go)
        {
            if (go.GetComponent<PlayerIgnore>() == null)
            {
                go.AddComponent<PlayerIgnore>();
            }
        }

        /// <summary>Створка: один бокс по габаритам её меша (локальные оси двери).</summary>
        private static void AddDoorBox(MeshFilter door)
        {
            Bounds b = door.sharedMesh.bounds;
            Vector3 lossy = door.transform.lossyScale;
            var box = door.gameObject.AddComponent<BoxCollider>();
            box.center = b.center;

            // Запас DoorInset и минимум 0.05 м — в МЕТРАХ: у узлов FBX стократный
            // масштаб (меш в сантиметровых единицах, локальный размер ~0.02),
            // поэтому вычитать сантиметры из локального размера нельзя — бокс
            // получался 5-8 м и накрывал всю капсулу.
            box.size = new Vector3(
                Mathf.Max(0.05f, (Mathf.Abs(lossy.x) * b.size.x) - DoorInset) / Mathf.Max(1e-6f, Mathf.Abs(lossy.x)),
                Mathf.Max(0.05f, (Mathf.Abs(lossy.y) * b.size.y) - DoorInset) / Mathf.Max(1e-6f, Mathf.Abs(lossy.y)),
                Mathf.Max(0.05f, (Mathf.Abs(lossy.z) * b.size.z) - DoorInset) / Mathf.Max(1e-6f, Mathf.Abs(lossy.z)));
        }

        private static void AttachHull(GameObject host, List<Vector3> vertices, List<int> indices,
            OrionCollisionMeshes owner)
        {
            var mesh = new Mesh { name = "OrionHull" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            owner.Meshes.Add(mesh);

            var collider = host.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.sharedMesh = mesh;
        }

        private static int Weld(Dictionary<Vector3Int, int> map, List<Vector3> vertices, Vector3 p)
        {
            var key = new Vector3Int(
                Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f));
            if (!map.TryGetValue(key, out int id))
            {
                id = vertices.Count;
                map.Add(key, id);
                vertices.Add(p);
            }

            return id;
        }

        private static MeshFilter Find(Transform root, string name)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].name == name && filters[i].sharedMesh != null)
                {
                    return filters[i];
                }
            }

            return null;
        }

        /// <summary>Вершины меша в мировых метрах (мировые оси модели уже содержат масштаб FBX).</summary>
        private static List<Vector3> Points(MeshFilter filter)
        {
            Vector3[] vertices = filter.sharedMesh.vertices;
            Matrix4x4 m = filter.transform.localToWorldMatrix;
            var result = new List<Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                result.Add(m.MultiplyPoint3x4(vertices[i]));
            }

            return result;
        }

        private static Vector3 Mean(List<Vector3> points)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                sum += points[i];
            }

            return points.Count > 0 ? sum / points.Count : Vector3.zero;
        }
    }
}
