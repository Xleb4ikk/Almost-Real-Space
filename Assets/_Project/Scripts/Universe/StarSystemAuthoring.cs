using System;
using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// System component: attach to the root GameObject (the star). Children in this
    /// hierarchy with a BodyAuthoring form the system. Baking parameters live here:
    /// how many years to bake, quality, where to put the files.
    ///
    /// Workflow:
    /// 1. In the editor: set up the body hierarchy and press "Generate ephemerides"
    ///    in the inspector (StarSystemAuthoringEditor, background thread with progress).
    /// 2. At runtime: BuildBlueprint().Build() → EphemerisRuntime.AttachFromStreamingAssets(sys)
    ///    → bodies get rails; with no files the bodies stay on Keplerian rails.
    /// </summary>
    public sealed class StarSystemAuthoring : MonoBehaviour
    {
        [Header("Выпечка эфемерид")]
        [Tooltip("Горизонт выпечки в годах. 1024 года ≈ 80 с и ≈ 120 МБ BG3 для системы 1+5+6.")]
        public int SpanYears = 1024;

        [Tooltip("Степень чебышёвского интерполянта на сегмент (узлов Degree+1).")]
        public int Degree = 12;

        [Tooltip("Кап подшага интегратора, с.")]
        public double MaxStepSeconds = 900d;

        [Tooltip("Бюджет ошибки квантования BG3, м на позицию за сегмент.")]
        public double QuantBudgetMeters = 2d;

        [Tooltip("Сжатые файлы BG3 (квантование + varint + gzip). Выключить только для отладки.")]
        public bool CompressFiles = true;

        [Tooltip("Папка внутри проекта для испечённых файлов (manifest.txt + тела).")]
        public string OutputFolder = "Assets/StreamingAssets/Ephemerides";

        /// <summary>
        /// Builds the system blueprint from the GameObject hierarchy: each
        /// BodyAuthoring in this hierarchy becomes a body; the transform parent
        /// is the orbital parent.
        /// </summary>
        public SystemBlueprint BuildBlueprint()
        {
            BodyAuthoring[] authorings = GetComponentsInChildren<BodyAuthoring>(true);
            if (authorings.Length == 0)
            {
                throw new InvalidOperationException(
                    "В иерархии " + name + " нет ни одного BodyAuthoring: добавьте компоненты тел (корень — звезда).");
            }

            var indices = new Dictionary<Transform, int>(authorings.Length);
            for (int i = 0; i < authorings.Length; i++)
            {
                indices[authorings[i].transform] = i;
            }

            var blueprint = new SystemBlueprint();
            blueprint.Bodies = new List<BodyBlueprint>(authorings.Length);
            for (int i = 0; i < authorings.Length; i++)
            {
                int parentIndex = -1;
                Transform parent = authorings[i].transform.parent;
                while (parent != null)
                {
                    if (indices.TryGetValue(parent, out int idx))
                    {
                        parentIndex = idx;
                        break;
                    }

                    parent = parent.parent;
                }

                blueprint.Bodies.Add(authorings[i].ToBlueprint(parentIndex));
            }

            return blueprint;
        }

        /// <summary>
        /// Full runtime path: builds the system from the hierarchy and tries to attach
        /// baked ephemerides from StreamingAssets. Returns true if rails were attached;
        /// false means no bodies or no files (Keplerian rails, not an error).
        /// </summary>
        public bool BuildAndAttach()
        {
            StarSystem sys = BuildBlueprint().Build();
            return EphemerisRuntime.AttachFromStreamingAssets(sys);
        }
    }
}
