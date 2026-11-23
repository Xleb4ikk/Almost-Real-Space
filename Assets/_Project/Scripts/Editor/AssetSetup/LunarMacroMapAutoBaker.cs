using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Keeps the baked macro maps (albedo / normal / horizon) in sync with the authoring
    /// data, so changing a crater parameter or the body seed no longer leaves a stale
    /// terminator that nobody remembers to rebake.
    ///
    /// What counts as "requires a rebake" — the fingerprint
    /// <see cref="TerrainProfile.MacroBakeHash"/> mixes:
    ///   * every LunarParams field (crater density, maria, giant basin, radius, seed);
    ///   * the three palette colours the macro albedo is derived from;
    ///   * <see cref="LunarMacroMapBaker.BakeVersion"/> (an algorithm change).
    /// A changed TerrainSeed, a deleted map reference or a profile created from scratch
    /// (hash 0) are stale too.
    ///
    /// Behaviour:
    ///   * the edit is debounced (1.5 s after the LAST change) so dragging a slider does
    ///     not start a bake per mouse move;
    ///   * the pixels are computed on a worker — the editor stays responsive, the cancel
    ///     button aborts the bake and it is not retried until the next edit;
    ///   * only the first body of a shared profile owns the bake: the PNG names are per
    ///     profile, so two bodies with different seeds cannot both be represented (that
    ///     case logs one warning instead of ping-ponging forever);
    ///   * never runs in Play or in batch mode (asset writes would not persist / would
    ///     make CI nondeterministic).
    ///
    /// Menu toggle: Tools/Galilego/Airless Bodies/Auto-rebake macro maps.
    /// </summary>
    [InitializeOnLoad]
    internal static class LunarMacroMapAutoBaker
    {
        private const string EnabledKey = "Galilego.LunarMacroAutoBake";
        private const double PollSeconds = 0.35;
        private const double DebounceSeconds = 1.5;

        private sealed class Seen
        {
            public long Hash;
            public long MutedHash;
            public double ChangedAt;
            public string Owner;
        }

        private static readonly Dictionary<TerrainProfileAsset, Seen> Profiles =
            new Dictionary<TerrainProfileAsset, Seen>();

        private static readonly HashSet<TerrainProfileAsset> SharedProfileWarned =
            new HashSet<TerrainProfileAsset>();

        private static readonly HashSet<BodyAuthoring> RadiusWarned =
            new HashSet<BodyAuthoring>();

        private static double nextPoll;
        private static double nextProgress;
        private static Task<LunarMacroMapBaker.BakeResult> worker;
        private static CancellationTokenSource workerCancel;
        private static BodyAuthoring workerBody;
        private static TerrainProfileAsset workerAsset;
        private static long workerHash;
        private static float workerProgress;
        private static bool pumpingProgress;

        private static bool Enabled
        {
            get { return EditorPrefs.GetBool(EnabledKey, true); }
            set { EditorPrefs.SetBool(EnabledKey, value); }
        }

        static LunarMacroMapAutoBaker()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += CancelWorker;
        }

        [MenuItem("Tools/Galilego/Airless Bodies/Auto-rebake macro maps", priority = 22)]
        private static void ToggleEnabled()
        {
            Enabled = !Enabled;
            Debug.Log("[LunarMacroAutoBake] автоперепечка " + (Enabled ? "включена" : "выключена"));
        }

        [MenuItem("Tools/Galilego/Airless Bodies/Auto-rebake macro maps", true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked("Tools/Galilego/Airless Bodies/Auto-rebake macro maps", Enabled);
            return true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                CancelWorker();
            }
        }

        // ------------------------------------------------------------------ polling

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;

            if (worker != null)
            {
                PumpWorker(now);
                return;
            }

            if (!Enabled || Application.isBatchMode)
            {
                return;
            }

            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling || EditorApplication.isUpdating
                || LunarMacroMapBaker.IsBaking)
            {
                return;
            }

            if (now < nextPoll)
            {
                return;
            }

            nextPoll = now + PollSeconds;

            if (!FindStale(now, out BodyAuthoring body, out TerrainProfileAsset asset,
                    out long hash, out int seed))
            {
                return;
            }

            Start(now, body, asset, hash, seed);
        }

        /// <summary>
        /// Scans the open scenes for a lunar body whose fingerprint (or texture
        /// references) no longer match the bake. Also maintains the per-profile seen
        /// state that implements the debounce.
        /// </summary>
        private static bool FindStale(double now, out BodyAuthoring body,
            out TerrainProfileAsset asset, out long hash, out int seed)
        {
            body = null;
            asset = null;
            hash = 0;
            seed = 0;

            BodyAuthoring[] bodies = UnityEngine.Object.FindObjectsByType<BodyAuthoring>(
                FindObjectsInactive.Include);
            // Deterministic owner: scene path + hierarchy, not instance order.
            Array.Sort(bodies, (a, b) => string.CompareOrdinal(PathOf(a), PathOf(b)));

            var used = new HashSet<TerrainProfileAsset>();
            for (int i = 0; i < bodies.Length; i++)
            {
                BodyAuthoring candidate = bodies[i];
                TerrainProfileAsset candidateAsset = candidate.TerrainPreset;
                if (candidateAsset == null || candidateAsset.Profile == null
                    || candidateAsset.Profile.Lunar == null
                    || !candidateAsset.Profile.Lunar.Enabled)
                {
                    continue;
                }

                TerrainProfile profile = candidateAsset.Profile;
                long current = LunarMacroMapBaker.ComputeHash(profile, candidate.TerrainSeed);
                Seen seen = SeenOf(candidateAsset);
                if (seen.Owner == null)
                {
                    seen.Owner = candidate.name;
                }

                if (Math.Abs(candidate.Radius - profile.Lunar.RadiusMeters) > 1d
                    && RadiusWarned.Add(candidate))
                {
                    Debug.LogWarning("[LunarMacroAutoBake] " + candidate.name
                        + ": BodyAuthoring.Radius (" + (candidate.Radius / 1000d).ToString("F1")
                        + " км) не совпадает с Lunar.RadiusMeters ("
                        + (profile.Lunar.RadiusMeters / 1000d).ToString("F1")
                        + " км) — меш и макро-карты описывают разные тела.");
                }

                if (!used.Add(candidateAsset))
                {
                    // The PNG files are per PROFILE, so only one body's seed can be
                    // represented. Rebaking for the second body would overwrite the first
                    // one's maps and the next poll would overwrite them back: warn once.
                    if (current != seen.Hash && SharedProfileWarned.Add(candidateAsset))
                    {
                        Debug.LogWarning("[LunarMacroAutoBake] профиль \"" + candidateAsset.name
                            + "\" используется телами с разными зернами/радиусами ("
                            + seen.Owner + " и " + candidate.name
                            + "). Макро-карты соответствуют \"" + seen.Owner
                            + "\"; второму телу нужен отдельный профиль.");
                    }

                    continue;
                }

                if (seen.Hash != current)
                {
                    seen.Hash = current;
                    seen.ChangedAt = now;
                    seen.MutedHash = 0;
                }

                bool stale = profile.MacroBakeHash != current
                    || profile.MacroAlbedo == null
                    || profile.MacroNormal == null
                    || profile.MacroHorizonA == null
                    || profile.MacroHorizonB == null;
                if (!stale || seen.MutedHash == current)
                {
                    continue;
                }

                if (now - seen.ChangedAt < DebounceSeconds)
                {
                    continue;
                }

                body = candidate;
                asset = candidateAsset;
                hash = current;
                seed = candidate.TerrainSeed;
                return true;
            }

            return false;
        }

        private static void Start(double now, BodyAuthoring body, TerrainProfileAsset asset,
            long hash, int seed)
        {
            LunarMacroMapBaker.BakeInputs inputs = LunarMacroMapBaker.Capture(asset.Profile, seed);
            if (inputs.Hash != hash)
            {
                // The profile changed between the scan and the snapshot: next poll.
                return;
            }

            workerBody = body;
            workerAsset = asset;
            workerHash = hash;
            workerProgress = 0f;
            nextProgress = now;
            workerCancel = new CancellationTokenSource();
            CancellationToken token = workerCancel.Token;

            // Snapshot already taken: the worker reads only arrays and structs, so the
            // inspector can keep editing without tearing the bake.
            worker = Task.Run(
                () => LunarMacroMapBaker.Compute(inputs, p => workerProgress = p, token),
                token);
            Debug.Log("[LunarMacroAutoBake] " + body.name
                + ": параметры рельефа изменились — перепекаю макро-карты.");
        }

        private static void PumpWorker(double now)
        {
            if (worker.IsCompleted)
            {
                FinishWorker();
                return;
            }

            if (now < nextProgress)
            {
                return;
            }

            nextProgress = now + 0.1;

            // DisplayCancelableProgressBar pumps the editor loop: a nested update must
            // not show a second bar or start another bake.
            if (pumpingProgress)
            {
                return;
            }

            float progress = Mathf.Clamp01(workerProgress);
            pumpingProgress = true;
            bool cancel;
            try
            {
                cancel = EditorUtility.DisplayCancelableProgressBar("Lunar macro maps",
                    (workerBody != null ? workerBody.name : "?") + ": перепекаю карты…",
                    progress * 0.99f);
            }
            finally
            {
                pumpingProgress = false;
            }

            if (cancel)
            {
                CancelWorker();
            }
        }

        private static void FinishWorker()
        {
            Task<LunarMacroMapBaker.BakeResult> finished = worker;
            BodyAuthoring body = workerBody;
            TerrainProfileAsset asset = workerAsset;
            long hash = workerHash;
            worker = null;
            workerBody = null;
            workerAsset = null;
            workerHash = 0;
            if (workerCancel != null)
            {
                workerCancel.Dispose();
                workerCancel = null;
            }

            EditorUtility.ClearProgressBar();

            if (body == null || asset == null || asset.Profile == null)
            {
                return;
            }

            // Asset writes during Play are not persisted; also never apply a result whose
            // body/preset was deleted while the worker ran.
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Mute(asset, hash);
                Debug.Log("[LunarMacroAutoBake] Play начался — результат бейка отброшен.");
                return;
            }

            LunarMacroMapBaker.BakeResult result;
            try
            {
                result = finished.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                Mute(asset, hash);
                Debug.Log("[LunarMacroAutoBake] отменено; повторю после следующего изменения.");
                return;
            }
            catch (Exception e)
            {
                Mute(asset, hash);
                Debug.LogException(e);
                return;
            }

            long current = LunarMacroMapBaker.ComputeHash(asset.Profile, body.TerrainSeed);
            if (current != result.Hash)
            {
                // The user kept editing during the bake: the pixels are already stale.
                Seen seen = SeenOf(asset);
                seen.Hash = current;
                seen.ChangedAt = EditorApplication.timeSinceStartup;
                Debug.Log("[LunarMacroAutoBake] профиль изменён во время бейка — результат "
                    + "отброшен, перепекаю заново.");
                return;
            }

            try
            {
                LunarMacroMapBaker.WriteAndAssign(body, asset, result);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Mute(asset, hash);
                Debug.LogException(e);
                return;
            }

            Seen done = SeenOf(asset);
            done.Hash = current;
            done.MutedHash = 0;
            done.ChangedAt = EditorApplication.timeSinceStartup;
            Debug.Log("[LunarMacroAutoBake] " + body.name + ": макро-карты готовы.");
        }

        private static void CancelWorker()
        {
            if (workerCancel != null && !workerCancel.IsCancellationRequested)
            {
                workerCancel.Cancel();
            }
        }

        /// <summary>A synchronous bake is about to write the same PNGs: abort the
        /// background one and forget it (its task may still unwind, but nothing reads
        /// its result any more).</summary>
        internal static void CancelForManualBake()
        {
            CancelWorker();
            worker = null;
            workerBody = null;
            workerAsset = null;
            workerHash = 0;
            workerCancel = null;
            EditorUtility.ClearProgressBar();
        }

        /// <summary>Do not retry a canceled/failed bake for THIS fingerprint: the next
        /// real edit (a new hash) clears the mute.</summary>
        private static void Mute(TerrainProfileAsset asset, long hash)
        {
            Seen seen = SeenOf(asset);
            seen.MutedHash = hash;
            seen.ChangedAt = EditorApplication.timeSinceStartup;
        }

        private static Seen SeenOf(TerrainProfileAsset asset)
        {
            if (!Profiles.TryGetValue(asset, out Seen seen))
            {
                seen = new Seen { ChangedAt = EditorApplication.timeSinceStartup };
                Profiles[asset] = seen;
            }

            return seen;
        }

        private static string PathOf(BodyAuthoring body)
        {
            if (body == null)
            {
                return string.Empty;
            }

            Transform t = body.transform;
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return (body.gameObject.scene.path ?? string.Empty) + ":" + path;
        }
    }
}
