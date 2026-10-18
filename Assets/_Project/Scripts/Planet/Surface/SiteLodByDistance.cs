using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Building LOD by METRES, not by screen size: switches levels by horizontal
    /// distance in the site axes (X north, Y zenith, Z east). Works only in Play;
    /// in the editor the LODGroup stays enabled so the author can still drag level
    /// boundaries in the Scene view.
    ///
    /// The real visibility limit is the planet horizon, not the LOD setting.
    /// 0 in LevelMaxDistanceMeters = never cull; otherwise the building is culled
    /// entirely beyond the last number.
    /// </summary>
    public sealed class SiteLodByDistance : MonoBehaviour
    {
        [Tooltip("Камера, от которой меряем. Пусто = Camera.main.")]
        public Camera View;

        [Tooltip("Горизонтальная дистанция, до которой держится уровень, м — по " +
            "одному числу на уровень (LOD0, LOD1, LOD2). Дальше последнего " +
            "числа постройка гаснет целиком. 0 или меньше = границы нет: тогда " +
            "режет только горизонт планеты и far камеры.")]
        public float[] LevelMaxDistanceMeters = { 350f, 1100f, 80000f };

        /// <summary>Index of the visible level, −1 if nothing is visible.</summary>
        public int CurrentLevel { get; private set; } = -1;

        /// <summary>Horizontal distance to the building, m — for debugging.</summary>
        public float DistanceMeters { get; private set; }

        private LODGroup lodGroup;
        private Renderer[][] levels;
        private bool disabledLodGroup;

        /// <summary>No levels found in the group — the component stays silent.</summary>
        public bool HasLevels => levels != null && levels.Length > 0;

        private void OnEnable()
        {
            Cache();
            if (Application.isPlaying)
            {
                TakeOver();
                Apply();
            }
        }

        private void LateUpdate()
        {
            if (Application.isPlaying)
            {
                Apply();
            }
        }

        private void OnDisable()
        {
            Release();
        }

        /// <summary>Re-read levels from the LODGroup — after editing the prefab in the editor.</summary>
        [ContextMenu("Пересобрать уровни")]
        public void Recache()
        {
            Cache();
            if (Application.isPlaying)
            {
                TakeOver();
                Apply();
            }
        }

        private void Cache()
        {
            lodGroup = GetComponentInChildren<LODGroup>(true);
            levels = null;
            CurrentLevel = -1;

            if (lodGroup == null)
            {
                return;
            }

            LOD[] lods = lodGroup.GetLODs();
            levels = new Renderer[lods.Length][];
            for (int i = 0; i < lods.Length; i++)
            {
                Renderer[] source = lods[i].renderers;
                int count = 0;
                for (int j = 0; j < source.Length; j++)
                {
                    if (source[j] != null)
                    {
                        count++;
                    }
                }

                Renderer[] level = new Renderer[count];
                int at = 0;
                for (int j = 0; j < source.Length; j++)
                {
                    if (source[j] != null)
                    {
                        level[at++] = source[j];
                    }
                }

                levels[i] = level;
            }
        }

        /// <summary>
        /// Take over the LODGroup: a disabled group does not switch renderers
        /// itself, and we own level switching entirely. The flag avoids re-enabling
        /// in OnDisable a group someone else disabled.
        /// </summary>
        private void TakeOver()
        {
            if (lodGroup != null && lodGroup.enabled)
            {
                lodGroup.enabled = false;
                disabledLodGroup = true;
            }
        }

        private void Release()
        {
            if (disabledLodGroup && lodGroup != null)
            {
                lodGroup.enabled = true;
            }

            disabledLodGroup = false;

            // Authoring state: LOD0 on, far levels off.
            ShowLevel(0);
            CurrentLevel = -1;
        }

        private void Apply()
        {
            if (!HasLevels)
            {
                return;
            }

            Camera view = View != null ? View : Camera.main;
            if (view == null)
            {
                return;
            }

            // Site axes: X north, Y zenith, Z east. Horizontal distance in this
            // plane is what the player actually covers past the building; the
            // vertical (our own zenith) plays no part in the bounds.
            Vector3 local = transform.InverseTransformPoint(view.transform.position);
            DistanceMeters = Mathf.Sqrt((local.x * local.x) + (local.z * local.z));

            int wanted = PickLevel(DistanceMeters);
            if (wanted == CurrentLevel)
            {
                return;
            }

            ShowLevel(wanted);
        }

        /// <summary>Level index for a distance: the first number that was enough.</summary>
        private int PickLevel(float distanceMeters)
        {
            int count = levels.Length;
            if (count == 0)
            {
                return -1;
            }

            if (LevelMaxDistanceMeters == null || LevelMaxDistanceMeters.Length == 0)
            {
                // No settings — show the cheapest level and never cull: there is
                // nothing to make the building disappear and nothing to switch by.
                return count - 1;
            }

            for (int i = 0; i < count; i++)
            {
                int slot = i < LevelMaxDistanceMeters.Length ? i : LevelMaxDistanceMeters.Length - 1;
                float limit = LevelMaxDistanceMeters[slot];
                if (limit <= 0f)
                {
                    // No limit: the cheapest level and never cull. Beyond that the
                    // building is cut only by the planet horizon and the camera far plane.
                    return count - 1;
                }

                if (distanceMeters <= limit)
                {
                    return i;
                }
            }

            return -1;
        }

        private void ShowLevel(int level)
        {
            if (!HasLevels)
            {
                return;
            }

            for (int i = 0; i < levels.Length; i++)
            {
                bool on = i == level;
                Renderer[] renderers = levels[i];
                for (int j = 0; j < renderers.Length; j++)
                {
                    if (renderers[j] != null && renderers[j].enabled != on)
                    {
                        renderers[j].enabled = on;
                    }
                }
            }

            CurrentLevel = level;
        }
    }
}
