using System.Collections.Generic;
using Galilego.Debris;
using Galilego.Spacecraft;
using UnityEngine;
using UnityEngine.Rendering;

namespace Galilego.Building
{
    /// <summary>Materials of the fairing system, built at runtime (HDRP).</summary>
    internal static class FairingMaterials
    {
        /// <summary>White painted shell/plate (HDRP/Lit).</summary>
        public static Material CreateWhite(string name)
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogError("[Fairing] шейдер HDRP/Lit не найден.");
                return null;
            }

            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", new Color(0.90f, 0.91f, 0.92f, 1f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.35f);
            material.SetFloat("_DoubleSidedEnable", 0f);
            return material;
        }

        /// <summary>
        /// Translucent projection (HDRP/Unlit, transparent surface). HDRP keeps transparency in
        /// keywords/passes that its editor GUI normally sets, so they are set by hand here. If your
        /// HDRP version still draws it opaque or not at all, set SpacecraftBuilder.FairingTransparentGhost
        /// to false: the opaque emissive ghost (like the other part ghosts) is used instead.
        /// </summary>
        public static Material CreateTransparentGhost(Color color, string name)
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader) { name = name };
            material.SetColor("_UnlitColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_SurfaceType", 1f);
            material.SetFloat("_BlendMode", 0f);
            material.SetFloat("_SrcBlend", 5f);          // SrcAlpha
            material.SetFloat("_DstBlend", 10f);         // OneMinusSrcAlpha
            material.SetFloat("_AlphaSrcBlend", 1f);
            material.SetFloat("_AlphaDstBlend", 10f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_CullMode", 0f);
            material.SetFloat("_CullModeForward", 0f);
            material.SetFloat("_DoubleSidedEnable", 1f);
            material.SetFloat("_AlphaCutoffEnable", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ALPHA");
            material.EnableKeyword("_DOUBLESIDED_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("DepthForwardOnly", false);
            material.SetShaderPassEnabled("MotionVectors", false);
            material.SetShaderPassEnabled("TransparentDepthPrepass", false);
            material.SetShaderPassEnabled("TransparentDepthPostpass", false);
            material.SetShaderPassEnabled("TransparentBackface", false);
            return material;
        }
    }

    /// <summary>
    /// Wire outline of the projected section: two rings and the petal seams as LineRenderers with an
    /// opaque material. Always visible even if the translucent fill is not (see CreateTransparentGhost).
    /// Lives in the ghost root's local space (origin: centre of the lower ring).
    /// </summary>
    internal sealed class FairingOutline
    {
        private const int CirclePoints = 40;
        private const float Width = 0.035f;

        private readonly GameObject root;
        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private Material material;

        public FairingOutline(Transform parent, Material material)
        {
            this.material = material;
            root = new GameObject("FairingOutline");
            root.transform.SetParent(parent, false);
        }

        public void SetMaterial(Material value)
        {
            material = value;
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i].sharedMaterial = value;
            }
        }

        public void SetActive(bool active)
        {
            root.SetActive(active);
        }

        public void Destroy()
        {
            if (root != null)
            {
                Object.Destroy(root);
            }
        }

        /// <summary>Lower ring (radius a, height 0), upper ring (radius b, height dy), seams between them.</summary>
        public void Set(float lowerRadius, float upperRadius, float dy, int petals)
        {
            int needed = 2 + Mathf.Max(1, petals);
            while (lines.Count < needed)
            {
                var go = new GameObject("Line " + lines.Count);
                go.transform.SetParent(root.transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.widthMultiplier = Width;
                line.numCapVertices = 0;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.sharedMaterial = material;
                lines.Add(line);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                lines[i].gameObject.SetActive(i < needed);
            }

            SetCircle(lines[0], lowerRadius, 0f);
            SetCircle(lines[1], upperRadius, dy);
            float step = (2f * Mathf.PI) / Mathf.Max(1, petals);
            for (int p = 0; p < petals; p++)
            {
                float angle = p * step;      // the seams of the petals (their middles are at +half a step)
                LineRenderer line = lines[2 + p];
                line.loop = false;
                line.positionCount = 2;
                line.SetPosition(0, new Vector3(lowerRadius * Mathf.Cos(angle), 0f, lowerRadius * Mathf.Sin(angle)));
                line.SetPosition(1, new Vector3(upperRadius * Mathf.Cos(angle), dy, upperRadius * Mathf.Sin(angle)));
            }
        }

        private static void SetCircle(LineRenderer line, float radius, float y)
        {
            line.loop = true;
            line.positionCount = CirclePoints;
            for (int i = 0; i < CirclePoints; i++)
            {
                float angle = (2f * Mathf.PI * i) / CirclePoints;
                line.SetPosition(i, new Vector3(radius * Mathf.Cos(angle), y, radius * Mathf.Sin(angle)));
            }
        }
    }

    /// <summary>
    /// Visuals of jettisoned petals. A petal is tied to a DebrisPool slot (the slot carries the n-body
    /// motion); DebrisView asks Drive() every frame: a driven slot draws the petal instead of the
    /// sphere marker. When the slot dies (lifetime, eviction, Clear) the petal is destroyed.
    /// </summary>
    public static class FairingDebrisVisuals
    {
        private sealed class Entry
        {
            public int Slot;
            public long Sequence;
            public Transform Visual;
            public Vector3 SpinDegreesPerSecond;
            public Quaternion Rotation;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        public static void Register(int slot, long sequence, Transform visual, Vector3 spinDegreesPerSecond)
        {
            if (visual == null)
            {
                return;
            }

            Entries.Add(new Entry
            {
                Slot = slot,
                Sequence = sequence,
                Visual = visual,
                SpinDegreesPerSecond = spinDegreesPerSecond,
                Rotation = visual.rotation,
            });
        }

        /// <summary>
        /// Called by DebrisView for an ACTIVE slot. Returns true when the slot is drawn by a petal
        /// (the sphere marker must stay hidden).
        /// </summary>
        public static bool Drive(int slot, DebrisSlot state, Vector3 renderPosition, float deltaTime)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry e = Entries[i];
                if (e.Slot != slot)
                {
                    continue;
                }

                if (e.Visual == null)
                {
                    Entries.RemoveAt(i);
                    return false;
                }

                if (e.Sequence != state.Sequence)
                {
                    // The slot was reused by another debris: the petal's slot is gone.
                    Object.Destroy(e.Visual.gameObject);
                    Entries.RemoveAt(i);
                    return false;
                }

                e.Rotation = Quaternion.Euler(e.SpinDegreesPerSecond * deltaTime) * e.Rotation;
                e.Visual.SetPositionAndRotation(renderPosition, e.Rotation);
                return true;
            }

            return false;
        }

        /// <summary>Called by DebrisView for an inactive slot: its petal (if any) disappears.</summary>
        public static void Release(int slot)
        {
            if (Entries.Count == 0)
            {
                return;
            }

            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].Slot != slot)
                {
                    continue;
                }

                if (Entries[i].Visual != null)
                {
                    Object.Destroy(Entries[i].Visual.gameObject);
                }

                Entries.RemoveAt(i);
            }
        }

        /// <summary>Destroy every petal visual (rollback to the build mode).</summary>
        public static void Clear()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Visual != null)
                {
                    Object.Destroy(Entries[i].Visual.gameObject);
                }
            }

            Entries.Clear();
        }
    }
}
