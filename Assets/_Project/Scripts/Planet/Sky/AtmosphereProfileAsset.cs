using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Ассет-профиль атмосферы. Физика (DragSource/детекторы) и визуал
    /// (PlanetAtmosphereView) по-прежнему читают чистый AtmosphereProfile —
    /// ассет лишь хранит именованный пресет (EarthAtmosphere и т.п.), чтобы
    /// тела и сцены могли его переиспользовать.
    /// </summary>
    [CreateAssetMenu(menuName = "Galilego/Atmosphere Profile", fileName = "AtmosphereProfile")]
    public sealed class AtmosphereProfileAsset : ScriptableObject
    {
        public AtmosphereProfile Profile = new AtmosphereProfile();
    }
}
