using System;
using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Компонент системы: вешается на корневой GameObject (звезду). Дети этой
    /// иерархии с BodyAuthoring образуют систему. Здесь же параметры выпечки:
    /// сколько лет печём, качество, куда класть файлы.
    ///
    /// Сценарий работы:
    /// 1. В редакторе: настроить иерархию тел, нажать «Сгенерировать эфемериды»
    ///    в инспекторе (StarSystemAuthoringEditor, фоновой поток с прогрессом).
    /// 2. В рантайме: BuildBlueprint().Build() → EphemerisRuntime.AttachFromStreamingAssets(sys)
    ///    → тела получают рельсы; файлов нет — тела остаются на кеплеровых рельсах.
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
        /// Собирает чертёж системы из иерархии GameObject'ов: каждый
        /// BodyAuthoring в этой иерархии становится телом; родитель в иерархии
        /// трансформов = орбитальный родитель.
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
        /// Полный рантайм-путь: сборка системы из иерархии + попытка прицепить
        /// испечённые эфемериды из StreamingAssets. Возвращает true, если рельсы
        /// прицеплены; false — тел нет или файлов нет (кеплеровы рельсы, не ошибка).
        /// </summary>
        public bool BuildAndAttach()
        {
            StarSystem sys = BuildBlueprint().Build();
            return EphemerisRuntime.AttachFromStreamingAssets(sys);
        }
    }
}
