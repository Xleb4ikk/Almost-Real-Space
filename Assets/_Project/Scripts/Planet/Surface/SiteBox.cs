using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Твёрдая постройка внутри места. Игрок двигается через SimulationRunner в
    /// double и Unity-физику не использует, поэтому выталкивание считается здесь,
    /// в render-пространстве рядом с якорем.
    ///
    /// UseMeshCollision = true: коллизия идёт по запечённым данным части.
    /// Выпуклая часть получает ОДИН convex MeshCollider из собственного меша —
    /// точная форма, включая скосы рампы, без лесенки из боксов. Вогнутая часть
    /// получает набор небольших боксов (вокселизация поверхности). Авторские
    /// коллайдеры (convex MeshCollider или BoxCollider) на части или на самом
    /// SiteBox имеют приоритет над автозапеканием.
    ///
    /// Center/Size остаётся грубым фильтром. Если запекания нет, используется
    /// один старый габаритный бокс на часть.
    ///
    /// ВАЖНО: невыпуклые (non-convex) MeshCollider нигде не используются —
    /// Physics.ComputePenetration, на котором держится пушаут, с ними молча
    /// возвращает отсутствие пересечения (проверено тестом).
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

        [Tooltip("Размер ячейки запекания в метрах. Меньше = точнее, но больше боксов.")]
        [Min(0.1f)]
        public float BakeVoxelSizeMeters = 1f;

        [System.Serializable]
        public struct BakedBoxData
        {
            public Vector3 Center;
            public Vector3 Size;
        }

        [System.Serializable]
        public sealed class BakedPartData
        {
            public MeshFilter Source;

            /// <summary>
            /// true = часть выпуклая, сварится в один convex MeshCollider по
            /// собственному мешу (Boxes пустые). false = вогнутая, коллизия по Boxes.
            /// </summary>
            public bool UseConvexMesh;

            public List<BakedBoxData> Boxes = new List<BakedBoxData>();
        }

        [HideInInspector]
        public List<BakedPartData> BakedParts = new List<BakedPartData>();

        /// <summary>Есть ли запечённые данные (боксы или флаги convex hull).</summary>
        public bool HasBakedParts => BakedParts != null && BakedParts.Count > 0;

        private sealed class Part
        {
            public Transform Source;
            public Collider Collider;
            public Vector3 LocalCenter;
            public Vector3 LocalExtents;
        }

        private readonly List<Part> parts = new List<Part>();
        private readonly List<GameObject> colliderObjects = new List<GameObject>();

        public bool HasMesh => parts.Count > 0;
        public int BakedBoxCount
        {
            get
            {
                int count = 0;
                if (BakedParts != null)
                {
                    for (int i = 0; i < BakedParts.Count; i++)
                    {
                        if (BakedParts[i] != null && BakedParts[i].Boxes != null)
                        {
                            count += BakedParts[i].Boxes.Count;
                        }
                    }
                }

                return count;
            }
        }

        private void OnEnable()
        {
            SiteBoxRegistry.Register(this);
            if (Application.isPlaying && UseMeshCollision)
            {
                BuildParts();
            }
        }

        /// <summary>
        /// Собрать коллайдеры, если ещё не собраны (страховка от порядка
        /// жизненного цикла: OnEnable мог отработать до готовности сцены).
        /// Вызывается из опоры игрока перед запросами.
        /// </summary>
        public void EnsureBuilt()
        {
            if (Application.isPlaying && UseMeshCollision && parts.Count == 0)
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

            // Авторские коллайдеры прямо на этом объекте (Blender-воркфлоу):
            // заменяют автозапекание целиком.
            if (TryBuildAuthoredSiteColliders())
            {
                return;
            }

            if (HasBakedParts)
            {
                BuildBakedParts();
                if (parts.Count > 0)
                {
                    return;
                }
            }

            List<MeshFilter> filters = CollectPartFilters(root);
            for (int i = 0; i < filters.Count; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || IsSkipped(filter.name))
                {
                    continue;
                }

                if (TryAuthoredPartCollider(filter, out Part authored))
                {
                    parts.Add(authored);
                    continue;
                }

                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                GameObject holder = CreateColliderHolder(filter.transform);
                BoxCollider boxCollider = holder.AddComponent<BoxCollider>();
                boxCollider.center = mesh.bounds.center;
                boxCollider.size = SafeSize(mesh.bounds.size);
                parts.Add(new Part
                {
                    Source = filter.transform,
                    Collider = boxCollider,
                    LocalCenter = mesh.bounds.center,
                    LocalExtents = boxCollider.size * 0.5f
                });
            }
        }

        /// <summary>
        /// Авторские MeshCollider/BoxCollider на самом SiteBox. Convex MeshCollider
        /// поддерживается, невыпуклый — игнорируется с предупреждением (см. шапку).
        /// </summary>
        private bool TryBuildAuthoredSiteColliders()
        {
            bool found = false;
            Collider[] authored = GetComponents<Collider>();
            for (int i = 0; i < authored.Length; i++)
            {
                Collider c = authored[i];
                if (c is MeshCollider meshCollider)
                {
                    if (meshCollider.sharedMesh == null)
                    {
                        continue;
                    }

                    if (!meshCollider.convex)
                    {
                        Debug.LogWarning("[SiteBox] Невыпуклый MeshCollider на '" + name +
                            "' игнорируется: ComputePenetration не работает с non-convex мешами. " +
                            "Сделайте его convex или уберите — автозапекание продолжит работу.", this);
                        continue;
                    }

                    parts.Add(new Part
                    {
                        Source = transform,
                        Collider = meshCollider,
                        LocalCenter = meshCollider.sharedMesh.bounds.center,
                        LocalExtents = meshCollider.sharedMesh.bounds.extents
                    });
                    found = true;
                }
                else if (c is BoxCollider boxCollider)
                {
                    parts.Add(new Part
                    {
                        Source = transform,
                        Collider = boxCollider,
                        LocalCenter = boxCollider.center,
                        LocalExtents = boxCollider.size * 0.5f
                    });
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Авторский коллайдер на самой части (повернутый бокс для скоса,
        /// convex MeshCollider из упрощённого меша). Без холдера — коллайдер
        /// уже лежит на нужном трансформе.
        /// </summary>
        private static bool TryAuthoredPartCollider(MeshFilter filter, out Part part)
        {
            part = null;
            MeshCollider meshCollider = filter.GetComponent<MeshCollider>();
            if (meshCollider != null && meshCollider.sharedMesh != null)
            {
                if (!meshCollider.convex)
                {
                    Debug.LogWarning("[SiteBox] Невыпуклый MeshCollider на части '" + filter.name +
                        "' игнорируется (ComputePenetration требует convex).");
                    return false;
                }

                part = new Part
                {
                    Source = filter.transform,
                    Collider = meshCollider,
                    LocalCenter = meshCollider.sharedMesh.bounds.center,
                    LocalExtents = meshCollider.sharedMesh.bounds.extents
                };
                return true;
            }

            BoxCollider box = filter.GetComponent<BoxCollider>();
            if (box != null)
            {
                part = new Part
                {
                    Source = filter.transform,
                    Collider = box,
                    LocalCenter = box.center,
                    LocalExtents = box.size * 0.5f
                };
                return true;
            }

            return false;
        }

        private void BuildBakedParts()
        {
            for (int i = 0; i < BakedParts.Count; i++)
            {
                BakedPartData baked = BakedParts[i];
                MeshFilter source = baked != null ? baked.Source : null;
                if (source == null || IsSkipped(source.name))
                {
                    continue;
                }

                if (TryAuthoredPartCollider(source, out Part authored))
                {
                    parts.Add(authored);
                    continue;
                }

                if (baked.UseConvexMesh)
                {
                    Mesh mesh = source.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    GameObject holder = CreateColliderHolder(source.transform);
                    MeshCollider hull = holder.AddComponent<MeshCollider>();
                    hull.convex = true;
                    hull.sharedMesh = mesh;
                    parts.Add(new Part
                    {
                        Source = source.transform,
                        Collider = hull,
                        LocalCenter = mesh.bounds.center,
                        LocalExtents = mesh.bounds.extents
                    });
                    continue;
                }

                if (baked.Boxes == null)
                {
                    continue;
                }

                GameObject boxHolder = CreateColliderHolder(source.transform);
                for (int j = 0; j < baked.Boxes.Count; j++)
                {
                    BakedBoxData data = baked.Boxes[j];
                    BoxCollider box = boxHolder.AddComponent<BoxCollider>();
                    box.center = data.Center;
                    box.size = SafeSize(data.Size);
                    parts.Add(new Part
                    {
                        Source = source.transform,
                        Collider = box,
                        LocalCenter = data.Center,
                        LocalExtents = box.size * 0.5f
                    });
                }
            }
        }

        private GameObject CreateColliderHolder(Transform source)
        {
            var holder = new GameObject("SitePartColliders")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = 2
            };
            // Поза холдера = поза исходной части: холдер — РЕБЁНОК части с нулевым
            // локальным трансформом. Так коллайдеры точны при любом порядке
            // обновления места (SurfaceGrounded/Refresh двигают содержимое после
            // OnEnable — привязка к SiteBox давала «уехавший» коллайдер).
            // SiteBox находим по SiteBoxPart.Owner (SiteBox лежит рядом, не в предках).
            holder.transform.SetParent(source, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one;
            holder.AddComponent<SiteBoxPart>().Owner = this;
            colliderObjects.Add(holder);
            return holder;
        }

        private static Vector3 SafeSize(Vector3 size)
        {
            return new Vector3(
                Mathf.Max(size.x, MinSize),
                Mathf.Max(size.y, MinSize),
                Mathf.Max(size.z, MinSize));
        }

        /// <summary>Та же выборка частей LOD0 используется при запекании и в рантайме.</summary>
        public static List<MeshFilter> CollectPartFilters(Transform root)
        {
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

            return filters;
        }

        public bool IsSkipped(string partName)
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
            for (int i = 0; i < colliderObjects.Count; i++)
            {
                if (colliderObjects[i] != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(colliderObjects[i]);
                    }
                    else
                    {
                        DestroyImmediate(colliderObjects[i]);
                    }
                }
            }

            colliderObjects.Clear();
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
                        Vector3 scale = part.Source.lossyScale;
                        scale = new Vector3(Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                            Mathf.Max(Mathf.Abs(scale.y), 0.0001f), Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
                        Vector3 lp = part.Source.InverseTransformPoint(mid) - part.LocalCenter;
                        float depthX = (side.x + (radius / scale.x)) - Mathf.Abs(lp.x);
                        float depthZ = (side.z + (radius / scale.z)) - Mathf.Abs(lp.z);
                        float depthY = (side.y + (radius / scale.y)) - Mathf.Abs(lp.y);
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

            if (BakedParts == null)
            {
                return;
            }

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.color = new Color(1f, 0.55f, 0.05f, 1f);
            for (int i = 0; i < BakedParts.Count; i++)
            {
                BakedPartData part = BakedParts[i];
                if (part == null || part.Source == null)
                {
                    continue;
                }

                if (part.UseConvexMesh)
                {
                    Mesh mesh = part.Source.sharedMesh;
                    if (mesh != null)
                    {
                        Transform st = part.Source.transform;
                        Gizmos.DrawWireMesh(mesh, st.position, st.rotation, st.lossyScale);
                    }

                    continue;
                }

                if (part.Boxes == null)
                {
                    continue;
                }

                Gizmos.matrix = part.Source.transform.localToWorldMatrix;
                for (int j = 0; j < part.Boxes.Count; j++)
                {
                    Gizmos.DrawWireCube(part.Boxes[j].Center, part.Boxes[j].Size);
                }
            }

            Gizmos.matrix = previousMatrix;
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

        /// <summary>Собрать коллайдеры всех зарегистрированных построек (страховка).</summary>
        public static void EnsureAllBuilt()
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i] != null)
                {
                    boxes[i].EnsureBuilt();
                }
            }
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

            Profiler.BeginSample("SiteBoxRegistry.TryResolve");
            try
            {
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
            finally
            {
                Profiler.EndSample();
            }
        }
    }
}
