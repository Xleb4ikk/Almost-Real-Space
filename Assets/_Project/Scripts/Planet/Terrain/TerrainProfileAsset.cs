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

        /// <summary>
        /// Точка входа проверки окон домена. Именно здесь, а не в самом
        /// TerrainProfile: данные лежат в plain [Serializable] классе, а
        /// OnValidate Unity вызывает только на UnityEngine.Object. Данные при
        /// этом НЕ меняются - автор должен увидеть проблему и решить сам.
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
