using System.Collections.Generic;
using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Твёрдая постройка внутри места. Игрок двигается через SimulationRunner в
    /// double и Unity-физику не использует, поэтому выталкивание считается здесь,
    /// в render-пространстве рядом с якорем.
    ///
    /// UseMeshCollision = true: столкновение идёт по геометрии здания — по
    /// отдельному ящику на каждую часть LOD0 (все выступы, крылья, ниши). Бокс
    /// (Center/Size) остаётся грубым фильтром: вне его мешы даже не проверяются,
    /// поэтому он должен ЦЕЛИКОМ накрывать здание. Для каждой части на старте
    /// создаётся скрытый коллайдер далеко от сцены (слой Ignore Raycast, не
    /// двигается), а поза берётся из исходного трансформа — физика сцены и лучи
    /// взаимодействия его не видят, двигать статические коллайдеры каждый кадр
    /// не нужно.
    ///
    /// UseMeshCollision = false (или мешей не нашлось): старое поведение, сплошной бокс.
    /// </summary>
    public sealed class SiteBox : MonoBehaviour
    {
        /// <summary>Минимальная толщина ящика части, м (в локальных единицах меша).</summary>
        private const float MinSize = 0.01f;

        [Tooltip("Грубый габарит в локальных осях объекта, м. Центр.")]
        public Vector3 Center = new Vector3(0f, 114f, 9f);

        [Tooltip("Грубый габарит в локальных осях объекта, м. Размер: должен накрывать всё здание.")]
        public Vector3 Size = new Vector3(150f, 228f, 150f);

        [Tooltip("Столкновение по реальным мешам здания (LOD0), а не по сплошному боксу.")]
        public bool UseMeshCollision = true;

        [Tooltip("Корень здания с мешами. Пусто = родитель этого объекта.")]
        public Transform MeshRoot;

        [Tooltip("Части, в имени которых есть одна из этих подстрок, в коллизию не берутся (мелкий декор).")]
        public string[] SkipNameContains = new string[0];

        private sealed class Part
        {
            public Transform Source;
            public Collider Collider;
            public Vector3 LocalCenter;
            public Vector3 LocalExtents;
        }

        private readonly List<Part> parts = new List<Part>();

        public bool HasMesh => parts.Count > 0;

        private void OnEnable()
        {
            SiteBoxRegistry.Register(this);
            if (Application.isPlaying && UseMeshCollision)
            {
                BuildParts();
            }
        }

        private void OnDisable()
        {
            SiteBoxRegistry.Unregister(this);
            DestroyParts();
        }

        private void BuildParts()
        {
            DestroyParts();
            Transform root = MeshRoot != null ? MeshRoot : transform.parent;
            if (root == null)
            {
                return;
            }

            var filters = new List<MeshFilter>();
            LODGroup lodGroup = root.GetComponentInChildren<LODGroup>(true);
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length > 0)
                {
                    foreach (Renderer r in lods[0].renderers)
                    {
                        MeshFilter f = r != null ? r.GetComponent<MeshFilter>() : null;
                        if (f != null)
                        {
                            filters.Add(f);
                        }
                    }
                }
            }

            if (filters.Count == 0)
            {
                root.GetComponentsInChildren(true, filters);
            }

            for (int i = 0; i < filters.Count; i++)
            {
                MeshFilter f = filters[i];
                Mesh mesh = f.sharedMesh;
                if (mesh == null || IsSkipped(f.name))
                {
                    continue;
                }

#if !UNITY_EDITOR
                if (!mesh.isReadable)
                {
                    Debug.LogWarning("[SiteBox] Меш '" + mesh.name + "' не читаем: включи Read/Write в импорте модели, иначе геометрия части посчитается по нулям.");
                }
#endif
                var go = new GameObject("SitePartCollider") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                go.transform.position = new Vector3(0f, -100000f, 0f);
                go.transform.localScale = f.transform.lossyScale;

                // Коллайдер — ЯЩИК по границам меша, а не MeshCollider.
                // Physics.ComputePenetration по невыпуклому MeshCollider, созданному
                // в рантайме, не возвращает пересечение ВООБЩЕ (проверено на
                // одинаковом меше: convex=true → dist 1.11, convex=false → false),
                // а у LOD0_VAB четыре части с 288…1056 треугольниками, где convex
                // запрещён лимитом Unity в 255. Примитив работает всегда, масштаб
                // трансформа учитывается (габарит 1.6 × 100 = 160 м), и для
                // box-built модели габарит части и есть её настоящая форма: каждая
                // часть приходит с КРЫЛЬЯМИ и нишами, поэтому общая форма здания
                // склеена из отдельных коробок, а не из одной.
                BoxCollider boxCollider = go.AddComponent<BoxCollider>();
                boxCollider.center = mesh.bounds.center;
                boxCollider.size = new Vector3(
                    Mathf.Max(mesh.bounds.size.x, MinSize),
                    Mathf.Max(mesh.bounds.size.y, MinSize),
                    Mathf.Max(mesh.bounds.size.z, MinSize));

                parts.Add(new Part
                {
                    Source = f.transform,
                    Collider = boxCollider,
                    LocalCenter = mesh.bounds.center,
                    LocalExtents = mesh.bounds.extents
                });
            }
        }

        private bool IsSkipped(string partName)
        {
            if (SkipNameContains == null)
            {
                return false;
            }

            for (int i = 0; i < SkipNameContains.Length; i++)
            {
                if (!string.IsNullOrEmpty(SkipNameContains[i]) && partName.Contains(SkipNameContains[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void DestroyParts()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Collider != null)
                {
                    Destroy(parts[i].Collider.gameObject);
                }
            }

            parts.Clear();
        }

        /// <summary>
        /// Вытолкнуть капсулу игрока (ноги в feet, ось вдоль up) из мешей здания.
        /// Только по горизонтали: пол/крыша (нормаль почти вертикальна) не толкают,
        /// высоту ведёт проекция на рельеф. Несколько итераций — для углов.
        /// </summary>
        public bool PushOutMesh(Vector3 feet, Vector3 up, float radius, out Vector3 push)
        {
            push = Vector3.zero;
            CapsuleCollider cap = SiteBoxRegistry.Probe(radius);
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, up);
            float half = SiteBoxRegistry.PlayerHeight * 0.5f;
            float reach = half + radius + 0.1f;
            Vector3 pos = feet;

            for (int iter = 0; iter < 4; iter++)
            {
                bool moved = false;
                Vector3 mid = pos + (up * half);

                for (int i = 0; i < parts.Count; i++)
                {
                    Part part = parts[i];
                    if (part.Source == null || part.Collider == null)
                    {
                        continue;
                    }

                    // Мировой AABB части из локальных границ меша (дёшево, без физики сцены).
                    Matrix4x4 m = part.Source.localToWorldMatrix;
                    Vector3 c = m.MultiplyPoint3x4(part.LocalCenter);
                    Vector3 e = part.LocalExtents;
                    float ex = (Mathf.Abs(m.m00) * e.x) + (Mathf.Abs(m.m01) * e.y) + (Mathf.Abs(m.m02) * e.z);
                    float ey = (Mathf.Abs(m.m10) * e.x) + (Mathf.Abs(m.m11) * e.y) + (Mathf.Abs(m.m12) * e.z);
                    float ez = (Mathf.Abs(m.m20) * e.x) + (Mathf.Abs(m.m21) * e.y) + (Mathf.Abs(m.m22) * e.z);
                    Vector3 d = mid - c;
                    if (Mathf.Abs(d.x) > ex + reach || Mathf.Abs(d.y) > ey + reach || Mathf.Abs(d.z) > ez + reach)
                    {
                        continue;
                    }

                    if (!Physics.ComputePenetration(
                            cap, pos, rot,
                            part.Collider, part.Source.position, part.Source.rotation,
                            out Vector3 dir, out float dist))
                    {
                        continue;
                    }

                    dir -= up * Vector3.Dot(dir, up);
                    float len = dir.magnitude;
                    if (len < 0.3f)
                    {
                        // Ближайший выход — пол или крыша: вертикаль толкать нельзя,
                        // высоту ведёт рельеф. Но оставлять игрока ВНУТРИ стены тоже
                        // нельзя, а именно это происходит, когда он уже внутри: там
                        // ближайшая поверхность — дно ящика, вертикальный вектор
                        // обнуляется, и без этого ветки игрок просто уходит насквозь.
                        // Поэтому выталкиваем горизонтально, к ближайшей БОКОВОЙ
                        // грани той же части. depthY > 0 обязателен: он отсекает
                        // случай «стоит сверху платформы/пандуса», где по горизонтали
                        // игрок внутри следа части, но вертикали он не касается.
                        Vector3 side = part.LocalExtents;
                        Vector3 s = part.Source.lossyScale;
                        Vector3 lp = part.Source.InverseTransformPoint(mid);
                        float depthX = (side.x + (radius / s.x)) - Mathf.Abs(lp.x);
                        float depthZ = (side.z + (radius / s.z)) - Mathf.Abs(lp.z);
                        float depthY = (side.y + (radius / s.y)) - Mathf.Abs(lp.y);
                        if (depthX > 0f && depthZ > 0f && depthY > 0f)
                        {
                            Vector3 sideLocal = depthX < depthZ
                                ? new Vector3(Mathf.Sign(lp.x) * depthX, 0f, 0f)
                                : new Vector3(0f, 0f, Mathf.Sign(lp.z) * depthZ);
                            pos += part.Source.TransformVector(sideLocal);
                            moved = true;
                        }

                        continue;
                    }

                    pos += (dir / len) * ((dist / len) + 0.005f);
                    moved = true;
                }

                if (!moved)
                {
                    break;
                }
            }

            push = pos - feet;
            return push.sqrMagnitude > 1e-10f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(Center, Size);
        }
    }

    /// <summary>
    /// Реестр построек и выталкивание из них игрока.
    ///
    /// Позиция приходит и уходит в astro-координатах (как PlayerPosition симулятора),
    /// а постройки живут в render-пространстве рядом с игроком — пересчёт идёт
    /// через FloatingOrigin: наружу отдаётся СМЕЩЕНИЕ, а не точка.
    /// Игрок по высоте перекрывается с постройкой только между ногами и головой,
    /// поэтому на крышу можно запрыгнуть сбоку, а сквозь стену пройти нельзя.
    /// </summary>
    public static class SiteBoxRegistry
    {
        internal const float PlayerHeight = 1.8f;
        private static readonly List<SiteBox> boxes = new List<SiteBox>();
        private static CapsuleCollider probe;

        public static int Count => boxes.Count;

        public static void Register(SiteBox b)
        {
            if (!boxes.Contains(b))
            {
                boxes.Add(b);
            }
        }

        public static void Unregister(SiteBox b)
        {
            boxes.Remove(b);
        }

        /// <summary>Капсула-зонд игрока для ComputePenetration (поза передаётся явно, сама стоит далеко).</summary>
        internal static CapsuleCollider Probe(float radius)
        {
            if (probe == null)
            {
                var go = new GameObject("SiteProbe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                go.transform.position = new Vector3(50000f, -100000f, 0f);
                probe = go.AddComponent<CapsuleCollider>();
                probe.direction = 1;
                probe.height = PlayerHeight;
                probe.center = new Vector3(0f, PlayerHeight * 0.5f, 0f);
            }

            if (!Mathf.Approximately(probe.radius, radius))
            {
                probe.radius = radius;
            }

            return probe;
        }

        public static bool TryResolve(Vector3d position, double playerRadius, out Vector3d resolved)
        {
            resolved = position;
            if (boxes.Count == 0 || !Application.isPlaying)
            {
                return false;
            }

            Vector3 p = FloatingOrigin.ToRender(position);
            Vector3 total = Vector3.zero;
            float r = (float)playerRadius;

            for (int i = 0; i < boxes.Count; i++)
            {
                SiteBox b = boxes[i];
                if (b == null || !b.isActiveAndEnabled)
                {
                    continue;
                }

                Transform t = b.transform;
                Vector3 c = t.InverseTransformPoint(p + total) - b.Center;
                Vector3 h = b.Size * 0.5f;
                if (c.y < -h.y - PlayerHeight || c.y > h.y)
                {
                    continue;
                }

                if (b.HasMesh)
                {
                    // Бокс — грубый фильтр, точное выталкивание — по мешам.
                    if (Mathf.Abs(c.x) > h.x + r + 1f || Mathf.Abs(c.z) > h.z + r + 1f)
                    {
                        continue;
                    }

                    if (b.PushOutMesh(p + total, t.up, r, out Vector3 meshPush))
                    {
                        total += meshPush;
                    }

                    continue;
                }

                float px = (h.x + r) - Mathf.Abs(c.x);
                float pz = (h.z + r) - Mathf.Abs(c.z);
                if (px <= 0f || pz <= 0f)
                {
                    continue;
                }

                Vector3 pushLocal = px < pz
                    ? new Vector3(Mathf.Sign(c.x) * px, 0f, 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(c.z) * pz);
                total += t.TransformVector(pushLocal);
            }

            if (total.sqrMagnitude <= 0f)
            {
                return false;
            }

            resolved = position + AstroFrame.ToAstro(total);
            return true;
        }
    }
}