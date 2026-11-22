using System;
using System.IO;
using Galilego.Core;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Creates airless bodies of roughly lunar mass with a detailed, varied, Moon-like relief:
    /// derives radius/gravity from mass and density, builds a TerrainProfile asset
    /// (LunarProfileFactory), and puts a ready body (BodyAuthoring + BodyView + PlanetSurfaceRenderer,
    /// no atmosphere, no water) under the chosen parent. Also previews the relief as a shaded map.
    /// Menu: Tools/Galilego/Airless Bodies/Lunar Body Creator...
    /// </summary>
    public sealed class LunarBodyCreator : EditorWindow
    {
        private const string ProfileFolder = "Assets/_Project/Profiles/Terrain/Lunar";

        private string bodyName = "Selene";
        private GameObject parent;
        private LunarStyle style = LunarStyle.MoonLike;
        private int seed = 12345;
        private bool randomizeVariation = true;
        private double massMoons = 1d;
        private double density = 3344d;
        private double semiMajorKm = 384400d;
        private double eccentricity = 0.0549d;
        private double inclinationDeg = 5.1d;
        private bool tidallyLocked = true;
        private double rotationHours = 655.7d;
        private int previewSize = 512;

        private Texture2D preview;
        private string statsText = string.Empty;
        private Vector2 scroll;

        [MenuItem("Tools/Galilego/Airless Bodies/Lunar Body Creator...")]
        private static void Open()
        {
            LunarBodyCreator window = GetWindow<LunarBodyCreator>("Lunar Body Creator");
            window.minSize = new Vector2(430f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            if (parent == null && Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<BodyAuthoring>() != null)
            {
                parent = Selection.activeGameObject;
            }
        }

        private void OnDestroy()
        {
            if (preview != null)
            {
                DestroyImmediate(preview);
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Объект", EditorStyles.boldLabel);
            bodyName = EditorGUILayout.TextField("Имя тела", bodyName);
            parent = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Родитель", "Планета (получится спутник) или звезда. Пусто = Sol/StarSystemAuthoring сцены."),
                parent, typeof(GameObject), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Рельеф", EditorStyles.boldLabel);
            style = (LunarStyle)EditorGUILayout.EnumPopup(
                new GUIContent("Тип", "MoonLike — моря и нагорья; HighlandsOnly — без морей; MercuryLike — гладкие равнины и уступы; AncientCratered — древняя сильно кратерированная кора."),
                style);
            EditorGUILayout.BeginHorizontal();
            seed = EditorGUILayout.IntField(new GUIContent("Зерно", "Одно зерно = один и тот же мир. Оно же — BodyAuthoring.TerrainSeed."), seed);
            if (GUILayout.Button("Бросить", GUILayout.Width(70f)))
            {
                seed = new System.Random().Next(1, int.MaxValue);
            }

            EditorGUILayout.EndHorizontal();
            randomizeVariation = EditorGUILayout.Toggle(
                new GUIContent("Индивидуальные вариации", "Зерно меняет охват морей, возраст/плотность кратеров, положение и размер гигантского бассейна."),
                randomizeVariation);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Физика", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            massMoons = Math.Max(0.05d, EditorGUILayout.DoubleField("Масса, масс Луны", massMoons));
            if (GUILayout.Button("Случайная", GUILayout.Width(80f)))
            {
                var rng = new System.Random();
                massMoons = Math.Exp(Math.Log(0.4d) + (rng.NextDouble() * (Math.Log(2.5d) - Math.Log(0.4d))));
                density = 2800d + (rng.NextDouble() * 1100d);
            }

            EditorGUILayout.EndHorizontal();
            density = Math.Max(500d, EditorGUILayout.DoubleField("Плотность, кг/м³", density));

            double mass = LunarBodyPhysics.MoonMassKg * massMoons;
            double radius = LunarBodyPhysics.RadiusFromMass(mass, density);
            double g = LunarBodyPhysics.SurfaceGravity(mass, radius);
            EditorGUILayout.LabelField("Радиус", (radius / 1000d).ToString("F1") + " км");
            EditorGUILayout.LabelField("Ускорение св. падения", g.ToString("F3") + " м/с²");
            EditorGUILayout.LabelField("Переход простой→сложный кратер",
                (LunarBodyPhysics.ComplexTransition(g) / 1000d).ToString("F1") + " км");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Орбита (вокруг родителя)", EditorStyles.boldLabel);
            semiMajorKm = Math.Max(1d, EditorGUILayout.DoubleField("Большая полуось, км", semiMajorKm));
            eccentricity = Math.Max(0d, Math.Min(0.95d, EditorGUILayout.DoubleField("Эксцентриситет", eccentricity)));
            inclinationDeg = EditorGUILayout.DoubleField("Наклонение, °", inclinationDeg);
            tidallyLocked = EditorGUILayout.Toggle("Приливный захват", tidallyLocked);
            if (!tidallyLocked)
            {
                rotationHours = Math.Max(0.1d, EditorGUILayout.DoubleField("Период вращения, ч", rotationHours));
            }

            string hill = HillWarning(radius);
            if (hill != null)
            {
                EditorGUILayout.HelpBox(hill, MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Превью", EditorStyles.boldLabel);
            previewSize = EditorGUILayout.IntPopup("Разрешение карты", previewSize,
                new[] { "256 x 128", "512 x 256", "1024 x 512" }, new[] { 256, 512, 1024 });
            if (GUILayout.Button("Построить превью (тень + альбедо)"))
            {
                BuildPreview(radius, g);
            }

            if (preview != null)
            {
                float w = Math.Min(position.width - 30f, 640f);
                Rect r = GUILayoutUtility.GetRect(w, w * preview.height / preview.width);
                GUI.DrawTexture(r, preview, ScaleMode.ScaleToFit);
            }

            if (!string.IsNullOrEmpty(statsText))
            {
                EditorGUILayout.HelpBox(statsText, MessageType.Info);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Создать / обновить тело в сцене", GUILayout.Height(30f)))
            {
                CreateOrUpdate(mass, radius, g);
            }

            EditorGUILayout.HelpBox(
                "Ассет профиля: " + ProfileFolder + "/<имя>.asset. После создания все параметры рельефа " +
                "(кратеры, моря, бассейны, палитра) правятся в инспекторе профиля, секция «Лунный рельеф».",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ profile + stats

        private TerrainProfile BuildProfile(double radius, double gravity)
        {
            TerrainProfile profile = LunarProfileFactory.Create(style, radius, gravity, seed, randomizeVariation);
            Calibrate(profile.Lunar, seed, out double lo, out double hi, out double darkFraction);
            statsText = "Высоты: " + (lo / 1000d).ToString("F1") + " … " + (hi / 1000d).ToString("F1")
                + " км (оценка по 4000 точек). Тёмные моря: ~" + (darkFraction * 100d).ToString("F0") + "% поверхности.\n"
                + "Поправка среднего уровня: " + profile.Lunar.MeanOffsetMeters.ToString("F0") + " м (подобрана автоматически).";
            return profile;
        }

        /// <summary>Samples the relief and removes the mean height so that Radius is a true mean radius.</summary>
        private static void Calibrate(LunarReliefSettings s, int seedValue, out double lo, out double hi, out double darkFraction)
        {
            s.MeanOffsetMeters = 0d;
            LunarParams p = s.ToParams(seedValue);
            const int n = 4000;
            double sum = 0d;
            lo = double.MaxValue;
            hi = double.MinValue;
            int dark = 0;
            for (int i = 0; i < n; i++)
            {
                double z = 1d - (2d * (i + 0.5d) / n);
                double r = Math.Sqrt(Math.Max(0d, 1d - (z * z)));
                double phi = i * 2.399963229728653d;
                double a;
                double h = LunarRelief.Sample(p, new double3(r * Math.Cos(phi), r * Math.Sin(phi), z), true, out a);
                sum += h;
                lo = Math.Min(lo, h);
                hi = Math.Max(hi, h);
                if (a > 0.4d)
                {
                    dark++;
                }
            }

            double mean = sum / n;
            s.MeanOffsetMeters = -mean;
            lo -= mean;
            hi -= mean;
            darkFraction = dark / (double)n;
        }

        private void BuildPreview(double radius, double gravity)
        {
            TerrainProfile profile = BuildProfile(radius, gravity);
            LunarParams p = profile.Lunar.ToParams(seed);
            int w = previewSize;
            int h = previewSize / 2;
            var heights = new double[w * h];
            var albedo = new float[w * h];
            try
            {
                for (int y = 0; y < h; y++)
                {
                    if ((y & 15) == 0 && EditorUtility.DisplayCancelableProgressBar("Lunar preview", "Считаю рельеф…", y / (float)h))
                    {
                        return;
                    }

                    double lat = (0.5d - ((y + 0.5d) / h)) * Math.PI;
                    double cl = Math.Cos(lat);
                    double sl = Math.Sin(lat);
                    for (int x = 0; x < w; x++)
                    {
                        // +π: centre the map on the near side (the hemisphere the tidal lock
                        // presents to the parent) so the maria sit in the middle, not on both edges.
                        double lon = (((((x + 0.5d) / w) * 2d) - 1d) * Math.PI) + Math.PI;
                        double a;
                        heights[(y * w) + x] = LunarRelief.Sample(p, new double3(cl * Math.Cos(lon), cl * Math.Sin(lon), sl), true, out a);
                        albedo[(y * w) + x] = (float)a;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (preview != null)
            {
                DestroyImmediate(preview);
            }

            preview = new Texture2D(w, h, TextureFormat.RGBA32, false);
            preview.hideFlags = HideFlags.HideAndDontSave;
            var pixels = new Color32[w * h];
            TerrainPaletteData pal = profile.Palette;
            double dy = Math.PI * radius / h;
            Vector3 sun = new Vector3(-0.6f, 0.5f, 0.62f).normalized;
            for (int y = 0; y < h; y++)
            {
                double dx = Math.Max(1d, (2d * Math.PI * radius * Math.Cos((0.5d - ((y + 0.5d) / h)) * Math.PI)) / w);
                for (int x = 0; x < w; x++)
                {
                    int xl = (x + w - 1) % w;
                    int xr = (x + 1) % w;
                    int yu = Math.Max(0, y - 1);
                    int yd = Math.Min(h - 1, y + 1);
                    double gx = (heights[(y * w) + xr] - heights[(y * w) + xl]) / (2d * dx);
                    double gy = (heights[(yd * w) + x] - heights[(yu * w) + x]) / (2d * dy);
                    Vector3 n = new Vector3((float)(-gx * 4d), (float)(gy * 4d), 1f).normalized;
                    float shade = Mathf.Clamp01(Vector3.Dot(n, sun));

                    float a = albedo[(y * w) + x];
                    Color c = pal.Snow;
                    c = a >= 0f ? Color.Lerp(c, pal.Soil, a) : Color.Lerp(c, pal.Lush, -a * 0.6f);
                    float k = 0.25f + (1.1f * shade);
                    pixels[(y * w) + x] = new Color32(
                        (byte)Mathf.Clamp(c.r * k * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(c.g * k * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(c.b * k * 255f, 0f, 255f), 255);
                }
            }

            preview.SetPixels32(pixels);
            preview.Apply(false);
            Repaint();
        }

        // ------------------------------------------------------------------ scene object

        private void CreateOrUpdate(double mass, double radius, double gravity)
        {
            GameObject parentGo = parent != null ? parent : FindSol();
            if (parentGo == null)
            {
                EditorUtility.DisplayDialog("Lunar Body Creator",
                    "Родитель не задан и в сцене нет Sol/StarSystemAuthoring. Откройте сцену системы.", "OK");
                return;
            }

            SimulationRunner runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null)
            {
                EditorUtility.DisplayDialog("Lunar Body Creator", "В сцене нет SimulationRunner.", "OK");
                return;
            }

            string safeName = string.IsNullOrWhiteSpace(bodyName) ? "Selene" : bodyName.Trim();
            Transform existing = parentGo.transform.Find(safeName);
            if (existing != null)
            {
                bool update = EditorUtility.DisplayDialog("Lunar Body Creator",
                    "Тело «" + safeName + "» уже есть под этим родителем. Перезаписать его рельеф и физику?",
                    "Перезаписать", "Отмена");
                if (!update)
                {
                    return;
                }
            }
            else if (NameTaken(safeName))
            {
                EditorUtility.DisplayDialog("Lunar Body Creator",
                    "Имя «" + safeName + "» уже занято другим телом системы (имена должны быть уникальны).", "OK");
                return;
            }

            TerrainProfile profile = BuildProfile(radius, gravity);

            EnsureFolder(ProfileFolder);
            string assetPath = ProfileFolder + "/" + safeName + ".asset";
            TerrainProfileAsset asset = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TerrainProfileAsset>();
                asset.Profile = profile;
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                asset.Profile = profile;
                EditorUtility.SetDirty(asset);
            }

            GameObject go = existing != null ? existing.gameObject : new GameObject(safeName);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "Create " + safeName);
                go.transform.SetParent(parentGo.transform, false);
            }

            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            BodyAuthoring authoring = EnsureComponent<BodyAuthoring>(go);
            BodyView bodyView = EnsureComponent<BodyView>(go);
            PlanetSurfaceRenderer renderer = EnsureComponent<PlanetSurfaceRenderer>(go);

            authoring.StandardGravitationalParameter = LunarBodyPhysics.GravitationalConstant * mass;
            authoring.Radius = radius;
            authoring.CrashToleranceMps = 5d;
            authoring.J2 = 0d;
            authoring.SurfaceStaticFrictionMu = 0.7d;
            authoring.SurfaceKineticFrictionMu = 0.5d;
            authoring.SemiMajorAxis = semiMajorKm * 1000d;
            authoring.Eccentricity = eccentricity;
            authoring.InclinationDegrees = inclinationDeg;
            authoring.TidallyLocked = tidallyLocked;
            authoring.RotationPeriodSeconds = rotationHours * 3600d;
            authoring.NorthPoleDirection = new Vector3d(0d, 0d, 1d);
            authoring.TerrainPreset = asset;
            authoring.TerrainSeed = seed;
            authoring.AtmospherePreset = null;
            authoring.CloudsPreset = null;
            authoring.GasGiantPreset = null;
            authoring.GroundDecorPreset = null;

            bodyView.Runner = runner;

            // Reuse the tuned LOD/material settings of the renderer that is already in the scene.
            PlanetSurfaceRenderer template = null;
            PlanetSurfaceRenderer[] all = UnityEngine.Object.FindObjectsByType<PlanetSurfaceRenderer>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != renderer)
                {
                    template = all[i];
                    break;
                }
            }

            if (template != null)
            {
                EditorUtility.CopySerialized(template, renderer);
            }

            renderer.Runner = runner;

            EditorUtility.SetDirty(authoring);
            EditorUtility.SetDirty(bodyView);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();

            // Bake the macro albedo/normal right away: without them the body reads as a
            // painted ball from orbit and on any far disc — see Docs/MoonLook.md.
            LunarMacroMapBaker.BakeOne(authoring, asset);
            AssetDatabase.SaveAssets();

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            Debug.Log("[LunarBodyCreator] «" + safeName + "»: R=" + (radius / 1000d).ToString("F1") + " км, g="
                + gravity.ToString("F3") + " м/с², профиль " + assetPath + ", зерно " + seed + ".");
        }

        private string HillWarning(double radius)
        {
            GameObject p = parent;
            BodyAuthoring pb = p != null ? p.GetComponent<BodyAuthoring>() : null;
            if (pb == null)
            {
                return null;
            }

            double a = semiMajorKm * 1000d;
            if (a * (1d - eccentricity) < pb.Radius + radius)
            {
                return "Периапсис ниже поверхности родителя (радиус родителя " + (pb.Radius / 1000d).ToString("F0") + " км).";
            }

            Transform gp = p.transform.parent;
            BodyAuthoring gb = gp != null ? gp.GetComponent<BodyAuthoring>() : null;
            if (gb != null && gb.StandardGravitationalParameter > 0d)
            {
                double rHill = pb.SemiMajorAxis * (1d - pb.Eccentricity)
                    * Math.Pow(pb.StandardGravitationalParameter / (3d * gb.StandardGravitationalParameter), 1d / 3d);
                if (a * (1d + eccentricity) > 0.4d * rHill)
                {
                    return "Орбита выходит за ~0.4 сферы Хилла родителя (" + (rHill / 1000d).ToString("F0")
                        + " км): она может быть неустойчивой.";
                }
            }

            return null;
        }

        private static bool NameTaken(string name)
        {
            BodyAuthoring[] bodies = UnityEngine.Object.FindObjectsByType<BodyAuthoring>(FindObjectsInactive.Include);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].gameObject.name == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObject FindSol()
        {
            GameObject sol = GameObject.Find("Sol");
            if (sol != null)
            {
                return sol;
            }

            StarSystemAuthoring authoring = UnityEngine.Object.FindAnyObjectByType<StarSystemAuthoring>();
            return authoring != null ? authoring.gameObject : null;
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component == null)
            {
                component = Undo.AddComponent<T>(target);
            }

            return component;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parentPath = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(name))
            {
                return;
            }

            EnsureFolder(parentPath);
            AssetDatabase.CreateFolder(parentPath, name);
        }
    }
}
