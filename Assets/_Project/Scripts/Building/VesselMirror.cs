using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Зеркалирование визуала аппарата на тела зоны контакта — В САМОМ КОНЦЕ
    /// кадра (порядок 900, после ContactZoneHost с его −50). Почему отдельным
    /// компонентом: у SpacecraftBuilder порядок −110 (его Update обязан идти до
    /// шага раннера), а LateUpdate с тем же порядком выполнялся РАНЬШЕ
    /// ContactZoneHost — тела читались до того, как зона подвинет корень кадра.
    /// Из-за этого визуал каждый кадр отставал от тел на сдвиг якоря
    /// (поверхность планеты летит ~83 м/с — на низком FPS это метры за кадр),
    /// и ракету «кидало туда-сюда» при включённой физике.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public sealed class VesselMirror : MonoBehaviour
    {
        [Tooltip("Режим строительства: у него берутся детали и тела прокси.")]
        public SpacecraftBuilder Builder;

        private void LateUpdate()
        {
            if (Builder != null)
            {
                Builder.MirrorVisualsToBodies();
            }
        }
    }
}
