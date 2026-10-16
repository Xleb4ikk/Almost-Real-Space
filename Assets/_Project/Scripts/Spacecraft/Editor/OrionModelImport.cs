#if UNITY_EDITOR
using UnityEditor;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Кладётся в папку Editor. Включает Read/Write у модели капсулы: коллайдеры
    /// строятся по её мешам в рантайме (OrionCollisionBuilder), а нечитаемый меш
    /// отдаёт вершины только с ошибкой. После добавления файла FBX переимпортируется сам.
    /// </summary>
    public sealed class OrionModelImport : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath.EndsWith("Orion_CrewModule.fbx") && assetImporter is ModelImporter importer)
            {
                importer.isReadable = true;
            }
        }
    }
}
#endif
