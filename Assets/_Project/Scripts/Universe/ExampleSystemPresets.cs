namespace Galilego.Universe
{
    /// <summary>
    /// Example fictional system: a star + 2 planets, one with a moon. A template:
    /// copy CreateOrbitingBody for your own bodies, plugging in your own (fictional)
    /// masses and orbital elements. Units: kg, metres, seconds, degrees.
    /// Design rule for moons: a ≤ ~0.3 of the planet's Hill radius
    /// (R_H = a_planet·(M_planet/(3·M_star))^(1/3)) and adjacent moons separated by
    /// ≥ 3 mutual R_H — otherwise the system is chaotic and any long-term ephemeris
    /// diverges from an independent n-body on the scale of the moon's orbit.
    /// </summary>
    public static class ExampleSystemPresets
    {
        public static StarSystem CreateExampleSystem()
        {
            OrbitingBody star = new OrbitingBody
            {
                Name = "Вымышленная звезда",
                StandardGravitationalParameter = 1.327e20d, // reference: mass ~ like the Sun
            };

            OrbitingBody planetA = CreateOrbitingBody(
                name: "Планета А",
                standardGravitationalParameter: 3.986e14d, // reference: mass ~ like Earth
                radius: 6.371e6d,
                semiMajorAxis: 1.5e11d,        // ~1 AU
                eccentricity: 0.02d,
                inclinationDegrees: 0d,
                longitudeOfAscendingNodeDegrees: 0d,
                argumentOfPeriapsisDegrees: 0d,
                meanAnomalyAtEpochDegrees: 0d);
            planetA.Parent = star;
            star.Children.Add(planetA);

            OrbitingBody planetAMoon = CreateOrbitingBody(
                name: "Спутник планеты А",
                standardGravitationalParameter: 4.9e12d, // reference: mass ~ like the Moon
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
                standardGravitationalParameter: 4.28e13d, // reference: mass ~ like Mars
                radius: 3.39e6d,
                semiMajorAxis: 2.28e11d,       // ~1.5 AU
                eccentricity: 0.09d,
                inclinationDegrees: 1.9d,
                longitudeOfAscendingNodeDegrees: 49d,
                argumentOfPeriapsisDegrees: 287d,
                meanAnomalyAtEpochDegrees: 19d);
            planetB.Parent = star;
            star.Children.Add(planetB);

            // Planet B has no moons — showing that it is optional.

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
