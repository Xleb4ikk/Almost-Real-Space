using System.Collections.Generic;
using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Твёрдый бокс постройки внутри места: стены, через которые нельзя пройти.
    ///
    /// Зачем свой бокс, а не Unity-коллайдер. Игрок двигается через
    /// SimulationRunner в double, Unity-физику он не использует: выталкивание
    /// есть только у стволов деревьев (GroundDecorCollisionRegistry, цилиндры в
    /// astro-координатах). Коллайдер сам по себе игрока не остановит — он нужен
    /// лишь для луча взаимодействия. Поэтому постройка описывается боксом в
    /// локальных осях (x — север, y — зенит, z — восток) и выталкивается тем же
    /// способом, что и стволы.
    ///
    /// Простой намеренно: стены твёрдые, на крышу не залезть, внутрь не войти.
    /// </summary>
    public sealed class SiteBox : MonoBehaviour
    {
        [Tooltip("Центр бокса в локальных осях объекта, м.")]
        public Vector3 Center = new Vector3(0f, 114f, 9f);

        [Tooltip("Размер бокса в локальных осях объекта, м.")]
        public Vector3 Size = new Vector3(150f, 228f, 150f);

        private void OnEnable()
        {
            SiteBoxRegistry.Register(this);
        }

        private void OnDisable()
        {
            SiteBoxRegistry.Unregister(this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(Center, Size);
        }
    }

    /// <summary>
    /// Реестр боксов построек и выталкивание из них игрока.
    ///
    /// Позиция приходит и уходит в astro-координатах (как PlayerPosition симулятора),
    /// а боксы живут в render-пространстве рядом с игроком — поэтому пересчёт
    /// идёт через FloatingOrigin: наружу отдаётся СМЕЩЕНИЕ, а не точка.
    ///
    /// Выталкивание по горизонтали, кратчайшей стороной: игрок выдавливается из
    /// грани, в которую вошёл. По вертикали бокс ловит только когда игрок
    /// перекрывается с ним по высоте (ноги…голова), поэтому на крышу можно
    /// запрыгнуть сбоку, а сквозь стену пройти нельзя.
    /// </summary>
    public static class SiteBoxRegistry
    {
        private const float PlayerHeight = 1.8f;
        private static readonly List<SiteBox> boxes = new List<SiteBox>();

        public static int Count => boxes.Count;

        public static void Register(SiteBox b)
        {
            if (!boxes.Contains(b))
            {
                boxes.Add(b);
            }
        }

        public static void Unregister(SiteBox b)
        {
            boxes.Remove(b);
        }

        public static bool TryResolve(Vector3d position, double playerRadius, out Vector3d resolved)
        {
resolved = position;
            if (boxes.Count == 0 || !Application.isPlaying)
            {
                return false;
            }

Vector3 p = FloatingOrigin.ToRender(position);
            Vector3 total = Vector3.zero;
            float r = (float)playerRadius;

            for (int i = 0; i < boxes.Count; i++)
            {
                SiteBox b = boxes[i];
                if (b == null || !b.isActiveAndEnabled)
                {
                    continue;
                }

Transform t = b.transform;
                Vector3 c = t.InverseTransformPoint(p + total) - b.Center;
                Vector3 h = b.Size * 0.5f;
                if (c.y < -h.y - PlayerHeight || c.y > h.y)
                {
                    continue;
                }

                float px = (h.x + r) - Mathf.Abs(c.x);
                float pz = (h.z + r) - Mathf.Abs(c.z);
                if (px <= 0f || pz <= 0f)
                {
                    continue;
                }

                Vector3 pushLocal = px < pz
                    ? new Vector3(Mathf.Sign(c.x) * px, 0f, 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(c.z) * pz);
                total += t.TransformVector(pushLocal);
            }

            if (total.sqrMagnitude <= 0f)
            {
                return false;
            }

resolved = position + AstroFrame.ToAstro(total);
            return true;
        }
    }
}