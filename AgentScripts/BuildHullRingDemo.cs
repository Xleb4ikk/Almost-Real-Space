using System.IO;
using Galilego.Spacecraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Builds the procedural hull-ring assets in the Almost Real Space project:
/// HDRP materials, prefab, demo scene with open + sealed rings, and a preview render.
/// Run with: unity command run_script --file AgentScripts/BuildHullRingDemo.cs --entry BuildHullRingDemo.Build
/// </summary>
public static class BuildHullRingDemo
{
    private const string Project = "Assets/_Project";
    private const string PartDir = Project + "/Models/Parts/HullRing";
    private const string RefDir = PartDir + "/Reference";
    private const string MatDir = Project + "/Materials/Parts/HullRing";
    private const string PrefabPath = PartDir + "/HullRing.prefab";
    private const string ProfilePath = Project + "/Settings/HullRingDemoProfile.asset";
    private const string ScenePath = Project + "/Scenes/HullRingDemo.unity";
    private const string ScreenshotPath = Project + "/Screenshots/hull-ring-demo.png";
    private const string FbxPath = RefDir + "/HullRing_M05.fbx";

    private static Color Linear(float r, float g, float b)
    {
        return new Color(r, g, b, 1f);
    }

    public static int Build()
    {
        EnsureFolder(PartDir);
        EnsureFolder(RefDir);
        EnsureFolder(MatDir);
        EnsureFolder(Project + "/Settings");
        EnsureFolder(Project + "/Screenshots");

        // The Blender FBX is the static visual reference; the prefab is procedural.
        if (File.Exists(FbxPath))
        {
            AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx != null)
            {
                var renderers = fbx.GetComponentsInChildren<Renderer>();
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                bool has = false;
                foreach (var r in renderers)
                {
                    if (!has) { bounds = r.bounds; has = true; }
                    else { bounds.Encapsulate(r.bounds); }
                }
                Debug.Log("[HullRing] FBX reference: " + renderers.Length + " renderers, bounds " +
                          (has ? bounds.size.ToString("F2") : "none"));
            }
        }

        Material hull = MaterialAsset("M_HullRing_Hull", "Hull", Linear(0.63f, 0.645f, 0.655f), 1f, 0.50f);
        Material primer = MaterialAsset("M_HullRing_Primer", "Primer", Linear(0.095f, 0.115f, 0.09f), 0f, 0.18f);
        Material liner = MaterialAsset("M_HullRing_Liner", "Liner", Linear(0.50f, 0.48f, 0.42f), 0f, 0.25f, true);
        liner.SetColor("_EmissiveColor", Linear(0.025f, 0.023f, 0.018f));
        liner.SetColor("_EmissiveColorLDR", Linear(0.025f, 0.023f, 0.018f));
        liner.SetFloat("_EmissiveColorMode", 1f);
        liner.SetFloat("_EmissiveIntensity", 1f);
        EditorUtility.SetDirty(liner);
        Material solder = MaterialAsset("M_HullRing_Solder", "Solder", Linear(0.60f, 0.575f, 0.52f), 1f, 0.72f, true);
        Material steel = MaterialAsset("M_HullRing_Steel", "Steel", Linear(0.20f, 0.21f, 0.22f), 1f, 0.48f);
        Material floorMat = MaterialAsset("M_HullRing_Floor", "Floor", Linear(0.028f, 0.030f, 0.034f), 0f, 0.30f);

        // Prefab: default sealed state; the open variant is a scene override.
        var root = new GameObject("HullRing");
        root.AddComponent<MeshFilter>();
        root.AddComponent<MeshRenderer>();
        var ring = root.AddComponent<ProceduralHullRing>();
        ring.Radius = 2.5f;
        ring.Height = 1f;
        ring.Segments = 160;
        ring.OuterSkin = 0.016f;
        ring.Cavity = 0.096f;
        ring.InnerLiner = 0.016f;
        ring.Sealed = true;
        ring.LinerPanels = 4;
        ring.FlangeHeight = 0.075f;
        ring.FlangeOverhang = 0.008f;
        ring.BoltCount = 32;
        ring.HullMaterial = hull;
        ring.PrimerMaterial = primer;
        ring.LinerMaterial = liner;
        ring.SolderMaterial = solder;
        ring.SteelMaterial = steel;
        ring.TextureMetersPerTile = 0.5f;
        ring.Rebuild();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        // Demo scene. Save the user's open scene first, so replacing it loses nothing.
        EditorSceneManager.SaveOpenScenes();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "HullRingDemo_Floor";
        floor.transform.position = new Vector3(0f, -0.01f, 0f);
        floor.transform.localScale = new Vector3(5f, 1f, 5f);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;
        Object.DestroyImmediate(floor.GetComponent<Collider>());

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var materials = new[] { hull, primer, liner, solder, steel };

        var openGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        openGo.name = "HullRing_Open";
        openGo.transform.position = new Vector3(-3.2f, 0f, 0f);
        var openRing = openGo.GetComponent<ProceduralHullRing>();
        openRing.HullMaterial = hull;
        openRing.PrimerMaterial = primer;
        openRing.LinerMaterial = liner;
        openRing.SolderMaterial = solder;
        openRing.SteelMaterial = steel;
        openRing.Sealed = false;
        openRing.Rebuild();
        openGo.GetComponent<MeshRenderer>().sharedMaterials = materials;

