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
    /// HDRP's own `Volume > Exposure` cannot carry this on the project's pipeline: in
    /// Play, EV100 2.26 and -12.74 produced a pixel-identical frame (measured 2026-11-24),
    /// while `_ExposureTexture` did report the value - the exposure is not composed into
    /// the image. So the pupil is applied to the SCENE LIGHT instead: every lit surface
    /// is multiplied by <see cref="Gain"/>. The sun's own disc, glare and corona are not,
    /// their surface brightness stays physical.
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
