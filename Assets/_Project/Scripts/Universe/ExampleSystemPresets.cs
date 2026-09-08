namespace Galilego.Universe
{
    /// <summary>
    /// Пример вымышленной системы: звезда + 2 планеты, у одной есть спутник.
    /// Обобщение GalileanMoonPresets — раньше там были реальные JPL-данные Юпитера,
    /// теперь это просто шаблон: копируешь CreateOrbitingBody под свои тела,
    /// подставляя собственные (вымышленные) массы и элементы орбит.
    /// Единицы те же, что и раньше: кг, метры, секунды, градусы.
    /// Дизайн-правило для спутников: a ≤ ~0.3 радиуса Хилла планеты
    /// (R_H = a_планеты·(M_планеты/(3·M_звезды))^(1/3)) и разнос соседних лун
    /// ≥ 3 взаимных R_H — иначе система хаотична (e-folding до долей года,
    /// измерено T60/probechaos) и любая долговременная эфемерида расходится
    /// с независимым n-body на масштабе орбиты луны.
    /// </summary>
    public static class ExampleSystemPresets
    {
        public static StarSystem CreateExampleSystem()
        {
            OrbitingBody star = new OrbitingBody
            {
                Name = "Вымышленная звезда",
                StandardGravitationalParameter = 1.327e20d, // ориентир: масса ~ как у Солнца
            };

            OrbitingBody planetA = CreateOrbitingBody(
                name: "Планета А",
                standardGravitationalParameter: 3.986e14d, // ориентир: масса ~ как у Земли
                radius: 6.371e6d,
                semiMajorAxis: 1.5e11d,        // ~1 а.е.
                eccentricity: 0.02d,
                inclinationDegrees: 0d,
                longitudeOfAscendingNodeDegrees: 0d,
                argumentOfPeriapsisDegrees: 0d,
                meanAnomalyAtEpochDegrees: 0d);
            planetA.Parent = star;
            star.Children.Add(planetA);

            OrbitingBody planetAMoon = CreateOrbitingBody(
                name: "Спутник планеты А",
                standardGravitationalParameter: 4.9e12d, // ориентир: масса ~ как у Луны
                radius: 1.737e6d,
                semiMajorAxis: 3.84e8d,
                eccentricity: 0.05d,
                inclinationDegrees: 5d,
                longitudeOfAscendingNodeDegrees: 0d,
                argumentOfPeriapsisDegrees: 0d,
                meanAnomalyAtEpochDegrees: 0d);
            planetAMoon.Parent = planetA;
            planetA.Children.Add(planetAMoon);

            OrbitingBody planetB = CreateOrbitingBody(
                name: "Планета Б",
                standardGravitationalParameter: 4.28e13d, // ориентир: масса ~ как у Марса
                radius: 3.39e6d,
                semiMajorAxis: 2.28e11d,       // ~1.5 а.е.
                eccentricity: 0.09d,
                inclinationDegrees: 1.9d,
                longitudeOfAscendingNodeDegrees: 49d,
                argumentOfPeriapsisDegrees: 287d,
                meanAnomalyAtEpochDegrees: 19d);
            planetB.Parent = star;
            star.Children.Add(planetB);

            // Спутников у планеты Б нет — показывает, что это не обязательно.

            return new StarSystem(star);
        }

        private static OrbitingBody CreateOrbitingBody(
            string name,
            double standardGravitationalParameter,
            double radius,
            double semiMajorAxis,
            double eccentricity,
            double inclinationDegrees,
            double longitudeOfAscendingNodeDegrees,
            double argumentOfPeriapsisDegrees,
            double meanAnomalyAtEpochDegrees)
        {
            OrbitingBody body = new OrbitingBody
            {
                Name = name,
                StandardGravitationalParameter = standardGravitationalParameter,
                Radius = radius,
                SemiMajorAxis = semiMajorAxis,
                Eccentricity = eccentricity,
                InclinationDegrees = inclinationDegrees,
                LongitudeOfAscendingNodeDegrees = longitudeOfAscendingNodeDegrees,
                ArgumentOfPeriapsisDegrees = argumentOfPeriapsisDegrees,
                MeanAnomalyAtEpochDegrees = meanAnomalyAtEpochDegrees,
                EpochTimeSeconds = 0d
            };
            body.SyncMassFromGravitationalParameter();
            return body;
        }
    }
}
