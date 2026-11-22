#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// A shader that is only ever created from code (<c>Shader.Find("Galilego/…")</c>)
    /// is referenced by no asset, so the player build strips it. The runtime then
    /// silently falls back: the star disc becomes a white HDRP/Unlit square, the
    /// underwater fog and the outline effect just disappear. That is exactly what
    /// happened to StarDisc/StarGlare/StarCorona in the 0.6 builds.
    ///
    /// This guard scans every runtime script for <c>Shader.Find</c> literals and fails
    /// the build if a project shader is not reachable: not in a <c>Resources</c> folder,
    /// not in Project Settings > Graphics > Always Included Shaders and not used by any
    /// material asset.
    /// </summary>
    internal sealed class RuntimeShaderBuildGuard : IPreprocessBuildWithReport
    {
        private const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";

        private static readonly Regex FindCall = new Regex(
            "Shader\\.Find\\(\\s*\"([^\"]+)\"\\s*\\)", RegexOptions.Compiled);

        private static readonly Regex MaterialShader = new Regex(
            "m_Shader: \\{fileID: -?\\d+, guid: ([0-9a-f]{32})", RegexOptions.Compiled);

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var unresolved = new List<string>();
            List<string> missing = FindMissingShaders(unresolved);
            if (missing.Count == 0 && unresolved.Count == 0)
            {
                return;
            }

            var message = new StringBuilder();
            message.AppendLine("[RuntimeShaderBuildGuard] Шейдеры, которые создаются из кода, " +
                               "не попадут в билд: Unity вырезает шейдеры, на которые не ссылается ни один ассет.");
            if (missing.Count > 0)
            {
                message.AppendLine("Добавьте их в Project Settings > Graphics > Always Included Shaders " +
                                   "или положите в папку Resources:");
                for (int i = 0; i < missing.Count; i++)
                {
                    message.Append("  • ").AppendLine(missing[i]);
                }
            }

            if (unresolved.Count > 0)
            {
                message.AppendLine("Не найдены в проекте вовсе (опечатка в имени шейдера?):");
                for (int i = 0; i < unresolved.Count; i++)
                {
                    message.Append("  • ").AppendLine(unresolved[i]);
                }
            }

            throw new BuildFailedException(message.ToString());
        }

        [MenuItem("Galilego/Настройки/Проверить шейдеры для билда")]
        private static void Menu()
        {
            var unresolved = new List<string>();
            List<string> missing = FindMissingShaders(unresolved);
            if (missing.Count == 0 && unresolved.Count == 0)
            {
                EditorUtility.DisplayDialog("Шейдеры для билда",
                    "Все шейдеры, создаваемые из кода, попадут в билд.", "OK");
                return;
            }

            for (int i = 0; i < missing.Count; i++)
            {
                Debug.LogError("[RuntimeShaderBuildGuard] Нет в билде: " + missing[i]);
            }

            for (int i = 0; i < unresolved.Count; i++)
            {
                Debug.LogError("[RuntimeShaderBuildGuard] Не найден в проекте: " + unresolved[i]);
            }

            EditorUtility.DisplayDialog("Шейдеры для билда",
                "Проблемных шейдеров: " + (missing.Count + unresolved.Count) + ". Подробности в Console.", "OK");
        }

        /// <summary>
        /// Project shaders named by <c>Shader.Find</c> in runtime scripts that the build
        /// would strip. Editor-only and test scripts are ignored: they never run in a player.
        /// </summary>
        private static List<string> FindMissingShaders(List<string> unresolved)
        {
            var missing = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> includedGuids = CollectAlwaysIncludedGuids();
            HashSet<string> materialGuids = CollectMaterialShaderGuids();

            string[] scripts = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            foreach (string guid in scripts)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("/Tests/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                string source = System.IO.File.ReadAllText(path);
                if (source.IndexOf("Shader.Find", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                foreach (Match match in FindCall.Matches(source))
                {
                    string name = match.Groups[1].Value;
                    if (!seen.Add(name))
                    {
                        continue;
                    }

                    Shader shader = Shader.Find(name);
                    if (shader == null)
                    {
                        // URP/other-pipeline fallbacks are expected to be absent here.
                        if (name.StartsWith("Galilego/", StringComparison.Ordinal))
                        {
                            unresolved.Add(name);
                        }

                        continue;
                    }

                    string shaderPath = AssetDatabase.GetAssetPath(shader);
                    // Only Assets/… shaders can be stripped: built-in and package shaders
                    // ship with the player or their render pipeline.
                    if (string.IsNullOrEmpty(shaderPath) ||
                        !shaderPath.StartsWith("Assets/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (shaderPath.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    string shaderGuid = AssetDatabase.AssetPathToGUID(shaderPath);
                    if (includedGuids.Contains(shaderGuid) || materialGuids.Contains(shaderGuid))
                    {
                        continue;
                    }

                    missing.Add(name + "   (" + shaderPath + ")");
                }
            }

            return missing;
        }

        /// <summary>Guids of the shaders listed in Project Settings > Graphics > Always Included Shaders.</summary>
        private static HashSet<string> CollectAlwaysIncludedGuids()
        {
            var guids = new HashSet<string>(StringComparer.Ordinal);

            UnityEngine.Object settings = GraphicsSettings.GetGraphicsSettings();
            if (settings == null)
            {
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsPath);
                settings = assets != null && assets.Length > 0 ? assets[0] : null;
            }

            if (settings == null)
            {
                return guids;
            }

            var so = new SerializedObject(settings);
            SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray)
            {
                return guids;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                UnityEngine.Object shader = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (shader == null)
                {
                    continue;
                }

                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out string guid, out long _))
                {
                    guids.Add(guid);
                }
            }

            return guids;
        }

        /// <summary>
        /// Guids of the shaders used by any material asset. Materials referenced by the
        /// scenes pull their shader into the build, so those are safe.
        /// </summary>
        private static HashSet<string> CollectMaterialShaderGuids()
        {
            var guids = new HashSet<string>(StringComparer.Ordinal);
            string[] materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            foreach (string guid in materials)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                string text = System.IO.File.ReadAllText(path);
                foreach (Match match in MaterialShader.Matches(text))
                {
                    guids.Add(match.Groups[1].Value);
                }
            }

            return guids;
        }
    }
}
#endif
