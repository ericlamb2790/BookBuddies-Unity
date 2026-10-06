using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// In the editor, writes one line to the console (and Editor.log) every 10 seconds: frame rate, the slowest frame,
    /// draw calls and garbage per frame, and the scripts and engine steps that took the most time.
    /// Temporary, for finding slow spots in v0.4.
    /// </summary>
    public sealed class PerfLog : MonoBehaviour
    {
        const float Every = 10;
        static readonly string[] Counters = { "Main Thread", "Batches Count", "SetPass Calls Count", "Triangles Count", "GC Allocated In Frame" };

        sealed class Watched { public string Name; public ProfilerRecorder Rec; public ProfilerMarkerDataUnit Unit; public double Sum; }
        readonly List<Watched> watched = new List<Watched>();
        readonly HashSet<string> names = new HashSet<string>();
        int frames;
        float since, worst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (!Application.isEditor) return;
            var go = new GameObject("perf log") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<PerfLog>();
        }

        // the counters above, every script's Update-style call, and the engine's canvas, rendering and editor steps
        void Watch()
        {
            var all = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(all);
            foreach (var h in all)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                string n = d.Name;
                if (names.Contains(n)) continue;
                bool timed = d.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds &&
                    (n.EndsWith("[Invoke]") || n.StartsWith("Canvas.") || n.StartsWith("Gfx.") || n.StartsWith("Render") ||
                     n.StartsWith("Camera.") || n.StartsWith("Culling") || n.StartsWith("Update.") || n.StartsWith("PreLateUpdate.") ||
                     n.StartsWith("PostLateUpdate.") || n == "EditorLoop" || n == "PlayerLoop" || n == "GC.Collect");
                if (!timed && System.Array.IndexOf(Counters, n) < 0) continue;
                names.Add(n);
                watched.Add(new Watched { Name = n, Unit = d.UnitType, Rec = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately) });
            }
        }

        void Start() => Watch();

        void Update()
        {
            frames++;
            since += Time.unscaledDeltaTime;
            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            foreach (var w in watched) if (w.Rec.Valid && w.Rec.Count > 0) w.Sum += w.Rec.LastValue;
            if (since < Every) return;

            string Avg(string k) { var w = watched.Find(x => x.Name == k); return w == null ? "?" : Show(w.Sum / frames, w.Unit); }
            var top = watched.Where(w => w.Unit == ProfilerMarkerDataUnit.TimeNanoseconds && System.Array.IndexOf(Counters, w.Name) < 0)
                .OrderByDescending(w => w.Sum).Take(14)
                .Select(w => $"{w.Name.Replace(" [Invoke]", "")} {w.Sum / frames / 1e6:0.00}");
            Debug.Log($"BookBuddies perf: {frames / since:0} fps, slowest {worst * 1000:0} ms, main thread {Avg("Main Thread")}, " +
                      $"batches {Avg("Batches Count")}, setpass {Avg("SetPass Calls Count")}, tris {Avg("Triangles Count")}, " +
                      $"garbage {Avg("GC Allocated In Frame")}/frame, objects {FindObjectsByType<Transform>(FindObjectsSortMode.None).Length}\n" +
                      "  ms per frame: " + string.Join(" | ", top));

            foreach (var w in watched) w.Sum = 0;
            frames = 0;
            since = worst = 0;
            Watch(); // scripts that loaded since
        }

        static string Show(double v, ProfilerMarkerDataUnit unit) =>
            unit == ProfilerMarkerDataUnit.TimeNanoseconds ? $"{v / 1e6:0.0} ms" : unit == ProfilerMarkerDataUnit.Bytes ? $"{v / 1024:0} KB" : $"{v:0}";

        void OnDestroy()
        {
            foreach (var w in watched) w.Rec.Dispose();
        }
    }
}
