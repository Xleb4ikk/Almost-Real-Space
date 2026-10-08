// Зона контакта (вертикальный срез). Проверено в редакторе Unity 6000.6.0f1 (см. ContactZoneHost.cs).
// Строит тела Unity (PhysX) по PartDefinition. Координаты Unity: x = север, y = вверх, z = восток
// (поворот от (восток, север, вверх), без зеркалирования). Ось Y корабля совпадает с Up места.
// Сборка смещается вверх так, чтобы низ формы совпал с точкой корабля (точка в игре — низ аппарата).
// Цилиндр — замкнутая 12-гранная призма: MeshCollider convex требует треугольники, не облако вершин.
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Simulation.ContactZone
{
    public sealed class ContactZoneProxy : MonoBehaviour
    {
        public Vector3d CenterOfMassLocal { get; private set; }
        public Vector3d CenterOfMassVelocityLocal { get; private set; }

        private readonly List<Rigidbody> bodies = new List<Rigidbody>();
        private readonly List<Joint> joints = new List<Joint>();

        // Разрывы стыков: индексы ДОЧЕРНИХ деталей с разорванным стыком; маска
        // корневого острова — какие тела ещё принадлежат кораблю (остальные —
        // отломки, живут в сцене как физика PhysX до выхода из зоны).
        private PartDefinition[] partDefs;
        private readonly List<int> brokenChildren = new List<int>();
        private bool[] rootMask;

        /// <summary>Индексы деталей с разорванными стыками (по порядку разрывов).</summary>
        public IReadOnlyList<int> BrokenChildren => brokenChildren;

        public int BodyCount => bodies.Count;

        public Rigidbody BodyAt(int index)
        {
            return index >= 0 && index < bodies.Count ? bodies[index] : null;
        }

        /// <summary>Принадлежит ли тело корневому острову (до разрывов — всем).</summary>
        public bool IsRootBody(int index)
        {
            if (rootMask == null)
            {
                return index >= 0 && index < bodies.Count;
            }

            return index >= 0 && index < rootMask.Length && rootMask[index];
        }

        public void Build(IReadOnlyList<PartDefinition> parts, SiteFrame frame, Vector3d localPosition, Vector3d localVelocity)
        {
            Clear();
            if (parts == null || parts.Count == 0) return;

            partDefs = new PartDefinition[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                partDefs[i] = parts[i];
            }

            Vector3 origin = ToUnity(localPosition);
            Vector3 vel = ToUnity(localVelocity);

            // Точка корабля в игре — низ аппарата (касание детектится ею), а Offset деталей
            // задан от внутреннего начала сборки: T1 свисает на 5.5 м ниже. Смещаем всю
            // сборку так, чтобы самая низкая точка формы легла на точку корабля (локальный up=0).
            float lowest = 0f;
            bool hasLowest = false;
            for (int i = 0; i < parts.Count; i++)
            {
                float low = parts[i].Offset.y - PartHalfHeight(parts[i]);
                if (!hasLowest || low < lowest)
                {
                    lowest = low;
                    hasLowest = true;
                }
            }

            Vector3 lift = new Vector3(0f, hasLowest ? -lowest : 0f, 0f);

            for (int i = 0; i < parts.Count; i++)
            {
                PartDefinition d = parts[i];
                var go = new GameObject("Part_" + i + "_" + d.Name);
                go.transform.SetParent(transform, false);
                // Положение детали относительно точки корабля (ось Y корабля = вверх места).
                go.transform.localPosition = origin + d.Offset + lift;
                go.transform.localRotation = new Quaternion(d.Orientation.x, d.Orientation.y, d.Orientation.z, d.Orientation.w);

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = (float)d.MassKg;
                rb.linearVelocity = vel;
                AddShapeCollider(go, d);
                bodies.Add(rb);
            }

            // Стыки: каждая деталь с ParentIndex >= 0 крепится к родителю фиксированным
            // соединением. На детали висит приёмник OnJointBreak (PhysX шлёт сообщение
            // GameObject'у стыка), он же сообщает прокси индекс разорванной детали.
            for (int i = 0; i < parts.Count; i++)
            {
                PartDefinition d = parts[i];
                if (d.ParentIndex < 0 || d.ParentIndex >= bodies.Count) continue;
                var fj = bodies[i].gameObject.AddComponent<FixedJoint>();
                fj.connectedBody = bodies[d.ParentIndex];
                fj.breakForce = (float)d.JointStrengthNewtons;
                fj.breakTorque = (float)d.BreakTorqueNm;
                fj.enableCollision = false; // соседние детали одного корабля не сталкиваются
                joints.Add(fj);
                bodies[i].gameObject.AddComponent<ZoneJointBreakListener>().Bind(this, i);
            }

            Physics.defaultSolverIterations = 20;
            Physics.defaultSolverVelocityIterations = 4;
        }

        /// <summary>
        /// Стык разорван (OnJointBreak детали): деталь и её подграф становятся
        /// отломками — исключаются из центра масс и скорости корневого острова,
        /// но остаются в сцене как тела PhysX до выхода из зоны (Clear). Передача
        /// отломков в DebrisPool — фаза 7 (отложено, см. отчёт среза).
        /// </summary>
        public void OnPartJointBreak(int partIndex, float breakForce)
        {
            if (brokenChildren.Contains(partIndex))
            {
                return;
            }

            brokenChildren.Add(partIndex);
            RecomputeRootMask();
            Debug.Log("[ContactZone] стык разорван: деталь " + partIndex
                + " (сила " + breakForce.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " Н),"
                + " отломков-островов: " + (rootMask != null ? DetachedIslandCount() : 0)
                + ", корневых деталей: " + RootCount());
        }

        private void RecomputeRootMask()
        {
            if (partDefs == null)
            {
                rootMask = null;
                return;
            }

            ZoneIslands.SplitResult split = ZoneIslands.Split(partDefs, brokenChildren);
            rootMask = new bool[bodies.Count];
            for (int i = 0; i < split.Root.Length; i++)
            {
                int index = split.Root[i];
                if (index >= 0 && index < rootMask.Length)
                {
                    rootMask[index] = true;
                }
            }
        }

        private int RootCount()
        {
            int count = 0;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (IsRootBody(i))
                {
                    count++;
                }
            }

            return count;
        }

        private int DetachedIslandCount()
        {
            if (partDefs == null)
            {
                return 0;
            }

            return ZoneIslands.Split(partDefs, brokenChildren).Detached.Count;
        }

        public void Clear()
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] != null) Destroy(bodies[i].gameObject);
            }

            bodies.Clear();
            joints.Clear();
            partDefs = null;
            brokenChildren.Clear();
            rootMask = null;
        }

        public void SetFrameGravity(Vector3d localGravity)
        {
            // Гравитация кадра в осях Unity: (север, вверх, восток).
            Physics.gravity = ToUnity(localGravity);
        }

        // Центр масс и его скорость по текущим телам (координаты места).
        public void GetCenterOfMassState(SiteFrame frame, double t, out Vector3d position, out Vector3d velocity)
        {
            if (bodies.Count == 0)
            {
                position = CenterOfMassLocal;
                velocity = CenterOfMassVelocityLocal;
                return;
            }

            Vector3 c = Vector3.zero;
            Vector3 v = Vector3.zero;
            float m = 0f;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (!IsRootBody(i))
                {
                    continue; // отломки в центр масс корабля не входят
                }

                float mi = bodies[i].mass;
                c += bodies[i].worldCenterOfMass * mi;
                v += bodies[i].linearVelocity * mi;
                m += mi;
            }

            if (m <= 0f)
            {
                position = CenterOfMassLocal;
                velocity = CenterOfMassVelocityLocal;
                return;
            }

            c /= m;
            v /= m;
            CenterOfMassLocal = FromUnity(c);
            CenterOfMassVelocityLocal = FromUnity(v);
            position = CenterOfMassLocal;
            velocity = CenterOfMassVelocityLocal;
        }

        // Полувысота формы вдоль оси Y корабля (вверх): используется для посадки низа сборки
        // на точку корабля. Цилиндр/коробка — Dimensions.y/2, сфера — радиус, точка — 0.
        private static float PartHalfHeight(PartDefinition d)
        {
            switch (d.Shape)
            {
                case PartShape.Box:
                case PartShape.Cylinder:
                    return 0.5f * d.Dimensions.y;
                case PartShape.Sphere:
                    return d.Dimensions.x;
                default:
                    return 0f;
            }
        }

        private static void AddShapeCollider(GameObject go, PartDefinition d)
        {
            switch (d.Shape)
            {
                case PartShape.Box:
                    var box = go.AddComponent<BoxCollider>();
                    box.size = new Vector3(d.Dimensions.x, d.Dimensions.y, d.Dimensions.z);
                    break;
                case PartShape.Sphere:
                    var sphere = go.AddComponent<SphereCollider>();
                    sphere.radius = d.Dimensions.x;
                    break;
                case PartShape.Cylinder:
                    // Выпуклый меш-призма (12 граней): цилиндр PhysX не поддерживает напрямую.
                    var mc = go.AddComponent<MeshCollider>();
                    mc.convex = true;
                    mc.sharedMesh = CylinderPrism(d.Dimensions.x, d.Dimensions.y, 12);
                    break;
                default:
                    // Point: масса без геометрии — маленький коллайдер, чтобы тело стояло.
                    var tiny = go.AddComponent<SphereCollider>();
                    tiny.radius = 0.05f;
                    break;
            }
        }

        private static Mesh CylinderPrism(float radius, float height, int sides)
        {
            // ЗАМКНУТАЯ призма: боковые грани + крышки. MeshCollider (convex) требует
            // треугольники — облако вершин не готовится PhysX-кукером.
            var mesh = new Mesh();
            var verts = new Vector3[(2 * sides) + 2];
            for (int i = 0; i < sides; i++)
            {
                float a = 2f * Mathf.PI * i / sides;
                float x = radius * Mathf.Cos(a);
                float z = radius * Mathf.Sin(a);
                verts[i] = new Vector3(x, -0.5f * height, z);
                verts[sides + i] = new Vector3(x, 0.5f * height, z);
            }

            verts[2 * sides] = new Vector3(0f, -0.5f * height, 0f);
            verts[(2 * sides) + 1] = new Vector3(0f, 0.5f * height, 0f);

            var tris = new int[(sides * 2 * 3) + (sides * 2 * 3)];
            int k = 0;
            for (int i = 0; i < sides; i++)
            {
                int i2 = (i + 1) % sides;
                int b0 = i;
                int b1 = i2;
                int t0 = sides + i;
                int t1 = sides + i2;

                // Боковая грань (два треугольника, внешняя ориентация).
                tris[k++] = b0; tris[k++] = b1; tris[k++] = t1;
                tris[k++] = b0; tris[k++] = t1; tris[k++] = t0;

                int bc = 2 * sides;
                int tc = (2 * sides) + 1;
                tris[k++] = bc; tris[k++] = b1; tris[k++] = b0;
                tris[k++] = tc; tris[k++] = t0; tris[k++] = t1;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        // Локальные оси места (восток, север, вверх) → Unity (север, вверх, восток).
        private static Vector3 ToUnity(Vector3d v) => new Vector3((float)v.Y, (float)v.Z, (float)v.X);
        private static Vector3d FromUnity(Vector3 v) => new Vector3d(v.z, v.x, v.y);
    }
}
