using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Terrain profile asset: the single source of truth for the shape/color/palette
    /// preset. A body references the asset (BodyAuthoring.TerrainPreset), so editing
    /// the profile updates every body that uses it, and the profile can be reused
    /// across scenes. The data is a plain TerrainProfile (not a ScriptableObject)
    /// for core testability.
    /// </summary>
    [CreateAssetMenu(menuName = "Galilego/Terrain Profile", fileName = "TerrainProfile")]
    public sealed class TerrainProfileAsset : ScriptableObject
    {
        public TerrainProfile Profile = new TerrainProfile();

        /// <summary>
        /// Entry point for the slope-domain window check. Here rather than in
        /// TerrainProfile itself: the data lives in a plain [Serializable] class and
        /// Unity only calls OnValidate on UnityEngine.Object. The data is NOT modified
        /// here — the author must see the problem and decide.
        /// </summary>
        private void OnValidate()
        {
            if (Profile == null)
            {
                return;
            }

            string problem = Profile.ValidateSlopeDampWindows();
            if (problem != null)
            {
                Debug.LogWarning("[TerrainProfileAsset] '" + name + "': " + problem, this);
            }
        }
    }
}
