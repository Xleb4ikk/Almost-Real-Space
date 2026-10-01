using System;
using System.Globalization;

internal static partial class P1bTests
{
    // Показатель преломления воды, как в шейдере (WaterSurface.shader, ветка
    // isUnderwater). Держим константой в двух местах: тест и шейдер.
    private const float WaterIndex = 1.33f;
    private const float AirIndex = 1.0f;

    /// <summary>
    /// Окно Снелла и Френель воды: ЭТАЛОН, а не проверка шейдера.
    ///
    /// Тест считает ту же формулу, что и шейдер, поэтому сам по себе он
    /// доказывает только одно: формулы в проекте непротиворечивы. Настоящая
    /// сверка с GPU делается снимком окна: в WaterSurface.shader есть
    /// _UnderwaterDebugMode (1 = Френель в градациях серого, 2 = маска окна),
    /// и инструмент съёмки (Editor/SurfaceCaptureTool, действие
    /// underwater-debug) снимает её при взгляде строго вверх с 3 м. Граница
    /// маски на снимке обязана лечь на 48.6° ± 0.5° — это и есть проверка
    /// шейдера, которую тест задаёт числом.
    /// </summary>
    private static int Test139_SnellWindowReference()
    {
        // В этом harness о провале сообщает Check: он же ведёт счётчик
        // failures, из-за которого печатается ALL PASS. Возврат из теста —
        // всегда 0, иначе один и тот же провал учитывался бы дважды.
        int Fail(string fmt, params object[] args)
        {
            Check(false, "Test139_SnellWindow", string.Format(CultureInfo.InvariantCulture, fmt, args));
            return 0;
        }

        int Ok(string fmt, params object[] args)
        {
            Console.WriteLine("    " + string.Format(CultureInfo.InvariantCulture, fmt, args));
            return 0;
        }

        // --- критический угол ------------------------------------------------
        // ПРО ЦИФРУ: учебные 48.6° — это n = 1.333. В шейдере стоит 1.33,
        // поэтому критический угол равен 48.75°. Разница 0.15° глазом не
        // видна, но выписать её «по памяти» было бы расхождением с шейдером,
        // поэтому число считается, а не зашито.
        double critical = Math.Asin(AirIndex / WaterIndex) * 180.0 / Math.PI;
        if (Math.Abs(critical - 48.75) > 0.05)
        {
            return Fail("критический угол {0:F2}°, ожидался 48.75° (n = 1.33)", critical);
        }

        double cosCritical = Math.Cos(critical * Math.PI / 180.0);
        if (Math.Abs(cosCritical - 0.6594) > 0.001)
        {
            return Fail("cos критического {0:F4}, ожидался 0.6594", cosCritical);
        }

        // --- Френель на нормальном падении ------------------------------------
        float f0 = FresnelWater(1.0f);
        float f0Expected = (float)Math.Pow((WaterIndex - AirIndex) / (WaterIndex + AirIndex), 2.0);
        if (Math.Abs(f0 - f0Expected) > 1e-4f)
        {
            return Fail("F(0°) = {0:F4}, ожидалось {1:F4}", f0, f0Expected);
        }

        if (Math.Abs(f0 - 0.0204f) > 0.0005f)
        {
            return Fail("F(0°) = {0:F4}, у воды должно быть около 0.02", f0);
        }

        // --- монотонный рост и форма кривой -----------------------------------
        // Френель НЕ растёт плавно к единице: на 48° (это всего 0.75° до
        // критического) отражение ещё 0.39, и в единицу оно уходит в
        // последние доли градуса. Первая версия теста ждала 0.97 на 48° и
        // падала — формула была права, ожидание нет.
        bool growing = true;
        float previous = f0;
        for (int degrees = 1; degrees <= 48; degrees++)
        {
            float f = FresnelWater((float)Math.Cos(degrees * Math.PI / 180.0));
            if (f < previous - 1e-6f)
            {
                growing = false;
            }

            previous = f;
        }

        if (!growing)
        {
            return Fail("Френель убывает на 1..48°, а должна расти монотонно");
        }

        if (Math.Abs(previous - 0.39f) > 0.04f)
        {
            return Fail("F(48°) = {0:F3}, ожидалось ~0.39 (до критического ещё далеко)", previous);
        }

        float atCritical = FresnelWater((float)cosCritical);
        if (Math.Abs(atCritical - 1.0f) > 0.002f)
        {
            return Fail("F на критическом угле = {0:F4}, ожидалась 1", atCritical);
        }

        Ok("кривая: F(0)={0:F4}, F(48)={1:F3}, F(крит)={2:F3} — резкий фронт у 48.75°", f0, previous, atCritical);

        // --- за критическим: полное внутреннее отражение ---------------------
        foreach (double degrees in new double[] { 49d, 60d, 89d })
        {
            double rad = degrees * Math.PI / 180.0;
            float f = FresnelWater((float)Math.Cos(rad));
            if (f < 0.999f)
            {
                return Fail("при {0:F0}° (за критическим) Френель = {1:F4}, должно быть 1", degrees, f);
            }
        }

        // --- закон сохранения: отражение + пропускание = 1 --------------------
        // Проверяем ПО ПОЛЯРИЗАЦИЯМ, а не «F + T = 1» одной формулой: у
        // неполяризованного света T — это (n2·cosθt)/(n1·cosθi)·(t_s²+t_p²)/2,
        // и подставлять «1 − F» значило бы проверить тождество само с собой.
        // Смысл проверки: rs² + T_s = 1 держится только если Френель и Снелл
        // взяты из одной оптики. Расхождение означало бы, что окно и зеркало
        // рисуются из разных формул.
        double worstSum = 0d;
        for (int degrees = 0; degrees <= 48; degrees += 4)
        {
            double rad = degrees * Math.PI / 180.0;
            double cosI = Math.Cos(rad);
            double sinT = WaterIndex * Math.Sin(rad);
            if (sinT >= 1.0)
            {
                return Fail("при {0:F0}° ожидался выход луча, а sinT = {1:F3}", degrees, sinT);
            }

            double cosT = Math.Sqrt(1.0 - (sinT * sinT));
            double rsA = ((WaterIndex * cosI) - (AirIndex * cosT)) / ((WaterIndex * cosI) + (AirIndex * cosT));
            double rpA = ((WaterIndex * cosT) - (AirIndex * cosI)) / ((WaterIndex * cosT) + (AirIndex * cosI));
            double ts = (2.0 * WaterIndex * cosI) / ((WaterIndex * cosI) + (AirIndex * cosT));
            double tp = (2.0 * WaterIndex * cosI) / ((AirIndex * cosI) + (WaterIndex * cosT));
            double transmitFactor = (AirIndex * cosT) / (WaterIndex * cosI);

            double sumS = (rsA * rsA) + (transmitFactor * ts * ts);
            double sumP = (rpA * rpA) + (transmitFactor * tp * tp);
            worstSum = Math.Max(worstSum, Math.Max(Math.Abs(sumS - 1.0), Math.Abs(sumP - 1.0)));
        }

        if (worstSum > 0.01)
        {
            return Fail("R + T не равно 1: худшее расхождение {0:F4} на 0..48°", worstSum);
        }

        // --- преломление: отклонение луча при наклоне нормали ----------------
        // Для малого наклона β отклонение преломлённого луча от исходного
        // равно (1 - 1/n)·β. Именно эта величина решает, будет ли рябь на
        // потолке вообще что-то искажать: при β = 10° это 2.5°, то есть
        // небо в окне смещается на 2.5° — почти незаметно, и поэтому
        // «плоский потолок» не лечится одной формулой Снелла.
        double beta = 10.0 * Math.PI / 180.0;
        F3 up = new F3(0f, 1f, 0f);

        // Нормаль передаётся УКАЗАТЕЛЕМ НА ПАДАЮЩУЮ СТОРОНУ, то есть вниз, к
        // камере. Со знаком наоборот dot(N, I) уходит в +0.98, и refract
        // выворачивает луч на 156° вместо 2.5° — на этом спотыкалась первая
        // версия теста.
        F3 n = new F3((float)-Math.Sin(beta), (float)-Math.Cos(beta), 0f);
        F3 view = new F3(0f, 1f, 0f);
        F3 refracted = Refract(view, n, WaterIndex / AirIndex);
        // --- преломление: отклонение луча при наклоне нормали ----------------
        // Луч смотрит строго вверх, нормаль наклонена на β: угол падения
        // θi = β, а в воздух луч уходит под θt = asin(n·sin β), то есть
        // ОТКЛОНЯЕТСЯ ОТ ВЕРТИКАЛИ НА (n − 1)·β.
        //
        // ВАЖНО, и это была ошибка в постановке задачи: коэффициент здесь
        // (n − 1) = 0.33, а НЕ (1 − 1/n) = 0.25. Разница 30%: 0.25 — это
        // отношение отклонения преломления к отклонению зеркала для ЛИНЗЫ
        // (отклонение 2β), а здесь луч именно преломляется, и наклоняется
        // поверхность вместе с нормалью. Первая версия теста ждала 2.48° и
        // получала 3.35° — формула была права.
        double deviation = RefractionDeviation(10.0);
        double expected = (WaterIndex - 1.0) * 10.0;
        if (Math.Abs(deviation - expected) > 0.15)
        {
            return Fail(
                "отклонение при наклоне 10° = {0:F3}°, ожидалось (n−1)·β = {1:F3}°",
                deviation, expected);
        }

        // На наклоне в 40° отклонение уже около 10°, то есть рябь с уклоном
        // 40° (а такая рябь в шейдере есть) двигает небо на десятки градусов.
        double betaBig = 40.0 * Math.PI / 180.0;
        F3 nBig = new F3((float)-Math.Sin(betaBig), (float)-Math.Cos(betaBig), 0f);
        F3 refractedBig = Refract(view, nBig, WaterIndex / AirIndex);
        // На наклоне 40° луч уходит в воздух под 58.8°, то есть отклоняется на
        // 18.8°: рябь с уклоном 40° (а такая в шейдере есть, см. 1.33·cosθi/
        // cosθt) двигает небо почти на двадцать градусов. Это и есть ответ на
        // вопрос «почему потолок плоский»: не хватает не формулы, а наклона.
        double deviationBig = RefractionDeviation(40.0);
        if (deviationBig < 15.0 || deviationBig > 22.0)
        {
            return Fail("отклонение при наклоне 40° = {0:F2}°, ожидалось 18-19°", deviationBig);
        }

        // --- преломление не выходит за критический угол ----------------------
        // Луч, смотрящий на нормаль под 40°, внутри воды преломляется на
        // 10° наружу, а зеркало начинается с 48.6°: то есть при наклоне 40°
        // луч ВСЁ ЕЩЁ выходит в небо. Это и есть причина, по которой окно
        // должно быть большим, а не «мягким пятном».
        if (!(deviationBig < critical))
        {
            return Fail("преломлённый луч должен выходить в небо при наклоне 40°");
        }

        return Ok(string.Format(
            CultureInfo.InvariantCulture,
            "критический {0:F2}°, F(0)={1:F4}, F(крит)={2:F3}, отклонение при 10°={3:F2}°, при 40°={4:F2}°",
            critical, f0, atCritical, deviation, deviationBig));
    }

