using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// Звёздное небо: сфера вокруг камеры (радиус 0.5·far), МИРОВАЯ ориентация —
    /// звёзды инерциальны (не вращаются с планетой). Additive, ZTest LEqual:
    /// планета перекрывает звёзды по глубине, атмосфера ложится сверху, яркость
    /// умножается на глобал _StarVisibility (ставит SkyEnvironment) — день/ночь,
    /// высота наблюдателя. Тот же floating-origin принцип, что у планет, но для
    /// неба: центр — камера, ориентация — мир.
    ///
    /// Плюс (Sky Type = None): гасим синий HDRP-фон камеры в чёрный, иначе он не
    /// темнеет ночью и небо всегда синее. Небо тогда = атмосфера + звёзды.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-34)]
    public sealed class StarFieldView : MonoBehaviour
    {
        [Tooltip("Камера (пусто — Camera.main).")]
        public Camera TargetCamera;

        [Tooltip("Ставить чёрный фон HDRP-камеры (иначе при Sky Type=None фон синий и не темнеет).")]
        public bool BlackCameraBackground = true;

        private Transform shell;
        private Material materialCache;

        private void Start()
        {
            Camera camera = TargetCamera != null ? TargetCamera : Camera.main;
            if (BlackCameraBackground && camera != null)
            {
                HDAdditionalCameraData hdCamera = camera.GetComponent<HDAdditionalCameraData>();
                if (hdCamera != null)
                {
                    hdCamera.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                    hdCamera.backgroundColorHDR = new Color(0f, 0f, 0f, 0f);
                }
            }

            Shader shader = Shader.Find("Galilego/StarField");
            materialCache = new Material(shader);

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sphere.GetComponent<Collider>());
            sphere.name = "StarField";
            MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = materialCache;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            shell = sphere.transform;
        }

        private void OnDestroy()
        {
            if (shell != null)
            {
                Destroy(shell.gameObject);
            }

            if (materialCache != null)
            {
                Destroy(materialCache);
            }
        }

        private void LateUpdate()
        {
            Camera camera = TargetCamera != null ? TargetCamera : Camera.main;
            if (camera == null || shell == null)
            {
                return;
            }

            if (!shell.gameObject.activeSelf)
            {
                shell.gameObject.SetActive(true);
            }

            // Держим сферу внутри far, центрируя на камере; ориентация мировая.
            float radius = Mathf.Max(1f, camera.farClipPlane * 0.5f);
            shell.position = camera.transform.position;
            shell.rotation = Quaternion.identity;
            shell.localScale = new Vector3(radius * 2f, radius * 2f, radius * 2f);
        }
    }
}
