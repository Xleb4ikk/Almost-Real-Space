using System.Text;
using Galilego.Core;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Безкамерный зонд: для каждого слоя декора и каждого пояса широты прогоняет
    /// те же фильтры, что GroundDecorDistribution.TryEvaluate, и печатает долю
    /// кандидатов, прошедших каждую стадию. Показывает, ГДЕ режется декор по широте.
    /// Положить в Assets/_Project/Scripts/Editor/SurfaceTools/. Меню: Tools/Galilego/Decor latitude probe.
    /// </summary>
    public static class DecorLatitudeProbe
    {
        private const int SamplesPerBand = 20000;

        [MenuItem("Tools/Galilego/Decor latitude probe")]
        public static void Run()
        {
            BodyAuthoring body = null;
            foreach (BodyAuthoring b in Object.FindObjectsOfType<BodyAuthoring>())
            {
                if (b.TerrainPreset != null && b.GroundDecorPreset != null)
                {
                    body = b;
                    break;
                }
            }

            if (body == null)
            {
                Debug.LogError("[LatProbe] нет BodyAuthoring с TerrainPreset и GroundDecorPreset в сцене");
                return;
            }

            HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(body.TerrainPreset.Profile, body.TerrainSeed);
            TerrainNoiseParams noise = TerrainNoiseParams.FromTerrain(terrain);
            GroundDecorProfile profile = body.GroundDecorPreset.Profile;

            var sb = new StringBuilder();
            sb.AppendLine("[LatProbe] body=" + body.name + " R=" + body.Radius + " seed=" + body.TerrainSeed);
            foreach (GroundDecorLayer layer in profile.Layers)
            {
                if (layer == null || !layer.Enabled)
                {
                    continue;
                }

                GroundDecorPlacementParams p = GroundDecorPlacementParams.FromLayer(
                    layer, terrain, body.Radius, new Vector3d(0d, 0d, 0d));

                sb.AppendLine("== " + layer.Name + " ==  lat | land% | allowed% | accepted% | meanSlopeTan(land)");
                for (int lat = 0; lat <= 85; lat += 5)
                {
                    var rng = new System.Random(1234 + lat);
                    double latRad = lat * System.Math.PI / 180d;
                    int land = 0, allowed = 0, accepted = 0;
                    double slopeSum = 0d;
                    for (int i = 0; i < SamplesPerBand; i++)
                    {
                        double lon = rng.NextDouble() * System.Math.PI * 2d;
                        double3 dir = new double3(
                            System.Math.Cos(latRad) * System.Math.Cos(lon),
                            System.Math.Cos(latRad) * System.Math.Sin(lon),
                            System.Math.Sin(latRad));

                        double raw = TerrainNoise.SampleHeight(noise, dir) * p.AmplitudeMeters;
                        if (raw > p.SeaLevelMeters)
                        {
                            land++;
                            slopeSum += GroundDecorDistribution.SlopeTan(noise, p, dir, out float3 _n);
                        }

                        if (GroundDecorDistribution.IsSurfaceAllowed(p, noise, dir))
                        {
                            allowed++;
                        }

                        var rnd = new double3(rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
                        if (GroundDecorDistribution.TryEvaluate(
                            p, noise, dir, rnd, rng.NextDouble(), rng.NextDouble(), rng.NextDouble(),
                            out GroundDecorInstance _))
                        {
                            accepted++;
                        }
                    }

                    double n = SamplesPerBand;
                    sb.AppendLine(string.Format("  {0,2}  {1,6:F1} {2,7:F1} {3,8:F2} {4,8:F2}",
                        lat, 100d * land / n, 100d * allowed / n, 100d * accepted / n,
                        land > 0 ? slopeSum / land : 0d));
                }
            }

            Debug.Log(sb.ToString());
        }
    }
}