    /// <summary>
    /// Отклонение луча в градусах при наклоне нормали на betaDegrees: луч
    /// смотрит строго вверх (как камера под водой), нормаль воды наклонена, и
    /// наружу луч уходит под углом asin(n·sin β) от нормали — то есть
    /// отклоняется от вертикали на (n−1)·β.
    ///
    /// Считается через refract — той же функцией, что в шейдере. Нормаль
    /// передаётся УКАЗАТЕЛЕМ НА ПАДАЮЩУЮ СТОРОНУ (вниз, к камере): в HLSL
    /// dot(N, I) обязан быть отрицательным, иначе refract выворачивает луч
    /// вместо преломления.
    /// </summary>
    private static double RefractionDeviation(double betaDegrees)
    {
        double beta = betaDegrees * Math.PI / 180.0;
        F3 view = new F3(0f, 1f, 0f);
        F3 n = new F3((float)-Math.Sin(beta), (float)-Math.Cos(beta), 0f);
        F3 refracted = Refract(view, n, WaterIndex / AirIndex);
        if (Dot(refracted, refracted) < 1e-12f)
        {
            return 180.0;
        }

        return Math.Acos(Sat(Dot(refracted, view))) * 180.0 / Math.PI;
    }

    /// <summary>
    /// Коэффициент отражения на границе двух диэлектриков для неполяризованного
    /// света — ровно как в WaterSurface.shader.
    /// </summary>
    private static float FresnelWater(float cosIncident)
    {
        float cosI = Sat(cosIncident);
        float sinT = WaterIndex * MathF.Sqrt(Sat(1f - (cosI * cosI)));
        if (sinT >= 1f)
        {
            return 1f;
        }

        float cosT = MathF.Sqrt(1f - (sinT * sinT));
        float rs = ((WaterIndex * cosI) - cosT) / ((WaterIndex * cosI) + cosT);
        float rp = ((WaterIndex * cosT) - cosI) / ((WaterIndex * cosT) + cosI);
        return 0.5f * ((rs * rs) + (rp * rp));
    }

    /// <summary>
    /// Преломление ровно как HLSL refract(I, N, eta): incident — падающий луч,
    /// normal указывает В ПАДАЮЩУЮ среду (dot(N, I) &lt; 0), eta = n1/n2.
    /// Возвращает единичный вектор либо ноль при полном внутреннем отражении.
    /// </summary>
    private static F3 Refract(F3 incident, F3 normal, float eta)
    {
        float dotNI = Dot(normal, incident);
        float k = 1f - ((eta * eta) * (1f - (dotNI * dotNI)));
        if (k < 0f)
        {
            return new F3(0f, 0f, 0f);
        }

        // HLSL: eta*I - (eta·dot(N,I) + sqrt(k))·N. Знак перед eta·dot(N,I)
        // обязан быть МИНУС, потому что dot(N,I) < 0; со знаком плюс луч
        // уезжал на 6.3° вместо 3.35°.
        F3 result = (incident * eta) + (normal * ((-eta * dotNI) - MathF.Sqrt(k)));
        return Normalize(result);
    }
}
