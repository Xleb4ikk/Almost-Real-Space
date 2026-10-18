using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>Player mode: in ship / EVA (flight) / on surface / in water.</summary>
    public enum PlayerMode
    {
        InShip,
        EVA,
        OnSurface,
        Swimming
    }

    /// <summary>
    /// Jetpack fuel. Contract for the future: currently infinite (MVP), later a
    /// tank with drain — implement this interface and substitute it in
    /// PlayerController.JetpackFuel; physics does not change.
    /// </summary>
    public interface IJetpackFuel
    {
        /// <summary>Remaining seconds of operation (for UI). ∞ = infinite.</summary>
        double RemainingSeconds { get; }

        /// <summary>Consume operating time; false — no fuel.</summary>
        bool TryConsume(double seconds);
    }

    /// <summary>MVP jetpack fuel: infinite.</summary>
    public sealed class InfiniteJetpackFuel : IJetpackFuel
    {
        public double RemainingSeconds => double.PositiveInfinity;

        public bool TryConsume(double seconds)
        {
            return true;
        }
    }

    /// <summary>
    /// Player intent for a frame (filled by PlayerController, executed by
    /// SimulationRunner). Input/physics split: the controller knows only buttons,
    /// the runner only integration.
    /// </summary>
    public struct PlayerIntent
    {
        /// <summary>Walk direction (tangent plane, unit or zero).</summary>
        public Vector3d WalkDirection;

        /// <summary>Walk speed (m/s).</summary>
        public double WalkSpeed;

        /// <summary>Jump (fires once, on the ground).</summary>
        public bool Jump;

        public bool JetpackToggle;

        /// <summary>Jetpack acceleration (m/s², EVA only).</summary>
        public Vector3d JetpackAccel;

        /// <summary>Hover toggle (double tap Shift in flight).</summary>
        public bool HoverToggle;

        /// <summary>
        /// Desired hover velocity relative to the local frame (astro, m/s):
        /// WASD horizontal, Space/Shift up/down. Zero means hold still.
        /// </summary>
        public Vector3d HoverVelocity;

        /// <summary>Swim direction (world astro frame, unit or zero).</summary>
        public Vector3d SwimDirection;

        /// <summary>Swim speed (m/s).</summary>
        public double SwimSpeed;

        /// <summary>
        /// Noclip direction (world astro frame, unit or zero). Filled only when
        /// SimulationRunner.NoclipActive is on — that is the single mode switch; a
        /// separate flag in the intent is deliberately absent: two switches drift out of sync.
        /// </summary>
        public Vector3d NoclipDirection;

        /// <summary>Noclip speed (m/s) — from walking to absurd values.</summary>
        public double NoclipSpeed;

        public static PlayerIntent Idle => default;
    }
}
