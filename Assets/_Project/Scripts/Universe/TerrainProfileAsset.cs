using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Ассет-профиль рельефа: один источник правды для пресета формы/цвета/палитры.
    /// Тело ссылается на ассет (BodyAuthoring.TerrainPreset), поэтому правка
    /// профиля обновляет все использующие его тела, а сам профиль можно
    /// переиспользовать между сценами. Данные — чистый TerrainProfile (не
    /// ScriptableObject) ради тестируемости ядра.
    /// </summary>
    [CreateAssetMenu(menuName = "Galilego/Terrain Profile", fileName = "TerrainProfile")]
    public sealed class TerrainProfileAsset : ScriptableObject
    {
        public TerrainProfile Profile = new TerrainProfile();
    }
}
