using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>Палитра планшета: зелёный «люминофор» на почти чёрном. Янтарный — только для предупреждений.</summary>
    public static class TabletTheme
    {
        public static readonly Color32 Bg = new Color32(2, 9, 5, 255);
        public static readonly Color32 PanelBg = new Color32(4, 17, 9, 255);
        public static readonly Color32 Line = new Color32(16, 84, 40, 255);
        public static readonly Color32 LineHi = new Color32(30, 140, 66, 255);
        public static readonly Color32 Dim = new Color32(40, 140, 76, 255);
        public static readonly Color32 Main = new Color32(76, 224, 124, 255);
        public static readonly Color32 Bright = new Color32(208, 255, 222, 255);
        public static readonly Color32 Warn = new Color32(240, 196, 80, 255);
        public static readonly Color32 WarnDim = new Color32(110, 84, 24, 255);
        public static readonly Color32 ButtonOn = new Color32(14, 70, 34, 255);
        public static readonly Color32 ButtonHover = new Color32(10, 44, 22, 255);

        // Навбол: небо (тёмное) и земля (светлее), по три тона оттенения.
        public static readonly Color32[] Sky =
        {
            new Color32(3, 24, 12, 255), new Color32(5, 34, 17, 255), new Color32(8, 46, 23, 255),
        };

        public static readonly Color32[] Ground =
        {
            new Color32(14, 74, 36, 255), new Color32(20, 102, 50, 255), new Color32(28, 132, 66, 255),
        };

        public static readonly Color32 GridSky = new Color32(20, 96, 48, 255);
        public static readonly Color32 GridGround = new Color32(52, 178, 98, 255);
        public static readonly Color32 Horizon = new Color32(190, 255, 214, 255);
    }
}
