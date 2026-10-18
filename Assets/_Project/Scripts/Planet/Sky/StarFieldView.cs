using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// Star field: a sphere around the camera (radius 0.5·far), WORLD orientation —
    /// stars are inertial (they do not rotate with the planet). Additive, ZTest
    /// LEqual: the planet occludes stars by depth, the atmosphere layers on top,
    /// and brightness is multiplied by the _StarVisibility global (set by
    /// SkyEnvironment) — day/night, observer altitude. The same floating-origin
    /// principle as the planets, but for the sky: center = camera, orientation =
    /// world.
    ///
    /// Also (Sky Type = None): the blue HDRP camera background is forced to black,
    /// otherwise it does not darken at night and the sky always looks blue. The
    /// sky is then atmosphere + stars.
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

            if (UnderwaterEffect.CameraIsUnderwater)
            {
                if (shell.gameObject.activeSelf)
                {
                    shell.gameObject.SetActive(false);
                }
                return;
            }

            if (!shell.gameObject.activeSelf)
            {
                shell.gameObject.SetActive(true);
            }

            // Keep the sphere inside far, centered on the camera; world orientation.
            float radius = Mathf.Max(1f, camera.farClipPlane * 0.5f);
            shell.position = camera.transform.position;
            shell.rotation = Quaternion.identity;
            shell.localScale = new Vector3(radius * 2f, radius * 2f, radius * 2f);
        }
    }
}
