using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Atmosphere profile asset. Physics (DragSource/detectors) and visuals
    /// (PlanetAtmosphereView) still read a plain AtmosphereProfile — the asset
    /// only stores a named preset (EarthAtmosphere etc.) so bodies and scenes can
    /// reuse it.
    /// </summary>
    [CreateAssetMenu(menuName = "Galilego/Atmosphere Profile", fileName = "AtmosphereProfile")]
    public sealed class AtmosphereProfileAsset : ScriptableObject
    {
        public AtmosphereProfile Profile = new AtmosphereProfile();
    }
}
