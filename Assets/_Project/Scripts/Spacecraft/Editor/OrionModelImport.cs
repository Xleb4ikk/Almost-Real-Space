#if UNITY_EDITOR
using UnityEditor;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Lives in the Editor folder. Enables Read/Write on the capsule model: colliders
    /// are built from its meshes at runtime (OrionCollisionBuilder), and a non-readable
    /// mesh returns vertices only with an error. Reimports the FBX when the file is added.
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
