using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Procedural Orion capsule monitor: the same body with a VESA plate and a screen
    /// quad with UV 0..1 as in the Blender scene (Monitor_*), but assembled in Unity.
    /// The main monitors already exist as FBX meshes (Orion_CrewModule.fbx,
    /// Monitor_Pilot / Monitor_Operator_1/2, Tablet_Screen); this component is for
    /// extra monitors on Mount_* pads and for cases where the screen must be drawn
    /// yourself (SetContent).
    ///
    /// Usage:
    ///  1. Empty GameObject + this component (MeshFilter/MeshRenderer are added).
    ///  2. Screen size, screen material (screenTemplate) and body material.
    ///  3. Component context menu → "Snap To Nearest Mount_*": the monitor snaps to
    ///     the nearest pad in the capsule model (Mount_Monitor_*, Mount_Tablet_Pilot),
    ///     determining the axis facing the cabin itself.
    ///  4. SetContent(renderTexture): each monitor has its own screen material instance,
    ///     so different monitors can show different things.
    ///  5. TryGetScreenUV(ray, out uv): a ray (mouse, controller, finger) to screen
    ///     coordinates 0..1 (bottom-left) — for UI drawn into the RenderTexture.
    ///
    /// Geometry: the origin is the back plane (against the wall), the front faces -Z
    /// (like a Quad and World Space Canvas). The screen is a separate submesh
    /// (material #1) with strictly 0..1 UVs.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralMonitor : MonoBehaviour
    {
        [Header("Размеры, метры")]
        public float screenWidth = 0.64f;
        public float screenHeight = 0.36f;
        public float bezel = 0.018f;
        public float bodyDepth = 0.030f;
        public float standoff = 0.020f;

        [Header("Материалы")]
        public Material bodyMaterial;
        public Material screenTemplate;
        public Color emissionTint = Color.white;
        [Range(0f, 4f)]
        public float emissionIntensity = 1f;

        [Header("Содержимое экрана")]
        public Texture content;

        [Header("Интерактив")]
        public bool addBoxCollider = true;

        [Header("Привязка к Mount_*")]
        public Transform cabinCenter;
        public bool overrideMountNormal;
        public Vector3 mountNormalLocal = Vector3.up;

        private Mesh mesh;
        private Material screenInstance;
        private Material defaultBody;
        private bool dirty = true;

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            dirty = true;
        }

        private void Update()
        {
            if (dirty)
            {
                Rebuild();
            }
        }

        private void OnDestroy()
        {
            DestroyObject(mesh);
            DestroyObject(screenInstance);
            DestroyObject(defaultBody);
        }

        private static void DestroyObject(Object o)
        {
            if (o == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(o);
            }
            else
            {
                DestroyImmediate(o);
            }
        }

        /// <summary>Rebuild the mesh and materials.</summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            dirty = false;
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();

            if (mesh == null)
            {
                mesh = new Mesh { name = "ProceduralMonitor" };
                mesh.hideFlags = HideFlags.DontSave;
            }

            mesh.Clear();

            float hx = (screenWidth * 0.5f) + bezel;
            float hy = (screenHeight * 0.5f) + bezel;
            float zBack = -standoff;
            float zFront = -(standoff + bodyDepth);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var body = new List<int>();
            var screen = new List<int>();

            AddBox(vertices, normals, uv, body, new Vector3(-0.06f, -0.06f, -standoff), new Vector3(0.06f, 0.06f, 0f));
            AddBox(vertices, normals, uv, body, new Vector3(-hx, -hy, zFront), new Vector3(hx, hy, zBack));

            float zs = zFront - 0.0006f;
            float hw = screenWidth * 0.5f;
            float hh = screenHeight * 0.5f;
            AddQuad(vertices, normals, uv, screen,
                new Vector3(-hw, -hh, zs), new Vector3(hw, -hh, zs), new Vector3(hw, hh, zs), new Vector3(-hw, hh, zs),
                new Vector3(0f, 0f, -1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(body, 0);
            mesh.SetTriangles(screen, 1);
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;

            // Own screen material instance per monitor.
            if (screenTemplate != null
                && (screenInstance == null || screenInstance.shader != screenTemplate.shader))
            {
                DestroyObject(screenInstance);
                screenInstance = new Material(screenTemplate) { name = name + "_Screen" };
                screenInstance.hideFlags = HideFlags.DontSave;
            }

            ApplyContent();

            Material bodyMat = bodyMaterial;
            if (bodyMat == null)
            {
                if (defaultBody == null)
                {
                    Shader shader = Shader.Find("HDRP/Lit");
                    if (shader == null)
                    {
                        shader = Shader.Find("Universal Render Pipeline/Lit");
                    }

                    if (shader == null)
                    {
                        shader = Shader.Find("Standard");
                    }

                    defaultBody = new Material(shader) { hideFlags = HideFlags.DontSave };
                    if (defaultBody.HasProperty("_BaseColor"))
                    {
                        defaultBody.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.035f));
                    }

                    if (defaultBody.HasProperty("_Color"))
                    {
                        defaultBody.color = new Color(0.03f, 0.03f, 0.035f);
                    }
                }

                bodyMat = defaultBody;
            }

            Material screenMat = screenInstance != null ? screenInstance : screenTemplate;
            renderer.sharedMaterials = new Material[] { bodyMat, screenMat };

            var box = GetComponent<BoxCollider>();
            if (addBoxCollider)
            {
                if (box == null)
                {
                    box = gameObject.AddComponent<BoxCollider>();
                }

                box.center = new Vector3(0f, 0f, (zBack + zFront) * 0.5f);
                box.size = new Vector3(2f * hx, 2f * hy, standoff + bodyDepth);
            }
            else if (box != null)
            {
                DestroyObject(box);
            }
        }

        // ── Screen content ───────────────────────────────────────────────────

        /// <summary>Set the picture (a RenderTexture with UI, or a texture).</summary>
        public void SetContent(Texture t)
        {
            content = t;
            ApplyContent();
        }

        private void ApplyContent()
        {
            Material m = screenInstance;
            if (m == null)
            {
                return;
            }

            if (content != null)
            {
                if (m.HasProperty("_UnlitColorMap"))
                {
                    m.SetTexture("_UnlitColorMap", content);      // HDRP/Unlit (our screen)
                }

                if (m.HasProperty("_BaseColorMap"))
                {
                    m.SetTexture("_BaseColorMap", content);       // HDRP/Lit
                }

                if (m.HasProperty("_BaseMap"))
                {
                    m.SetTexture("_BaseMap", content);            // URP
                }

                if (m.HasProperty("_MainTex"))
                {
                    m.SetTexture("_MainTex", content);            // Built-in
                }

                if (m.HasProperty("_EmissiveColorMap"))
                {
                    m.EnableKeyword("_EMISSIVE_COLOR_MAP");
                    m.SetTexture("_EmissiveColorMap", content);
                    m.EnableKeyword("_EMISSION");
                }
            }

            if (m.HasProperty("_EmissiveColor"))
            {
                m.SetColor("_EmissiveColor", emissionTint * emissionIntensity);
            }

            if (m.HasProperty("_UnlitColor"))
            {
                m.SetColor("_UnlitColor", Color.white);
            }
        }

        // ── Interaction ──────────────────────────────────────────────────────

        public Vector3 ScreenCenterWorld
        {
            get { return transform.TransformPoint(new Vector3(0f, 0f, -(standoff + bodyDepth))); }
        }

        /// <summary>Normal of the screen face.</summary>
        public Vector3 ScreenNormalWorld
        {
            get { return -transform.forward; }
        }

        /// <summary>Ray → screen coordinates 0..1 (bottom-left). true if the ray hit the screen.</summary>
        public bool TryGetScreenUV(Ray ray, out Vector2 uv)
        {
            uv = Vector2.zero;
            var plane = new Plane(ScreenNormalWorld, ScreenCenterWorld);
            float distance;
            if (!plane.Raycast(ray, out distance))
            {
                return false;
            }

            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(distance));
            uv = new Vector2((local.x / screenWidth) + 0.5f, (local.y / screenHeight) + 0.5f);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        /// <summary>UV 0..1 → RenderTexture pixels.</summary>
        public Vector2 UVToPixel(Vector2 uv, int texWidth, int texHeight)
        {
            return new Vector2(uv.x * texWidth, uv.y * texHeight);
        }

        // ── Snapping to Mount_* pads ─────────────────────────────────────────

        /// <summary>Snap the monitor to the nearest Mount_* pad (including Mount_Tablet_Pilot).</summary>
        [ContextMenu("Snap To Nearest Mount_*")]
        public void SnapToNearestMount()
        {
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (!t.name.StartsWith("Mount_"))
                {
                    continue;
                }

                float d = (t.position - transform.position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = t;
                }
            }

            if (best != null)
            {
                SnapTo(best);
            }
            else
            {
                Debug.LogWarning("ProceduralMonitor: объекты Mount_* в сцене не найдены");
            }
        }

        /// <summary>Align to a pad: back plane on the pad, screen facing the cabin.</summary>
        public void SnapTo(Transform mount)
        {
            Vector3 center = cabinCenter != null ? cabinCenter.position : mount.root.position;
            Vector3 normal;
            if (overrideMountNormal)
            {
                normal = mount.TransformDirection(mountNormalLocal).normalized;
            }
            else
            {
                // Blender's empty axes may be permuted: the pad normal is whichever of
                // the 6 local axes points towards the cabin centre.
                Vector3 toCenter = (center - mount.position).normalized;
                Vector3[] axes = { mount.right, -mount.right, mount.up, -mount.up, mount.forward, -mount.forward };
                float bestDot = -2f;
                normal = axes[0];
                foreach (Vector3 axis in axes)
                {
                    float dot = Vector3.Dot(axis, toCenter);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        normal = axis;
                    }
                }
            }

            // Monitor "up": of the pad axes perpendicular to the normal, the one closest to world up.
            Vector3 up = Vector3.up;
            float bestUp = -2f;
            foreach (Vector3 axis in new Vector3[] { mount.right, -mount.right, mount.up, -mount.up, mount.forward, -mount.forward })
            {
                if (Mathf.Abs(Vector3.Dot(axis, normal)) > 0.5f)
                {
                    continue;
                }

                float dot = Vector3.Dot(axis, Vector3.up);
                if (dot > bestUp)
                {
                    bestUp = dot;
                    up = axis;
                }
            }

            // Screen face = -forward, so forward = -normal.
            transform.SetPositionAndRotation(mount.position, Quaternion.LookRotation(-normal, up));
        }

        // ── Mesh construction ────────────────────────────────────────────────

        private static void AddQuad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = v.Count;
            v.Add(a);
            v.Add(b);
            v.Add(c);
            v.Add(d);
            n.Add(normal);
            n.Add(normal);
            n.Add(normal);
            n.Add(normal);
            uv.Add(ua);
            uv.Add(ub);
            uv.Add(uc);
            uv.Add(ud);

            // Front face is clockwise: Cross(b−a, c−a) points along the normal.
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) >= 0f)
            {
                t.Add(i);
                t.Add(i + 1);
                t.Add(i + 2);
                t.Add(i);
                t.Add(i + 2);
                t.Add(i + 3);
            }
            else
            {
                t.Add(i);
                t.Add(i + 2);
                t.Add(i + 1);
                t.Add(i);
                t.Add(i + 3);
                t.Add(i + 2);
            }
        }

        private static void AddBox(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 min, Vector3 max)
        {
            Vector2 z = Vector2.zero;
            AddQuad(v, n, uv, t, new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(0, 0, -1), z, z, z, z);
            AddQuad(v, n, uv, t, new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(0, 0, 1), z, z, z, z);
            AddQuad(v, n, uv, t, new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z), new Vector3(0, -1, 0), z, z, z, z);
            AddQuad(v, n, uv, t, new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(0, 1, 0), z, z, z, z);
            AddQuad(v, n, uv, t, new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, min.y, max.z), new Vector3(-1, 0, 0), z, z, z, z);
            AddQuad(v, n, uv, t, new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(1, 0, 0), z, z, z, z);
        }
    }
}
