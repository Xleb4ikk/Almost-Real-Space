using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Редактор дороги: точки видны в Scene view, их можно двигать, добавлять
    /// Shift+кликом и удалять кнопкой в инспекторе.
    ///
    /// Shift+клик ставит точку на горизонтальную плоскость места (Y = 0 места).
    /// На ровной площадке это и есть земля. Если стоите на склоне — поставьте
    /// точку, потом подправьте её хендлом: высота всё равно берётся с рельефа.
    /// </summary>
    [CustomEditor(typeof(SurfaceRoad))]
    public sealed class SurfaceRoadEditor : UnityEditor.Editor
    {
        private int selected = -1;

        public override void OnInspectorGUI()
        {
            var road = (SurfaceRoad)target;

            if (road.GetComponentInParent<SurfaceSite>() == null)
            {
                EditorGUILayout.HelpBox(
                    "Дорога должна лежать ребёнком объекта с SurfaceSite (место). Иначе строить не из чего.",
                    MessageType.Warning);
            }

            var renderer = road.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial == null)
            {
                EditorGUILayout.HelpBox(
                    "Назначьте материал в Mesh Renderer (HDRP/Lit с текстурой асфальта).",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "Scene view: Shift+клик — добавить точку в конец. Клик по жёлтой точке — выбрать и двигать стрелками.",
                MessageType.Info);

            DrawDefaultInspector();

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Перестроить", GUILayout.Height(24f)))
                {
                    road.Rebuild();
                    SceneView.RepaintAll();
                }

                using (new EditorGUI.DisabledScope(selected < 0 || road.Points == null || selected >= road.Points.Count))
                {
                    if (GUILayout.Button("Удалить выбранную точку", GUILayout.Height(24f)))
                    {
                        Undo.RecordObject(road, "Road: удалить точку");
                        road.Points.RemoveAt(selected);
                        selected = -1;
                        EditorUtility.SetDirty(road);
                        road.Rebuild();
                        SceneView.RepaintAll();
                    }
                }
            }

            if (GUILayout.Button("Очистить все точки"))
            {
                Undo.RecordObject(road, "Road: очистить точки");
                road.Points.Clear();
                selected = -1;
                EditorUtility.SetDirty(road);
                road.Rebuild();
                SceneView.RepaintAll();
            }
        }

        private void OnSceneGUI()
        {
            var road = (SurfaceRoad)target;
            SurfaceSite site = road.GetComponentInParent<SurfaceSite>();
            if (site == null)
            {
                return;
            }

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            Vector3[] world = road.GetPointsWorld();
            if (world != null)
            {
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.95f);
                if (world.Length > 1)
                {
                    Handles.DrawAAPolyLine(3f, world);
                    if (road.Shape == SurfaceRoad.RoadShape.Polygon && world.Length > 2)
                    {
                        Handles.DrawAAPolyLine(3f, world[world.Length - 1], world[0]);
                    }
                }

                for (int i = 0; i < world.Length; i++)
                {
                    float size = HandleUtility.GetHandleSize(world[i]) * 0.08f;
                    if (i == selected)
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 moved = Handles.PositionHandle(world[i], site.transform.rotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(road, "Road: переместить точку");
                            road.SetPointFromWorld(i, moved);
                            EditorUtility.SetDirty(road);
                            road.Rebuild();
                        }
                    }
                    else if (Handles.Button(world[i], Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                    {
                        selected = i;
                        Repaint();
                    }
                }
            }

            // Клики по пустому месту не должны снимать выделение с дороги.
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
            }

            if (e.type == EventType.MouseDown && e.button == 0 && e.shift && !e.alt && !e.control)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                var plane = new Plane(site.transform.up, site.transform.position);
                float distance;
                if (plane.Raycast(ray, out distance))
                {
                    Undo.RecordObject(road, "Road: добавить точку");
                    road.AddPointFromWorld(ray.GetPoint(distance));
                    selected = road.Points.Count - 1;
                    EditorUtility.SetDirty(road);
                    road.Rebuild();
                    Repaint();
                    e.Use();
                }
            }
        }
    }
}
