#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// The in-game "Render scale" slider needs Dynamic Resolution switched on in the HDRP
    /// asset (the project had it off). This does it for every HDRP asset: Software mode,
    /// 25..100 %. Cameras only use it when the player lowers the slider below 100 %, so
    /// the default image is unchanged. Runs once automatically; also in the menu.
    /// </summary>
    internal static class EnableDynamicResolution
    {
        private const string DoneKey = "Galilego.Settings.DynamicResolutionEnabled.v1";

        [InitializeOnLoadMethod]
        private static void RunOnce()
        {
            if (EditorPrefs.GetBool(DoneKey, false))
            {
                return;
            }

            // Assets may not be imported yet right after a domain reload.
            EditorApplication.delayCall += () =>
            {
                if (Apply(false) >= 0)
                {
                    EditorPrefs.SetBool(DoneKey, true);
                }
            };
        }

        [MenuItem("Galilego/Настройки/Включить динамическое разрешение в HDRP-ассетах")]
        private static void Menu()
        {
            int changed = Apply(true);
            EditorUtility.DisplayDialog("Динамическое разрешение",
                changed > 0 ? "Включено в HDRP-ассетах: " + changed + "." : "Уже включено везде (или HDRP-ассеты не найдены).", "OK");
        }

        /// <returns>Number of changed assets, or -1 if no HDRP asset was found.</returns>
        private static int Apply(bool log)
        {
            string[] guids = AssetDatabase.FindAssets("t:HDRenderPipelineAsset");
            if (guids.Length == 0)
            {
                return -1;
            }

            int changed = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null)
                {
                    continue;
                }

                var so = new SerializedObject(asset);
                SerializedProperty root = so.FindProperty("m_RenderPipelineSettings.dynamicResolutionSettings");
                SerializedProperty enabled = root != null ? root.FindPropertyRelative("enabled") : null;
                if (enabled == null)
                {
                    Debug.LogWarning("[Settings] В " + path + " не найдено dynamicResolutionSettings — пропущено.");
                    continue;
                }

                if (enabled.boolValue)
                {
                    continue;
                }

                enabled.boolValue = true;
                SetInt(root, "dynResType", 0);          // Software: works on every API
                SetFloat(root, "minPercentage", 25f);
                SetFloat(root, "maxPercentage", 100f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                changed++;
                Debug.Log("[Settings] Dynamic Resolution включён в " + path);
            }

            if (changed > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return changed;
        }

        private static void SetInt(SerializedProperty root, string name, int value)
        {
            SerializedProperty p = root.FindPropertyRelative(name);
            if (p != null)
            {
                p.intValue = value;
            }
        }

        private static void SetFloat(SerializedProperty root, string name, float value)
        {
            SerializedProperty p = root.FindPropertyRelative(name);
            if (p != null)
            {
                p.floatValue = value;
            }
        }
    }
}
#endif
