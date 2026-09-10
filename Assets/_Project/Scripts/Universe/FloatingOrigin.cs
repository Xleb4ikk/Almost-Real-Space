using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Единый якорь рендера (floating origin, double). Абсолютная astro-позиция,
    /// отображаемая в Unity-ноль. Все объекты ставятся как ToSimulation(abs − Anchor):
    /// рядом с игроком координаты малы (доли мм), а не ~радиус планеты, где
    /// float-ulp ≈ 0.1 м давал дрожание рельефа, смаз TAA и «сумасшедшее» солнце.
    /// Anchor выставляет SimulationRunner = PlayerPosition каждый кадр после физики
    /// (вьюхи — в LateUpdate, значит читают свежий якорь).
    /// </summary>
    public static class FloatingOrigin
    {
        public static Vector3d Anchor;

        public static Vector3 ToRender(Vector3d absoluteAstro)
        {
            return AstroFrame.ToSimulation(absoluteAstro - Anchor);
        }

        public static Quaternion RenderRotation(QuaternionD orientation)
        {
            return AstroFrame.ToSimulation(orientation);
        }
    }
}
