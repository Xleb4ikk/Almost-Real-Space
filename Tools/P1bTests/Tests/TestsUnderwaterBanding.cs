using System;
using System.Collections.Generic;
using System.Globalization;

internal static partial class P1bTests
{
    /// <summary>Локальный float3: в заглушках Unity есть только double3, а
    /// квантование надо считать именно в float, как в шейдере.</summary>
    private struct F3
    {
        public float x, y, z;
        public F3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static F3 operator *(F3 a, float s) => new F3(a.x * s, a.y * s, a.z * s);
        public static F3 operator *(float s, F3 a) => new F3(a.x * s, a.y * s, a.z * s);
        public static F3 operator *(F3 a, F3 b) => new F3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static F3 operator +(F3 a, F3 b) => new F3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static F3 operator -(F3 a, F3 b) => new F3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static F3 operator -(F3 a) => new F3(-a.x, -a.y, -a.z);
    }

    private static float Dot(F3 a, F3 b) => (a.x * b.x) + (a.y * b.y) + (a.z * b.z);

    private static F3 Normalize(F3 v)
    {
        float l = MathF.Sqrt(Dot(v, v));
        return l > 1e-20f ? new F3(v.x / l, v.y / l, v.z / l) : new F3(0f, 0f, 1f);
    }

    private static float Sat(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    private static F3 Exp3(F3 v) => new F3(MathF.Exp(v.x), MathF.Exp(v.y), MathF.Exp(v.z));

    private static string F3s(F3 v) => string.Format(
        CultureInfo.InvariantCulture, "({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);

    private static int Quant8(float v)
    {
        float c = v < 0f ? 0f : (v > 1f ? 1f : v);
        return (int)MathF.Round(c * 255f, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Полосы под водой: разбор ЧИСЛЕННЫЙ, без Unity.
    ///
    /// Воспроизводит арифметику UnderwaterViewFog во float (ровно как в шейдере)
    /// с векторами, снятыми с живой сессии, и меряет, сколько 8-битных уровней
    /// помещается в кадр по вертикали. Если уровней десятки, полосы - это
    /// квантование очень пологого градиента, и лечится дизерингом, а не
    /// правкой формулы.
    ///
    /// Векторы и параметры сняты из живой сессии: камера на 8.5686 м глубины,
    /// σ = (0.40, 0.12, 0.08), вода (0.02, 0.16, 0.30), ambient 0.08.
    /// </summary>
    private static int Test140_UnderwaterBanding()
    {
        F3 up = new F3(-0.433188f, -0.467012f, 0.770875f);
        F3 fwd = new F3(-0.680460f, -0.156067f, -0.715973f);
        F3 camRight = new F3(0.462598f, -0.849244f, -0.254536f);
        F3 camUp = new F3(-0.568311f, -0.504409f, 0.650073f);
        F3 sun = new F3(-1f, 0.000021f, -0.000032f);

        const float tanX = 1.026400f;
        const float tanY = 0.577350f;
        const float r = 1142991.00f;

        F3 sigma = new F3(0.40f, 0.12f, 0.08f);
        F3 waterColor = new F3(0.02f, 0.16f, 0.30f);
        const float ambient = 0.08f;
        const float sunPenetration = 1f;
        const float downwardDarkening = 0.6f;
        const int rows = 1440;

        // Сцена в этом кадре пустая (только вода), поэтому "пиксель" - дальняя
        // плоскость, и путь в воде равен выходу луча через поверхность.
        const float farPlane = 1.0e9f;

        Console.WriteLine("    sigma=" + F3s(sigma) + " вода=" + F3s(waterColor)
            + " N·L=" + Sat(Dot(up, sun)).ToString("F3", CultureInfo.InvariantCulture)
            + " взгляд вниз=" + Sat(-Dot(fwd, up)).ToString("F3", CultureInfo.InvariantCulture));
        Console.WriteLine("    кадр 2560x1440, FOV 60° верт., aspect 1.778, " + rows + " строк");
        Console.WriteLine();
        Console.WriteLine("  глубина  | tExit верх/низ, м   | T_g верх/низ | выход 8-бит R/G/B | уровней | макс. ступень");

        foreach (float depth in new float[] { 2f, 8.5686f, 30f, 200f })
        {
            float k = depth * (2f * r + depth);
            float ndl = Sat(Dot(up, sun));

            var levels = new SortedSet<int>();
            var rowG = new float[rows];
            float tTop = 0f, tBot = 0f, tgTop = 0f, tgBot = 0f;
            int qTopR = 0, qTopG = 0, qTopB = 0, qBotR = 0, qBotG = 0, qBotB = 0;

            for (int i = 0; i < rows; i++)
            {
                // ndc.y от +1 (верх кадра) до -1 (низ), как в шейдере.
                float ndcY = 1f - (2f * (i + 0.5f) / rows);
                F3 d = Normalize(fwd + (camUp * (ndcY * tanY)) + (camRight * (0f * tanX)));
                float b = r * Dot(up, d);
                float tExit = (k > 0f) ? (k / (MathF.Sqrt((b * b) + k) + b)) : -1f;
                float path = farPlane;
                if (tExit > 0f)
                {
                    path = MathF.Min(path, tExit);
                }

                F3 T = Exp3(sigma * -path);
                F3 sunT = Exp3(sigma * -(depth * sunPenetration));
                F3 scatter = waterColor * (sunT * ndl);
                scatter = scatter + (waterColor * ambient);
                float down = Sat(-Dot(d, up));
                scatter = scatter * (1f + ((downwardDarkening - 1f) * down));

                // scene = дальняя плоскость: геометрии в кадре нет, и за
                // поглощённым светом остаётся только вуаль.
                F3 outC = scatter - (scatter * T);

                int qr = Quant8(outC.x), qg = Quant8(outC.y), qb = Quant8(outC.z);
                levels.Add(qr);
                levels.Add(qg);
                levels.Add(qb);
                rowG[i] = outC.y;

                if (i == 0) { tTop = tExit; tgTop = T.y; qTopR = qr; qTopG = qg; qTopB = qb; }
                if (i == rows - 1) { tBot = tExit; tgBot = T.y; qBotR = qr; qBotG = qg; qBotB = qb; }
            }

            int maxStep = 0;
            int stepsAt = 0;
            for (int i = 1; i < rows; i++)
            {
                int step = Math.Abs(Quant8(rowG[i]) - Quant8(rowG[i - 1]));
                if (step > 0)
                {
                    stepsAt++;
                }

                if (step > maxStep)
                {
                    maxStep = step;
                }
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,7:F1} м | {1,8:N0} / {2,8:N0} | {3:F3} / {4:F3} | {5,3} {6,3} {7,3} -> {8,3} {9,3} {10,3} | {11,5} | {12,3} (рядов с шагом: {13,4})",
                depth, tTop, tBot, tgTop, tgBot,
                qTopR, qTopG, qTopB, qBotR, qBotG, qBotB, levels.Count, maxStep, stepsAt));
        }

        Console.WriteLine();
        Console.WriteLine("  «уровней» - число различных 8-битных значений на весь кадр.");
        Console.WriteLine("  «макс. ступень» - насколько меняется зелёный канал между соседними строками.");
        Console.WriteLine();

        CompareVeilModels(depth: 8.5686f, r: r, up: up, sun: sun, camUp: camUp, tanY: tanY,
            sigma: sigma, waterColor: waterColor, ndl: Sat(Dot(up, sun)), rows: rows);
        return 0;
    }

    /// <summary>
    /// Сравнивает текущую модель вуали с исправленной.
    ///
    /// Текущая: цвет воды трактуется как АЛЬБЕДО и домножается на малое число
    /// света (0.36*0.433 + 0.08 = 0.236), отчего вуаль выходит ~0.025 линейного
    /// и вся картинка умещается в 4-12 кодов из 256 - отсюда полосы.
    ///
    /// Исправленная: рассеянный свет - это ИЗЛУЧЕНИЕ, а не отражение, поэтому
    /// уровень освещения берётся ~1 и только гаснет с глубиной. Плюс направление
    /// на солнце смягчено: рассеяние в объёме почти изотропно, ламбертовский
    /// cos там неуместен и дополнительно съедает яркость.
    /// </summary>
    private static void CompareVeilModels(
        float depth, float r, F3 up, F3 sun, F3 camUp, float tanY,
        F3 sigma, F3 waterColor, float ndl, int rows)
    {
        const float downwardDarkening = 0.6f;

        // Свет, дошедший до камеры: солнце погасло на пути вниз, плюс подсветка
        // от неба/рассеяния, которой в воде много.
        F3 sunT = Exp3(sigma * -depth);
        F3 lightOld = (sunT * ndl) + new F3(0.08f, 0.08f, 0.08f);
        F3 lightNew = (sunT * (0.35f + (0.65f * ndl))) + new F3(0.55f, 0.55f, 0.55f);

        Console.WriteLine("  сравнение моделей вуали на 8.5686 м, кадр из " + rows + " строк:");
        Console.WriteLine("    свет: старый " + F3s(lightOld) + "   новый " + F3s(lightNew));
        Console.WriteLine("    вуаль G: старый " + F3s(waterColor * lightOld)
            + "   новый " + F3s(waterColor * lightNew));
        Console.WriteLine();
        Console.WriteLine("    глубина дна | уровней 8-бит | зелёный сверху->снизу | ступень/строка");

        foreach (float bottomDist in new float[] { 8.5f, 20f, 60f, 200f })
        {
            // Дно внизу кадра на расстоянии bottomDist, у горизонта - далеко.
            float topDist = bottomDist + 400f;
            MeasureBands(bottomDist, topDist, depth, r, up, camUp, tanY, sigma,
                waterColor, lightOld, downwardDarkening, rows, "старый");
            MeasureBands(bottomDist, topDist, depth, r, up, camUp, tanY, sigma,
                waterColor, lightNew, downwardDarkening, rows, "новый");
        }
    }

    private static void MeasureBands(
        float bottomDist, float topDist, float depth, float r, F3 up, F3 camUp, float tanY,
        F3 sigma, F3 waterColor, F3 light, float downwardDarkening, int rows, string tag)
    {
        var levels = new SortedSet<int>();
        var rowG = new float[rows];
        int qTopG = 0, qBotG = 0;

        for (int i = 0; i < rows; i++)
        {
            float t = i / (float)(rows - 1);

            // Расстояние до дна: внизу близко, к горизонту далеко.
            float dist = bottomDist + ((topDist - bottomDist) * (t * t));
            float ndcY = 1f - (2f * (i + 0.5f) / rows);
            F3 d = Normalize(camUp + (new F3(0f, 0f, 0f) * (ndcY * tanY)));
            d = new F3(d.x, ndcY * 0.6f, -0.8f);
            d = Normalize(d);

            F3 T = Exp3(sigma * -dist);
            F3 scatter = waterColor * light;
            float down = Sat(-Dot(d, up));
            scatter = scatter * (1f + ((downwardDarkening - 1f) * down));
            F3 outC = scatter - (scatter * T);

            int qg = Quant8(outC.y);
            levels.Add(Quant8(outC.x));
            levels.Add(qg);
            levels.Add(Quant8(outC.z));
            rowG[i] = outC.y;
            if (i == 0)
            {
                qTopG = qg;
            }

            if (i == rows - 1)
            {
                qBotG = qg;
            }
        }

        int maxStep = 0;
        for (int i = 1; i < rows; i++)
        {
            int step = Math.Abs(Quant8(rowG[i]) - Quant8(rowG[i - 1]));
            if (step > maxStep)
            {
                maxStep = step;
            }
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    {0,9:F1} м {1} | {2,12} | {3,6} -> {4,-6} | макс {5,3}",
            bottomDist, tag.PadRight(5), levels.Count, qTopG, qBotG, maxStep));
    }
}
