using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// LOD постройки по МЕТРАМ, а не по экранному размеру.
    ///
    /// Почему не штатный LODGroup: у него после последнего уровня всегда есть
    /// состояние «выключено». Как только «considered screen height» падает ниже
    /// порога последнего уровня, Unity гасит ВСЕ рендереры группы, и здание
    /// просто исчезает. Держать что-то «навсегда» экранно-относительный LOD не
    /// умеет в принципе, а на планете R = 1143 км это ровно тот случай: постройка
    /// должна читаться как ориентир за горизонтом, а не пустота в кадре.
    ///
    /// Поэтому компонент забирает LODGroup себе (выключает его — и тогда
    /// рендерерами управляем мы, а не Unity) и переключает уровни по
    /// горизонтальной дистанции в осях места (X — север, Y — зенит, Z — восток).
    /// Именно горизонталь: здание высотой 228 м, игрок на 2 м — вертикаль съела бы
    /// сотни метров у самой постройки и передвинула бы все границы.
    ///
    /// Работает только в Play. В редакторе LODGroup остаётся включённым, чтобы
    /// автор по-прежнему мог тянуть границы уровней мышкой в Scene view.
    ///
    /// Про «с огромного расстояния». Настоящая граница видимости здесь не
    /// настройка LOD, а горизонт планеты: насколько далеко видна вершина
    /// высоты H при наблюдателе на высоте h — это √(2·R·(h + H)). Для Terra
    /// (R = 1143 км) и базы высотой 228 м это ~23 км с земли (глаз 2 м),
    /// ~35 км с 300 м и ~53 км с километра. Дальше вершина базы уходит под
    /// горизонт, и никакой LOD её не вернёт — это геометрия сферы.
    ///
    /// Но игра и так моделирует «видно за горизонт»: амплитуда рельефа 9144 м
    /// (с континентальной глубиной пик ~20 км), из-за чего горизонт камеры
    /// (SkyPhysics.MaxSightDistance) уходит за 200 км, чанки рельефа режутся
    /// по maxVisibleDistance около 166 км (PlanetSurfaceRenderer), far камеры —
    /// ~238 км (FirstPersonCamera.UpdateClipPlanes). Ни одна из этих границ не
    /// мешает 40 км.
    ///
    /// Про 80 км в последнем уровне: это осознанная граница, а не «на всякий
    /// случай». Дальше 80 км база — меньше пикселя (≈3.5 px на 1440p при FOV
    /// 60°) и всё равно не читается, но рендереры продолжают попадать в кадр.
    /// 0 = не гасить никогда — если понадобится именно такое поведение.
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

        /// <summary>Индекс видимого уровня, −1 если не видно ничего.</summary>
        public int CurrentLevel { get; private set; } = -1;

        /// <summary>Горизонтальная дистанция до постройки, м — для отладки.</summary>
        public float DistanceMeters { get; private set; }

        private LODGroup lodGroup;
        private Renderer[][] levels;
        private bool disabledLodGroup;

        /// <summary>Уровней в группе не нашлось — компонент молчит.</summary>
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

        /// <summary>Перечитать уровни из LODGroup — после правки префаба в редакторе.</summary>
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
        /// Забираем LODGroup: выключенная группа не гасит рендереры сама, и
        /// переключением уровней целиком владеем мы. Флаг — чтобы в OnDisable
        /// не включить обратно группу, которую выключил кто-то ещё.
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

            // Авторское состояние: LOD0 включён, дальние уровни выключены.
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

            // Оси места: X — север, Y — зенит, Z — восток. Горизонтальная
            // дистанция в этой плоскости — то, что игрок реально проходит мимо
            // здания; вертикаль (наш собственный зенит) в границах не участвует.
            Vector3 local = transform.InverseTransformPoint(view.transform.position);
            DistanceMeters = Mathf.Sqrt((local.x * local.x) + (local.z * local.z));

            int wanted = PickLevel(DistanceMeters);
            if (wanted == CurrentLevel)
            {
                return;
            }

            ShowLevel(wanted);
        }

        /// <summary>Индекс уровня для дистанции: первое число, которого хватило.</summary>
        private int PickLevel(float distanceMeters)
        {
            int count = levels.Length;
            if (count == 0)
            {
                return -1;
            }

            if (LevelMaxDistanceMeters == null || LevelMaxDistanceMeters.Length == 0)
            {
                // Настроек нет — показываем самый дешёвый уровень и не гасим:
                // исчезать постройке не от чего, а переключать нечем.
                return count - 1;
            }

            for (int i = 0; i < count; i++)
            {
                int slot = i < LevelMaxDistanceMeters.Length ? i : LevelMaxDistanceMeters.Length - 1;
                float limit = LevelMaxDistanceMeters[slot];
                if (limit <= 0f)
                {
                    // Границы нет: самый дешёвый уровень и не гасим. Дальше
                    // постройку отрезает только горизонт планеты и far камеры.
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
