using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Galilego.Universe
{
    /// <summary>
    /// Camera-facing stand-in for a body that is farther than the camera can draw.
    ///
    /// The terrain camera's far plane is the PHYSICAL horizon (≈300 km standing on
    /// Terra), so a moon at 83 000 km is never rasterised: its chunks are clipped and
    /// the body simply does not exist in the frame. This component keeps a quad at
    /// 0.28·far with the body's exact angular radius and a Lambert phase, so the disc
    /// looks like the real one without touching near/far (near/far ≈ 3·10^6 is what
    /// Doc/SkyDomes.md warns about).
    ///
    /// Same contract as GasGiantView's far disc: the cloud tops/surface take the body over
    /// as soon as they fit inside the far plane, and the material is premultiplied so it
    /// covers the star field without an extra opaque depth pass. The hand-over itself is a
    /// FADE, not a cut (see the Hand-over block in LateUpdate): a hard swap between the flat
    /// quad and the real sphere reads as a flash, and if the chunks of that view are still
    /// building it reads as a hole.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-30)]
    public sealed class BodyFarDiscView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Рендерер звезды сцены: даёт цвет поверхности светила для света диска. " +
            "Пусто — ищется в сцене при старте.")]
        public StarRenderer Star;

        [Tooltip("Камера игрока. Пусто = Camera.main.")]
        public Camera PlayerCamera;

        [Tooltip("Доля дальней плоскости камеры, на которой висит диск. 0.25 совпадает с " +
            "солнечным билбордом, 0.28 держит луну за ним (затмение рисует солнце сверху).")]
        public float FarFraction = 0.28f;

        [Tooltip("Минимальный радиус диска на экране, px. Физика тут бессильна: 0.57° при FOV 83° " +
            "и 720p — это 5 px, точка. Пол включается плавно по мере удаления и гаснет к моменту, " +
            "когда подхватывают настоящие чанки. 0 = строго физический размер.")]
        public float MinScreenRadiusPixels = 11f;

        [Tooltip("Расстояние до ПОВЕРХНОСТИ (в радиусах тела), на котором диск полностью уступает " +
            "рельефу. Внутри этого радиуса плоский билборд уже физически неверен (параллакс), и его " +
            "нельзя возвращать даже ради дыр в рельефе. 1.6 = 659 км над Artemis.")]
        public float HandoverRadii = 1.6f;

        [Tooltip("Расстояние до поверхности (в радиусах тела), с которого начинается плавное " +
            "растворение диска в рельефе. Между ним и HandoverRadii диск полупрозрачный: игрок не " +
            "видит переключения низкополигональной подмены на настоящий рельеф, обе картинки " +
            "смешиваются. 0 = резкая отсечка (старое поведение).")]
        public float FadeStartRadii = 4f;

        [Tooltip("Доля застроенного вида, начиная с которой рельеф считается готовым принять " +
            "тело у диска. Меньше единицы — допускаем пару непокрытых дальних узлов (у них есть " +
            "родитель-затычка). Диск держится, пока вид НЕ покрыт: холодный старт на новом теле " +
            "иначе показывает половину сферы и дыру вместо неё.")]
        [Range(0.5f, 1f)]
        public float HandoverCoverage = 0.98f;

        [Tooltip("Компенсация яркости при раздувании диска. 1 = поверхностная яркость сохраняется " +
            "(луна просто крупнее), 0.08 = сохраняется полный поток (крупный диск тускнеет).")]
        public float MinEnergyScale = 1f;

        [Tooltip("Яркость диска в масштабе сцены (albedo·ndl·_TerrainRadianceScale) — том же, в " +
            "котором считает рельеф. 1 = диск так же ярок, как собственный рельеф тела под ним: " +
            "переход диска в чанки не меняет яркость, а диск освещён тем же светилом.")]
        public float Brightness = 1f;

        [Range(1f, 64f)]
        [Tooltip("Ночной перцептивный подъём диска, множитель. Физический диск ночью — " +
            "правильная яркость, но сцена живёт в 14 EV лунной адаптации (MoonlightSystem), и " +
            "на её фоне он читается тусклой монетой. Включается по высоте Солнца: днём 1. " +
            "1 = диск строго физически и днём, и ночью.")]
        public float NightBrightness = 24f;

        [Tooltip("Высота наблюдателя над поверхностью текущего тела, ниже которой ночной подъём " +
            "полный. Подъём компенсирует НОЧНУЮ АДАПТАЦИЮ СЦЕНЫ (MoonlightSystem поднимает лунный " +
            "пейзаж на 14 EV), а адаптируется только пейзаж под наблюдателем: у неба над головой " +
            "ночи нет. 100 км — верх атмосферы, ниже него земля и есть кадр.")]
        public float NightBoostFullAltitudeMeters = 100000f;

        [Tooltip("Высота над поверхностью текущего тела, на которой ночной подъём выключен " +
            "полностью. Между ней и предыдущей — плавно. Нужна, потому что _SkySunDir·_SkyUp — это " +
            "СОЛНЦЕ ОТНОСИТЕЛЬНО ТЕКУЩЕГО ТЕЛА, а тело меняется на границе SOI: с ночной стороны " +
            "Terra на дневную сторону Артемиды sinEl перескакивает с −0.28 на +0.25, и диск " +
            "прыгал на NightBrightness (24×, 4.6 EV) посреди полёта.")]
        public float NightBoostFadeAltitudeMeters = 2000000f;

        [Range(0f, 1f)]
        [Tooltip("Доля атмосферы МЕЖДУ глазом и диском: 1 = ровно тот воздух, которым " +
            "нарисовано небо (физика), 0 = вакуум. Держать 1: дымка перед Луной — это и есть " +
            "небо за ней, поэтому ослабление делает неосвещённую половину темнее неба, и диск " +
            "снова выглядит ночным. «Меньше дымки» настраивается яркостью (Brightness).")]
        public float HazeStrength = 1f;

        [Range(0f, 1f)]
        [Tooltip("Затемнение к краю: у безатмосферного тела лимб не чёрный, но и не плоский.")]
        public float LimbDarkening = 0.35f;

        [Tooltip("Альбедо диска в линейном пространстве. При UseTerrainPalette берётся " +
            "из палитры профиля рельефа этого тела.")]
        public Color Albedo = new Color(0.5f, 0.49f, 0.47f);

        [Tooltip("Взять альбедо из BodyAuthoring.TerrainPreset (палитра Snow).")]
        public bool UseTerrainPalette = true;

        [Tooltip("Рисовать диск ТОЛЬКО когда настоящая сфера не рисуется (тело за дальней " +
            "плоскостью камеры или позади неё). Для планеты с рельефом это правильный режим: " +
            "видимый меш всегда лучше плоского квада, а диск — запасное представление на " +
            "случай, когда чанки обрезаны far-плоскостью. У Артемиды выключено: там диск " +
            "сознательно держится до 4R ради стабильной яркости и карты.")]
        public bool OnlyWhenMeshMissing;

        [Tooltip("Испечь цветовую карту альбедо из собственного рельефа тела " +
            "(HeightfieldTerrain + палитра), если SurfaceMap не назначена. Один раз, на " +
            "фоновом потоке, только когда диск впервые понадобился. Без неё диск Земли — " +
            "серый шар: у атмосферной планеты нет готовой macro-карты, как у Луны.")]
        public bool BakeTerrainAlbedo;

        [Tooltip("SurfaceMap — ЦВЕТНАЯ карта альбедо (RGB, sRGB), а не скалярная карта " +
            "яркости лунного грунта (R×2). Включается автоматически для испечённой карты.")]
        public bool SurfaceMapIsColor;

        [Tooltip("Карта альбедо в телесных координатах (maria/нагорья). Без неё диск — ровный шар.")]
        public Texture2D SurfaceMap;

        [Range(0f, 1f)]
        [Tooltip("Сила карты альбедо.")]
        public float SurfaceMapStrength = 1f;

        [Tooltip("Карта нормалей ПОВЕРХНОСТИ в телесных координатах (xyz = n*0.5+0.5). " +
            "Без неё диск — плоский раскрашенный круг: кратеры и валы видны только тенью, " +
            "то есть никак. Пекётся вместе с альбедо (Tools/Galilego/Airless Bodies/Bake lunar macro maps).")]
        public Texture2D NormalMap;

        [Range(0f, 1f)]
        [Tooltip("Сила рельефа диска: 1 = нормаль карты целиком, 0 = гладкий шар.")]
        public float NormalStrength = 1f;

        [Range(0f, 0.2f)]
        [Tooltip("Постоянная подсветка диска (доля яркости). Держать 0: у безатмосферного тела " +
            "нет чем светить ночную сторону, а ненулевой ambient рисовал на ней светлое альбедо " +
            "кратеров — «солнце не светит, а участки яркие» в полосе смешивания с рельефом.")]
        public float Ambient = 0f;

        private OrbitingBody body;
        private OrbitingBody star;
        private Transform disc;
        private Material material;
        private PlanetSurfaceRenderer surface;
        private HeightfieldTerrain terrain;
        private System.Threading.Tasks.Task<Color32[]> albedoTask;
        private bool albedoFailed;
        private Texture2D bakedAlbedo;
        private static readonly int TerrainRadianceScaleId = Shader.PropertyToID("_TerrainRadianceScale");
        private static readonly int SkySunDirId = Shader.PropertyToID("_SkySunDir");
        private static readonly int SkyUpId = Shader.PropertyToID("_SkyUp");
        private Vector3d LastBodyPosition;
        private double LastRadius;
        private float LastFar;
        private float LastPixelRadius;
        private float LastOpacity = 1f;
        /// <summary>How strongly the disc is held up because the terrain is incomplete.</summary>
        private float hold;

        private void Start()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (Runner == null || Runner.SystemState == null)
            {
                enabled = false;
                return;
            }

            foreach (OrbitingBody candidate in Runner.SystemState.AllBodies)
            {
                if (candidate.Name == gameObject.name)
                {
                    body = candidate;
                    break;
                }
            }

            if (body == null)
            {
                Debug.LogWarning("BodyFarDiscView: тело \"" + gameObject.name + "\" не найдено в системе — диск отключён.");
                enabled = false;
                return;
            }

            star = Runner.SystemState.Root;

            // A heightfield body can bake its own colour map: the far disc then shows the
            // same ocean/land/ice the terrain does instead of a flat grey ball. The bake
            // itself is lazy (first frame the disc is actually needed).
            terrain = body.Terrain as HeightfieldTerrain;

            if (Star == null)
            {
                Star = FindFirstObjectByType<StarRenderer>();
            }

            // The surface renderer of the same body: its build progress decides when the
            // billboard may fade out (see the hand-over block in LateUpdate).
            surface = GetComponent<PlanetSurfaceRenderer>();
            if (surface == null || !surface.enabled)
            {
                surface = null;
            }

            BodyAuthoring authoring = GetComponent<BodyAuthoring>();
            if (UseTerrainPalette && authoring != null && authoring.TerrainPreset != null
                && authoring.TerrainPreset.Profile != null && authoring.TerrainPreset.Profile.Palette != null)
            {
                Albedo = authoring.TerrainPreset.Profile.Palette.SnowLinear;
            }

            Shader shader = Shader.Find("Galilego/BodyFarDisc");
            if (shader == null)
            {
                Debug.LogWarning("BodyFarDiscView: нет шейдера Galilego/BodyFarDisc — диск отключён.");
                enabled = false;
                return;
            }

            GameObject go = new GameObject(name + "FarDisc");
            go.hideFlags = HideFlags.DontSave;
            disc = go.transform;
            disc.SetParent(null, false);

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildQuad();

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            material = new Material(shader) { name = name + "FarDisc (runtime)" };
            material.SetColor("_BFD_Albedo", Albedo);
            material.SetFloat("_BFD_LimbDarkening", LimbDarkening);
            material.SetFloat("_BFD_Ambient", Ambient);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            disc.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (disc != null)
            {
                Destroy(disc.gameObject);
            }

            if (material != null)
            {
                Destroy(material);
            }

            if (bakedAlbedo != null)
            {
                Destroy(bakedAlbedo);
                bakedAlbedo = null;
            }
        }

        private void LateUpdate()
        {
            if (body == null || star == null || disc == null || Runner == null || Runner.SystemState == null)
            {
                return;
            }

            Camera camera = PlayerCamera != null ? PlayerCamera : Camera.main;
            if (camera == null)
            {
                SetActive(false);
                return;
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d toBody = bodyPos - Runner.PlayerPosition;
            double distance = toBody.Magnitude;

            float far = Mathf.Max(1f, camera.farClipPlane);
            double nearSurface = distance - body.Radius;
            if (!(distance > 1d))
            {
                SetActive(false);
                return;
            }

            float angularRadius = Mathf.Atan2((float)body.Radius, (float)distance);
            float fovRadians = Mathf.Max(0.01f, camera.fieldOfView) * Mathf.Deg2Rad;
            float pixelRadius = angularRadius / Mathf.Tan(fovRadians * 0.5f) * (Screen.height * 0.5f);
            float floorPixels = Mathf.Max(pixelRadius, MinScreenRadiusPixels);
            LastBodyPosition = bodyPos;
            LastRadius = body.Radius;
            LastFar = far;
            LastPixelRadius = pixelRadius;

            // ---- Hand-over to the real chunks -------------------------------------
            // The disc stands in for a body the camera cannot draw OR draws so small that
            // it reads as a star, and it must give the body to the terrain WITHOUT a visible
            // switch. Two independent reasons keep it up:
            //
            //  * distance — a flat quad at a fixed depth and the real sphere never match in
            //    parallax, so inside FadeStartRadii the disc starts blending into the
            //    terrain and at HandoverRadii it is gone;
            //  * readiness — chunks of a body arrive asynchronously (its first view is
            //    hundreds of tiles). If the disc went away on distance alone the player
            //    would watch the sphere assemble out of a hole. ViewCoverage lets us hold
            //    it until the surface renderer has the view.
            float surfaceRendererCoverage = 1f;
            bool meshDrawn = false;
            if (surface != null && surface.isActiveAndEnabled)
            {
                surfaceRendererCoverage = surface.ViewCoverage;
                meshDrawn = surface.SurfaceVisible;
            }

            // The lazy colour map is only worth baking when the disc is the body's ONLY
            // representation. On the ground the real chunks are drawn and the bake would
            // cost a startup hitch for a texture the player never sees.
            if (!meshDrawn && BakeTerrainAlbedo && SurfaceMap == null && !albedoFailed)
            {
                EnsureTerrainAlbedo();
            }

            float handoverMeters = (float)(body.Radius * System.Math.Max(1f, HandoverRadii));
            float fadeStartMeters = (float)(body.Radius * System.Math.Max(HandoverRadii, FadeStartRadii));

            float opacity;
            if (nearSurface <= handoverMeters)
            {
                // The billboard's flat parallax is plainly wrong this close: the near
                // terrain is what the player has to see. Never resurrect the disc here,
                // even if chunks are still missing.
                opacity = 0f;
            }
            else if (!meshDrawn)
            {
                // No real geometry can be rasterised (the body is behind the camera or
                // beyond its far plane): the disc IS the body, at its true angular size and
                // phase. Keep the hold primed, so the first frame the mesh comes back waits
                // for coverage instead of flashing a hole.
                hold = 1f;
                opacity = 1f;
            }
            else if (OnlyWhenMeshMissing)
            {
                // The real sphere is drawn: it is at the true position with the real
                // shader, so it wins at any distance. But it arrives ASYNCHRONOUSLY (a
                // first view is many chunks): hold the disc until the mesh covers the
                // view, then fade it out. Turning it off the frame the mesh appears
                // would show the planet as a hole for the first seconds of the approach.
                bool coverReady = surfaceRendererCoverage >= HandoverCoverage;
                float holdTarget = coverReady ? 0f : 1f;
                hold = holdTarget > hold
                    ? holdTarget
                    : Mathf.MoveTowards(hold, holdTarget, Time.deltaTime * 1.5f);
                opacity = hold;
            }
            else
            {
                float distanceFade = Mathf.InverseLerp(handoverMeters, fadeStartMeters, (float)nearSurface);

                // Hold the disc while the terrain for this view is NOT covered by geometry
                // (no built ancestor at all — a half-arrived sphere, black gaps between
                // chunks). Released smoothly so the hand-over is a blend, not a cut: rising
                // is immediate (a hole must never be shown), falling takes ~0.7 s.
                bool coverReady = surfaceRendererCoverage >= HandoverCoverage;
                float holdTarget = coverReady ? 0f : 1f;
                hold = holdTarget > hold
                    ? holdTarget
                    : Mathf.MoveTowards(hold, holdTarget, Time.deltaTime * 1.5f);

                opacity = Mathf.Clamp01(Mathf.Max(distanceFade, hold));
            }

            // The quad must sit IN FRONT of the body it represents, or the near terrain
            // slices it (far*0.28 lands inside a body at low altitude). The 0.55 cap also
            // keeps the billboard in front when `far` is the horizon of ANOTHER body.
            float discDistance = Mathf.Min(far * FarFraction, (float)(nearSurface * 0.55d));
            LastOpacity = opacity;
            if (opacity <= 0.002f || discDistance < 1f)
            {
                SetActive(false);
                return;
            }

            Vector3 simDirection = AstroFrame.ToSimulation(toBody / distance);
            Vector3 cameraPosition = camera.transform.position;
            disc.position = cameraPosition + (simDirection * discDistance);

            Vector3 up = Vector3.ProjectOnPlane(camera.transform.up, simDirection);
            if (up.sqrMagnitude < 1e-8f)
            {
                up = Vector3.ProjectOnPlane(Vector3.up, simDirection);
            }

            if (up.sqrMagnitude < 1e-8f)
            {
                up = Vector3.ProjectOnPlane(Vector3.right, simDirection);
            }

            disc.rotation = Quaternion.LookRotation(simDirection, up.normalized);

            float size = discDistance * 2f * Mathf.Tan(angularRadius) * 1.02f;

            // Subpixel discs flicker; the floor keeps them readable, and MinEnergyScale
            // decides whether the extra area costs surface brightness or total flux.
            float pixelScale = pixelRadius > 1e-6f ? Mathf.Max(1f, floorPixels / pixelRadius) : 1f;
            disc.localScale = new Vector3(size * pixelScale, size * pixelScale, 1f);
            float energyScale = Mathf.Max(MinEnergyScale, 1f / (pixelScale * pixelScale));

            star.EvaluateWorldState(Runner.TimeSeconds, out Vector3d starPos, out _);
            Vector3 sunDirection = AstroFrame.ToSimulation((starPos - bodyPos).Normalized);

            // Any body can hide the Sun from this one: Terra from Artemis, a moon
            // from its planet, a second moon from the first. The geometry is shared
            // with the star disc, the sky and MoonlightSystem, so the disc cannot
            // show a full moon over a black ground (or the reverse) at the same time.
            OrbitingBody eclipseOccluder = null;
            double eclipse = Runner.SystemState != null
                ? Runner.SystemState.StarVisibilityForBody(body, Runner.TimeSeconds, out eclipseOccluder)
                : StarOcclusion.VisibilityForBody(body, Runner.TimeSeconds, out eclipseOccluder);

            // The copper "blood moon" tint is refraction through the occluder's
            // atmosphere, not a generic shadow. An airless occluder leaves a neutral
            // dim disc; a small occluder that only clips the Sun does too.
            bool copperEclipse = false;
            if (eclipseOccluder != null && eclipseOccluder.Atmosphere != null)
            {
                eclipseOccluder.EvaluateWorldState(Runner.TimeSeconds, out Vector3d occluderPos, out _);
                double separation = (occluderPos - bodyPos).Magnitude;
                copperEclipse = separation > 0d
                    && EclipseGeometry.CoversBody(body.Radius, eclipseOccluder.Radius, separation);
            }

            Runner.SystemState.GetVisualFrame(body, Runner.TimeSeconds, out _, out _, out QuaternionD orientation);
            Quaternion bodyRotation = AstroFrame.ToSimulation(orientation);

            // A colour albedo map already carries the surface colour: the material albedo
            // is a fallback before the (lazy) map lands, so it must not tint the map.
            bool colorMap = SurfaceMap != null && SurfaceMapIsColor;
            material.SetColor("_BFD_Albedo", colorMap ? Color.white : Albedo);
            material.SetFloat("_BFD_MapColor", colorMap ? 1f : 0f);
            material.SetVector("_BFD_SunDirWS", ToVec(sunDirection));
            material.SetVector("_BFD_RightWS", disc.right);
            material.SetVector("_BFD_UpWS", disc.up);
            material.SetVector("_BFD_ViewWS", ToVec(-simDirection));
            material.SetFloat("_BFD_IntensityScale", Brightness * NightGain() * energyScale);

            // The disc is lit by the STAR, not by the observer's sky: its sunlight
            // crossed no atmosphere the observer can see (the Moon is 83 000 km up),
            // and the ray from the observer to the disc carries the air separately in
            // the shader. _TerrainRadianceScale is the scene's "albedo·ndl → radiance"
            // factor, so the disc and the body's own relief share a scale.
            Vector3d starLinear = Star != null && Star.IsReady ? Star.LightColorLinear : new Vector3d(1d, 1d, 1d);
            float terrainScale = Shader.GetGlobalFloat(TerrainRadianceScaleId);
            material.SetVector("_BFD_SunColor",
                new Vector4((float)starLinear.X, (float)starLinear.Y, (float)starLinear.Z, 0f));
            material.SetFloat("_BFD_SunScale", terrainScale > 0f ? terrainScale : 1f);
            material.SetFloat("_BFD_Haze", Mathf.Clamp01(HazeStrength));
            material.SetFloat("_BFD_DiscDistance", discDistance);
            material.SetFloat("_BFD_LimbDarkening", LimbDarkening);
            material.SetFloat("_BFD_Ambient", Ambient);
            material.SetFloat("_BFD_Eclipse", (float)eclipse);
            material.SetFloat("_BFD_EclipseRed", copperEclipse ? 1f : 0f);
            material.SetTexture("_BFD_Map", SurfaceMap);
            material.SetFloat("_BFD_MapStrength", SurfaceMap != null ? SurfaceMapStrength : 0f);
            material.SetTexture("_BFD_NormalMap", NormalMap);
            material.SetFloat("_BFD_NormalStrength", NormalMap != null ? NormalStrength : 0f);
            material.SetFloat("_BFD_Opacity", opacity);
            material.SetFloat("_BFD_AngularRadius", angularRadius);
            // The body->world rotation as its three columns: a Matrix property cannot be
            // declared in ShaderLab, so SetMatrix on an undeclared uniform was dropped
            // ("Material ... doesn't have a matrix property") and the disc's body-fixed
            // map lookup kept an identity — the relief/maria rotated with the world.
            material.SetVector("_BFD_BodyRight", bodyRotation * Vector3.right);
            material.SetVector("_BFD_BodyUp", bodyRotation * Vector3.up);
            material.SetVector("_BFD_BodyForward", bodyRotation * Vector3.forward);

            SetActive(true);
        }

        /// <summary>
        /// Lazy equirectangular ALBEDO of this body's own terrain, built once on a worker
        /// the first time the disc is the only representation (the real chunks are clipped
        /// by the camera far plane — a planet seen from another body's surface). 512×256
        /// is enough: Terra from Artemis is ~15 px across, and the texture is only the
        /// surface under the disc, not a close-up. The palette rules are the same ones
        /// the terrain uses (TerrainPalette.SurfaceColor), so the disc and the real
        /// ground cannot disagree about where the ocean and the ice are.
        /// </summary>
        private void EnsureTerrainAlbedo()
        {
            if (terrain == null)
            {
                albedoFailed = true;
                return;
            }

            if (albedoTask == null)
            {
                const int width = 512;
                const int height = 256;
                TerrainMacroAlbedo.Input input = TerrainMacroAlbedo.Capture(terrain, body.Radius, width, height);
                if (input == null)
                {
                    albedoFailed = true;
                    return;
                }

                albedoTask = System.Threading.Tasks.Task.Run(() => TerrainMacroAlbedo.Build(input));
                return;
            }

            if (!albedoTask.IsCompleted)
            {
                return;
            }

            Color32[] pixels = albedoTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                ? albedoTask.Result
                : null;
            albedoTask = null;
            if (pixels == null)
            {
                albedoFailed = true;
                return;
            }

            var texture = new Texture2D(512, 256, TextureFormat.RGB24, false, false)
            {
                name = name + "DiscAlbedo (runtime)",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            bakedAlbedo = texture;
            SurfaceMap = texture;
            SurfaceMapIsColor = true;
            if (material != null)
            {
                material.SetTexture("_BFD_Map", texture);
                material.SetFloat("_BFD_MapStrength", 1f);
                material.SetFloat("_BFD_MapColor", 1f);
            }
        }

        private void SetActive(bool active)
        {
            if (disc != null && disc.gameObject.activeSelf != active)
            {
                disc.gameObject.SetActive(active);
                Debug.Log("[BodyFarDisc] " + name + (active ? " ON" : " OFF")
                    + " | alt=" + (((Runner.PlayerPosition - LastBodyPosition).Magnitude - LastRadius) / 1000d).ToString("F0")
                    + " km far=" + (LastFar / 1000f).ToString("F0")
                    + " km pxR=" + LastPixelRadius.ToString("F2")
                    + " floor=" + MinScreenRadiusPixels.ToString("F1")
                    + " handover=" + HandoverRadii.ToString("F2")
                    + "R opacity=" + LastOpacity.ToString("F2"));
            }
        }

        private static Vector4 ToVec(Vector3 v)
        {
            return new Vector4(v.x, v.y, v.z, 0f);
        }

        /// <summary>
        /// Perceptual gain for the dark half of the day. Daylight is physical: the disc
        /// is a sunlit airless surface and the atmosphere in front of it does the rest.
        /// At night the scene is NOT physical — MoonlightSystem lifts the ground by
        /// 14 EV because a strictly physical moonlit landscape is black — so the same
        /// physical disc would be the only object at its true brightness and would
        /// read as a dim coin.
        ///
        /// The signal is the SUN's elevation, not the sky's brightness: on an airless
        /// body (or in flight) the sky is black at noon too, and _StarVisibility there
        /// says "vacuum", not "night". The ramp sits below civil twilight, where the
        /// sky still lights the ground and the disc is a real silhouette in it.
        ///
        /// It is also gated by the observer's ALTITUDE over the current body (see
        /// LandscapeFactor): the gain compensates a night-adapted LANDSCAPE, and space
        /// has none. Without that gate the gain follows the camera body, which changes
        /// at an SOI boundary — flying from Terra's night side to Artemis' day side
        /// flipped sinEl from −0.28 to +0.25 and the disc jumped 24× (4.6 EV) halfway
        /// to the Moon: "the Moon changes colour abruptly on approach".
        /// </summary>
        private float NightGain()
        {
            Vector3 sunDir = Shader.GetGlobalVector(SkySunDirId);
            Vector3 up = Shader.GetGlobalVector(SkyUpId);
            float sinElevation = Vector3.Dot(sunDir, up);
            float night = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.10f, -0.21f, sinElevation));
            if (night <= 0f)
            {
                return 1f;
            }

            float weight = night * LandscapeFactor();
            return Mathf.Lerp(1f, Mathf.Max(1f, NightBrightness), weight);
        }

        /// <summary>
        /// 1 on the ground, 0 in space. The night boost exists because the scene's
        /// exposure is adapted to a moonlit landscape; there is no landscape in space,
        /// so a sunlit body there is exactly as bright as it physically is. This also
        /// makes the boost independent of which body owns the camera: the altitude is
        /// large on both sides of an SOI boundary, so the value cannot jump there.
        /// </summary>
        private float LandscapeFactor()
        {
            float full = NightBoostFullAltitudeMeters;
            float none = NightBoostFadeAltitudeMeters;
            if (none <= full || Runner == null || Runner.SystemState == null)
            {
                return 1f;
            }

            OrbitingBody cameraBody = Runner.CameraBody;
            if (cameraBody == null)
            {
                return 1f;
            }

            cameraBody.EvaluateWorldState(Runner.TimeSeconds, out Vector3d center, out _);
            double altitude = (Runner.PlayerPosition - center).Magnitude - cameraBody.Radius;
            double t = (altitude - full) / (none - full);
            if (t <= 0d)
            {
                return 1f;
            }

            return t >= 1d ? 0f : 1f - (float)t;
        }

        /// <summary>Unit quad centred on the origin, UV 0..1 — the disc is cut in the shader.</summary>
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "BodyFarDiscQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
