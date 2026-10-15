using System;
using Galilego.Core;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Р§РёСЃС‚Р°СЏ Р»РѕРіРёРєР° С…РѕРґСЊР±С‹ РёРіСЂРѕРєР° РїРѕ РѕРїРѕСЂР°Рј (Р±РµР· UnityEngine): РІРµСЂС‚РёРєР°Р»СЊРЅР°СЏ РѕРїРѕСЂР°,
    /// С€Р°Рі РІРІРµСЂС…, РЅР°РєР»РѕРЅ, РїРѕС‚РѕР»РѕРє, СѓРіР»С‹, РїР°РґРµРЅРёРµ, РїРµСЂРµРЅРѕСЃ РґРІРёР¶СѓС‰РµР№СЃСЏ РїР»Р°С‚С„РѕСЂРјРѕР№.
    /// Р Р°Р±РѕС‚Р°РµС‚ РІ Р»РѕРєР°Р»СЊРЅС‹С… РјРµС‚СЂР°С… РєР°СЃР°С‚РµР»СЊРЅРѕР№ СЃРёСЃС‚РµРјС‹ РјРµСЃС‚Р° (x = РІРѕСЃС‚РѕРє, y = СЃРµРІРµСЂ,
    /// z = РІРІРµСЂС…). РЎРѕСЃС‚РѕСЏРЅРёРµ вЂ” РЅРѕРіРё РєР°РїСЃСѓР»С‹ (Position), СЃРєРѕСЂРѕСЃС‚СЊ (Velocity),
    /// Airborne. РЁР°Рі С„РёРєСЃРёСЂРѕРІР°РЅРЅС‹Р№, РІС‹Р·С‹РІР°РµС‚СЃСЏ РЅР° РєР°Р¶РґРѕРј РїРѕРґС€Р°РіРµ РёРіСЂРѕРєР°.
    ///
    /// РџРѕСЂСЏРґРѕРє РІ РїРѕРґС€Р°РіРµ (РєР°Рє РІ СЃС‚Р°СЂРѕРј StepPlayerSurface): Р±Р»РѕРєРёСЂРѕРІРєР° РїСЂРѕРІРµСЂСЏРµС‚СЃСЏ
    /// Р’ РќРђР§РђР›Р• РїРѕРґС€Р°РіР° вЂ” РґРѕ РїСЂРёРјРµРЅРµРЅРёСЏ РґРІРёР¶РµРЅРёСЏ. Render/astro-СЂР°СЃСЃРёРЅС…СЂРѕРЅ РЅРµ
    /// РїРµСЂРµРЅРѕСЃРёС‚СЃСЏ: РёСЃС‚РѕС‡РЅРёРє РѕРїРѕСЂС‹ РѕР±СЏР·Р°РЅ СЂР°Р±РѕС‚Р°С‚СЊ РЅР° С‚РѕР№ Р¶Рµ РїРѕР·Рµ, С‡С‚Рѕ Рё Р·Р°РїСЂРѕСЃ.
    ///
    /// РџСЂР°РІРёР»Р° (Р·РЅР°С‡РµРЅРёСЏ РїРѕ СѓРјРѕР»С‡Р°РЅРёСЋ, РјРµРЅСЏС‚СЊ С‚РѕР»СЊРєРѕ СЃ Р·Р°РїРёСЃСЊСЋ РІ РѕС‚С‡С‘С‚):
    ///  - С€Р°Рі РІРІРµСЂС… в‰¤ StepUpMeters РїРѕРґРЅРёРјР°РµС‚ Р±РµР· РїСЂС‹Р¶РєР°, РІС‹С€Рµ вЂ” Р±Р»РѕРєРёСЂРѕРІРєР°;
    ///  - СѓРєР»РѕРЅ в‰¤ MaxSlopeDegrees С…РѕРґРёРјС‹Р№, РєСЂСѓС‡Рµ вЂ” СЃРєРѕР»СЊР¶РµРЅРёРµ Р±РµР· РїРѕРґСЉС‘РјР°;
    ///  - РїРѕС‚РѕР»РѕРє РЅРёР¶Рµ CapsuleHeight Р±Р»РѕРєРёСЂСѓРµС‚ РІС…РѕРґ (С„РёР·РёС‡РµСЃРєРё вЂ” СЃРІРёРїРѕРј РєР°РїСЃСѓР»С‹);
    ///  - РЅРµС‚ РѕРїРѕСЂС‹ РІ РїСЂРµРґРµР»Р°С… SupportDropMeters вЂ” СЃРІРѕР±РѕРґРЅРѕРµ РїР°РґРµРЅРёРµ;
    ///  - СѓРіРѕР»: СЃРєРѕР»СЊР¶РµРЅРёРµ РІРґРѕР»СЊ СЃС‚РµРЅС‹, РґРѕ MaxSweepIterations Р·Р° РїРѕРґС€Р°Рі.
    /// </summary>
    public sealed class PlayerSurfaceController
    {
        public const double CapsuleRadius = 0.45d;
        public const double CapsuleHeight = 1.8d;

        /// <summary>
        /// Р’С‹СЃРѕС‚Р° РєР°РїСЃСѓР»С‹ РІ РїСЂРёСЃРµРґРµ (Рј). РџСЂРѕС‘Рј Р»СЋРєР° Orion (~1.2 Рј) РЅРёР¶Рµ РїРѕР»РЅРѕРіРѕ
        /// СЂРѕСЃС‚Р°: РїСЂРёРіРЅСѓРІС€РёСЃСЊ РёРіСЂРѕРє РїСЂРѕС…РѕРґРёС‚, СЃС‚РѕСЏ вЂ” РЅРµС‚. РџСЂРёРіРёР±Р°РЅРёРµ
        /// Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРѕРµ (СЃРј. StepGrounded), РєРЅРѕРїРѕРє РЅРµС‚.
        /// </summary>
        public const double CapsuleHeightCrouched = 1.05d;

        public const double StepUpMeters = 0.5d;
        public const double SupportDropMeters = 0.5d;
        public const double MaxSlopeDegrees = 30d;
        public const int MaxSweepIterations = 4;
        public const double SlideSpeedMps = 1.5d;

        /// <summary>
        /// Р§РёСЃР»РµРЅРЅС‹Р№ РґРѕРїСѓСЃРє С€РІРѕРІ РјРµР¶РґСѓ РєРѕР»Р»Р°Р№РґРµСЂР°РјРё (Рј): СЃС‚СѓРїРµРЅСЊ С‡СѓС‚СЊ РІС‹С€Рµ StepUp
        /// РЅР° СЃС‚С‹РєРµ РґРІСѓС… РјРµС€РµР№ (РїРѕРіСЂРµС€РЅРѕСЃС‚СЊ Р·Р°РїРµС‡РєРё/РјР°СЃС€С‚Р°Р±Р°) РЅРµ РґРѕР»Р¶РЅР° Р±Р»РѕРєРёСЂРѕРІР°С‚СЊ
        /// С€Р°Рі. Р­С‚Рѕ РќР• РёРіСЂРѕРІРѕР№ РїРѕСЂРѕРі (StepUpMeters), Р° СЌРїСЃРёР»РѕРЅ РіРµРѕРјРµС‚СЂРёРё.
        /// </summary>
        public const double SeamToleranceMeters = 0.02d;

        /// <summary>РќРѕРіРё РєР°РїСЃСѓР»С‹ РІ Р»РѕРєР°Р»СЊРЅРѕРј РєР°РґСЂРµ РјРµСЃС‚Р°.</summary>
        public Vector3d Position;

        /// <summary>РЎРєРѕСЂРѕСЃС‚СЊ РѕС‚РЅРѕСЃРёС‚РµР»СЊРЅРѕ РјРµСЃС‚Р° (Рј/СЃ).</summary>
        public Vector3d Velocity;

        /// <summary>РЎРІРѕР±РѕРґРЅРѕРµ РїР°РґРµРЅРёРµ (РЅРµС‚ РѕРїРѕСЂС‹ РїРѕРґ РЅРѕРіР°РјРё).</summary>
        public bool Airborne;

        /// <summary>
        /// Р’РёСЃРµРЅРёРµ РІ РІРѕР·РґСѓС…Рµ: РіСЂР°РІРёС‚Р°С†РёРё РЅРµС‚, СЃРєРѕСЂРѕСЃС‚СЊ РµРґРµС‚ Рє HoverVelocity
        /// (РЅРѕР»СЊ вЂ” Р·Р°РјРёСЂР°РЅРёРµ). РЎС‚Р°РІРёС‚ SimulationRunner РїСЂРё hover-СЂРµР¶РёРјРµ.
        /// </summary>
        public bool Hovering;

        /// <summary>Р–РµР»Р°РµРјР°СЏ СЃРєРѕСЂРѕСЃС‚СЊ РІРёСЃРµРЅРёСЏ РІ Р»РѕРєР°Р»СЊРЅРѕРј РєР°РґСЂРµ (Рј/СЃ).</summary>
        public Vector3d HoverVelocity;

        /// <summary>РћС‚РєР»РёРє СЃРєРѕСЂРѕСЃС‚Рё РІ РІРёСЃРµРЅРёРё (1/СЃ).</summary>
        public double HoverResponse = 6d;

        /// <summary>Р’С…РѕРґ РІ РїРѕРґС€Р°РіРµ Р·Р°Р±Р»РѕРєРёСЂРѕРІР°РЅ (СЃС‚РµРЅР°/РїРѕС‚РѕР»РѕРє): РїРѕР·РёС†РёСЏ РЅРµ РёР·РјРµРЅРёР»Р°СЃСЊ.</summary>
        public bool Blocked;

        /// <summary>РЈСЃРєРѕСЂРµРЅРёРµ СЃРІРѕР±РѕРґРЅРѕРіРѕ РїР°РґРµРЅРёСЏ РІРґРѕР»СЊ Р»РѕРєР°Р»СЊРЅРѕР№ РІРµСЂС‚РёРєР°Р»Рё (Рј/СЃВІ).</summary>
        public double Gravity = 9.81d;

        /// <summary>
        /// РџСЂРёРіРЅСѓР»СЃСЏ Р»Рё РёРіСЂРѕРє (Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё, С‡С‚РѕР±С‹ РїСЂРѕР№С‚Рё РЅРёР·РєРёР№ РїСЂРѕС‘Рј).
        /// РљР°РјРµСЂР° РѕС‚СЂР°Р¶Р°РµС‚ СЌС‚Рѕ РІС‹СЃРѕС‚РѕР№ РіР»Р°Р· (FirstPersonCamera).
        /// </summary>
        public bool Crouched { get; private set; }

        /// <summary>Р’С‹СЃРѕС‚Р° РєР°РїСЃСѓР»С‹ СЃРµР№С‡Р°СЃ: РїРѕР»РЅС‹Р№ СЂРѕСЃС‚ РёР»Рё РїСЂРёСЃРµРґ.</summary>
        private double CurrentHeight
        {
            get { return Crouched ? CapsuleHeightCrouched : CapsuleHeight; }
        }

        /// <summary>РћРїРѕСЂР° РїРѕРґ РЅРѕРіР°РјРё (sourceId РїРѕСЃР»РµРґРЅРµРіРѕ Floor); -1 вЂ” РЅРµС‚.</summary>
        public int GroundSourceId { get; private set; } = -1;

        /// <summary>Р—Р°РґР°С‚СЊ С‚РµРєСѓС‰СѓСЋ РѕРїРѕСЂСѓ (РїРѕСЃР»Рµ С‚РµР»РµРїРѕСЂС‚Р°/СЃРїР°РІРЅР° РЅР° РїР»Р°С‚С„РѕСЂРјРµ).</summary>
        public void SetGroundSource(int sourceId)
        {
            GroundSourceId = sourceId;
        }

        private static readonly Vector3d Up = new Vector3d(0d, 0d, 1d);

        public void Step(IPlayerSupport support, double dt, Vector3d intentVelocity, Vector3d extraAcceleration = default)
        {
            if (support == null)
            {
                throw new ArgumentNullException(nameof(support));
            }

            if (!(dt > 0d) || !double.IsFinite(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt), "РЁР°Рі РёРіСЂРѕРєР° РѕР±СЏР·Р°РЅ Р±С‹С‚СЊ РєРѕРЅРµС‡РЅС‹Рј Рё РїРѕР»РѕР¶РёС‚РµР»СЊРЅС‹Рј.");
            }

            Blocked = false;
            if (extraAcceleration.SqrMagnitude > 0d)
            {
                // Р”РѕРїРѕР»РЅРёС‚РµР»СЊРЅРѕРµ СѓСЃРєРѕСЂРµРЅРёРµ (РґР¶РµС‚РїР°Рє): РґРѕР±Р°РІР»СЏРµС‚СЃСЏ Рє СЃРєРѕСЂРѕСЃС‚Рё РґРѕ С€Р°РіР°.
                Velocity += extraAcceleration * dt;
            }

            Vector3d intent = new Vector3d(intentVelocity.X, intentVelocity.Y, 0d);

            if (Airborne)
            {
                StepAirborne(support, dt);
            }
            else
            {
                StepGrounded(support, dt, intent);
            }
        }

        private void StepGrounded(IPlayerSupport support, double dt, Vector3d intent)
        {
            Vector3d saved = Position;
            Vector3d move = intent * dt;

            // РџРµСЂРµРЅРѕСЃ РґРІРёР¶СѓС‰РµР№СЃСЏ РѕРїРѕСЂРѕР№ (РїР»Р°С‚С„РѕСЂРјР°, РґРµС‚Р°Р»СЊ РєРѕСЂР°Р±Р»СЏ).
            if (GroundSourceId >= 0 && support is IMovingSupport moving
                && moving.TryGetSupportVelocity(GroundSourceId, out Vector3d supportVelocity))
            {
                move += new Vector3d(supportVelocity.X, supportVelocity.Y, 0d) * dt;
            }

            // РЁР°Рі РІРІРµСЂС…: РѕРїРѕСЂР° РїРѕРґ РєР°РЅРґРёРґР°С‚РѕРј Рё РЅР° СЂР°РґРёСѓСЃРµ РІРїРµСЂС‘Рґ (РєР°РїСЃСѓР»Р° СѓР¶Рµ
            // РєР°СЃР°РµС‚СЃСЏ СѓСЃС‚СѓРїР°, РєРѕРіРґР° РµРіРѕ РєСЂРѕРјРєР° РµС‰С‘ РІРїРµСЂРµРґРё С‚РѕС‡РєРё РЅРѕРі) РІ РїСЂРµРґРµР»Р°С…
            // StepUp РїРѕРґРЅРёРјР°РµС‚ РЅРѕРіРё, С‡С‚РѕР±С‹ РЅРёР·РєРёР№ СѓСЃС‚СѓРї РЅРµ СЃС‡РёС‚Р°Р»СЃСЏ СЃС‚РµРЅРѕР№ РїСЂРё СЃРІРёРїРµ.
            Vector3d candidate = Position + move;
            if (!TryFindStepFloor(support, candidate, move, out double floorHeight, out Vector3d floorNormal, out int floorId))
            {
                // РћРїРѕСЂС‹ РЅРµС‚ вЂ” СЃС…РѕРґ СЃ РєСЂР°СЏ: РїР°РґРµРЅРёРµ РІ СЌС‚РѕРј Р¶Рµ РїРѕРґС€Р°РіРµ.
                Airborne = true;
                GroundSourceId = -1;
                Velocity = intent;
                return;
            }

            if (SlopeAngleDegrees(floorNormal) > MaxSlopeDegrees)
            {
                // РљСЂСѓС‡Рµ РїСЂРµРґРµР»Р°: РЅРµ РїРѕРґРЅРёРјР°РµРјСЃСЏ, СЃРєРѕР»СЊР·РёРј РІРЅРёР· РІРґРѕР»СЊ РїРѕРІРµСЂС…РЅРѕСЃС‚Рё.
                // Р“РѕСЂРёР·РѕРЅС‚Р°Р»СЊ СЂРµР¶РµС‚СЃСЏ СЃРІРёРїРѕРј Рѕ СЃС‚РµРЅС‹ (РєР°Рє РїСЂРё С…РѕРґСЊР±Рµ): Сѓ РєСЂСѓС‚РѕРіРѕ
                // СЃРєР°С‚Р° СЃС‚РµРЅР° РЅРµ РґРѕР»Р¶РЅР° РїСЂРѕРїСѓСЃРєР°С‚СЊ РёРіСЂРѕРєР°.
                Vector3d slid = SweepHorizontal(support, Position, move, CurrentHeight);
                Position = new Vector3d(slid.X, slid.Y, Position.Z);

                // РћРїРѕСЂР° РїРѕРґ РёС‚РѕРіРѕРІРѕР№ С‚РѕС‡РєРѕР№ (СЃРІРёРї РјРѕРі СЃРґРІРёРЅСѓС‚СЊ РІРґРѕР»СЊ СЃС‚РµРЅС‹).
                Vector3d slideProbe = new Vector3d(Position.X, Position.Y, Position.Z + StepUpMeters);
                if (support.Floor(slideProbe, StepUpMeters + SupportDropMeters,
                        out double slideHeight, out _, out int slideId))
                {
                    Position = new Vector3d(Position.X, Position.Y, Math.Min(Position.Z, slideHeight));
                    GroundSourceId = slideId;
                }
                else
                {
                    GroundSourceId = floorId;
                }

                Velocity = new Vector3d(intent.X, intent.Y, -SlideSpeedMps);
                return;
            }

            Position = new Vector3d(Position.X, Position.Y, floorHeight);

            // Р“РѕСЂРёР·РѕРЅС‚Р°Р»СЊРЅС‹Р№ СЃРІРёРї СЃРѕ СЃРєРѕР»СЊР¶РµРЅРёРµРј РІРґРѕР»СЊ СЃС‚РµРЅ (РґРѕ 4 РёС‚РµСЂР°С†РёР№).
            // РЎРЅР°С‡Р°Р»Р° РІ РїРѕР»РЅС‹Р№ СЂРѕСЃС‚; РµСЃР»Рё РїСЂРѕС…РѕРґ СѓСЂРµР·Р°РЅ вЂ” РїСЂРѕР±СѓРµРј РїСЂРёРіРЅСѓРІС€РёСЃСЊ:
            // РїСЂРѕРґРІРёРЅСѓР»РёСЃСЊ РґР°Р»СЊС€Рµ? Р·РЅР°С‡РёС‚ РїСЂРёРіРёР±Р°РЅРёРµ РѕС‚РєСЂС‹РІР°РµС‚ РїСѓС‚СЊ (Р»СЋРє РєР°РїСЃСѓР»С‹).
            Vector3d p = SweepHorizontal(support, Position, move, CurrentHeight);
            if (!Crouched && move.Magnitude > 1e-9d
                && (p - Position).Magnitude < move.Magnitude - 1e-3d)
            {
                Vector3d pCrouched = SweepHorizontal(support, Position, move, CapsuleHeightCrouched);
                if ((pCrouched - Position).Magnitude > (p - Position).Magnitude + 0.02d)
                {
                    Crouched = true;
                    p = pCrouched;
                }
            }

            Position = new Vector3d(p.X, p.Y, Position.Z);

            // РўРѕС‡РЅР°СЏ РѕРїРѕСЂР° РїРѕРґ РёС‚РѕРіРѕРІРѕР№ РїРѕР·РёС†РёРµР№ (Рё РїРѕРІС‚РѕСЂРЅР°СЏ РїСЂРѕРІРµСЂРєР° СѓРєР»РѕРЅР°).
            Vector3d finalProbe = new Vector3d(Position.X, Position.Y, Position.Z + StepUpMeters);
            if (!support.Floor(finalProbe, StepUpMeters + SupportDropMeters,
                    out double finalHeight, out Vector3d finalNormal, out int finalId)
                || finalHeight > Position.Z + StepUpMeters + SeamToleranceMeters
                || SlopeAngleDegrees(finalNormal) > MaxSlopeDegrees)
            {
                Airborne = true;
                GroundSourceId = -1;
                Velocity = intent;
                return;
            }

            // РџРѕС‚РѕР»РѕРє: РІС…РѕРґ РїРѕРґ РЅРёР·РєРёР№ СЃРІРѕРґ С„РёР·РёС‡РµСЃРєРё Р±Р»РѕРєРёСЂСѓРµС‚СЃСЏ СЃРІРёРїРѕРј; Р·РґРµСЃСЊ вЂ”
            // РєРѕРЅС‚СЂРѕР»СЊ Р·Р°РїР°СЃР° РЅР°Рґ РіРѕР»РѕРІРѕР№ (РІС‹СЃРѕС‚Р° вЂ” С‚РµРєСѓС‰Р°СЏ: РїСЂРёСЃРµРґ СѓС‡РёС‚С‹РІР°РµС‚СЃСЏ).
            if (support.Ceiling(new Vector3d(Position.X, Position.Y, finalHeight + CurrentHeight), StepUpMeters,
                    out double ceilingHeight)
                && ceilingHeight - finalHeight < CurrentHeight)
            {
                Position = saved;
                Blocked = true;
                return;
            }

            Position = new Vector3d(Position.X, Position.Y, finalHeight);
            GroundSourceId = finalId;
            Velocity = intent;

            // Р’СЃС‚Р°С‚СЊ, РєРѕРіРґР° РЅР°Рґ РіРѕР»РѕРІРѕР№ СЃРЅРѕРІР° РµСЃС‚СЊ РїРѕР»РЅС‹Р№ СЂРѕСЃС‚ (РІС‹С€Р»Рё РёР· РїСЂРѕС‘РјР°).
            if (Crouched)
            {
                bool ceilingAbove = support.Ceiling(
                    new Vector3d(Position.X, Position.Y, Position.Z + CapsuleHeight), StepUpMeters, out _);
                if (!ceilingAbove)
                {
                    Crouched = false;
                }
            }

            // РџРѕР»РЅРѕСЃС‚СЊСЋ Р·Р°Р¶Р°С‚С‹Р№ РІС…РѕРґ (РІСЃРµ РёС‚РµСЂР°С†РёРё СЃРІРёРїР° СЃСЉРµРґРµРЅС‹) вЂ” В«РІС…РѕРґ Р±Р»РѕРєРёСЂРѕРІР°РЅВ».
            if (move.Magnitude > 1e-9d && (Position - saved).Magnitude < 1e-3d * move.Magnitude)
            {
                Position = saved;
                Blocked = true;
            }
        }

        /// <summary>
        /// РЎР°РјР°СЏ РІС‹СЃРѕРєР°СЏ РѕРїРѕСЂР° РІ РїСЂРµРґРµР»Р°С… С€Р°РіР° РІРІРµСЂС…: РїРѕРґ РєР°РЅРґРёРґР°С‚РѕРј Рё РЅР° СЂР°РґРёСѓСЃРµ
        /// РєР°РїСЃСѓР»С‹ РІРїРµСЂС‘Рґ РїРѕ РґРІРёР¶РµРЅРёСЋ. РЈСЃС‚СѓРї РІС‹С€Рµ StepUp РЅРµ РїРѕРґРЅРёРјР°РµС‚ (СЃРІРёРї Р·Р°Р±Р»РѕРєРёСЂСѓРµС‚).
        /// </summary>
        private static bool TryFindStepFloor(IPlayerSupport support, Vector3d candidate, Vector3d move,
            out double height, out Vector3d normal, out int sourceId)
        {
            double limit = double.NegativeInfinity;
            height = 0d;
            normal = Up;
            sourceId = -1;
            bool found = false;
            double maxDrop = StepUpMeters + SupportDropMeters;

            Vector3d probeFrom = new Vector3d(candidate.X, candidate.Y, candidate.Z + StepUpMeters);
            if (support.Floor(probeFrom, maxDrop, out double h1, out Vector3d n1, out int id1)
                && h1 <= candidate.Z + StepUpMeters + SeamToleranceMeters)
            {
                height = h1;
                normal = n1;
                sourceId = id1;
                found = true;
            }

            double moveLength = move.Magnitude;
            if (moveLength > 1e-12d)
            {
                // РќРµСЃРєРѕР»СЊРєРѕ СЃСЌРјРїР»РѕРІ РїРѕ СЂР°РґРёСѓСЃСѓ РІРїРµСЂС‘Рґ: РѕРґРёРЅРѕС‡РЅС‹Р№ Р·РѕРЅРґ РЅР° РїРѕР»РЅС‹Р№ СЂР°РґРёСѓСЃ
                // РїРµСЂРµР»РµС‚Р°РµС‚ СѓР·РєРёРµ СЃС‚СѓРїРµРЅРё (С€РёСЂРёРЅР° РјРµРЅСЊС€Рµ СЂР°РґРёСѓСЃР°) Рё РЅРµ РІРёРґРёС‚ СѓСЃС‚СѓРї.
                const int samples = 4;
                Vector3d direction = move * (1d / moveLength);
                for (int k = 1; k <= samples; k++)
                {
                    Vector3d ahead = candidate + (direction * (CapsuleRadius * k / samples));
                    Vector3d aheadProbe = new Vector3d(ahead.X, ahead.Y, candidate.Z + StepUpMeters);
                    if (support.Floor(aheadProbe, maxDrop, out double h2, out Vector3d n2, out int id2)
                        && h2 <= candidate.Z + StepUpMeters + SeamToleranceMeters
                        && (!found || h2 > height))
                    {
                        height = h2;
                        normal = n2;
                        sourceId = id2;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Р“РѕСЂРёР·РѕРЅС‚Р°Р»СЊРЅС‹Р№ СЃРІРёРї РєР°РїСЃСѓР»С‹ СЃРѕ СЃРєРѕР»СЊР¶РµРЅРёРµРј РІРґРѕР»СЊ СЃС‚РµРЅ (РґРѕ MaxSweepIterations
        /// РёС‚РµСЂР°С†РёР№). height вЂ” РІС‹СЃРѕС‚Р° РєР°РїСЃСѓР»С‹ (РїРѕР»РЅС‹Р№ СЂРѕСЃС‚/РїСЂРёСЃРµРґ). Р’РѕР·РІСЂР°С‰Р°РµС‚
        /// РёС‚РѕРіРѕРІСѓСЋ РїРѕР·РёС†РёСЋ; Z РЅРµ РјРµРЅСЏРµС‚СЃСЏ.
        /// </summary>
        private static Vector3d SweepHorizontal(IPlayerSupport support, Vector3d from, Vector3d move, double height)
        {
            Vector3d p = from;
            Vector3d remaining = move;
            for (int iteration = 0; iteration < MaxSweepIterations; iteration++)
            {
                if (remaining.Magnitude < 1e-12d)
                {
                    break;
                }

                if (!support.Sweep(p, p + remaining, CapsuleRadius, height, out Vector3d wallNormal, out double fraction))
                {
                    p += remaining;
                    break;
                }

                if (!(fraction > 0d) || !(fraction < 1d))
                {
                    fraction = fraction < 0d ? 0d : (fraction > 1d ? 1d : fraction);
                }

                p += remaining * fraction;
                Vector3d left = remaining * (1d - fraction);
                // РЎРєРѕР»СЊР·РёРј РїРѕ РџР›РћРЎРљРћР™ СЃС‚РµРЅРµ: Сѓ РЅР°РєР»РѕРЅРЅРѕР№ РїРѕРІРµСЂС…РЅРѕСЃС‚Рё РЅРѕСЂРјР°Р»СЊ РёРјРµРµС‚
                // Z-СЃРѕСЃС‚Р°РІР»СЏСЋС‰СѓСЋ, Рё РµС‘ РїСЂРѕРµРєС†РёСЏ В«СЃСЉРµРґР°Р»Р°В» Р±С‹ С‡Р°СЃС‚СЊ С…РѕРґР°, Р°
                // РѕСЃС‚Р°РІС€РµРµСЃСЏ РґРІРёР¶РµРЅРёРµ СѓРІРѕРґРёР»Р° Р±С‹ РїРѕ РІРµСЂС‚РёРєР°Р»Рё (Z В«РїР»С‹Р»В» РјРµР¶РґСѓ
                // РёС‚РµСЂР°С†РёСЏРјРё). РќРѕСЂРјР°Р»СЊ СЃРІРёРїР° вЂ” С‚РѕР»СЊРєРѕ РїРѕ РіРѕСЂРёР·РѕРЅС‚Р°Р»Рё.
                Vector3d wallFlat = new Vector3d(wallNormal.X, wallNormal.Y, 0d);
                if (wallFlat.SqrMagnitude > 1e-12d)
                {
                    wallFlat = wallFlat.Normalized;
                    left -= wallFlat * Vector3d.Dot(left, wallFlat);
                }

                remaining = new Vector3d(left.X, left.Y, 0d);
            }

            return p;
        }

        private void StepAirborne(IPlayerSupport support, double dt)
        {
            if (Hovering)
            {
                // Р’РёСЃРµРЅРёРµ: РіСЂР°РІРёС‚Р°С†РёРё РЅРµС‚, СЃРєРѕСЂРѕСЃС‚СЊ РµРґРµС‚ Рє Р¶РµР»Р°РµРјРѕР№ (РІРІРѕРґ);
                // Р±РµР· РІРІРѕРґР° вЂ” Рє РЅСѓР»СЋ: РёРіСЂРѕРє Р·Р°РјРёСЂР°РµС‚ РІ РІРѕР·РґСѓС…Рµ.
                double blend = Math.Min(1d, dt * HoverResponse);
                Velocity += (HoverVelocity - Velocity) * blend;
            }
            else
            {
                Velocity = new Vector3d(Velocity.X, Velocity.Y, Velocity.Z - (Gravity * dt));
            }

            Vector3d step = Velocity * dt;

            // Р“РѕСЂРёР·РѕРЅС‚Р°Р»СЊ вЂ” СЃРІРёРїРѕРј РІРґРѕР»СЊ СЃС‚РµРЅ (РєР°Рє Р±С‹Р»Рѕ).
            Vector3d p = SweepHorizontal(support, Position, new Vector3d(step.X, step.Y, 0d), CurrentHeight);

            // Р’РµСЂС‚РёРєР°Р»СЊ РґСЂРѕР±РёРј С‡Р°РЅРєР°РјРё: РїРѕРґС€Р°Рі РјРѕР¶РµС‚ Р±С‹С‚СЊ РґРѕ 0.5 СЃ (РјРµС‚СЂС‹ РїСЂРѕР»С‘С‚Р°),
            // Рё С‚РѕРЅРєР°СЏ РїР»РёС‚Р° РЅР° РїСѓС‚Рё РЅРµ РґРѕР»Р¶РЅР° РїСЂРѕСЃРєР°РєРёРІР°С‚СЊСЃСЏ С†РµР»РёРєРѕРј. РџРѕСЃР°РґРєР°
            // РїСЂРѕРІРµСЂСЏРµС‚СЃСЏ Р”Рћ СЃРґРІРёРіР° С‡Р°РЅРєР°, РїРѕСЌС‚РѕРјСѓ РїР»РёС‚Р° РІРЅСѓС‚СЂРё С‡Р°РЅРєР° РІРёРґРЅР°.
            const double maxVerticalChunk = 0.2d;
            double remainingZ = step.Z;
            while (Math.Abs(remainingZ) > 1e-12d)
            {
                double chunk = Math.Max(-maxVerticalChunk, Math.Min(maxVerticalChunk, remainingZ));
                if (chunk < 0d)
                {
                    // РџРѕСЃР°РґРєР° С‚РѕР»СЊРєРѕ РЅР° СЃРїСѓСЃРєРµ: РїРѕРґСЉС‘Рј (РїСЂС‹Р¶РѕРє) РЅРµ РґРѕР»Р¶РµРЅ РјРіРЅРѕРІРµРЅРЅРѕ
                    // В«РїСЂРёР·РµРјР»СЏС‚СЊСЃСЏВ» РѕР±СЂР°С‚РЅРѕ РЅР° С‚Сѓ Р¶Рµ РѕРїРѕСЂСѓ. РћС‚СЃРєРѕРєР° РЅРµС‚ (v_z = 0).
                    // Р›СѓС‡ РЅР°С‡РёРЅР°РµС‚СЃСЏ РЅР° StepUp РІС‹С€Рµ РЅРѕРі, Рё РґРѕРїСѓСЃРє С‚РѕС‚ Р¶Рµ StepUp:
                    // СЃРІРёРї РёРіРЅРѕСЂРёСЂСѓРµС‚ СЃРєР»РѕРЅС‹ РєСЂСѓС‚РёР·РЅРѕР№ РґРѕ ~45В° (СЃРј. SiteBoxSupport.
                    // Sweep), РїРѕСЌС‚РѕРјСѓ РїР°РґРµРЅРёРµ СЃ РіРѕСЂРёР·РѕРЅС‚Р°Р»СЊРЅС‹Рј С…РѕРґРѕРј РјРѕР¶РµС‚ РІРЅРµСЃС‚Рё
                    // РЅРѕРіРё РїРѕРґ СЃРєРѕСЃ РїР»РёС‚С‹/СѓСЃС‚СѓРї. РўРѕРіРґР° С‚РµР»Рѕ РЅРµ Р·Р°СЂС‹РІР°РµС‚СЃСЏ РІ РјРµС€, Р°
                    // В«С€Р°РіР°РµС‚В» РЅР° РЅР°Р№РґРµРЅРЅСѓСЋ РїРѕРІРµСЂС…РЅРѕСЃС‚СЊ вЂ” Р·РµСЂРєР°Р»Рѕ TryFindStepFloor
                    // Сѓ Р·РµРјР»Рё (Р·Р°РїРёСЃСЊ РІ РѕС‚С‡С‘С‚ В«РґРѕСЂРѕРіР°: СЃРєРѕСЃ Рё РїРѕСЃР°РґРєР°В», 2026-10-09).
                    double fall = -chunk + 0.1d;
                    Vector3d landFrom = new Vector3d(p.X, p.Y, p.Z + StepUpMeters);
                    if (support.Floor(landFrom, StepUpMeters + fall, out double floorHeight, out Vector3d floorNormal, out int floorId)
                        && floorHeight <= p.Z + StepUpMeters
                        && SlopeAngleDegrees(floorNormal) <= MaxSlopeDegrees)
                    {
                        Position = new Vector3d(p.X, p.Y, floorHeight);
                        Velocity = new Vector3d(Velocity.X, Velocity.Y, 0d);
                        Airborne = false;
                        GroundSourceId = floorId;
                        return;
                    }
                }

                p = new Vector3d(p.X, p.Y, p.Z + chunk);
                remainingZ -= chunk;
            }

            Position = p;
        }

        public static double SlopeAngleDegrees(Vector3d normal)
        {
            Vector3d n = normal.Normalized;
            double cosine = Math.Max(-1d, Math.Min(1d, Vector3d.Dot(n, Up)));
            return Math.Acos(cosine) * (180d / Math.PI);
        }
    }
}

