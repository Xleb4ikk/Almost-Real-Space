using Galilego.Core;
using Galilego.Universe;
using UnityEditor;
using UnityEngine;

namespace Galilego.EditorTools
{
    /// <summary>
    /// Inspector read-out for StarPreset: shows the complete derived state, so the
    /// author never has to trust a hand-tuned number. Editing the preset is the only
    /// way to change the star (mass, optional overrides, art multipliers).
    /// </summary>
    [CustomEditor(typeof(StarPreset))]
    public sealed class StarPresetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            StarPreset preset = (StarPreset)target;
            StarState state;
            try
            {
                state = preset.Derive();
            }
            catch (System.Exception exception)
            {
                EditorGUILayout.HelpBox("Не удалось вывести звезду: " + exception.Message, MessageType.Error);
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Выведено из массы (StarPhysics, double)", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Label("Светимость", state.LuminositySolar.ToString("E4") + " L☉");
                Label("Радиус", state.RadiusSolar.ToString("E4") + " R☉  (" + state.RadiusKm.ToString("N0") + " км)");
                Label("Teff", state.EffectiveTemperatureK.ToString("F0") + " K");
                Label("g", state.SurfaceGravityMps2.ToString("F1") + " м/с²");
                Label("Ячейка грануляции", state.GranulationCellKm.ToString("F1") + " км");
                Label("Ячеек поперёк диска", state.GranulationCells.ToString("F0"));
                Label("Контраст грануляции", state.GranulationContrast.ToString("F4"));
                Label("Пятна", (state.SpotCoverage * 100d).ToString("F2") + " %  (Tумбра " +
                    state.SpotUmbraTemperatureK.ToString("F0") + " K)");
                Label("Цвет cT'", string.Format("({0:F3}, {1:F3}, {2:F3})  xy=({3:F4}, {4:F4})",
                    state.ColorR, state.ColorG, state.ColorB, state.ChromaticityX, state.ChromaticityY));
                Label("eta / eta☉", state.VisibleEtaRatioToSun.ToString("F4"));
                Label("V (до сжатия)", state.DiscBrightness.ToString("E2"));
                Label("V' (после сжатия)", state.DiscBrightnessCompressed.ToString("F2"));
                Label("Угловой диаметр", string.Format(
                    "0.075 а.е. — {0:F2}°,  1 а.е. — {1:F2}′,  5.2 а.е. — {2:F2}′",
                    state.AngularDiameterDeg(0.075d * StarPhysics.AuKm),
                    state.AngularDiameterDeg(StarPhysics.AuKm) * 60d,
                    state.AngularDiameterDeg(5.2d * StarPhysics.AuKm) * 60d));
                Label("m", string.Format("1 а.е. — {0:F2},  5.2 а.е. — {1:F2}",
                    state.ApparentMagnitude(StarPhysics.AuKm),
                    state.ApparentMagnitude(5.2d * StarPhysics.AuKm)));
                Label("Лимб-даркенинг", string.Format("R {0:F3}, G {1:F3}, B {2:F3}",
                    state.LimbR, state.LimbG, state.LimbB));
            }

            if (state.HasOverrides)
            {
                EditorGUILayout.HelpBox(state.OverrideWarning, MessageType.Warning);
            }

            if (GUILayout.Button("Записать вывод в консоль"))
            {
                Debug.Log(string.Format(
                    "[StarPreset] {0}: M={1} M☉, L={2:E6} L☉, R={3:E6} R☉ ({4:E1} км), Teff={5:F1} K, " +
                    "g={6:F2} м/с², cell={7:F2} км, N={8:F1}, spots={9:P3}, V={10:E4}, V'={11:F3}, " +
                    "color=({12:F3},{13:F3},{14:F3}), eta/eta☉={15:F5}",
                    preset.name, state.MassSolar, state.LuminositySolar, state.RadiusSolar, state.RadiusKm,
                    state.EffectiveTemperatureK, state.SurfaceGravityMps2, state.GranulationCellKm,
                    state.GranulationCells, state.SpotCoverage, state.DiscBrightness,
                    state.DiscBrightnessCompressed, state.ColorR, state.ColorG, state.ColorB,
                    state.VisibleEtaRatioToSun), preset);
            }
        }

        private static void Label(string name, string value)
        {
            EditorGUILayout.LabelField(name, value);
        }
    }
}