        var sealedGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        sealedGo.name = "HullRing_Sealed";
        sealedGo.transform.position = new Vector3(3.2f, 0f, 0f);
        var sealedRing = sealedGo.GetComponent<ProceduralHullRing>();
        sealedRing.HullMaterial = hull;
        sealedRing.PrimerMaterial = primer;
        sealedRing.LinerMaterial = liner;
        sealedRing.SolderMaterial = solder;
        sealedRing.SteelMaterial = steel;
        sealedRing.Sealed = true;
        sealedRing.Rebuild();
        sealedGo.GetComponent<MeshRenderer>().sharedMaterials = materials;

        var camGo = new GameObject("HullRingDemoCamera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 34f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        cam.allowHDR = true;
        camGo.transform.position = new Vector3(7.8f, 4.4f, -10.8f);
        camGo.transform.LookAt(new Vector3(0f, 0.55f, 0f));

        var hd = camGo.AddComponent<HDAdditionalCameraData>();
        hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        hd.volumeLayerMask = ~0;

        var sunGo = new GameObject("HullRingDemo_Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Linear(1f, 0.95f, 0.88f);
        sun.intensity = 100000f;
        sun.lightUnit = LightUnit.Lux;
        sun.shadows = LightShadows.Soft;
        // Sun on the camera side (the camera looks from +X/-Z), so the visible
        // side of the rings is front-lit instead of a silhouette.
        sunGo.transform.rotation = Quaternion.LookRotation(
            new Vector3(8f, 10f, -8f).normalized);

        var fillGo = new GameObject("HullRingDemo_Fill");
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.color = Linear(0.78f, 0.88f, 1f);
        fill.intensity = 60000f;
        fill.lightUnit = LightUnit.Lumen;
        fill.range = 24f;
        fill.shadows = LightShadows.None;
        fillGo.transform.position = new Vector3(-6f, 4.5f, 5f);

        var rimGo = new GameObject("HullRingDemo_Rim");
        var rim = rimGo.AddComponent<Light>();
        rim.type = LightType.Point;
        rim.color = Linear(1f, 0.72f, 0.48f);
        rim.intensity = 40000f;
        rim.lightUnit = LightUnit.Lumen;
        rim.range = 20f;
        rim.shadows = LightShadows.None;
        rimGo.transform.position = new Vector3(-4f, 3.2f, 8f);

        BuildVolume();

        // Realtime reflection probe: the metal needs the studio/sky, not a black world.
        var probeGo = new GameObject("HullRingDemo_Reflection");
        probeGo.transform.position = new Vector3(0f, 2.2f, 0f);
        var probe = probeGo.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
        probe.resolution = 256;
        probe.size = new Vector3(30f, 16f, 30f);
        probe.hdr = true;
        probe.importance = 1;
        probe.RenderProbe();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[HullRing] Prefab: " + PrefabPath + "; scene: " + ScenePath);
        return 1;
    }

    private static void BuildVolume()
    {
        AssetDatabase.DeleteAsset(ProfilePath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "HullRingDemoProfile";
        AssetDatabase.CreateAsset(profile, ProfilePath);

        var exposure = profile.Add<Exposure>(true);
        exposure.mode.value = ExposureMode.Fixed;
        exposure.fixedExposure.value = 13f;
        exposure.compensation.value = 0f;

        var sky = profile.Add<GradientSky>(true);
        sky.top.value = new Color(300f, 360f, 460f, 1f);
        sky.middle.value = new Color(1000f, 1100f, 1300f, 1f);
        sky.bottom.value = new Color(120f, 130f, 150f, 1f);

        var visual = profile.Add<VisualEnvironment>(true);
        visual.skyType.value = (int)SkyType.Gradient;
        visual.skyAmbientMode.value = SkyAmbientMode.Dynamic;

        var tonemap = profile.Add<Tonemapping>(true);
        tonemap.mode.value = TonemappingMode.ACES;

        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0f);

        var volumeGo = new GameObject("HullRingDemo_Volume");
        var volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.sharedProfile = profile;
    }

    private static Material MaterialAsset(string name, string suffix, Color color, float metallic, float smoothness,
        bool doubleSided = false)
    {
        string path = MatDir + "/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        Shader shader = Shader.Find("HDRP/Lit");
        if (shader == null)
        {
            Debug.LogError("[HullRing] HDRP/Lit shader not found");
            shader = Shader.Find("Standard");
        }

        var material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_SmoothnessTextureChannel")) material.SetFloat("_SmoothnessTextureChannel", 0f);
        if (doubleSided)
        {
            material.SetFloat("_DoubleSidedEnable", 1f);
            material.SetFloat("_CullMode", 0f);
            material.SetFloat("_CullModeForward", 0f);
            material.SetFloat("_DoubleSidedNormalMode", 0f);
            material.EnableKeyword("_DOUBLESIDED_ON");
        }

        material.enableInstancing = true;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
