// ЧЕРНОВИК (вертикальный срез). НЕ ПОДКЛЮЧЁН И НЕ КОМПИЛИРОВАЛСЯ — см. ContactZoneHost.cs.
// Строит тела Unity (PhysX) по PartDefinition. Координаты Unity: x = север, y = вверх, z = восток
// (поворот от (восток, север, вверх), без зеркалирования). Ось Y корабля совпадает с Up места.
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

        public void Build(IReadOnlyList<PartDefinition> parts, SiteFrame frame, Vector3d localPosition, Vector3d localVelocity)
        {
            Clear();
            if (parts == null || parts.Count == 0) return;

            Vector3 origin = ToUnity(localPosition);
            Vector3 vel = ToUnity(localVelocity);
            for (int i = 0; i < parts.Count; i++)
            {
                PartDefinition d = parts[i];
                var go = new GameObject("Part_" + i + "_" + d.Name);
                go.transform.SetParent(transform, false);
                // Положение детали относительно центра корабля (ось Y корабля = вверх места).
                go.transform.localPosition = origin + d.Offset;
                go.transform.localRotation = new Quaternion(d.Orientation.x, d.Orientation.y, d.Orientation.z, d.Orientation.w);

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = (float)d.MassKg;
                rb.linearVelocity = vel;
                AddShapeCollider(go, d);
                bodies.Add(rb);
            }

            // Стыки: каждая деталь с ParentIndex >= 0 крепится к родителю фиксированным соединением.
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
            }

            Physics.defaultSolverIterations = 20;
            Physics.defaultSolverVelocityIterations = 4;
        }

        public void Clear()
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] != null) Destroy(bodies[i].gameObject);
            }

            bodies.Clear();
            joints.Clear();
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
                float mi = bodies[i].mass;
                c += bodies[i].worldCenterOfMass * mi;
                v += bodies[i].linearVelocity * mi;
                m += mi;
            }

            c /= m;
            v /= m;
            CenterOfMassLocal = FromUnity(c);
            CenterOfMassVelocityLocal = FromUnity(v);
            position = CenterOfMassLocal;
            velocity = CenterOfMassVelocityLocal;
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
            var mesh = new Mesh();
            var verts = new Vector3[2 * sides];
            for (int i = 0; i < sides; i++)
            {
                float a = 2f * Mathf.PI * i / sides;
                float x = radius * Mathf.Cos(a);
                float z = radius * Mathf.Sin(a);
                verts[i] = new Vector3(x, -0.5f * height, z);
                verts[sides + i] = new Vector3(x, 0.5f * height, z);
            }

            mesh.vertices = verts;
            // Выпуклая оболочка сама строится из вершин; индексы для отрисовки не нужны.
            mesh.RecalculateBounds();
            return mesh;
        }

        // Локальные оси места (восток, север, вверх) → Unity (север, вверх, восток).
        private static Vector3 ToUnity(Vector3d v) => new Vector3((float)v.Y, (float)v.Z, (float)v.X);
        private static Vector3d FromUnity(Vector3 v) => new Vector3d(v.z, v.x, v.y);
    }
}
