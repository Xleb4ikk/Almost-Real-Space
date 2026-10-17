using Galilego.Universe;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// Пара мониторов оператора Orion: рядом перед глазами, экраны на глаза.
    public static class OperatorMonitorLayout
    {
        public static float DistanceMeters = 0.50f;  // глаза -> центры экранов (стена близко)
        public static float PitchDownDegrees = 32f;  // ниже горизонта; меньше - верх экранов уходит в стену
        public static float Scale = 0.60f;           // масштаб мониторов
        public static float GapMeters = 0.01f;

        public static void Apply(GameObject model)
        {
            CockpitSeat seat = model.GetComponentInChildren<CockpitSeat>(true);
            if (seat == null || seat.Anchor == null) return;

            string[] id = { "Monitor_Operator_1", "Monitor_Operator_2" };
            var mon = new Transform[2]; var ctr = new Transform[2]; var scr = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                mon[i] = Find(model, id[i]);
                ctr[i] = Find(model, id[i] + "_ScreenCenter");
                scr[i] = Find(model, id[i] + "_Screen");
                if (mon[i] == null || ctr[i] == null || scr[i] == null) return;
            }

            Vector3 eye = seat.Anchor.position;
            Vector3 up = seat.Anchor.forward;                     // «вверх» кресла (+Z якоря)
            Vector3 mid = (ctr[0].position + ctr[1].position) * 0.5f;
            Vector3 fwd = Vector3.ProjectOnPlane(mid - eye, up).normalized;
            Vector3 right = Vector3.Cross(up, fwd);
            float side0 = Mathf.Sign(Vector3.Dot(ctr[0].position - mid, right));

            float w = Measure(scr[0], eye - ctr[0].position, out _) * Scale;
            float half = Mathf.Asin(Mathf.Clamp01((w * 0.5f + GapMeters * 0.5f) / DistanceMeters)) * Mathf.Rad2Deg;
            Vector3 down = Quaternion.AngleAxis(PitchDownDegrees, right) * fwd;

            for (int i = 0; i < 2; i++)
            {
                Measure(scr[i], eye - ctr[i].position, out Vector3 n0);
                float side = i == 0 ? side0 : -side0;
                Vector3 target = eye + Quaternion.AngleAxis(side * half, up) * down * DistanceMeters;
                Vector3 nNew = (eye - target).normalized;
                Vector3 u0 = Vector3.ProjectOnPlane(up, n0).normalized;
                Vector3 uNew = Vector3.ProjectOnPlane(up, nNew).normalized;

                mon[i].localScale *= Scale;
                mon[i].rotation = Quaternion.LookRotation(nNew, uNew)
                    * Quaternion.Inverse(Quaternion.LookRotation(n0, u0)) * mon[i].rotation;
                mon[i].position += target - ctr[i].position;
            }
        }

        /// Ширина квада экрана (м) и его нормаль, повёрнутая к глазам.
        private static float Measure(Transform screen, Vector3 toEye, out Vector3 normal)
        {
            normal = -toEye.normalized;
            MeshFilter mf = screen.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < 4) return 0.56f;
            Vector3[] v = mf.sharedMesh.vertices;
            var p = new Vector3[4];
            for (int i = 0; i < 4; i++) p[i] = screen.TransformPoint(v[i]);
            normal = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
            if (Vector3.Dot(normal, toEye) < 0f) normal = -normal;
            var d = new float[6]; int k = 0;
            for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++) d[k++] = Vector3.Distance(p[i], p[j]);
            System.Array.Sort(d);
            return d[2];                                          // длинная сторона (не диагональ)
        }

        private static Transform Find(GameObject root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
