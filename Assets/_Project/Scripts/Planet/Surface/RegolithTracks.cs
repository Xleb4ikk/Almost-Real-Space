using System.Collections.Generic;
using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Galilego.Universe
{
    /// <summary>
    /// PERMANENT REGOLITH DEFORMATION around the player.
    ///
    /// The Moon has no wind and no water, so a footprint is geometry, not a decal:
    /// it must keep a crisp edge under a 1-degree sun forever. A vertex mesh cannot
    /// carry a 1-2 cm print (chunk triangles are metres), so the print lives in one
    /// RG16F height/compaction map over a 256 m tangent patch anchored at the first
    /// grounded position. <see cref="PlanetSurface.shader"/> derives the surface
    /// normal from the height gradient and darkens the compacted ring; nothing ever
    /// erodes it, and nothing reads it for physics (the loose layer is millimetres
    /// of visual relief — the brief forbids sinking).
    ///
    /// The map is camera-body-owned: it is created only for an airless body with a
    /// regolith detail set, and the globals are switched off on any other body.
    /// </summary>
    [DefaultExecutionOrder(-35)]
    public sealed class RegolithTracks : MonoBehaviour
    {
        /// <summary>Texels across the patch. 4096 over 256 m = 6.25 cm/texel: a boot
        /// (30 cm) is 5 texels, the smallest readable print at eye height.</summary>
        public const int Resolution = 4096;

        /// <summary>Patch edge in metres. Covers the landing zone (50-100 m) and the
        /// play area around it; outside this the shader simply does not sample.</summary>
        public const float SizeMeters = 256f;

        /// <summary>Depth of a normal print. Polar loose regolith is 3-5 cm, PSR ice
        /// crust under 1 cm; the per-body value can override it later.</summary>
        public const float PrintDepthMeters = 0.02f;

        private const float StepSpacingMeters = 0.70f;
        private const float LandingRadiusMeters = 55f;

        private static readonly int MapId = Shader.PropertyToID("_RegolithTrackMap");
        private static readonly int OriginId = Shader.PropertyToID("_RegolithTrackOrigin");
        private static readonly int RightId = Shader.PropertyToID("_RegolithTrackRight");
        private static readonly int ForwardId = Shader.PropertyToID("_RegolithTrackForward");
        private static readonly int OnId = Shader.PropertyToID("_RegolithTrackOn");
        private static readonly int DepthId = Shader.PropertyToID("_RegolithTrackDepth");
        private static readonly int TexelId = Shader.PropertyToID("_RegolithTrackTexel");
        private static readonly int TexelMetersId = Shader.PropertyToID("_RegolithTrackTexelMeters");
        private static readonly int LandingId = Shader.PropertyToID("_RegolithLandingRadius");

        private static readonly int StampCenterId = Shader.PropertyToID("_StampCenter");
        private static readonly int StampParamsId = Shader.PropertyToID("_StampParams");

        private enum StampKind
        {
            Boot = 0,
            Wheel = 1,
            Blast = 2,
        }

        private struct Stamp
        {
            public float U;
            public float V;
            public float Yaw;
            public float Depth;
            public float Mask;
            public float HalfSizeUv;
            public StampKind Kind;
        }

        private static RegolithTracks instance;

        private RenderTexture map;
        private Material stampMaterial;
        private Mesh quad;
        private CommandBuffer commands;
        private readonly List<Stamp> pending = new List<Stamp>(32);

        private SimulationRunner runner;
        private OrbitingBody body;
        private bool anchored;
        private bool enabledNow;

        private Vector3d originAstro;
        private Vector3d rightAstro;
        private Vector3d forwardAstro;
        private Vector3d upAstro;
        private Vector3d lastStepAstro;
        private int footIndex;

        public static RegolithTracks Ensure()
        {
            if (instance != null)
            {
                return instance;
            }

            // A domain reload (script edit) clears the static reference but leaves
            // DontSave runtime objects alive. Reuse the first and destroy the extras:
            // three managers publish three different origins every frame, and the
            // shader ends up sampling whichever wrote last.
            RegolithTracks[] existing = Resources.FindObjectsOfTypeAll<RegolithTracks>();
            for (int i = 0; i < existing.Length; i++)
            {
                RegolithTracks candidate = existing[i];
                if (candidate == null)
                {
                    continue;
                }

                if (instance == null)
                {
                    instance = candidate;
                }
                else
                {
                    Object.Destroy(candidate.gameObject);
                }
            }

            if (instance != null)
            {
                instance.enabled = true;
                return instance;
            }

            var go = new GameObject("RegolithTracks") { hideFlags = HideFlags.DontSave };
            instance = go.AddComponent<RegolithTracks>();
            return instance;
        }

        private void OnDestroy()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            if (map != null)
            {
                map.Release();
                Destroy(map);
                map = null;
            }

            if (stampMaterial != null)
            {
                Destroy(stampMaterial);
                stampMaterial = null;
            }

            if (quad != null)
            {
                Destroy(quad);
                quad = null;
            }

            if (commands != null)
            {
                commands.Release();
                commands = null;
            }

            if (instance == this)
            {
                instance = null;
            }
        }

        private int lastTickFrame = int.MinValue;

        private void Update()
        {
            Tick();
        }

        /// <summary>
        /// Driven by SkyEnvironment every frame (the camera body owns the globals).
        /// The engine's own Update is kept as a fallback; the frame guard makes
        /// double-driving harmless.
        /// </summary>
        public void Tick()
        {
            if (lastTickFrame == Time.frameCount)
            {
                return;
            }

            lastTickFrame = Time.frameCount;

            if (runner == null)
            {
                runner = Object.FindAnyObjectByType<SimulationRunner>();
            }

            OrbitingBody cameraBody = runner != null ? runner.CameraBody : null;
            HeightfieldTerrain terrain = cameraBody != null
                ? cameraBody.Terrain as HeightfieldTerrain
                : null;
            bool airless = terrain != null && terrain.DetailAlbedoMean > 0f;

            if (!airless)
            {
                // Leaving the regolith body: the map stays resident (the tracks are
                // permanent) but the shader must not sample it on Terra.
                if (enabledNow)
                {
                    Shader.SetGlobalFloat(OnId, 0f);
                    enabledNow = false;
                }

                anchored = false;
                body = null;
                return;
            }

            if (anchored && body == cameraBody)
            {
                // A teleport (or a very long walk) leaves the 256 m patch. Re-anchor
                // at the new site: the old prints cannot be stored outside the map,
                // and the landing blast belongs where the player actually is.
                Vector3d offset = runner.PlayerPosition - originAstro;
                Vector3d horizontal = offset - (upAstro * Vector3d.Dot(offset, upAstro));
                if (horizontal.Magnitude > SizeMeters * 0.6d)
                {
                    anchored = false;
                    Shader.SetGlobalFloat(OnId, 0f);
                }
            }

            if (!anchored || body != cameraBody)
            {
                // A different body means a different tangent patch. The old map stays
                // resident (tracks on the previous body are permanent), but the globals
                // must not sample it here: re-anchor on the first grounded frame.
                if (body != cameraBody)
                {
                    anchored = false;
                    body = null;
                    Shader.SetGlobalFloat(OnId, 0f);
                }

                if (runner.PlayerMode != PlayerMode.OnSurface || !runner.PlayerGrounded)
                {
                    return;
                }

                Anchor(cameraBody);
                return;
            }

            if (runner.Paused)
            {
                PublishGlobals();
                return;
            }

            TrackPlayer();
            Flush();
            PublishGlobals();
        }

        private void Anchor(OrbitingBody cameraBody)
        {
            if (!EnsureResources())
            {
                return;
            }

            body = cameraBody;
            originAstro = runner.PlayerPosition;
            upAstro = (originAstro - BodyPosition()).Normalized;
            if (upAstro.SqrMagnitude < 1e-12d)
            {
                upAstro = new Vector3d(0d, 0d, 1d);
            }

            // Tangent basis at the landing site. The patch is flat over 256 m on a
            // 412 km body (sag 2 cm at the edge) — far below the print depth.
            QuaternionD orientation = body.GetVisualOrientation(runner.TimeSeconds);
            Vector3d north = orientation.Rotate(new Vector3d(0d, 0d, 1d));
            rightAstro = Vector3d.Cross(north, upAstro);
            if (rightAstro.SqrMagnitude < 1e-12d)
            {
                rightAstro = Vector3d.Cross(new Vector3d(1d, 0d, 0d), upAstro);
            }

            rightAstro = rightAstro.Normalized;
            forwardAstro = Vector3d.Cross(upAstro, rightAstro).Normalized;
            lastStepAstro = originAstro;
            footIndex = 0;
            anchored = true;

            ClearMap();

            // The landing site: loose dust blown flat, a smooth pale patch — no
            // crater under the engine. 55 m radius, very shallow, marks the site.
            AddStamp(originAstro, 0f, StampKind.Blast, 0.10f, 0f, 55f);
            Flush();
            PublishGlobals();
        }

        private Vector3d BodyPosition()
        {
            body.EvaluateWorldState(runner.TimeSeconds, out Vector3d position, out _);
            return position;
        }

        private void TrackPlayer()
        {
            if (runner.PlayerMode != PlayerMode.OnSurface || !runner.PlayerGrounded)
            {
                lastStepAstro = runner.PlayerPosition;
                return;
            }

            Vector3d position = runner.PlayerPosition;
            Vector3d delta = position - lastStepAstro;
            Vector3d horizontal = delta - (upAstro * Vector3d.Dot(delta, upAstro));
            double distance = horizontal.Magnitude;
            if (distance < StepSpacingMeters)
            {
                return;
            }

            Vector3d direction = horizontal / distance;
            double side = ((footIndex++ & 1) == 0) ? 0.11d : -0.11d;
            Vector3d footprint = position + (rightAstro * side);

            float along = (float)Vector3d.Dot(direction, forwardAstro);
            float across = (float)Vector3d.Dot(direction, rightAstro);
            float yaw = Mathf.Atan2(across, along);

            AddStamp(footprint, yaw, StampKind.Boot, 0.70f, 0.10f, 0.24f);
            lastStepAstro = position;
        }

        /// <summary>Stamp a wheel rut between two ground points. Public for a future
        /// rover; the brief's rover does not exist yet, so only the API ships.</summary>
        public static void WheelRut(Vector3d fromAstro, Vector3d toAstro, float halfWidthMeters)
        {
            if (instance == null)
            {
                return;
            }

            instance.AddWheelSegment(fromAstro, toAstro, halfWidthMeters);
        }

        private void AddWheelSegment(Vector3d fromAstro, Vector3d toAstro, float halfWidthMeters)
        {
            if (!anchored)
            {
                return;
            }

            Vector3d mid = (fromAstro + toAstro) * 0.5d;
            Vector3d delta = toAstro - fromAstro;
            Vector3d horizontal = delta - (upAstro * Vector3d.Dot(delta, upAstro));
            if (horizontal.SqrMagnitude < 1e-8d)
            {
                return;
            }

            Vector3d direction = horizontal.Normalized;
            float along = (float)Vector3d.Dot(direction, forwardAstro);
            float across = (float)Vector3d.Dot(direction, rightAstro);
            float yaw = Mathf.Atan2(across, along);
            float length = (float)horizontal.Magnitude;
            // The stamp is a square; a rut segment is a short wheel patch repeated.
            int segments = Mathf.Max(1, Mathf.CeilToInt(length / (halfWidthMeters * 2f)));
            for (int i = 0; i < segments; i++)
            {
                double t = (i + 0.5d) / segments;
                Vector3d point = fromAstro + (delta * t);
                AddStamp(point, yaw, StampKind.Wheel, 0.55f, 0.16f, halfWidthMeters * 1.6f);
            }
        }

        /// <summary>Diagnostic: stamp a line of boot prints from a ground point in a
        /// tangent direction. The game itself calls AddStamp from the player's stride;
        /// this exists so a headless probe can verify the shader without input.</summary>
        public static void DebugStampLine(Vector3d startAstro, Vector3d directionAstro, int count)
        {
            if (instance == null || !instance.anchored)
            {
                return;
            }

            Vector3d direction = directionAstro
                - (instance.upAstro * Vector3d.Dot(directionAstro, instance.upAstro));
            if (direction.SqrMagnitude < 1e-9d)
            {
                return;
            }

            direction = direction.Normalized;
            Vector3d side = Vector3d.Cross(instance.upAstro, direction).Normalized;
            for (int i = 0; i < count; i++)
            {
                double lateral = ((i & 1) == 0) ? 0.11d : -0.11d;
                Vector3d point = startAstro + (direction * (i * StepSpacingMeters))
                    + (side * lateral);
                instance.AddStamp(point, 0f, StampKind.Boot, 0.70f, 0.10f, 0.24f);
            }

            instance.Flush();
            instance.PublishGlobals();
        }

        private void AddStamp(Vector3d positionAstro, float yaw, StampKind kind,
            float depth, float mask, float halfSizeMeters)
        {
            Vector3d delta = positionAstro - originAstro;
            float u = (float)(Vector3d.Dot(delta, rightAstro) / SizeMeters) + 0.5f;
            float v = (float)(Vector3d.Dot(delta, forwardAstro) / SizeMeters) + 0.5f;
            float halfUv = halfSizeMeters / SizeMeters;
            if (u < -halfUv || u > 1f + halfUv || v < -halfUv || v > 1f + halfUv)
            {
                return;
            }

            pending.Add(new Stamp
            {
                U = u,
                V = v,
                Yaw = yaw,
                Depth = depth,
                Mask = mask,
                HalfSizeUv = halfUv,
                Kind = kind,
            });
        }

        private bool EnsureResources()
        {
            if (map == null)
            {
                map = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.RGHalf,
                    RenderTextureReadWrite.Linear)
                {
                    name = "RegolithTracks",
                    hideFlags = HideFlags.DontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    useMipMap = false,
                    autoGenerateMips = false,
                };
                map.Create();
            }

            if (stampMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("RegolithTrackStamp");
                if (shader == null)
                {
                    shader = Shader.Find("Galilego/RegolithTrackStamp");
                }

                if (shader == null)
                {
                    Debug.LogError("[RegolithTracks] Galilego/RegolithTrackStamp не найден: " +
                        "положи RegolithTrackStamp.shader в любую папку Resources.");
                    return false;
                }

                stampMaterial = new Material(shader)
                {
                    name = "RegolithTrackStamp",
                    hideFlags = HideFlags.DontSave,
                };
            }

            if (quad == null)
            {
                quad = BuildQuad();
            }

            if (commands == null)
            {
                commands = new CommandBuffer { name = "RegolithTracks" };
            }

            return true;
        }

        private void ClearMap()
        {
            commands.Clear();
            commands.SetRenderTarget(map, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            commands.ClearRenderTarget(false, true, new Color(0f, 0f, 0f, 0f));
            Graphics.ExecuteCommandBuffer(commands);
        }

        private void Flush()
        {
            if (pending.Count == 0 || map == null)
            {
                return;
            }

            commands.Clear();
            commands.SetRenderTarget(map, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            for (int i = 0; i < pending.Count; i++)
            {
                Stamp stamp = pending[i];
                var block = new MaterialPropertyBlock();
                block.SetVector(StampCenterId, new Vector4(
                    stamp.U, stamp.V, stamp.HalfSizeUv, Mathf.Cos(stamp.Yaw)));
                block.SetVector(StampParamsId, new Vector4(
                    Mathf.Sin(stamp.Yaw), stamp.Depth, stamp.Mask, (float)stamp.Kind));
                commands.DrawMesh(quad, Matrix4x4.identity, stampMaterial, 0, 0, block);
            }

            Graphics.ExecuteCommandBuffer(commands);
            pending.Clear();
        }

        private void PublishGlobals()
        {
            if (map == null)
            {
                return;
            }

            Vector3 originRender = FloatingOrigin.ToRender(originAstro);
            Shader.SetGlobalTexture(MapId, map);
            Shader.SetGlobalVector(OriginId, new Vector4(
                originRender.x, originRender.y, originRender.z, SizeMeters));
            Shader.SetGlobalVector(RightId, AstroFrame.ToSimulation(rightAstro));
            Shader.SetGlobalVector(ForwardId, AstroFrame.ToSimulation(forwardAstro));
            Shader.SetGlobalFloat(DepthId, PrintDepthMeters);
            Shader.SetGlobalFloat(TexelId, 1f / Resolution);
            Shader.SetGlobalFloat(TexelMetersId, SizeMeters / Resolution);
            Shader.SetGlobalFloat(LandingId, LandingRadiusMeters);
            Shader.SetGlobalFloat(OnId, 1f);
            enabledNow = true;
        }

        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "RegolithTrackQuad", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(-1f, 1f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }
    }
}
