using Galilego.Universe;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Orion operator monitors are placed BY HAND in the prefab (blocks in the wall,
    /// screens on the blocks) — so Apply moves nothing: it only aims the seated crew
    /// member's view at the pair of monitors on landing (the camera is otherwise free
    /// to look around with the mouse).
    ///
    /// Call once right after Instantiating the capsule model (PartCatalog).
    /// false — the model has no seat/anchor/screens (nothing to aim).
    /// </summary>
    public static class OperatorMonitorLayout
    {
        public static bool Apply(GameObject model)
        {
            if (model == null)
            {
                return false;
            }

            CockpitSeat seat = model.GetComponentInChildren<CockpitSeat>(true);
            if (seat == null || seat.Anchor == null)
            {
                return false;
            }

            string[] id = { "Monitor_Operator_1", "Monitor_Operator_2" };
            var ctr = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                ctr[i] = Find(model, id[i] + "_ScreenCenter");
                if (ctr[i] == null)
                {
                    ctr[i] = Find(model, id[i]);
                }

                if (ctr[i] == null)
                {
                    return false;
                }
            }

            // Seated eye: anchor + (camera SeatedEyeMeters - seat EyeHeightMeters).
            Vector3 up = seat.Anchor.forward;
            float above = 0.30f;
            FirstPersonCamera cam = UnityEngine.Object.FindAnyObjectByType<FirstPersonCamera>();
            if (cam != null)
            {
                above = Mathf.Max(0f, (float)cam.SeatedEyeMeters - seat.EyeHeightMeters);
            }

            Vector3 eye = seat.Anchor.position + (up * above);
            Vector3 mid = (ctr[0].position + ctr[1].position) * 0.5f;
            Vector3 look = mid - eye;
            if (look.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            seat.LocalLookDirection = seat.Anchor.InverseTransformDirection(look.normalized);
            return true;
        }

        private static Transform Find(GameObject root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
