using System;
using Galilego.Core;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Settings of the "Mars-like planet" factory. Defaults = the real Mars' mass, size,
    /// rotation, tilt and orbit; the RELIEF is an analogue (seed-driven), not a copy.
    /// </summary>
    [Serializable]
    public sealed class MarsLikeSettings
    {
        public string BodyName = "MarsLike";

        [Tooltip("Сид рельефа: меняет расположение вулканов, каньонов, бассейнов, кратеров.")]
        public int Seed = 4242;

        [Tooltip("0 = «классический» Марс-аналог. >0 = другой, но тоже Марс-подобный мир (плотность кратеров, глубина дихотомии, масштаб каньонов и т.д. зависят от этого числа).")]
        public int Variant = 0;

        [Tooltip("Радиус, км. Реальный Марс = 3389.5. Рельеф и амплитуда масштабируются вместе с радиусом.")]
        public double RadiusKm = 3389.5d;

        [Tooltip("Масса, кг. Реальный Марс = 6.4171e23 (μ = G·M = 4.2828e13 м³/с²).")]
        public double MassKg = 6.4171e23d;

        [Tooltip("Большая полуось орбиты вокруг родителя, м (Марс: 2.2794e11 при звезде 1 M☉).")]
        public double SemiMajorAxisMeters = 2.2794e11d;

        [Tooltip("Родитель-звезда. Пусто = корневой BodyAuthoring сцены.")]
        public BodyAuthoring Parent;

        [Tooltip("Создать атмосферу (разреженная пыльная, давление ≈ 0.6 кПа).")]
        public bool AddAtmosphere = true;

        [Tooltip("Фобос и Деймос (приливно захвачены; после добавления перепечь эфемериды, если ваша сцена их использует).")]
        public bool AddMoons = false;

        [Tooltip("Если в сцене уже есть объект с таким именем — заменить его (Undo работает).")]
        public bool ReplaceExisting = true;
    }

    /// <summary>
    /// Creates a Mars-like planet: terrain + atmosphere profile assets and a body GameObject
    /// (BodyAuthoring + BodyView + PlanetSurfaceRenderer + PlanetAtmosphereView) under the star.
    /// Menu: Tools/Galilego/Planets/...
    /// </summary>
    public static class MarsLikePlanetFactory
    {
        public const double GravitationalConstant = 6.6743e-11d;
        public const double MarsRadiusKm = 3389.5d;

        private const string TerrainFolder = "Assets/_Project/Profiles/Terrain";
        private const string AtmosphereFolder = "Assets/_Project/Profiles/Atmosphere";
        private const string PreviewFolder = "Assets/_Project/Profiles/Terrain/Previews";

        [MenuItem("Tools/Galilego/Planets/Create Mars-like Planet (defaults)")]
        public static void CreateDefaultMenu()
        {
            BodyAuthoring body = CreateInOpenScene(new MarsLikeSettings());
            if (body != null)
            {
                Selection.activeGameObject = body.gameObject;
            }
        }

        [MenuItem("Tools/Galilego/Planets/Mars-like Planet Window...")]
        public static void OpenWindow()
        {
            EditorWindow.GetWindow<MarsLikePlanetWindow>(true, "Mars-like Planet", true);
        }

        // ------------------------------------------------------------------ scene

        /// <summary>Creates the assets and the body in the active scene. Returns the body or null on failure.</summary>
        public static BodyAuthoring CreateInOpenScene(MarsLikeSettings s)
        {
            if (s == null)
            {
                s = new MarsLikeSettings();
            }

            BodyAuthoring parent = s.Parent != null ? s.Parent : FindRootBody();
            if (parent == null)
            {
                Debug.LogError(
                    "[MarsLike] В сцене нет корневого BodyAuthoring (звезды). Откройте сцену со StarSystemAuthoring " +
                    "или задайте Parent в окне Tools/Galilego/Planets/Mars-like Planet Window.");
                return null;
            }

            string terrainPath;
            string atmospherePath;
            TerrainProfileAsset terrain = CreateOrUpdateTerrainAsset(s, out terrainPath);
            AtmosphereProfileAsset atmosphere = s.AddAtmosphere ? CreateOrUpdateAtmosphereAsset(out atmospherePath) : null;

            if (s.ReplaceExisting)
            {
                Transform existing = parent.transform.Find(s.BodyName);
                if (existing != null)
                {
                    Undo.DestroyObjectImmediate(existing.gameObject);
                }
            }

            SimulationRunner runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>(FindObjectsInactive.Include);
            Camera camera = Camera.main;

            GameObject go = new GameObject(s.BodyName);
            Undo.RegisterCreatedObjectUndo(go, "Create Mars-like planet");
            go.transform.SetParent(parent.transform, false);

            BodyAuthoring body = go.AddComponent<BodyAuthoring>();
            ConfigureBody(body, s);
            body.TerrainPreset = terrain;
            body.TerrainSeed = s.Seed;
            body.AtmospherePreset = atmosphere;

            BodyView view = go.AddComponent<BodyView>();
            view.Runner = runner;

            PlanetSurfaceRenderer surface = go.AddComponent<PlanetSurfaceRenderer>();
            surface.Runner = runner;
            surface.UseHdrpWaterPatch = false;
            surface.UseLitWaterFallback = false;

            if (s.AddAtmosphere)
            {
                PlanetAtmosphereView atmosphereView = go.AddComponent<PlanetAtmosphereView>();
                atmosphereView.Runner = runner;
                atmosphereView.PlayerCamera = camera;
            }

            if (s.AddMoons)
            {
                CreateMoon(go.transform, "Phobos", 7.11e5d, 11267d, 9.376e6d, 0.0151d, 1.08d, 33.4d, true);
                CreateMoon(go.transform, "Deimos", 9.8e4d, 6200d, 2.3463e7d, 0.00033d, 1.79d, 240.0d, true);
            }

            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log(
                "[MarsLike] Создано тело \"" + s.BodyName + "\" под \"" + parent.name + "\" (R = " +
                (body.Radius / 1000d).ToString("F1") + " км, μ = " + body.StandardGravitationalParameter.ToString("E4") +
                " м³/с², сид " + s.Seed + "). Профиль рельефа: " + terrainPath +
                ". Сохраните сцену (Ctrl+S) и нажмите Play. Если в сцене есть запечённые эфемериды/снимок системы — перестройте их.");
            return body;
        }

        private static BodyAuthoring FindRootBody()
        {
            BodyAuthoring[] bodies = UnityEngine.Object.FindObjectsByType<BodyAuthoring>(FindObjectsInactive.Include);
            foreach (BodyAuthoring candidate in bodies)
            {
                Transform t = candidate.transform.parent;
                bool hasBodyParent = false;
                while (t != null)
                {
                    if (t.GetComponent<BodyAuthoring>() != null)
                    {
                        hasBodyParent = true;
                        break;
                    }

                    t = t.parent;
                }

                if (!hasBodyParent)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void ConfigureBody(BodyAuthoring body, MarsLikeSettings s)
        {
            double scale = s.RadiusKm / MarsRadiusKm;
            body.Radius = s.RadiusKm * 1000d;
            body.StandardGravitationalParameter = GravitationalConstant * s.MassKg;
            body.CrashToleranceMps = 5d;
            body.J2 = 1.96045e-3d;
            body.J2ReferenceRadius = 3396200d * scale;
            body.SurfaceStaticFrictionMu = 0.7d;
            body.SurfaceKineticFrictionMu = 0.5d;

            body.SemiMajorAxis = s.SemiMajorAxisMeters;
            body.Eccentricity = 0.0934d;
            body.InclinationDegrees = 1.85d;
            body.LongitudeOfAscendingNodeDegrees = 49.58d;
            body.ArgumentOfPeriapsisDegrees = 286.5d;
            body.MeanAnomalyAtEpochDegrees = 19.4d;
            body.EpochTimeSeconds = 0d;

            body.RotationPeriodSeconds = 88642.66d;
            body.TidallyLocked = false;
            body.PrimeMeridianOffsetDegrees = 0d;
            double tilt = 25.19d * Math.PI / 180d;
            body.NorthPoleDirection = new Vector3d(Math.Sin(tilt), 0d, Math.Cos(tilt));
        }

        private static void CreateMoon(
            Transform parent, string moonName, double mu, double radiusMeters, double semiMajorAxis,
            double eccentricity, double inclinationDeg, double meanAnomalyDeg, bool tidallyLocked)
        {
            GameObject go = new GameObject(moonName);
            Undo.RegisterCreatedObjectUndo(go, "Create moon");
            go.transform.SetParent(parent, false);
            BodyAuthoring moon = go.AddComponent<BodyAuthoring>();
            moon.StandardGravitationalParameter = mu;
            moon.Radius = radiusMeters;
            moon.SemiMajorAxis = semiMajorAxis;
            moon.Eccentricity = eccentricity;
            moon.InclinationDegrees = inclinationDeg;
            moon.MeanAnomalyAtEpochDegrees = meanAnomalyDeg;
            moon.TidallyLocked = tidallyLocked;
            // Orbital period; TidallyLocked makes the rotation follow the orbit (the value is a fallback).
            double parentMu = parent.GetComponent<BodyAuthoring>().StandardGravitationalParameter;
            moon.RotationPeriodSeconds = 2d * Math.PI * Math.Sqrt((semiMajorAxis * semiMajorAxis * semiMajorAxis) / parentMu);
            moon.TerrainPreset = null;
        }

        // ------------------------------------------------------------------ assets

        /// <summary>The Mars-like terrain profile for the given settings (also usable from tests).</summary>
        public static TerrainProfile BuildTerrainProfile(MarsLikeSettings s)
        {
            double scale = s.RadiusKm / MarsRadiusKm;
            MartianReliefParams m = s.Variant > 0 ? MartianReliefParams.CreateVariant(s.Variant) : MartianReliefParams.CreateMarsLike();

            var p = new TerrainProfile();
            p.Martian = m;

            // Heights: 1.0 normalized = HeightUnitMeters of the REAL Mars, scaled with the radius.
            p.AmplitudeMeters = m.HeightUnitMeters * scale;

            // The palette maps t = (h - SeaLevel) / Amplitude. A deep "datum" below the lowest basin
            // (variants reach about -16 km) keeps the whole planet in the land bands: no ocean/beach,
            // the sea is never reached and the physics height clamp never triggers.
            p.SeaLevelMeters = -17000d * scale;

            // Everything below belongs to the legacy generator, which is replaced by Martian; switch it off.
            p.ContinentFrequency = 0d;
            p.ContinentDepth = 0d;
            p.OceanFloorDepth = 0d;
            p.OceanShelfDepth = 0d;
            p.InteriorFloor = 0d;
            p.RidgedMix = 0d;
            p.PlainMix = 0d;
            p.DetailMix = 0d;
            p.WarpStrength = 0d;
            p.BeachHeightMeters = 0d;
            p.BeachShelfWidth = 0d;

            // Polar caps: the SAME numbers drive the white albedo (shader) and the layered deposit (relief).
            p.PolarIceLatitudeDeg = m.PolarCapLatitudeDeg;
            p.PolarIceFadeDeg = m.PolarCapFadeDeg;
            p.PolarIceThicknessMeters = 0d;
            p.PolarIceEdgeAmplitudeDeg = m.PolarCapEdgeJitterDeg;
            p.PolarIceEdgeFrequency = 5d;
            p.PolarIceEdgeOctaves = 3;

            // Colour: slope-rock is off (cliffs would turn grey; the strata are coloured through the mask).
            p.ColorRockSlopeTan = 0d;
            p.ColorNoiseFrequency = 4d;
            p.ColorNoiseOctaves = 3;
            p.ColorNoiseStrength = 0.03d;
            p.ColorNoiseSeedOffset = 4;
            p.ColorDetailFrequency = 60d;
            p.ColorDetailOctaves = 4;
            p.ColorDetailStrength = 0.35d;
            p.ColorDetailSeedOffset = 7;

            var pal = new TerrainPaletteData();
            pal.Sand = new Color(0.78f, 0.60f, 0.42f, 1f);
            pal.Desert = new Color(0.66f, 0.38f, 0.22f, 1f);
            pal.DryGrass = new Color(0.74f, 0.52f, 0.32f, 1f);
            pal.Grass = new Color(0.45f, 0.25f, 0.17f, 1f);
            pal.Forest = new Color(0.24f, 0.17f, 0.14f, 1f);
            pal.Tundra = new Color(0.50f, 0.38f, 0.30f, 1f);
            pal.Rock = new Color(0.30f, 0.20f, 0.15f, 1f);
            // Snow is also used by the palette's built-in polar tint (latitude > 63 deg fades to Snow), so it is a warm frosty white.
            pal.Snow = new Color(0.88f, 0.82f, 0.76f, 1f);
            pal.Sea = new Color(0.20f, 0.12f, 0.10f, 1f);
            pal.Soil = new Color(0.38f, 0.24f, 0.17f, 1f);
            pal.Lush = new Color(0.30f, 0.28f, 0.28f, 1f);
            p.Palette = pal;
            return p;
        }

        private static TerrainProfileAsset CreateOrUpdateTerrainAsset(MarsLikeSettings s, out string path)
        {
            EnsureFolder(TerrainFolder);
            path = TerrainFolder + "/MarsLike_" + Sanitize(s.BodyName) + ".asset";
            TerrainProfileAsset asset = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(path);
            bool created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<TerrainProfileAsset>();
            }

            asset.Profile = BuildTerrainProfile(s);
            if (created)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            return asset;
        }

        private static AtmosphereProfileAsset CreateOrUpdateAtmosphereAsset(out string path)
        {
            EnsureFolder(AtmosphereFolder);
            path = AtmosphereFolder + "/MarsAtmosphere.asset";
            AtmosphereProfileAsset asset = AssetDatabase.LoadAssetAtPath<AtmosphereProfileAsset>(path);
            bool created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<AtmosphereProfileAsset>();
            }

            var a = new AtmosphereProfile();
            a.TopAltitudeMeters = 150000d;
            a.SeaLevelDensityKgPerCubicMeter = 0.020d;
            a.ScaleHeightMeters = 11100d;
            a.OzoneEnabled = false;
            a.VisualEnabled = true;
            // Mars' sky is dust (Mie), not Rayleigh. Optical depth ~ beta * AerosolScale * density ratio *
            // (0.15 * ScaleHeight); 2500 gives tau ~ 0.3. Tune AerosolScale 500..6000 to taste.
            a.RayleighColor = new Color(1f, 1f, 1f, 1f);
            a.MieColor = new Color(0.95f, 0.66f, 0.40f, 1f);
            a.Intensity = 1.2f;
            a.MieAnisotropy = 0.6f;
            a.AerosolScale = 2500f;
            a.StepCount = 48;
            a.PlanetOcclusion = true;
            a.GroundColor = new Color(0.36f, 0.20f, 0.13f, 1f);
            a.HorizonFade = 0.02f;
            asset.Profile = a;
            if (created)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            return asset;
        }

        // ------------------------------------------------------------------ preview

        /// <summary>
        /// Renders an equirectangular hillshaded preview of the relief with the CPU (no Play mode
        /// needed) and saves it as a PNG. Slow-ish in the editor (Mono): ~10-40 s for 512x256.
        /// </summary>
        public static string RenderPreview(MarsLikeSettings s, int width)
        {
            TerrainProfile profile = BuildTerrainProfile(s);
            MartianReliefParams m = profile.Martian;
            int w = Mathf.Clamp(width, 128, 2048);
            int h = w / 2;
            double unitKm = m.HeightUnitMeters / 1000d;
            var heights = new float[w * h];
            for (int j = 0; j < h; j++)
            {
                if ((j & 7) == 0 && EditorUtility.DisplayCancelableProgressBar("Mars-like preview", "Высоты...", j / (float)h))
                {
                    EditorUtility.ClearProgressBar();
                    return null;
                }

                double lat = (0.5d - ((j + 0.5d) / h)) * Math.PI;
                for (int i = 0; i < w; i++)
                {
                    double lon = (((i + 0.5d) / w) * 2d * Math.PI) - Math.PI;
                    double c = Math.Cos(lat);
                    var d = new double3(c * Math.Cos(lon), c * Math.Sin(lon), Math.Sin(lat));
                    heights[(j * w) + i] = (float)(MartianRelief.SampleHeight(m, s.Seed, d, -1d) * unitKm);
                }
            }

            EditorUtility.ClearProgressBar();

            var pixels = new Color[w * h];
            double kmPerPixelY = Math.PI * MarsRadiusKm / h;
            for (int j = 0; j < h; j++)
            {
                double lat = (0.5d - ((j + 0.5d) / h)) * Math.PI;
                double kmPerPixelX = (2d * Math.PI * MarsRadiusKm / w) * Math.Max(Math.Cos(lat), 0.02d);
                for (int i = 0; i < w; i++)
                {
                    int i0 = Math.Max(0, i - 1);
                    int i1 = Math.Min(w - 1, i + 1);
                    int j0 = Math.Max(0, j - 1);
                    int j1 = Math.Min(h - 1, j + 1);
                    double gx = (heights[(j * w) + i1] - heights[(j * w) + i0]) / (kmPerPixelX * (i1 - i0));
                    double gy = (heights[(j0 * w) + i] - heights[(j1 * w) + i]) / (kmPerPixelY * (j1 - j0));
                    double ex = 6d;
                    double nx = -gx * ex;
                    double ny = -gy * ex;
                    double nl = Math.Sqrt((nx * nx) + (ny * ny) + 1d);
                    double shade = Math.Max(0d, ((-0.5d * nx) + (0.6d * ny) + 0.62d) / (nl * 0.994d));
                    double t = Math.Min(1d, Math.Max(0d, (heights[(j * w) + i] + 8d) / 26d));
                    var low = new Color(0.34f, 0.18f, 0.12f);
                    var mid = new Color(0.74f, 0.46f, 0.28f);
                    var high = new Color(0.93f, 0.84f, 0.74f);
                    Color baseColor = t < 0.5d ? Color.Lerp(low, mid, (float)(t * 2d)) : Color.Lerp(mid, high, (float)((t - 0.5d) * 2d));
                    float k = (float)(0.35d + (0.75d * shade));
                    pixels[((h - 1 - j) * w) + i] = new Color(baseColor.r * k, baseColor.g * k, baseColor.b * k, 1f);
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            EnsureFolder(PreviewFolder);
            string path = PreviewFolder + "/MarsLike_" + Sanitize(s.BodyName) + "_seed" + s.Seed + "_v" + s.Variant + ".png";
            System.IO.File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path);
            return path;
        }

        // ------------------------------------------------------------------ helpers

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Planet";
            }

            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }

    /// <summary>Window with the factory settings: Tools/Galilego/Planets/Mars-like Planet Window...</summary>
    public sealed class MarsLikePlanetWindow : EditorWindow
    {
        private readonly MarsLikeSettings settings = new MarsLikeSettings();

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Создаёт планету-аналог Марса: профиль рельефа и атмосферы (Assets/_Project/Profiles/...) и тело в открытой сцене. " +
                "Масса, размер, вращение, наклон оси и орбита — как у Марса; рельеф — аналог (вулканы, каньоны, кратеры, слои пород), а не копия.",
                MessageType.Info);

            settings.BodyName = EditorGUILayout.TextField("Имя тела", settings.BodyName);
            settings.Seed = EditorGUILayout.IntField(new GUIContent("Сид рельефа", "Меняет расположение всех структур."), settings.Seed);
            settings.Variant = EditorGUILayout.IntField(new GUIContent("Вариант (0 = классический)", "> 0 — другой, но Марс-подобный мир."), settings.Variant);
            settings.RadiusKm = EditorGUILayout.DoubleField("Радиус, км", settings.RadiusKm);
            settings.MassKg = EditorGUILayout.DoubleField("Масса, кг", settings.MassKg);
            settings.SemiMajorAxisMeters = EditorGUILayout.DoubleField("Большая полуось, м", settings.SemiMajorAxisMeters);
            settings.Parent = (BodyAuthoring)EditorGUILayout.ObjectField("Родитель (звезда)", settings.Parent, typeof(BodyAuthoring), true);
            settings.AddAtmosphere = EditorGUILayout.Toggle("Атмосфера", settings.AddAtmosphere);
            settings.AddMoons = EditorGUILayout.Toggle("Фобос и Деймос", settings.AddMoons);
            settings.ReplaceExisting = EditorGUILayout.Toggle("Заменить существующий", settings.ReplaceExisting);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Создать в открытой сцене", GUILayout.Height(28)))
            {
                BodyAuthoring body = MarsLikePlanetFactory.CreateInOpenScene(settings);
                if (body != null)
                {
                    Selection.activeGameObject = body.gameObject;
                }
            }

            if (GUILayout.Button("Только PNG-превью рельефа (512×256)"))
            {
                string path = MarsLikePlanetFactory.RenderPreview(settings, 512);
                if (path != null)
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }
    }
}
