using System;
using Galilego.Core;

namespace Galilego.Planning
{
    /// <summary>
    /// Оси манёвра T/N/B и перевод «вектор ΔV ↔ три компоненты». Построение ОДИН-В-ОДИН как в
    /// NavMapModel.ApplyBurn (касательная — вдоль скорости относительно тела-системы, бинормаль — r×v,
    /// нормаль = бинормаль×касательная, т.е. к телу), иначе импульс планировщика и импульс прогноза разойдутся.
    /// </summary>
    public static class ManeuverAxes
    {
        public static bool TryBasis(Vector3d r, Vector3d v, Vector3d fallbackNormal,
            out Vector3d tangent, out Vector3d normal, out Vector3d binormal)
        {
            tangent = v.Normalized;
            normal = Vector3d.Zero;
            binormal = Vector3d.Zero;
            if (!tangent.IsFinite || !(tangent.SqrMagnitude > 0.5d))
            {
                return false;
            }

            binormal = Vector3d.Cross(r, v).Normalized;
            if (!binormal.IsFinite || !(binormal.SqrMagnitude > 0.5d))
            {
                binormal = fallbackNormal.IsFinite && fallbackNormal.SqrMagnitude > 0.5d
                    ? fallbackNormal.Normalized
                    : new Vector3d(0d, 0d, 1d);
            }

            normal = Vector3d.Cross(binormal, tangent).Normalized;
            return normal.IsFinite && normal.SqrMagnitude > 0.5d;
        }

        /// <summary>Мировой ΔV → (касательная, нормаль, бинормаль) в осях корабля относительно тела-системы.</summary>
        public static bool TryToComponents(Vector3d r, Vector3d v, Vector3d fallbackNormal, Vector3d dvWorld,
            out double tangent, out double normal, out double binormal)
        {
            tangent = normal = binormal = 0d;
            if (!TryBasis(r, v, fallbackNormal, out Vector3d t, out Vector3d n, out Vector3d b))
            {
                return false;
            }

            tangent = Vector3d.Dot(dvWorld, t);
            normal = Vector3d.Dot(dvWorld, n);
            binormal = Vector3d.Dot(dvWorld, b);
            return true;
        }

        /// <summary>Компоненты → мировой ΔV (то, что применяет прогноз).</summary>
        public static bool TryToWorld(Vector3d r, Vector3d v, Vector3d fallbackNormal,
            double tangent, double normal, double binormal, out Vector3d dvWorld)
        {
            dvWorld = Vector3d.Zero;
            if (!TryBasis(r, v, fallbackNormal, out Vector3d t, out Vector3d n, out Vector3d b))
            {
                return false;
            }

            dvWorld = (t * tangent) + (n * normal) + (b * binormal);
            return dvWorld.IsFinite;
        }
    }
}
