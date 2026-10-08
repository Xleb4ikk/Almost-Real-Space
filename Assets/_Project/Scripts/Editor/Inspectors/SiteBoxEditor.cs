using Galilego.Universe;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    [CustomEditor(typeof(SiteBox))]
    public sealed class SiteBoxEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "BakedParts");
            serializedObject.ApplyModifiedProperties();

            SiteBox site = (SiteBox)target;
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Точная форма коллизии", EditorStyles.boldLabel);

            int hulls = 0;
            int boxParts = 0;
            int boxCount = 0;
            if (site.BakedParts != null)
            {
                foreach (SiteBox.BakedPartData part in site.BakedParts)
                {
                    if (part == null)
                    {
                        continue;
                    }

                    if (part.UseConvexMesh)
                    {
                        hulls++;
                    }
                    else
                    {
                        boxParts++;
                        if (part.Boxes != null)
                        {
                            boxCount += part.Boxes.Count;
                        }
                    }
                }
            }

            EditorGUILayout.LabelField("Запечено частей", (hulls + boxParts).ToString());
            EditorGUILayout.LabelField("Convex hull (точных)", hulls.ToString());
            EditorGUILayout.LabelField("Боксами", boxParts + " частей / " + boxCount + " боксов");

            if (hulls + boxParts == 0)
            {
                EditorGUILayout.HelpBox(
                    "Сейчас используется один грубый бокс на каждый меш. Запекание: выпуклые части " +
                    "получают точный convex MeshCollider (скосы — без лесенки), вогнутые — набор боксов. " +
                    "Авторский MeshCollider (convex) или BoxCollider на части/SiteBox имеет приоритет.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Hull'ы показаны каркасом меша, боксы — оранжевым контуром, зелёный — грубый фильтр. " +
                    "В рантайме коллайдеры создаются скрытыми и проверяются через ComputePenetration " +
                    "(non-convex MeshCollider не поддерживается этой системой).",
                    MessageType.None);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Запечь коллайдеры"))
                {
                    Bake(site);
                }

                using (new EditorGUI.DisabledScope(hulls + boxParts == 0))
                {
                    if (GUILayout.Button("Очистить", GUILayout.Width(90f)))
                    {
                        SiteBoxBaker.Clear(site);
                    }
                }
            }
        }

        private static void Bake(SiteBox site)
        {
            try
            {
                int count = SiteBoxBaker.Bake(site);
                Debug.Log("Готово: " + count + " коллайдеров. Сохраните сцену (Ctrl+S).", site);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, site);
                EditorUtility.DisplayDialog("Не удалось запечь коллайдеры", exception.Message, "OK");
            }
        }
    }
}
