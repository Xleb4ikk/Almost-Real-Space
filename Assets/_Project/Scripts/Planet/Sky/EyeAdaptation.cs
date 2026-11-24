using System;

namespace Galilego.Universe
{
    /// <summary>
    /// The observer's pupil: how far the eye has opened for the light it is standing in,
    /// and how fast it gets there.
    ///
    /// Why this exists at all. The scene's light is physical: every surface multiplies the
    /// star's flux (E/E_sun) by its albedo, so a body at 4.1 AU is lit 128x weaker than
    /// Terra. That number is right for a photometer and wrong for a person: at 11 W/m2
    /// (~10^3 lx) a human reads a book, and the pupil compensates the 7 EV by itself. A
    /// fixed-exposure camera shows the raw number, which is why Mars was a hole in the
    /// star field.
    ///
    /// HDRP's own `Volume > Exposure` cannot carry this on the project's pipeline: the
    /// frame is drawn by custom shaders that never apply HDRP's pre-exposure, so the
    /// exposure moves only the HDRP/Lit props (launch pad, roads, VAB, ship). And that
    /// move is not a small correction: in Automatic mode the uber-post does not cancel
    /// the pre-exposure of the props, so the auto exposure's floor is the difference
    /// between a moonlit and a daylit asphalt — measured 2026-11-24, the pad read
    /// 118/255 at `limitMin = -8` and 10/255 at -1 on the same frozen frame. The camera
    /// is therefore a fixed-exposure daylight camera (`limitMin = -1` in
    /// `StarRenderer.ConfigureVolume`), and the pupil is applied to the SCENE LIGHT:
    /// every lit surface is multiplied by <see cref="Gain"/> — the custom shaders through
    /// `_TerrainSun`, the HDRP/Lit props through the sun's and the moon's Directional
    /// Light. The star's own disc, glare and corona are not, their surface brightness
    /// stays physical.
    ///
    /// The one non-physical step is the share: the eye pays back
    /// <c>share</c> of the deficit below 1 E_sun, capped at <c>maxEv</c>. A share of 1
    /// would be a fully dark-adapted eye (Mars noon as bright as Terra noon); 0.66 is a
    /// pupil that has opened most of the way - a dull, legible day that still reads as
    /// "far from the Sun". It is the same kind of step as
    /// <c>MoonlightSystem.NightAdaptationGainEv</c> (14 EV for a 21 EV moon deficit).
    /// </summary>
    public static class EyeAdaptation
    {
        /// <summary>Default share of the illumination deficit the pupil pays back.</summary>
        public const double DefaultShare = 0.66d;

        /// <summary>Default ceiling of the opening, EV.</summary>
        public const double DefaultMaxEv = 8d;

        /// <summary>
        /// Ceiling of the pupil factor applied to a BODY's own disc (`BodyFarDiscView`,
        /// `GasGiantView`). A disc's surface brightness is physical, like the star's own
        /// disc, so it must not be multiplied by the night opening: the pupil reaches
        /// 2^14 at night, and 16 384x turns the Moon into a featureless white blob (its
        /// maria are 0.35 scene units before the gain). 4x (2 EV) keeps a sunlit disc
        /// bright and readable while the GROUND under it still gets the full pupil.
        /// </summary>
        public const float DiscMaxGain = 4f;

        /// <summary>
        /// Target opening in EV for an eye standing in <paramref name="illuminance"/>
        /// (1 = Terra's noon, 0 = darkness). 0 EV at full daylight, positive when dark.
        /// </summary>
        public static double TargetEv(double illuminance, double share, double maxEv)
        {
            if (!(illuminance > 0d))
            {
                return maxEv;
            }

            double ev = -Math.Log(illuminance, 2d) * share;
            return Math.Clamp(ev, 0d, maxEv);
        }

        /// <summary>Linear scene-light multiplier for an opening of <paramref name="ev"/>.</summary>
        public static double Gain(double ev)
        {
            return Math.Pow(2d, ev);
        }

        /// <summary>
        /// One adaptation step towards <paramref name="target"/>: an exponential approach
        /// that covers ~95 % of the distance in <paramref name="brightSeconds"/> when the
        /// scene gets brighter (the pupil closes) and <paramref name="darkSeconds"/> when
        /// it gets darker (it opens). The pupil constricts much faster than it dilates,
        /// which is why the two times are separate.
        /// </summary>
        public static double Approach(
            double current,
            double target,
            double deltaTime,
            double brightSeconds,
            double darkSeconds)
        {
            double seconds = target < current ? brightSeconds : darkSeconds;
            if (!(seconds > 0d) || !(deltaTime > 0d))
            {
                return target;
            }

            double alpha = 1d - Math.Exp(-3d * deltaTime / seconds);
            double next = current + ((target - current) * alpha);
            return Math.Abs(target - next) < 1e-3d ? target : next;
        }
    }
}
