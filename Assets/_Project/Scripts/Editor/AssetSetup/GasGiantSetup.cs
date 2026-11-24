using System;
using System.IO;
using Galilego.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// One-shot authoring for the fictional gas giant Aeolus. It follows the
    /// Terra workflow: a BodyAuthoring + BodyView on a child of Sol, a named
    /// profile asset, and a GasGiantView that builds the visual at runtime.
    ///
    /// The body is deliberately not Jupiter: 0.6 M_J, density 1724 kg/m3,
    /// R_eq = 55 254 km, a = 245 million km, 12 deg obliquity. The solver below
    /// computes the initial mean anomaly for opposition from the actual scene
    /// elements instead of hardcoding it.
    /// </summary>
    public static class GasGiantSetup
    {
        private const string ProfileFolder = "Assets/_Project/Profiles/GasGiant";
        private const string ProfilePath = ProfileFolder + "/Aeolus.asset";
        private const string MaterialFolder = "Assets/_Project/Materials/GasGiant";
        private const string SurfaceMaterialPath = MaterialFolder + "/AeolusSurface.mat";
        private const string DiscMaterialPath = MaterialFolder + "/AeolusDisc.mat";
        private const string SurfaceMapPath = "Assets/_Project/Textures/GasGiant/AeolusSurfaceMap.jpg";

        private const double AeolusMu = 7.60121e16;
        private const double AeolusRadiusMeters = 55254000d;
        private const double AeolusJ2 = 1.47e-2;
        private const double AeolusRotationSeconds = 37800d;
        private const double AeolusSemiMajorAxis = 2.45e11;
        private const double AeolusEccentricity = 0.0489;
        private const double AeolusInclinationDegrees = 1.303;
        private const double AeolusNodeDegrees = 100.464;
        private const double AeolusPeriapsisDegrees = 273.867;

        [MenuItem("Tools/Galilego/Setup Aeolus gas giant")]
        public static void Run()
        {
            Shader surfaceShader = Shader.Find("Galilego/GasGiantSurface");
            Shader discShader = Shader.Find("Galilego/GasGiantDisc");
            if (surfaceShader == null || discShader == null)
            {
                EditorUtility.DisplayDialog("Aeolus",
                    "РќРµ РЅР°Р№РґРµРЅС‹ С€РµР№РґРµСЂС‹ Galilego/GasGiantSurface РёР»Рё Galilego/GasGiantDisc. " +
                    "Р”РѕР¶РґРёС‚РµСЃСЊ РёРјРїРѕСЂС‚Р° С€РµР№РґРµСЂРѕРІ Рё РїРѕРІС‚РѕСЂРёС‚Рµ.", "OK");
                return;
            }

            EnsureFolder(ProfileFolder);
            EnsureFolder(MaterialFolder);

            GasGiantProfileAsset profileAsset = AssetDatabase.LoadAssetAtPath<GasGiantProfileAsset>(ProfilePath);
            if (profileAsset == null)
            {
                profileAsset = ScriptableObject.CreateInstance<GasGiantProfileAsset>();
                profileAsset.Profile = GasGiantProfile.CreateAeolus();
                AssetDatabase.CreateAsset(profileAsset, ProfilePath);
            }
            else if (profileAsset.Profile == null || profileAsset.Profile.Bands == null
                || profileAsset.Profile.Bands.Count == 0)
            {
                profileAsset.Profile = GasGiantProfile.CreateAeolus();
                EditorUtility.SetDirty(profileAsset);
            }

            AssignSurfaceMap(profileAsset);

            Material surfaceMaterial = EnsureMaterial(SurfaceMaterialPath, surfaceShader);
            Material discMaterial = EnsureMaterial(DiscMaterialPath, discShader);

            GameObject sol = FindSol();
            if (sol == null)
            {
                EditorUtility.DisplayDialog("Aeolus",
                    "Р’ Р°РєС‚РёРІРЅРѕР№ СЃС†РµРЅРµ РЅРµ РЅР°Р№РґРµРЅ Sol (StarSystemAuthoring). РћС‚РєСЂРѕР№С‚Рµ OutdoorsScene Рё РїРѕРІС‚РѕСЂРёС‚Рµ.", "OK");
                return;
            }

            Transform existing = sol.transform.Find("Aeolus");
            GameObject bodyGo = existing != null ? existing.gameObject : new GameObject("Aeolus");
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(bodyGo, "Create Aeolus");
                bodyGo.transform.SetParent(sol.transform, false);
            }

            bodyGo.transform.localPosition = Vector3.zero;
            bodyGo.transform.localRotation = Quaternion.identity;
            bodyGo.transform.localScale = Vector3.one;

            // A gas giant has no heightfield, water, Earth clouds or decor.
            RemoveIfPresent<PlanetSurfaceRenderer>(bodyGo);
            RemoveIfPresent<PlanetAtmosphereView>(bodyGo);
            RemoveIfPresent<PlanetCloudsView>(bodyGo);

            BodyAuthoring authoring = EnsureComponent<BodyAuthoring>(bodyGo);
            BodyView bodyView = EnsureComponent<BodyView>(bodyGo);
            GasGiantView gasView = EnsureComponent<GasGiantView>(bodyGo);

            SimulationRunner runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null)
            {
                EditorUtility.DisplayDialog("Aeolus", "Р’ СЃС†РµРЅРµ РЅРµС‚ SimulationRunner.", "OK");
                return;
            }

            authoring.StandardGravitationalParameter = AeolusMu;
            authoring.Radius = AeolusRadiusMeters;
            authoring.CrashToleranceMps = 0d;
            authoring.J2 = AeolusJ2;
            authoring.J2ReferenceRadius = AeolusRadiusMeters;
            authoring.SurfaceStaticFrictionMu = 0d;
            authoring.SurfaceKineticFrictionMu = 0d;
            authoring.SemiMajorAxis = AeolusSemiMajorAxis;
            authoring.Eccentricity = AeolusEccentricity;
            authoring.InclinationDegrees = AeolusInclinationDegrees;
            authoring.LongitudeOfAscendingNodeDegrees = AeolusNodeDegrees;
            authoring.ArgumentOfPeriapsisDegrees = AeolusPeriapsisDegrees;
            authoring.EpochTimeSeconds = 0d;
            authoring.RotationPeriodSeconds = AeolusRotationSeconds;
            authoring.TidallyLocked = false;
            authoring.PrimeMeridianOffsetDegrees = 0d;
            authoring.NorthPoleDirection = new Vector3d(0d, -0.2079d, 0.9781d); // 12 deg
            authoring.TerrainPreset = null;
            authoring.AtmospherePreset = null;
            authoring.CloudsPreset = null;
            authoring.GroundDecorPreset = null;
            authoring.GasGiantPreset = profileAsset;
            authoring.TerrainSeed = 0;
            if (authoring.TerrainPads != null)
            {
                authoring.TerrainPads.Clear();
            }

            bodyView.Runner = runner;
            gasView.Runner = runner;
            gasView.Profile = profileAsset;
            gasView.SurfaceMaterial = surfaceMaterial;
            gasView.DiscMaterial = discMaterial;
            gasView.RenderNearSphere = true;
            gasView.RenderFarDisc = true;
            gasView.MinScreenRadiusPixels = 0.5f;
            gasView.SurfaceBrightness = 3.0f;
            gasView.FarDiscBrightness = 12f;
            gasView.MinEnergyScale = 0f;
            gasView.MeshResolution = 128;
            gasView.LogDiagnostics = true;
            gasView.PlayerCamera = Camera.main != null ? Camera.main : UnityEngine.Object.FindAnyObjectByType<Camera>();

            SolveOpposition(authoring, bodyGo.name);

            EditorUtility.SetDirty(authoring);
            EditorUtility.SetDirty(bodyView);
            EditorUtility.SetDirty(gasView);
            EditorUtility.SetDirty(profileAsset);
            AssetDatabase.SaveAssets();

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = bodyGo;
            EditorGUIUtility.PingObject(bodyGo);
            Debug.Log("[Aeolus] Р“РѕС‚РѕРІРѕ: РїСЂРѕС„РёР»СЊ " + ProfilePath + ", РјР°С‚РµСЂРёР°Р»С‹ " + SurfaceMaterialPath +
                " / " + DiscMaterialPath + ". РЎС†РµРЅР° СЃРѕС…СЂР°РЅРµРЅР°.");
        }

        [MenuItem("Tools/Galilego/Reset Aeolus profile (new defaults)")]
        public static void ResetProfile()
        {
            EnsureFolder(ProfileFolder);
            GasGiantProfileAsset profileAsset = AssetDatabase.LoadAssetAtPath<GasGiantProfileAsset>(ProfilePath);
            if (profileAsset == null)
            {
                profileAsset = ScriptableObject.CreateInstance<GasGiantProfileAsset>();
                AssetDatabase.CreateAsset(profileAsset, ProfilePath);
            }

            profileAsset.Profile = GasGiantProfile.CreateAeolus();
            AssignSurfaceMap(profileAsset);
            EditorUtility.SetDirty(profileAsset);
            AssetDatabase.SaveAssets();
            Debug.Log("[Aeolus] профиль сброшен на новые дефолты.");
        }

        /// <summary>
        /// Assign the baked Jupiter surface map to the existing profile without
        /// touching the band/wind data (those remain the fallback for a missing
        /// texture and for the procedural detail pass).
        /// </summary>
        [MenuItem("Tools/Galilego/Assign Jupiter surface map")]
        public static void AssignSurfaceMapMenu()
        {
            GasGiantProfileAsset profileAsset = AssetDatabase.LoadAssetAtPath<GasGiantProfileAsset>(ProfilePath);
            if (profileAsset == null)
            {
                Debug.LogWarning("[Aeolus] профиль не найден: " + ProfilePath);
                return;
            }

            if (!AssignSurfaceMap(profileAsset))
            {
                return;
            }

            EditorUtility.SetDirty(profileAsset);
            AssetDatabase.SaveAssets();

            foreach (GasGiantView view in UnityEngine.Object.FindObjectsByType<GasGiantView>(FindObjectsInactive.Include))
            {
                view.SurfaceBrightness = 3.0f;
                view.MeshResolution = 128;
                EditorUtility.SetDirty(view);
            }

            if (!UnityEngine.Application.isPlaying)
            {
                Scene scene = SceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log("[Aeolus] карта поверхности назначена: " + SurfaceMapPath);
        }

        private static bool AssignSurfaceMap(GasGiantProfileAsset profileAsset)
        {
            if (profileAsset == null || profileAsset.Profile == null)
            {
                return false;
            }

            Texture2D map = AssetDatabase.LoadAssetAtPath<Texture2D>(SurfaceMapPath);
            if (map == null)
            {
                Debug.LogWarning("[Aeolus] текстура не найдена: " + SurfaceMapPath);
                return false;
            }

            profileAsset.Profile.SurfaceMap = map;
            profileAsset.Profile.SurfaceMapStrength = 1f;
            profileAsset.Profile.SurfaceMapLongitudeOffset = 0f;
            profileAsset.Profile.SurfaceMapBump = 0f;
            profileAsset.Profile.PolarTint = 0f;
            profileAsset.Profile.DetailStrength = 0f;
            profileAsset.Profile.Saturation = 1.0f;
            profileAsset.Profile.Contrast = 1.15f;
            profileAsset.Profile.LimbDarkening = 0.3f;
            profileAsset.Profile.RimIntensity = 0.08f;
            profileAsset.Profile.HazeDensity = 0.03f;
            profileAsset.Profile.WrapDiffuse = 0.45f;
            profileAsset.Profile.RimColor = new Color(0.72f, 0.66f, 0.88f, 1f);
            profileAsset.Profile.HazeColor = new Color(0.82f, 0.92f, 0.86f, 1f);
            profileAsset.Profile.NorthPoleColor = new Color(0.32f, 0.28f, 0.45f, 1f);
            profileAsset.Profile.SouthPoleColor = new Color(0.26f, 0.22f, 0.38f, 1f);
            return true;
        }

        /// <summary>
        /// Debug entry point: configure the next Play session to start 1.2 R
        /// above Aeolus in a circular 2.2 R orbit, then enter Play. The scene is
        /// deliberately NOT saved, so exiting Play restores the normal Terra
        /// spawn; the menu can be used again at any time.
        /// </summary>
        [MenuItem("Tools/Galilego/Play near Aeolus")]
        public static void PlayNearAeolus()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Aeolus] Play near Aeolus: уже в Play Mode.");
                return;
            }

            SimulationRunner runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            BodyAuthoring aeolus = FindBody("Aeolus");
            if (runner == null || aeolus == null)
            {
                Debug.LogWarning("[Aeolus] Play near Aeolus: нет SimulationRunner или тело Aeolus не найдено.");
                return;
            }

            double orbitRadius = aeolus.Radius * 2.2d;
            runner.SpawnBodyName = "Aeolus";
            runner.SpawnOnSurface = false;
            runner.SpawnAltitudeMeters = aeolus.Radius * 1.2d;
            runner.SpawnFacingSun = true;
            runner.SpawnPhaseDegrees = 30d;
            runner.SpawnHorizontalSpeedMps = System.Math.Sqrt(
                aeolus.StandardGravitationalParameter / orbitRadius);

            EditorUtility.SetDirty(runner);
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorApplication.isPlaying = true;

            Debug.Log(string.Format(
                "[Aeolus] Play near Aeolus: старт на {0:F2} R, круговая скорость {1:F0} м/с. " +
                "Сцена не сохранена — выход из Play вернёт обычный спавн.",
                orbitRadius / aeolus.Radius, runner.SpawnHorizontalSpeedMps));
        }

        [MenuItem("Tools/Galilego/Recalculate Aeolus opposition")]
        public static void RecalculateOpposition()
        {
            GameObject bodyGo = GameObject.Find("Aeolus");
            if (bodyGo == null)
            {
                Debug.LogWarning("[Aeolus] GameObject Aeolus РЅРµ РЅР°Р№РґРµРЅ.");
                return;
            }

            BodyAuthoring authoring = bodyGo.GetComponent<BodyAuthoring>();
            if (authoring == null)
            {
                Debug.LogWarning("[Aeolus] РќР° РѕР±СЉРµРєС‚Рµ РЅРµС‚ BodyAuthoring.");
                return;
            }

            SolveOpposition(authoring, bodyGo.name);
            EditorUtility.SetDirty(authoring);
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void SolveOpposition(BodyAuthoring aeolus, string name)
        {
            BodyAuthoring terra = FindBody("Terra");
            if (terra == null)
            {
                Debug.LogWarning("[Aeolus] Terra РЅРµ РЅР°Р№РґРµРЅР° вЂ” РѕСЃС‚Р°РІР»СЏСЋ MeanAnomaly РєР°Рє РµСЃС‚СЊ.");
                return;
            }

            double mDeg = SolveOppositionMeanAnomaly(
                terra.SemiMajorAxis, terra.Eccentricity, terra.InclinationDegrees,
                terra.LongitudeOfAscendingNodeDegrees, terra.ArgumentOfPeriapsisDegrees,
                aeolus.SemiMajorAxis, aeolus.Eccentricity, aeolus.InclinationDegrees,
                aeolus.LongitudeOfAscendingNodeDegrees, aeolus.ArgumentOfPeriapsisDegrees);

            aeolus.MeanAnomalyAtEpochDegrees = mDeg;

            double mRad = mDeg * Math.PI / 180d;
            Vector3d terraPos = KeplerPosition(
                terra.SemiMajorAxis, terra.Eccentricity, terra.InclinationDegrees,
                terra.LongitudeOfAscendingNodeDegrees, terra.ArgumentOfPeriapsisDegrees, 0d);
            Vector3d aeolusPos = KeplerPosition(
                aeolus.SemiMajorAxis, aeolus.Eccentricity, aeolus.InclinationDegrees,
                aeolus.LongitudeOfAscendingNodeDegrees, aeolus.ArgumentOfPeriapsisDegrees, mRad);

            double distance = (aeolusPos - terraPos).Magnitude;
            double angularDiameter = 2d * Math.Atan(aeolus.Radius / distance) * (180d / Math.PI);
            Vector3d toSun = -terraPos;
            Vector3d toAeolus = aeolusPos - terraPos;
            double elongation = Math.Acos(Math.Max(-1d, Math.Min(1d,
                Vector3d.Dot(toSun.Normalized, toAeolus.Normalized)))) * (180d / Math.PI);

            Debug.Log(string.Format(
                "[Aeolus] Opposition: M={0:F6} deg, d={1:E6} m, angular diameter={2:F5} deg, " +
                "elongation={3:F3} deg, R_eq={4:N0} m",
                mDeg, distance, angularDiameter, elongation, aeolus.Radius));
        }

        /// <summary>
        /// Maximize the dot product of the heliocentric directions (opposition in
        /// ecliptic longitude). e/i/Omega/omega are honoured; the solver uses the
        /// same Kepler equations as OrbitingBody. The actual minimum distance can
        /// differ slightly because of eccentricity; the angular-size difference is
        /// negligible, and the script logs both.
        /// </summary>
        private static double SolveOppositionMeanAnomaly(
            double aT, double eT, double iT, double omT, double wT,
            double aJ, double eJ, double iJ, double omJ, double wJ)
        {
            Vector3d terra = KeplerPosition(aT, eT, iT, omT, wT, 0d).Normalized;

            double bestM = 0d;
            double bestDot = -double.MaxValue;
            // Two passes: 0.25 deg scan, then 0.002 deg around the maximum.
            for (int pass = 0; pass < 2; pass++)
            {
                double center = pass == 0 ? 0d : bestM;
                double half = pass == 0 ? 360d : 0.5d;
                double step = pass == 0 ? 0.25d : 0.002d;
                double start = center - half;
                double end = center + half;
                for (double deg = start; deg <= end; deg += step)
                {
                    double m = deg * Math.PI / 180d;
                    Vector3d p = KeplerPosition(aJ, eJ, iJ, omJ, wJ, m).Normalized;
                    double dot = Vector3d.Dot(terra, p);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestM = deg;
                    }
                }
            }

            double normalized = bestM % 360d;
            if (normalized < 0d)
            {
                normalized += 360d;
            }

            return normalized;
        }

        private static Vector3d KeplerPosition(
            double a, double e, double inclinationDegrees, double nodeDegrees,
            double periapsisDegrees, double meanAnomalyRadians)
        {
            double i = inclinationDegrees * Math.PI / 180d;
            double om = nodeDegrees * Math.PI / 180d;
            double w = periapsisDegrees * Math.PI / 180d;
            double E = SolveKepler(meanAnomalyRadians, e);
            double nu = 2d * Math.Atan2(
                Math.Sqrt(1d + e) * Math.Sin(E * 0.5d),
                Math.Sqrt(1d - e) * Math.Cos(E * 0.5d));
            double u = w + nu;
            double r = a * (1d - (e * Math.Cos(E)));

            double x = r * ((Math.Cos(om) * Math.Cos(u)) - (Math.Sin(om) * Math.Sin(u) * Math.Cos(i)));
            double y = r * ((Math.Sin(om) * Math.Cos(u)) + (Math.Cos(om) * Math.Sin(u) * Math.Cos(i)));
            double z = r * (Math.Sin(u) * Math.Sin(i));
            return new Vector3d(x, y, z);
        }

        private static double SolveKepler(double meanAnomaly, double e)
        {
            double m = meanAnomaly % (2d * Math.PI);
            if (m < 0d)
            {
                m += 2d * Math.PI;
            }

            double E = m;
            for (int k = 0; k < 60; k++)
            {
                double f = E - (e * Math.Sin(E)) - m;
                double fp = 1d - (e * Math.Cos(E));
                double step = f / fp;
                E -= step;
                if (Math.Abs(step) < 1e-14d)
                {
                    break;
                }
            }

            return E;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static Material EnsureMaterial(string path, Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            return material;
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

        private static BodyAuthoring FindBody(string name)
        {
            BodyAuthoring[] bodies = UnityEngine.Object.FindObjectsByType<BodyAuthoring>(
                FindObjectsInactive.Include);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].gameObject.name == name)
                {
                    return bodies[i];
                }
            }

            return null;
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

        private static void RemoveIfPresent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null)
            {
                Undo.DestroyObjectImmediate(component);
            }
        }
    }
}





