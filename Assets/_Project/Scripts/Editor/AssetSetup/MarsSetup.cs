using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Galilego.Core;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// One-shot authoring for the game's Mars: a Mars-like terrain (MartianRelief) on a
    /// body with 7 % of Mars' mass inside R = 800 km, on an orbit that runs from 610 to
    /// 720 million km from Sol.
    ///
    /// Mass and radius are the owner's design numbers. The result is dense for its size
    /// (mean density ~2.1e4 kg/m3, about 21 g/cm3), but the surface gravity is
    /// g = GM/R^2 = 4.684 m/s2 (0.478 g) and the escape speed is 2.74 km/s — a landable
    /// world. The GM is taken as 7 % of Mars' IAU nominal GM instead of G*M: GM is known
    /// much better and this keeps the gravity field exactly the requested fraction.
    ///
    /// The orbit is given by its apsides: a = (610 + 720)/2 = 665e9 m,
    /// e = (720 - 610)/(720 + 610) = 0.082707. Rotation, obliquity and J2 stay Mars'.
    /// No atmosphere: the body is airless like the real Mars analogue.
    /// </summary>
    public static class MarsSetup
    {
        public const string BodyName = "Mars";

        /// <summary>IAU 2015 nominal GM of the Mars system, m3/s2.</summary>
        public const double MarsNominalGm = 4.282837e13d;

        /// <summary>Owner's design value: 7 % of Mars' mass.</summary>
        public const double MassFractionOfMars = 0.07d;

        /// <summary>Owner's design value: mean radius, metres.</summary>
        public const double RadiusMeters = 800000d;

        /// <summary>Orbit range from Sol, metres (owner's design value).</summary>
        public const double PeriapsisMeters = 610e9d;

        public const double ApoapsisMeters = 720e9d;

        public const double SemiMajorAxisMeters = 0.5d * (PeriapsisMeters + ApoapsisMeters);

        public const double Eccentricity = (ApoapsisMeters - PeriapsisMeters) / (ApoapsisMeters + PeriapsisMeters);

        [MenuItem("Tools/Galilego/Planets/Setup Mars (7% Mars, R=800 km)")]
        public static void Run()
        {
            var settings = new MarsLikeSettings
            {
                BodyName = BodyName,
                Seed = 4242,
                Variant = 0,
                RadiusKm = RadiusMeters / 1000d,
                MassKg = MassFractionOfMars * (MarsNominalGm / MarsLikePlanetFactory.GravitationalConstant),
                SemiMajorAxisMeters = SemiMajorAxisMeters,
                AddAtmosphere = false,
                AddMoons = false,
                ReplaceExisting = true
            };

            BodyAuthoring body = MarsLikePlanetFactory.CreateInOpenScene(settings);
            if (body == null)
            {
                return;
            }

            // GM from the IAU nominal Mars GM, not G*M: the requested fraction is exact.
            body.StandardGravitationalParameter = MassFractionOfMars * MarsNominalGm;
            body.Radius = RadiusMeters;
            body.CrashToleranceMps = 5d;
            body.J2 = 1.96045e-3d;
            body.J2ReferenceRadius = RadiusMeters;

            // Orbit: a = 665e9 m, e = 0.082707 (610e9 .. 720e9 m around Sol).
            body.SemiMajorAxis = SemiMajorAxisMeters;
            body.Eccentricity = Eccentricity;
            body.InclinationDegrees = 1.85d;
            body.LongitudeOfAscendingNodeDegrees = 49.58d;
            body.ArgumentOfPeriapsisDegrees = 286.5d;
            body.MeanAnomalyAtEpochDegrees = 19.4d;
            body.EpochTimeSeconds = 0d;

            // Mars' rotation and obliquity.
            body.RotationPeriodSeconds = 88642.66d;
            body.TidallyLocked = false;
            body.PrimeMeridianOffsetDegrees = 0d;
            double tilt = 25.19d * Math.PI / 180d;
            body.NorthPoleDirection = new Vector3d(Math.Sin(tilt), 0d, Math.Cos(tilt));

            // No atmosphere/clouds/gas giant/decor: a bare rocky body.
            body.AtmospherePreset = null;
            body.CloudsPreset = null;
            body.GasGiantPreset = null;
            body.GroundDecorPreset = null;
            var atmosphereView = body.GetComponent<PlanetAtmosphereView>();
            if (atmosphereView != null)
            {
                UnityEngine.Object.DestroyImmediate(atmosphereView, true);
            }

            // Mars is alone in deep space: spend the whole budget on its globe.
            // Full grid at every LOD depth (no far-level halving), no speed-tier
            // coarsening, and a denser base grid than the default 65.
            var surface = body.GetComponent<PlanetSurfaceRenderer>();
            if (surface != null)
            {
                surface.TileResolution = 129;
                surface.ResolutionHalveEveryLevels = 99;
                surface.EnableQualityTiers = false;
                surface.SplitFactor = 3.0f;
                // No other body is within millions of km: the horizon cull can
                // shave a chunk off the limb, and there is nothing else to render.
                surface.CullBeyondHorizon = false;
                EditorUtility.SetDirty(surface);
            }

            EditorUtility.SetDirty(body);
            AssetDatabase.SaveAssets();

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = body.gameObject;
            EditorGUIUtility.PingObject(body.gameObject);
            Debug.Log(
                "[Mars] Готово: R = " + (RadiusMeters / 1000d).ToString("F0") + " км, μ = " +
                body.StandardGravitationalParameter.ToString("E6") + " м³/с² (7 % от GM Марса), " +
                "орбита " + (PeriapsisMeters / 1e9).ToString("F0") + "–" + (ApoapsisMeters / 1e9).ToString("F0") +
                " млн км (a = " + (SemiMajorAxisMeters / 1e9).ToString("F1") + " млн км, e = " +
                Eccentricity.ToString("F6") + "). Сцена сохранена.");
        }

    }
}
