using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// «Зона прицела» — конус направлений, ровно покрывающий нарисованное
    /// кольцо Crosshair (радиус в точках экрана). Наводка строителя (снап
    /// по узлам, удаление по прицелу) и интеракция (E) считают попадание
    /// только внутри этого конуса: зона взаимодействия совпадает с областью
    /// кольца, а не «примерно рядом» и не точкой ровно в центре.
    ///
    /// Угол берётся от ЖИВОГО FOV камеры: ПКМ-приближение сужает угол обзора —
    /// кольцо при этом закрывает меньше направлений, и конус сужается вместе
    /// с ним (иначе зона «уезжала» бы относительно нарисованного кольца).
    /// </summary>
    public static class AimCone
    {
        /// <summary>
        /// Проб на конус: центр + 8 по кольцу. Восьмиугольник закрывает диск
        /// с зазором ~6% радиуса (≈0.3 px при радиусе 5) — для кольца
        /// неразличимо, а точный конус для тел вращения — это уже сложная
        /// аналитика ради тех же 0.3 px.
        /// </summary>
        public const int Samples = 9;

        /// <summary>
        /// Тангенс половинного угла конуса для камеры: пиксельный радиус
        /// кольца переводится в угол через вертикальный FOV. Камера null —
        /// ноль (зона вырождается в точку — запасной путь для кадров без
        /// камеры, как было до прицела).
        /// </summary>
        public static float Tangent(Camera camera)
        {
            if (camera == null)
            {
                return 0f;
            }

            Crosshair crosshair = camera.GetComponent<Crosshair>();
            float radiusPx = crosshair != null ? crosshair.Radius : Crosshair.DefaultRadiusPx;
            return (radiusPx / (Mathf.Max(1f, Screen.height) * 0.5f))
                * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        /// <summary>
        /// Направления проб: 0 — центр, далее Samples-1 по кольцу радиуса
        /// tangent (в тангенциальной плоскости взгляда: та же величина, что
        /// проверяет снап). Базис строится от forward; при взгляде почти
        /// вдоль мировой вертикали опорой берётся right — вырожденный крест
        /// дал бы нулевые направления.
        /// </summary>
        public static void FillDirections(Vector3 forward, float tangent, Vector3[] directions)
        {
            directions[0] = forward;
            Vector3 axis = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.9f
                ? Vector3.right
                : Vector3.up;
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, axis));
            Vector3 up = Vector3.Cross(right, forward);
            for (int i = 0; i < Samples - 1; i++)
            {
                float angle = (Mathf.PI * 2f * i) / (Samples - 1);
                Vector3 offset = (right * Mathf.Cos(angle)) + (up * Mathf.Sin(angle));
                directions[i + 1] = Vector3.Normalize(forward + (offset * tangent));
            }
        }
    }
}
