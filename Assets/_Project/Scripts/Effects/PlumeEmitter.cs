using Galilego.Core;
using Galilego.Flight;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Effects
{
    /// <summary>
    /// One visible exhaust plume: a bounding box with the volumetric shader + a point light.
    /// Lives as a child of the nozzle (engine visual root / RCS marker); local +Z = exhaust direction.
    /// Driven by PlumeRig every frame from the real Thruster state (Level, gimbal) and ambient pressure.
    /// </summary>
    public sealed class PlumeEmitter : MonoBehaviour
    {
        private static readonly int PlumeA = Shader.PropertyToID("_PlumeA");
        private static readonly int PlumeB = Shader.PropertyToID("_PlumeB");
        private static readonly int PlumeC = Shader.PropertyToID("_PlumeC");
        private static readonly int PlumeD = Shader.PropertyToID("_PlumeD");
        private static readonly int ColCore = Shader.PropertyToID("_ColCore");
        private static readonly int ColEdge = Shader.PropertyToID("_ColEdge");
        private static readonly int ColAfter = Shader.PropertyToID("_ColAfter");

        private static Mesh cube;
        private static Shader shader;

        [Tooltip("Множитель яркости факела (общий). Подбирай под экспозицию сцены.")]
        public float IntensityScale = 1f;

        private Thruster thruster;
        private VesselFlightModel flight;
        private PlumeModel model;
        private PlumeStyle style;
        private double exitRadius;
        private Quaternion partRotation = Quaternion.identity;
        private bool gimbaled;

        private Transform volume;
        private MeshRenderer volumeRenderer;
        private Material material;
        private Light lamp;
        private HDAdditionalLightData lampHd;
        private float visLevel;

        public static PlumeEmitter Create(
            Transform nozzle, Thruster thruster, VesselFlightModel flight,
            PlumeNozzle nozzleData, PlumeStyle style, Quaternion partRotation, bool gimbaled)
        {
            if (shader == null)
            {
                shader = Resources.Load<Shader>("EnginePlume");
                if (shader == null) shader = Shader.Find("Galilego/EnginePlume");
                if (shader == null)
                {
                    Debug.LogError("[Plume] шейдер Galilego/EnginePlume не найден: положи EnginePlume.shader в любую папку Resources.");
                    return null;
                }
            }

            var go = new GameObject("Plume " + thruster.Name);
            go.transform.SetParent(nozzle, false);
            var e = go.AddComponent<PlumeEmitter>();
            e.thruster = thruster;
            e.flight = flight;
            e.style = style;
            e.exitRadius = nozzleData.ExitRadiusM;
            e.model = new PlumeModel(nozzleData, style);
            e.partRotation = partRotation;
            e.gimbaled = gimbaled;
            e.Build();
            return e;
        }

        private void Build()
        {
            if (cube == null) cube = BuildCube();

            var v = new GameObject("PlumeVolume");
            v.transform.SetParent(transform, false);
            volume = v.transform;
            v.AddComponent<MeshFilter>().sharedMesh = cube;
            volumeRenderer = v.AddComponent<MeshRenderer>();
            material = new Material(shader) { name = "Plume (" + style.Id + ")", hideFlags = HideFlags.DontSave };
            volumeRenderer.sharedMaterial = material;
            volumeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            volumeRenderer.receiveShadows = false;
            volumeRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            volumeRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            volumeRenderer.enabled = false;

            // Same HDRP light pattern as OrionCrewModule (Lumen unit + HDAdditionalLightData).
            var lg = new GameObject("PlumeLight");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(0f, 0f, 3f * (float)exitRadius);
            lamp = lg.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.shadows = LightShadows.None;
            lamp.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
            lamp.color = style.LightColor;
            lamp.range = 60f * (float)exitRadius;
            lampHd = lg.GetComponent<HDAdditionalLightData>();
            if (lampHd == null) lampHd = lg.AddComponent<HDAdditionalLightData>();
            lampHd.affectDiffuse = true;
            lampHd.affectSpecular = true;
            lamp.enabled = false;
        }

        /// <summary>Called once per frame by PlumeRig AFTER the part visuals were posed.</summary>
        public void Tick(double ambientPa, float dt)
        {
            if (thruster == null || flight == null) return;

            double target = flight.IsGone(thruster.PartIndex) || !thruster.Enabled ? 0d : thruster.Level;
            // Visual smoothing: instant rise for RCS pulses would flicker, so ease both ways.
            float k = target > visLevel ? 30f : 14f;
            visLevel = Mathf.Lerp(visLevel, (float)target, 1f - Mathf.Exp(-k * dt));
            if (visLevel < 0.004f)
            {
                volumeRenderer.enabled = false;
                lamp.enabled = false;
                return;
            }

            if (gimbaled)
            {
                // Exhaust follows the real nozzle swing (ship axes -> part-visual axes).
                Vector3d d = thruster.CurrentDirection();
                Vector3 exhaust = Quaternion.Inverse(partRotation) * new Vector3(-(float)d.X, -(float)d.Y, -(float)d.Z);
                Vector3 up = Mathf.Abs(Vector3.Dot(exhaust, Vector3.forward)) > 0.95f ? Vector3.right : Vector3.forward;
                transform.localRotation = Quaternion.LookRotation(exhaust, up);
            }

            PlumeParams p = model.Evaluate(visLevel, ambientPa);
            float re = (float)exitRadius;

            volume.localPosition = new Vector3(0f, 0f, 0.5f * p.BoxLength * re);
            volume.localScale = new Vector3(2f * p.BoxHalfWidth * re, 2f * p.BoxHalfWidth * re, p.BoxLength * re);

            material.SetVector(PlumeA, new Vector4(p.ThetaB, p.N, p.SourceDist, re));
            material.SetVector(PlumeB, new Vector4(style.Nits * IntensityScale * p.Intensity, p.EnvLength, p.DiamondGain, p.CellLength));
            material.SetVector(PlumeC, new Vector4(p.BarrelAmp, p.AfterburnK, p.Air, p.Turbulence));
            material.SetVector(PlumeD, new Vector4(p.BoxHalfWidth * re, p.BoxLength * re, style.Absorption, p.Free));
            material.SetVector(ColCore, style.Core);
            material.SetVector(ColEdge, style.Edge);
            material.SetVector(ColAfter, style.Afterburn);
            volumeRenderer.enabled = true;

            // Light cast on the pad / regolith / hull: scales with thrust, dimmer in free expansion.
            float area = Mathf.PI * re * re;
            lamp.intensity = style.LightLumensPerM2 * area * visLevel * Mathf.Lerp(1f, 0.5f, p.Free);
            lamp.enabled = true;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        private static Mesh BuildCube()
        {
            var m = new Mesh { name = "PlumeCube", hideFlags = HideFlags.DontSave };
            m.vertices = new[]
            {
                new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, .5f, -.5f), new Vector3(-.5f, .5f, -.5f),
                new Vector3(-.5f, -.5f, .5f), new Vector3(.5f, -.5f, .5f), new Vector3(.5f, .5f, .5f), new Vector3(-.5f, .5f, .5f),
            };
            m.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,   // z-
                4, 5, 6, 4, 6, 7,   // z+
                0, 4, 7, 0, 7, 3,   // x-
                1, 2, 6, 1, 6, 5,   // x+
                0, 1, 5, 0, 5, 4,   // y-
                3, 7, 6, 3, 6, 2,   // y+
            };
            m.bounds = new Bounds(Vector3.zero, Vector3.one);
            return m;
        }
    }
}
