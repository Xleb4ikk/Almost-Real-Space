using System;
using System.Collections.Generic;
using System.IO;
using Galilego.Spacecraft;
using Galilego.Universe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Загрузка тестового аппарата T1 (ТЗ v2, раздел 8) из JSON в Parts
    /// SimulationRunner в открытой сцене. Данные — Tools/P1bTests/Data/
    /// test_vessel_t1.json (тот же файл, что читает стенд). Сцена сохраняется.
    /// Меню: Galilego > Test Vessel > Apply T1 From JSON.
    /// </summary>
    public static class TestVesselSceneTool
    {
        private const string JsonRelativePath = "Tools/P1bTests/Data/test_vessel_t1.json";

        [MenuItem("Galilego/Test Vessel/Apply T1 From JSON")]
        public static void ApplyFromJsonMenu()
        {
            Debug.Log(ApplyFromJson());
        }

        /// <summary>Загрузить T1 из JSON в PartDefinition[] (без применения к сцене). null — ошибка.</summary>
        public static List<PartDefinition> LoadT1Parts()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string jsonPath = Path.Combine(projectRoot, JsonRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(jsonPath))
            {
                return null;
            }

            var dto = JsonUtility.FromJson<VesselDto>(File.ReadAllText(jsonPath));
            if (dto == null || dto.parts == null || dto.parts.Length == 0)
            {
                return null;
            }

            var parts = new List<PartDefinition>(dto.parts.Length);
            for (int i = 0; i < dto.parts.Length; i++)
            {
                PartDto p = dto.parts[i];
                if (!Enum.TryParse(p.shape, true, out PartShape shape))
                {
                    return null;
                }

                parts.Add(new PartDefinition
                {
                    Name = p.name,
                    MassKg = p.massKg,
                    JointStrengthNewtons = p.jointStrengthNewtons,
                    BreakTorqueNm = p.breakTorqueNm,
                    Offset = new Vector3(p.offset.x, p.offset.y, p.offset.z),
                    Shape = shape,
                    Dimensions = new Vector3(p.dimensions.x, p.dimensions.y, p.dimensions.z),
                    Orientation = new Vector4(p.orientation.x, p.orientation.y, p.orientation.z, p.orientation.w),
                    ParentIndex = p.parentIndex,
                    JointAnchor = new Vector3(p.jointAnchor.x, p.jointAnchor.y, p.jointAnchor.z),
                    EngineOffset = new Vector3(p.engineOffset.x, p.engineOffset.y, p.engineOffset.z),
                });
            }

            return parts;
        }

        /// <summary>Применить T1 к раннеру в активной сцене; вернуть отчёт.</summary>
        public static string ApplyFromJson()
        {
            List<PartDefinition> loaded = LoadT1Parts();
            if (loaded == null)
            {
                return "T1: JSON не найден или не разобран";
            }

            SimulationRunner runner = FindRunner();
            if (runner == null)
            {
                return "T1: SimulationRunner в открытой сцене не найден.";
            }

            PartDefinition[] parts = loaded.ToArray();
            double dryMass = 0d;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Name != "Топливо")
                {
                    dryMass += parts[i].MassKg;
                }
            }

            runner.Parts = parts;
            EditorUtility.SetDirty(runner);
            UnityEngine.SceneManagement.Scene scene = runner.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            return string.Format(
                "T1: {0} деталей, сухая масса {1:F0} кг, сцена \"{2}\" {3}.",
                parts.Length, dryMass, scene.name, saved ? "сохранена" : "НЕ сохранена");
        }

        private static SimulationRunner FindRunner()
        {
            SimulationRunner[] runners = UnityEngine.Object.FindObjectsByType<SimulationRunner>(
                FindObjectsInactive.Include);
            return runners.Length > 0 ? runners[0] : null;
        }

        [Serializable]
        private sealed class Vec3Dto
        {
            public float x;
            public float y;
            public float z;
        }

        [Serializable]
        private sealed class Vec4Dto
        {
            public float x;
            public float y;
            public float z;
            public float w;
        }

        [Serializable]
        private sealed class PartDto
        {
            public string name;
            public float massKg;
            public float jointStrengthNewtons;
            public float breakTorqueNm;
            public Vec3Dto offset;
            public string shape;
            public Vec3Dto dimensions;
            public Vec4Dto orientation;
            public int parentIndex;
            public Vec3Dto jointAnchor;
            public Vec3Dto engineOffset;
        }

        [Serializable]
        private sealed class VesselDto
        {
            public string name;
            public string note;
            public PartDto[] parts;
        }
    }
}
