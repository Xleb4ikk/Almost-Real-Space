using System;
using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>Who is looking at the star: a living eye or a camera lens.</summary>
    public enum ObserverMode
    {
        /// <summary>
        /// A living observer: round pupil, no lens elements, no coatings. Strong
        /// intraocular veiling glare; no aperture ghosts and no diffraction star.
        /// </summary>
        HumanEye = 0,

        /// <summary>
        /// A camera/lens: polygonal iris, internal reflections between coated
        /// elements, diffraction star. Only for views through an instrument.
        /// </summary>
        CameraLens = 1,
    }

    /// <summary>One aperture ghost: a pair of lens surfaces reflecting the source.</summary>
    [Serializable]
    public struct LensGhost
    {
        [Tooltip("Рисовать этот призрак.")]
        public bool Enabled;

        [Tooltip("Отражение вдоль оптической оси: центр призрака = −k·(центр звезды), " +
                 "в долях NDC от центра кадра. 1 = точное зеркальное отражение. " +
                 "Реальная линза даёт пузыри на k ≈ 0.3…1.5.")]
        public double Reflection;

        [Tooltip("Угловой радиус изображения диафрагмы, градусы. Задаёт размер и форму пузыря.")]
        public double RadiusDegrees;

        [Tooltip("Произведение коэффициентов отражения двух поверхностей R₁·R₂. " +
                 "Без просветления воздух–стекло даёт ~0.04 на поверхность (0.0016 на пару), " +
                 "с просветлением — 0.001…0.01.")]
        public double Reflectance;

        [Tooltip("Цвет просветляющего покрытия (линейный). Призрак наследует цвет покрытия, " +
                 "а не фотосферы: так выглядят зелёные/фиолетовые/янтарные пузыри.")]
        public Color Tint;

        [Tooltip("Поворот изображения диафрагмы, градусы: ориентация граней этого пузыря.")]
        public double RotationDegrees;
    }

    /// <summary>
    /// Light that the OBSERVER'S OPTICS add around the main star — the veiling
    /// glare and the corona that belong to the observer, not to the star (the disc,
    /// the atmospheric aureole and the stellar corona are StarRenderer's job).
    ///
    /// Two observers, because they are physically different:
    ///  * HumanEye (default) — a living being. Intraocular stray light, normalised
    ///    to the star's rendered radiance: a strong soft glare that washes out the
    ///    sky next to the Sun out to tens of degrees. The pupil is round and there
    ///    are no lens elements, so there are NO aperture ghosts, no hexagonal bokeh
    ///    and no diffraction star.
    ///  * CameraLens — a view through an instrument: flux-conserving veiling glare,
    ///    aperture ghosts reflected through the optical axis and the diffraction
    ///    star of the iris blades (λ/D wide, N-blade symmetric).
    ///
    /// One full-screen additive custom pass at BeforePostProcess. Every amplitude
    /// comes from <see cref="LensOptics"/> and the star's rendered disc radiance, so
    /// the glare scales with the actual sky brightness, the atmosphere's
    /// transmittance, the star's angular size and the exposure. Occlusion is the
    /// same depth test the star proxies use (GalilegoStarOcclusion.hlsl): an opaque
    /// hull in front of the star kills the glare, a strut crossing the disc dims it.
    ///
    /// Created automatically by <see cref="StarRenderer"/> (OpticalFlare) on the
    /// same GameObject; can also be placed by hand in any scene.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    [DisallowMultipleComponent]
    public sealed class StarLensFlare : MonoBehaviour
    {
        [Header("Сцена")]
        [Tooltip("SimulationRunner сцены. Пусто — найдётся сам.")]
        public SimulationRunner Runner;

        [Tooltip("Камера игрока. Пусто — Camera.main.")]
        public Camera PlayerCamera;

        [Tooltip("Физический рендер звезды: источник яркости и углового размера. Пусто — на этом же объекте.")]
        public StarRenderer Star;

        [Tooltip("Физика неба: атмосферная прозрачность к звезде. Пусто — найдётся сам.")]
        public SkyEnvironment Sky;

        [Header("Наблюдатель")]
        [Tooltip("Глаз (по умолчанию) — рассеяние в среде глаза: сильная мягкая вуаль, никаких призраков " +
                 "диафрагмы; цилиарная корона только у точечных источников. Объектив камеры — диафрагма, " +
                 "призраки и дифракционные лучи: только если игрок смотрит через прибор, а не глазами.")]
        public ObserverMode Mode = ObserverMode.HumanEye;

        [Header("Глаз наблюдателя (режим HumanEye)")]
        [Tooltip("Доля яркости диска звезды, которая доходит до глаза рассеянной на 1° от неё. " +
                 "Молодой здоровый глаз ≈0.10–0.15, с возрастом 0.2–0.4 (van den Berg).")]
        public double EyeGlare = 0.07d;

        [Tooltip("Угловой радиус ядра глазной вуали θ0, градусы: плотное свечение у диска. " +
                 "Работает и в вакууме — это рассеяние в самом глазу.")]
        public double EyeGlareTheta0Degrees = 0.8d;

        [Tooltip("Спад ядра глазной вуали. 2.0–2.6 — стандартная GSF глаза.")]
        public double EyeGlareBeta = 2.4d;

        [Tooltip("Доля глазной вуали в широкой составляющей: дымка ВОЗДУХА перед глазом (аэрозоль " +
                 "и молекулярное рассеяние в столбе между глазом и звездой). В вакууме гаснет — " +
                 "остаётся только плотное ядро.")]
        public double EyeGlareWideShare = 0.30d;

        [Tooltip("Угловой радиус широкой глазной вуали θ0, градусы.")]
        public double EyeGlareWideTheta0Degrees = 4d;

        [Tooltip("Спад широкой глазной вуали. Меньше — шире и мягче дымка.")]
        public double EyeGlareWideBeta = 1.6d;

        [Tooltip("Угловой радиус ядра PSF глаза, градусы: источник меньше него отдаёт глазу только поток, " +
                 "поэтому вуаль масштабируется как (α/α_psf)². Солнце (0.5–1.5°) много больше ядра.")]
        public double EyePointSpreadDegrees = 0.1d;

        [Tooltip("Множитель вуали в скафандре (EVA): щиток шлема рассеивает сильнее глаза, " +
                 "а его микроцарапины дают заметно более широкий ореол.")]
        public double HelmetGlareMultiplier = 1.6d;

        [Header("Объектив камеры (только режим CameraLens)")]
        [Tooltip("Фокусное расстояние, мм. Вместе с диафрагмой задаёт диаметр входного зрачка D = f/N.")]
        public double FocalLengthMm = 24d;

        [Tooltip("Относительное отверстие f/N. Масштаб дифракции λ/D и площадь зрачка.")]
        public double FNumber = 8d;

        [Tooltip("Число лепестков диафрагмы. Чётное N — N лучей, нечётное — 2N; меньше 3 — круглый зрачок " +
                 "(нет лучей, призраки круглые).")]
        public int ApertureBlades = 6;

        [Tooltip("Поворот диафрагмы, градусы: направление лучей и граней призраков.")]
        public double BladePhaseDegrees = 0d;

        [Tooltip("Длина волны дифракции, нм. 550 — зелёный максимум глаза.")]
        public double WavelengthNm = 550d;

        [Tooltip("Пиковая яркость лучей относительно диска звезды. Физически лучи у Солнца субпиксельные " +
                 "при 1080p, поэтому сила вынесена отдельно от геометрии (ширина и число лучей — физические).")]
        public double DiffractionStrength = 0.06d;

        [Tooltip("Минимальная ширина луча, пикселей: субпиксельный луч иначе рассыпается в пунктир.")]
        public double DiffractionMinWidthPixels = 0.7d;

        [Tooltip("Длина лучей, градусы (дальше яркость падает как 1/θ и гаснет).")]
        public double DiffractionExtentDegrees = 8d;

        [Header("Вуаль объектива (только CameraLens)")]
        [Tooltip("Полная доля потока звезды, которую оптика рассеивает в вуаль. 0.01–0.03 — чистая линза, " +
                 "0.05–0.15 — царапанный щиток/грязный передний элемент.")]
        public double VeilingGlare = 0.03d;

        [Tooltip("Угловой радиус ядра вуали θ0, градусы: плотный ореол у самого диска.")]
        public double VeilTheta0Degrees = 0.6d;

        [Tooltip("Показатель спада крыльев ядра ω ∝ 1/θ^(2β). 2.6 — фотографическая GSF, " +
                 "1.5 — широкий ореол.")]
        public double VeilBeta = 2.6d;

        [Tooltip("Доля вуали в широкой составляющей (рассеяние в толще стекла и на покрытии): " +
                 "вторая, далеко тянущаяся дымка помимо плотного ядра.")]
        public double VeilWideShare = 0.45d;

        [Tooltip("Угловой радиус широкой вуали θ0, градусы.")]
        public double VeilWideTheta0Degrees = 2.5d;

        [Tooltip("Показатель спада широкой вуали. 1.6 — пологая дымка.")]
        public double VeilWideBeta = 1.6d;

        [Header("Призраки диафрагмы (только CameraLens)")]
        [Tooltip("Пузыри диафрагмы. Каждый — пара поверхностей линзы; порядок в массиве не важен.")]
        public LensGhost[] Ghosts = DefaultGhosts();

        [Tooltip("Общий множитель призраков. 1 — коэффициенты отражения из массива выше; больше — " +
                 "компенсация логарифмического сжатия диска V' (см. Docs/StarRender.md).")]
        public double GhostGain = 6d;

        [Tooltip("Подсветка края пузыря: коэффициент отражения просветления растёт с углом падения, " +
                 "поэтому реальный призрак ярче к краю зрачка. 0 — плоский, 0.5 — как у просветлённой оптики.")]
        public double GhostRim = 0.5d;

        [Tooltip("Хроматическое уширение края призрака: синий пузырь чуть больше красного.")]
        public double ChromaticFringe = 0.012d;

        [Header("Отладка")]
        [Tooltip("0 — обычный вид; 1 — покрытие диска (видимость); 2 — только вуаль; 3 — только призраки; " +
                 "4 — только лучи.")]
        [Range(0, 4)]
        public int DebugMode;

        [Tooltip("HUD с параметрами блика.")]
        public bool DebugHud;

        private const int MaxGhosts = 6;
        private const double DegToRad = Math.PI / 180d;

        private CustomPassVolume volume;
        private FullScreenCustomPass pass;
        private Material material;
        private Camera boundCamera;
        private readonly Vector4[] ghostPose = new Vector4[MaxGhosts];
        private readonly Vector4[] ghostColor = new Vector4[MaxGhosts];
        private int ghostCount;

        private static readonly int SunId = Shader.PropertyToID("_FlareSun");
        private static readonly int ScreenId = Shader.PropertyToID("_FlareScreen");
        private static readonly int ColorId = Shader.PropertyToID("_FlareColor");
        private static readonly int VeilId = Shader.PropertyToID("_FlareVeil");
        private static readonly int VeilWideId = Shader.PropertyToID("_FlareVeilWide");
        private static readonly int StarId = Shader.PropertyToID("_FlareStar");
        private static readonly int GhostPoseId = Shader.PropertyToID("_FlareGhostPose");
        private static readonly int GhostColorId = Shader.PropertyToID("_FlareGhostColor");
        private static readonly int GhostCountId = Shader.PropertyToID("_FlareGhostCount");
        private static readonly int ChromaId = Shader.PropertyToID("_FlareChroma");
        private static readonly int GhostRimId = Shader.PropertyToID("_FlareGhostRim");
        private static readonly int BladePhaseId = Shader.PropertyToID("_FlareBladePhase");
        private static readonly int DebugId = Shader.PropertyToID("_FlareDebug");

        /// <summary>
        /// Physical defaults for a 24 mm f/8 lens with a 6-blade iris. Reflectances
        /// are pairs of AR-coated surfaces (0.1–1 %), radii are defocus sizes of a
        /// real multi-element lens, tints are the coatings' residual colours.
        /// </summary>
        public static LensGhost[] DefaultGhosts()
        {
            return new[]
            {
                new LensGhost
                {
                    Enabled = true,
                    Reflection = 0.35d,
                    RadiusDegrees = 1.5d,
                    Reflectance = 5e-3d,
                    Tint = new Color(0.55f, 0.72f, 1.0f, 1f),
                    RotationDegrees = 12d,
                },
                new LensGhost
                {
                    Enabled = true,
                    Reflection = 0.62d,
                    RadiusDegrees = 0.9d,
                    Reflectance = 3e-3d,
                    Tint = new Color(0.62f, 1.0f, 0.72f, 1f),
                    RotationDegrees = -9d,
                },
                new LensGhost
                {
                    Enabled = true,
                    Reflection = 1.15d,
                    RadiusDegrees = 3.0d,
                    Reflectance = 8e-3d,
                    Tint = new Color(1.0f, 0.78f, 0.55f, 1f),
                    RotationDegrees = 4d,
                },
                new LensGhost
                {
                    Enabled = true,
                    Reflection = 1.75d,
                    RadiusDegrees = 0.6d,
                    Reflectance = 2e-3d,
                    Tint = new Color(0.90f, 0.62f, 1.0f, 1f),
                    RotationDegrees = -22d,
                },
            };
        }

        private void Start()
        {
            if (Star == null)
            {
                Star = GetComponent<StarRenderer>();
            }

            if (Sky == null)
            {
                Sky = FindAnyObjectByType<SkyEnvironment>();
            }

            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (PlayerCamera == null)
            {
                PlayerCamera = Camera.main;
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Camera camera = PlayerCamera != null ? PlayerCamera : Camera.main;
            if (camera == null || Star == null || !Star.IsReady)
            {
                SetPassActive(false);
                return;
            }

            if (camera != boundCamera)
            {
                DisposePasses();
                boundCamera = camera;
            }

            Vector3 direction = Star.SunDirectionSim;
            if (direction.sqrMagnitude < 0.5f)
            {
                SetPassActive(false);
                return;
            }

            // The projection of a direction is exact: the proxy distance only has
            // to be large enough not to sit behind the near plane.
            Vector3 viewport = camera.WorldToViewportPoint(
                camera.transform.position + (direction * Mathf.Max(10f, camera.nearClipPlane * 100f)));
            if (viewport.z <= 0f)
            {
                SetPassActive(false);
                return;
            }

            Vector2 ndc = new Vector2((viewport.x * 2f) - 1f, (viewport.y * 2f) - 1f);

            // The field stop. A camera lens is shadowed by its barrel at the frame
            // edge; a living eye has a ~200° field, so a source just outside the
            // screen still glare the visible part of the view (the screen is only
            // a window into the eye's field).
            bool eye = Mode == ObserverMode.HumanEye;
            float fadeStart = eye ? 1.20f : 0.98f;
            float fadeEnd = eye ? 2.00f : 1.25f;
            float edge = Mathf.Max(Mathf.Abs(ndc.x), Mathf.Abs(ndc.y));
            float frameFade = 1f - Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(fadeStart, fadeEnd, edge));
            if (frameFade <= 0.001f)
            {
                SetPassActive(false);
                return;
            }

            if (!EnsurePass(camera))
            {
                return;
            }

            SetPassActive(true);
            Upload(camera, ndc, frameFade);
        }

        private void Upload(Camera camera, Vector2 ndc, float frameFade)
        {
            double tanY = Math.Tan(camera.fieldOfView * 0.5d * DegToRad);
            double tanX = tanY * camera.aspect;
            if (!(tanY > 1e-6d))
            {
                return;
            }

            double angularRadius = Star.AngularRadiusRad;
            if (!(angularRadius > 0d))
            {
                return;
            }

            // Star colour through the observer's atmosphere: the same physical
            // transmittance the disc, glare and corona already receive.
            Vector3 transmittance = Sky != null && Sky.isActiveAndEnabled
                ? Sky.Transmittance
                : Vector3.one;

            Vector3 tint = Vector3.one;
            double discRadiance = 0d;
            if (Star.State != null)
            {
                tint = new Vector3(
                    (float)(Star.State.ColorR * transmittance.x),
                    (float)(Star.State.ColorG * transmittance.y),
                    (float)(Star.State.ColorB * transmittance.z));
                discRadiance = Star.State.DiscBrightnessCompressed;
            }

            // Everything the lens collects is scaled by three physical factors:
            // the celestial eclipse (a planet between the eye and the star removes
            // the flux), the cos⁴ field falloff (the entrance pupil is seen at a
            // slant off axis) and the field stop fade at the frame edge.
            double sunTan = Math.Sqrt(
                ((ndc.x * tanX) * (ndc.x * tanX)) + ((ndc.y * tanY) * (ndc.y * tanY)));
            double scale = frameFade
                * LensOptics.CameraVignetting(sunTan)
                * StarPhysics.Clamp(Star.EclipseVisibility, 0d, 1d);

            double pixelTan = (2d * tanY) / Math.Max(1, camera.pixelHeight);
            double theta0;
            double beta;
            double wideTheta0;
            double wideBeta;
            double veilPeak;
            double widePeak;
            double scatter;
            double wideShare;
            double spikePeak = 0d;
            double spikeWidth = 0d;
            double spikeExtent = 0d;

            if (Mode == ObserverMode.CameraLens)
            {
                // A camera: the veil is a flux-conserving scattering halo (a fixed
                // fraction of the star's flux is moved into the wings by the glass),
                // and the iris produces ghosts and a diffraction star.
                theta0 = VeilTheta0Degrees * DegToRad;
                beta = Math.Max(1.01d, VeilBeta);
                wideTheta0 = VeilWideTheta0Degrees * DegToRad;
                wideBeta = Math.Max(1.01d, VeilWideBeta);
                wideShare = Math.Max(0d, Math.Min(0.95d, VeilWideShare));
                scatter = Math.Max(0d, VeilingGlare);
                veilPeak = LensOptics.VeilPeakRadiance(
                    discRadiance, angularRadius, scatter * (1d - wideShare), theta0, beta);
                widePeak = LensOptics.VeilPeakRadiance(
                    discRadiance, angularRadius, scatter * wideShare, wideTheta0, wideBeta);

                // λ/D is the physical spike width; the raster can only draw ~one
                // pixel, so the width is floored and the strength carries the rest.
                double physicalWidth = LensOptics.DiffractionAngleRad(
                    Math.Max(1d, WavelengthNm) * 1e-9d, FocalLengthMm, FNumber);
                spikeWidth = LensOptics.SpikeDrawWidthRad(
                    physicalWidth, Math.Max(0.1d, DiffractionMinWidthPixels) * pixelTan);
                spikePeak = discRadiance * Math.Max(0d, DiffractionStrength);
                spikeExtent = Math.Tan(Math.Max(0.5d, DiffractionExtentDegrees) * DegToRad);
            }
            else
            {
                // A living eye: the intraocular stray light is normalised to the
                // star's RADIANCE (that is what the observer sees), so the glare is
                // a visible fraction of the disc out to several degrees and washes
                // the sky out next to it. The pupil is round and there are no
                // lens-element reflections, so no ghosts and no aperture star.
                theta0 = EyeGlareTheta0Degrees * DegToRad;
                beta = Math.Max(1.01d, EyeGlareBeta);
                wideTheta0 = EyeGlareWideTheta0Degrees * DegToRad;
                wideBeta = Math.Max(1.01d, EyeGlareWideBeta);
                wideShare = Math.Max(0d, Math.Min(0.95d, EyeGlareWideShare));
                scatter = Math.Max(0d, EyeGlare);

                // A suit visor scatters more than the cornea: in EVA the observer
                // looks through a scratched, dusty polycarbonate shell.
                if (Runner != null && Runner.PlayerMode == PlayerMode.EVA)
                {
                    scatter *= Math.Max(1d, HelmetGlareMultiplier);
                }

                double pointScale = Math.Max(0.01d, EyePointSpreadDegrees) * DegToRad;

                // The wide haze around the Sun on the ground is the AIR COLUMN in
                // front of the eye, not the eye: it is the same forward-scattered
                // aureole StarRenderer already models, measured by how much of the
                // direct beam the atmosphere extinguishes. In vacuum the column is
                // exactly zero, so only the tight intraocular core survives — the
                // sun in space is a hard disc with a compact glow, not a fog ball.
                double transmittanceLuminance = (0.2126d * transmittance.x)
                    + (0.7152d * transmittance.y)
                    + (0.0722d * transmittance.z);
                double airColumn = StarPhysics.Clamp(
                    -Math.Log(StarPhysics.Clamp(transmittanceLuminance, 1e-6d, 1d)) / 0.15d, 0d, 1d);

                // A point-like star delivers only its flux to the eye; a resolved
                // disc delivers its surface brightness. Without this the same star
                // at 5 AU would keep a solar-size halo.
                double resolved = LensOptics.PointSourceFactor(angularRadius, pointScale);

                veilPeak = LensOptics.EyeGlareRadiance(
                    discRadiance, scatter * (1d - wideShare) * resolved, 0d, theta0, beta);
                widePeak = LensOptics.EyeGlareRadiance(
                        discRadiance, scatter * wideShare * resolved, 0d, wideTheta0, wideBeta)
                    * airColumn;
            }

            material.SetVector(SunId, new Vector4(
                ndc.x, ndc.y, (float)(Math.Tan(angularRadius) / tanY), (float)angularRadius));
            material.SetVector(ScreenId, new Vector4(
                (float)tanX, (float)tanY, camera.aspect, (float)pixelTan));
            material.SetVector(ColorId, new Vector4(tint.x, tint.y, tint.z, 1f));
            material.SetVector(VeilId, new Vector4(
                (float)(veilPeak * scale), (float)theta0, (float)beta, 0f));
            material.SetVector(VeilWideId, new Vector4(
                (float)(widePeak * scale), (float)wideTheta0, (float)wideBeta, 0f));
            material.SetVector(StarId, new Vector4(
                ApertureBlades,
                (float)(spikePeak * scale),
                (float)spikeWidth,
                (float)spikeExtent));
            material.SetFloat(BladePhaseId, (float)(BladePhaseDegrees * DegToRad));
            material.SetFloat(ChromaId, (float)Math.Max(0d, ChromaticFringe));
            material.SetFloat(GhostRimId, (float)Math.Max(0d, GhostRim));
            material.SetFloat(DebugId, DebugMode);

            ghostCount = 0;
            if (Mode == ObserverMode.CameraLens && Ghosts != null)
            {
                for (int i = 0; i < Ghosts.Length && ghostCount < MaxGhosts; i++)
                {
                    LensGhost ghost = Ghosts[i];
                    if (!ghost.Enabled)
                    {
                        continue;
                    }

                    double radius = ghost.RadiusDegrees * DegToRad;
                    double peak = LensOptics.GhostRadiance(
                            discRadiance, angularRadius, radius, Math.Max(0d, ghost.Reflectance))
                        * Math.Max(0d, GhostGain)
                        * scale;
                    if (!(peak > 0d))
                    {
                        continue;
                    }

                    LensOptics.GhostCenter(
                        ndc.x, ndc.y, ghost.Reflection, out double gx, out double gy);

                    ghostPose[ghostCount] = new Vector4(
                        (float)gx,
                        (float)gy,
                        (float)(Math.Tan(radius) / tanY),
                        (float)peak);
                    Color coat = ghost.Tint;
                    ghostColor[ghostCount] = new Vector4(
                        coat.r * tint.x,
                        coat.g * tint.y,
                        coat.b * tint.z,
                        (float)(ghost.RotationDegrees * DegToRad));
                    ghostCount++;
                }
            }

            for (int i = ghostCount; i < MaxGhosts; i++)
            {
                ghostPose[i] = Vector4.zero;
                ghostColor[i] = Vector4.zero;
            }

            material.SetVectorArray(GhostPoseId, ghostPose);
            material.SetVectorArray(GhostColorId, ghostColor);
            material.SetFloat(GhostCountId, ghostCount);
        }

        private bool EnsurePass(Camera camera)
        {
            if (volume != null)
            {
                volume.targetCamera = camera;
                return true;
            }

            Shader shader = Shader.Find("Galilego/StarLensFlare");
            if (shader == null)
            {
                Debug.LogWarning(
                    "[StarLensFlare] Не найден шейдер Galilego/StarLensFlare — оптический блик выключен.");
                enabled = false;
                return false;
            }

            material = new Material(shader);
            pass = new FullScreenCustomPass
            {
                name = "Star Lens Flare",
                fullscreenPassMaterial = material,
                fetchColorBuffer = false,
                materialPassName = "StarLensFlare",

                // Additive into the camera colour, before the post-process stack:
                // the flare is scene radiance, so exposure, bloom and tonemapping
                // treat it exactly like the star disc.
                targetColorBuffer = CustomPass.TargetBuffer.Camera,
                targetDepthBuffer = CustomPass.TargetBuffer.None,
                clearFlags = ClearFlag.None,
            };

            GameObject volumeObject = new GameObject("StarLensFlareVolume");
            volumeObject.SetActive(false);
            volumeObject.layer = gameObject.layer;
            volume = volumeObject.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.targetCamera = camera;
            volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;

            // Before the underwater fog (priority 0): the water column attenuates
            // the flare along with the scene instead of the flare sitting on top.
            volume.priority = -10f;
            volume.customPasses.Add(pass);
            volumeObject.SetActive(true);
            return true;
        }

        private void SetPassActive(bool active)
        {
            if (volume != null && volume.gameObject.activeSelf != active)
            {
                volume.gameObject.SetActive(active);
            }
        }

        private void DisposePasses()
        {
            if (volume != null)
            {
                DestroyNow(volume.gameObject);
                volume = null;
            }

            if (material != null)
            {
                DestroyNow(material);
                material = null;
            }

            pass = null;
        }

        private void OnDestroy()
        {
            DisposePasses();
        }

        private static void DestroyNow(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }

        private void OnValidate()
        {
            FocalLengthMm = Math.Max(1d, FocalLengthMm);
            FNumber = Math.Max(0.7d, FNumber);
            ApertureBlades = Math.Max(0, Math.Min(16, ApertureBlades));
            WavelengthNm = Math.Max(380d, Math.Min(780d, WavelengthNm));
            DiffractionStrength = Math.Max(0d, DiffractionStrength);
            DiffractionMinWidthPixels = Math.Max(0.1d, DiffractionMinWidthPixels);
            DiffractionExtentDegrees = Math.Max(0.5d, DiffractionExtentDegrees);
            VeilingGlare = Math.Max(0d, VeilingGlare);
            VeilTheta0Degrees = Math.Max(0.05d, VeilTheta0Degrees);
            VeilBeta = Math.Max(1.01d, VeilBeta);
            VeilWideShare = Math.Max(0d, Math.Min(0.95d, VeilWideShare));
            VeilWideTheta0Degrees = Math.Max(0.05d, VeilWideTheta0Degrees);
            VeilWideBeta = Math.Max(1.01d, VeilWideBeta);
            GhostGain = Math.Max(0d, GhostGain);
            GhostRim = Math.Max(0d, Math.Min(2d, GhostRim));
            EyeGlare = Math.Max(0d, EyeGlare);
            EyeGlareTheta0Degrees = Math.Max(0.05d, EyeGlareTheta0Degrees);
            EyeGlareBeta = Math.Max(1.01d, EyeGlareBeta);
            EyeGlareWideShare = Math.Max(0d, Math.Min(0.95d, EyeGlareWideShare));
            EyeGlareWideTheta0Degrees = Math.Max(0.05d, EyeGlareWideTheta0Degrees);
            EyeGlareWideBeta = Math.Max(1.01d, EyeGlareWideBeta);
            EyePointSpreadDegrees = Math.Max(0.01d, EyePointSpreadDegrees);
            HelmetGlareMultiplier = Math.Max(1d, Math.Min(6d, HelmetGlareMultiplier));
            ChromaticFringe = Math.Max(0d, Math.Min(0.2d, ChromaticFringe));

            if (Ghosts != null)
            {
                for (int i = 0; i < Ghosts.Length; i++)
                {
                    LensGhost ghost = Ghosts[i];
                    ghost.Reflection = Math.Max(0d, Math.Min(4d, ghost.Reflection));
                    ghost.RadiusDegrees = Math.Max(0.01d, ghost.RadiusDegrees);
                    ghost.Reflectance = Math.Max(0d, Math.Min(0.25d, ghost.Reflectance));
                    Ghosts[i] = ghost;
                }
            }
        }

        private void OnGUI()
        {
            if (!DebugHud)
            {
                return;
            }

            GUI.Label(new Rect(10f, 60f, 1200f, 44f), string.Format(
                "Глаз/оптика: {0}, вуаль {1:P1}, призраков {2}, " +
                "N̂=({3:F2},{4:F2}), α={5:F3}°, V'={6:F1}, шейдер={7}",
                Mode == ObserverMode.HumanEye ? "глаз" : "объектив f/" + FNumber.ToString("F1"),
                Mode == ObserverMode.HumanEye ? EyeGlare : VeilingGlare,
                ghostCount,
                Star != null ? Star.SunDirectionSim.x : 0f,
                Star != null ? Star.SunDirectionSim.y : 0f,
                Star != null ? Star.AngularRadiusRad * (180d / Math.PI) : 0d,
                Star != null && Star.State != null ? Star.State.DiscBrightnessCompressed : 0d,
                volume != null && volume.gameObject.activeSelf ? "on" : "off"));
        }
    }
}
