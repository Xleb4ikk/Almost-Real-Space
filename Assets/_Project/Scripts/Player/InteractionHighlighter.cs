using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// One unified "show what can be interacted with": a white outline around the
    /// silhouette of the object under the crosshair (HDRP Custom Pass, follows any
    /// corners) and an "[E] action" hint pill under the crosshair.
    ///
    /// Works with ANY Interactable without setup: the target comes from
    /// PlayerController.Focused, renderers from Interactable.CollectHighlight, text
    /// from Interactable.Prompt. The object creates itself on scene start; to tweak
    /// the look (color, width, glow), put this component on any scene GO —
    /// it will be found instead of the auto-created one.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class InteractionHighlighter : MonoBehaviour
    {
        [Header("Outline")]
        public bool OutlineEnabled = true;
        public Color LineColor = Color.white;

        [Tooltip("Толщина линии, px при высоте экрана 1080 (дальше масштабируется).")]
        [Range(0.5f, 6f)]
        public float LineWidthPx = 2.2f;

        [Tooltip("Радиус мягкого свечения снаружи линии, px. 0 — без свечения.")]
        [Range(0f, 24f)]
        public float GlowRadiusPx = 10f;

        [Range(0f, 2f)]
        public float GlowStrength = 0.6f;

        [Tooltip("Яркость (HDR-множитель): >1 даёт лёгкий блум, если он включён в Volume.")]
        [Range(0.2f, 6f)]
        public float Intensity = 2.2f;

        [Tooltip("Скорость появления/исчезновения, 1/с.")]
        [Range(2f, 40f)]
        public float FadeSpeed = 14f;

        [Header("Prompt")]
        public bool ShowPrompt = true;

        private static InteractionHighlighter instance;
        private static readonly List<Renderer> scratch = new List<Renderer>(32);

        private PlayerController player;
        private float nextFind;
        private Interactable current;
        private float alpha;
        private float promptAlpha;
        private string promptText = string.Empty;
        private Color tint = Color.white;

        private Renderer[] renderers = new Renderer[0];
        private int[] subMeshes = new int[0];
        private int count;
        private readonly List<Renderer> gather = new List<Renderer>(32);

        private CustomPassVolume volume;
        private InteractOutlinePass pass;
        private Material material;
        private bool shaderWarned;

        private GUIStyle textStyle;
        private GUIStyle keyStyle;
        private float styleScale;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<InteractionHighlighter>();
            }

            if (instance == null)
            {
                instance = new GameObject("InteractionHighlighter").AddComponent<InteractionHighlighter>();
            }
        }

        /// <summary>
        /// Collect renderers to outline: meshes only (no particles or lines), no
        /// "shadow" duplicates. For overriding CollectHighlight in subclasses.
        /// </summary>
        public static void Gather(Transform root, List<Renderer> into)
        {
            if (root == null)
            {
                return;
            }

            root.GetComponentsInChildren(false, scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                Renderer r = scratch[i];
                if ((r is MeshRenderer || r is SkinnedMeshRenderer)
                    && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
                {
                    into.Add(r);
                }
            }

            scratch.Clear();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void LateUpdate()
        {
            if (player == null && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1f;
                player = FindAnyObjectByType<PlayerController>();
            }

            Interactable target = player != null ? player.Focused : null;
            if (target != current)
            {
                current = target;
                if (current != null)
                {
                    Rebuild(current);
                }
            }

            bool on = current != null;
            float step = FadeSpeed * Time.unscaledDeltaTime;
            alpha = Mathf.MoveTowards(alpha, on && OutlineEnabled ? 1f : 0f, step);
            promptAlpha = Mathf.MoveTowards(promptAlpha, on && ShowPrompt ? 1f : 0f, step);
            if (on)
            {
                promptText = CleanPrompt(current.Prompt);
            }

            UpdatePass();
        }

        private void Rebuild(Interactable target)
        {
            gather.Clear();
            target.CollectHighlight(gather);
            if (renderers.Length < gather.Count)
            {
                renderers = new Renderer[gather.Count];
                subMeshes = new int[gather.Count];
            }

            count = 0;
            for (int i = 0; i < gather.Count; i++)
            {
                int n = SubMeshCount(gather[i]);
                if (n > 0)
                {
                    renderers[count] = gather[i];
                    subMeshes[count] = n;
                    count++;
                }
            }

            tint = target.HighlightColor;
        }

        private static int SubMeshCount(Renderer r)
        {
            Mesh mesh = null;
            SkinnedMeshRenderer skinned = r as SkinnedMeshRenderer;
            if (skinned != null)
            {
                mesh = skinned.sharedMesh;
            }
            else
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
            }

            return mesh != null ? Mathf.Max(1, mesh.subMeshCount) : 0;
        }

        private void UpdatePass()
        {
            Camera cam = Camera.main;
            if (!OutlineEnabled || alpha <= 0.001f || count == 0 || cam == null)
            {
                SetVolumeActive(false);
                return;
            }

            if (!EnsurePass(cam))
            {
                return;
            }

            float eased = alpha * alpha * (3f - (2f * alpha));
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            Color linear = tint.linear;
            pass.Renderers = renderers;
            pass.SubMeshCounts = subMeshes;
            pass.Count = count;
            pass.Color = new Vector4(linear.r * Intensity, linear.g * Intensity, linear.b * Intensity, eased);
            pass.Params = new Vector4(LineWidthPx * scale, GlowRadiusPx * scale, GlowStrength, Time.unscaledTime);
            volume.targetCamera = cam;
            SetVolumeActive(true);
        }

        private bool EnsurePass(Camera cam)
        {
            if (volume != null)
            {
                return true;
            }

            Shader shader = Shader.Find("Galilego/InteractOutline");
            if (shader == null)
            {
                if (!shaderWarned)
                {
                    shaderWarned = true;
                    Debug.LogWarning("[InteractionHighlighter] не найден шейдер Galilego/InteractOutline - обводка выключена " +
                        "(в билде добавьте его в Project Settings > Graphics > Always Included Shaders).");
                }

                return false;
            }

            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            pass = new InteractOutlinePass
            {
                name = "Interact Outline",
                Material = material,
                // Custom — buffer for the mask; camera depth is needed for the test.
                targetColorBuffer = CustomPass.TargetBuffer.Custom,
                targetDepthBuffer = CustomPass.TargetBuffer.Camera,
                clearFlags = ClearFlag.None,
            };

            GameObject volumeObject = new GameObject("InteractOutlineVolume");
            volumeObject.SetActive(false);
            volume = volumeObject.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.targetCamera = cam;
            // BeforePostProcess: depth is still valid, and SMAA/bloom will smooth the line later.
            volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            volume.customPasses.Add(pass);
            return true;
        }

        private void SetVolumeActive(bool active)
        {
            if (volume != null && volume.gameObject.activeSelf != active)
            {
                volume.gameObject.SetActive(active);
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            if (volume != null)
            {
                Destroy(volume.gameObject);
            }

            if (material != null)
            {
                Destroy(material);
            }
        }

        // ------------------------------------------------------------------- hint

        private static string CleanPrompt(string prompt)
        {
            if (string.IsNullOrEmpty(prompt))
            {
                return string.Empty;
            }

            prompt = prompt.Trim();
            return prompt.EndsWith("(E)") ? prompt.Substring(0, prompt.Length - 3).TrimEnd() : prompt;
        }

        private void OnGUI()
        {
            if (!ShowPrompt || Event.current.type != EventType.Repaint)
            {
                return;
            }

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            if (player != null && player.FocusedScreen != null)
            {
                // Focused on a screen: the cursor is free, the exit hint is at the bottom.
                DrawPill(Screen.width * 0.5f, Screen.height - (70f * scale), "E", "Отдалить", 1f, scale);
                return;
            }

            if (promptAlpha <= 0.01f || string.IsNullOrEmpty(promptText)
                || Cursor.lockState != CursorLockMode.Locked || !Application.isFocused)
            {
                return;
            }

            float eased = promptAlpha * promptAlpha * (3f - (2f * promptAlpha));
            float top = (Screen.height * 0.5f) + (40f * scale) + ((1f - eased) * 8f * scale);
            DrawPill(Screen.width * 0.5f, top, "E", promptText, eased, scale);
        }

        private void EnsureStyles(float scale)
        {
            if (textStyle != null && Mathf.Approximately(styleScale, scale))
            {
                return;
            }

            styleScale = scale;
            textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(16f * scale),
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
            };
            keyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(16f * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        private void DrawPill(float centerX, float top, string key, string text, float a, float scale)
        {
            EnsureStyles(scale);
            float h = 34f * scale;
            float inset = 5f * scale;
            float keySize = h - (2f * inset);
            float textWidth = textStyle.CalcSize(new GUIContent(text)).x;
            float width = inset + keySize + (10f * scale) + textWidth + (14f * scale);
            Rect pill = new Rect(Mathf.Round(centerX - (width * 0.5f)), Mathf.Round(top), width, h);

            GUI.DrawTexture(pill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(0.04f, 0.05f, 0.07f, 0.62f * a), Vector4.zero, Vector4.one * (h * 0.5f));
            GUI.DrawTexture(pill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(1f, 1f, 1f, 0.30f * a), Vector4.one * Mathf.Max(1f, scale), Vector4.one * (h * 0.5f));

            Rect keyRect = new Rect(pill.x + inset, pill.y + inset, keySize, keySize);
            GUI.DrawTexture(keyRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(0.96f, 0.98f, 1f, 0.96f * a), Vector4.zero, Vector4.one * (7f * scale));
            keyStyle.normal.textColor = new Color(0.06f, 0.07f, 0.09f, a);
            GUI.Label(keyRect, key, keyStyle);

            textStyle.normal.textColor = new Color(1f, 1f, 1f, 0.95f * a);
            GUI.Label(new Rect(keyRect.xMax + (10f * scale), pill.y, textWidth + (6f * scale), h), text, textStyle);
        }
    }
}
