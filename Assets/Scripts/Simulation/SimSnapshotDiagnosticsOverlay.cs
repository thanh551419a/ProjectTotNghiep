using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Native.Snapshot;

namespace Simulation
{
    /// <summary>
    /// Development diagnostics for the complete authoritative snapshot received from C++.
    ///   F1 : toggle overlay (contract status, validation counters, entity/state summary,
    ///        tick-scoped intents, landing prediction).
    ///   F2 : dump the complete received snapshot of the current tick (raw native JSON +
    ///        flattened field list + validation report) to Application.persistentDataPath.
    /// Read-only: never writes to the simulation.
    /// </summary>
    public class SimSnapshotDiagnosticsOverlay : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;
        [SerializeField] private KeyCode dumpKey = KeyCode.F2;

        private SpikeSimulationManager manager;
        private Vector2 scroll;
        private string lastDumpPath;

        private void Awake()
        {
            manager = GetComponent<SpikeSimulationManager>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
            if (Input.GetKeyDown(dumpKey)) lastDumpPath = DumpCurrentSnapshot();
            // Diagnostics-only JSON fetch: never part of the per-tick gameplay path unless the overlay is shown.
            if (visible && manager != null) manager.RefreshDiagnosticsSnapshot();
        }

        /// <summary>Writes the complete current snapshot to disk; returns the file path (or null).</summary>
        public string DumpCurrentSnapshot()
        {
            if (manager == null) manager = GetComponent<SpikeSimulationManager>();
            if (manager != null) manager.RefreshDiagnosticsSnapshot();
            var snap = manager != null ? manager.DecodedSnapshot : null;
            if (snap == null || snap.RawJson == null)
            {
                Debug.LogWarning("[SimSnapshotDiagnostics] No decoded snapshot available to dump.");
                return null;
            }

            string dir = Path.Combine(Application.persistentDataPath, "SimSnapshots");
            Directory.CreateDirectory(dir);
            string basePath = Path.Combine(dir, $"snapshot_tick{snap.Tick:D6}");
            File.WriteAllText(basePath + ".json", snap.RawJson, new UTF8Encoding(false));

            var sb = new StringBuilder();
            sb.AppendLine("# validation: " + snap.Validation.Summary());
            foreach (var e in snap.Validation.Errors) sb.AppendLine("# error: " + e);
            foreach (var u in snap.Validation.UnknownFields) sb.AppendLine("# unknown: " + u);
            foreach (var m in snap.Validation.MissingFields) sb.AppendLine("# missing: " + m);
            foreach (var w in snap.Validation.Warnings) sb.AppendLine("# warning: " + w);
            foreach (var kv in SimJson.Flatten(snap.Root)) sb.Append(kv.Key).Append(" = ").AppendLine(SimJson.LeafToString(kv.Value));
            File.WriteAllText(basePath + ".fields.txt", sb.ToString(), new UTF8Encoding(false));

            Debug.Log($"[SimSnapshotDiagnostics] Dumped complete snapshot tick {snap.Tick} to {basePath}.json (+ .fields.txt)");
            return basePath + ".json";
        }

        private void OnGUI()
        {
            if (!visible || manager == null) return;
            var snap = manager.DecodedSnapshot;
            var schema = manager.SnapshotSchema;

            GUILayout.BeginArea(new Rect(10, 120, 560, Screen.height - 180), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label($"<b>Native contract</b>: {(manager.ApiV2Available ? "API v2" : "UNAVAILABLE")} ({manager.NativeApiStatus})", Rich());
            GUILayout.Label($"Decode failures: {manager.DecodeFailureCount}  validation issues: {manager.ValidationIssueCount}  " +
                            $"v1 cross-check mismatches: {manager.CrossCheckMismatchCount}  tick mismatches: {manager.TickMismatchCount}");
            if (manager.LastCrossCheck != null) GUILayout.Label("Last v1 cross-check: " + manager.LastCrossCheck);
            if (lastDumpPath != null) GUILayout.Label("Last dump: " + lastDumpPath);

            if (snap == null)
            {
                GUILayout.Label("No decoded snapshot.");
            }
            else
            {
                GUILayout.Label($"<b>Tick {snap.Tick}</b>  schema {snap.Schema} v{snap.SchemaVersion} api {snap.ApiVersion}  bytes {snap.RawJson.Length}", Rich());
                GUILayout.Label("Validation: " + snap.Validation.Summary());
                foreach (var u in snap.Validation.UnknownFields.Take(5)) GUILayout.Label("  unknown: " + u);
                foreach (var m in snap.Validation.MissingFields.Take(5)) GUILayout.Label("  missing: " + m);
                foreach (var e in snap.Validation.Errors.Take(5)) GUILayout.Label("  error: " + e);

                if (snap.TryGetMatchView(snap.MatchId, out var mv))
                {
                    string team(int v) => schema != null && schema.Enums.TryGetValue("Team", out var en) ? en.NameOf(v) : v.ToString();
                    GUILayout.Label($"<b>Match {mv.MatchId}</b> score {mv.LeftScore}-{mv.RightScore} serving {team(mv.ServingTeam)} " +
                                    $"(L#{mv.LeftServingEntity} R#{mv.RightServingEntity}) finished {mv.Finished} winner {team(mv.MatchWinner)}", Rich());
                    GUILayout.Label($"Rally: stateFrame {mv.StateFrame} landingX {mv.LandingX:F1} collisionState {mv.CollisionState} " +
                                    $"lastTouch {mv.LastTouch} touchCount {mv.TouchCount}");
                }

                string action(int v) => schema != null && schema.Enums.TryGetValue("ActionState", out var en) ? en.NameOf(v) : v.ToString();
                string intent(int v) => schema != null && schema.Enums.TryGetValue("FinalIntent", out var en) ? en.NameOf(v) : v.ToString();
                foreach (var e in snap.Entities)
                {
                    if (snap.TryGetCharacter(e.Id, out var c))
                    {
                        GUILayout.Label($"#{e.Id} {e.Role} {e.Team}/{e.Controller} pos {c.Position} size {c.Size} jump {c.JumpRemainingFrames} " +
                                        $"status {action(c.ActionStatus)} {c.ActionRemainTimeMs:F1}ms" +
                                        (c.HasIntent ? $" intent {intent(c.IntentFinal)} moveX {c.IntentMoveX:F2}" : "") +
                                        (c.HasVelocityIntent ? $" vel ({c.VelocityVx:F2},{c.VelocityVy:F2})" : ""));
                    }
                    else if (snap.TryGetBall(e.Id, out var b))
                    {
                        GUILayout.Label($"#{e.Id} {e.Role} pos {b.Position} traj type {b.TrajectoryType} speed {b.Speed:F2} a {b.A:G6} b {b.B:F2} c {b.C:F2}");
                    }
                    else
                    {
                        GUILayout.Label($"#{e.Id} {e.Role} {e.Team}/{e.Controller} components: {(e.Components.Count == 0 ? "none (absent, not zero)" : string.Join(",", e.Components.Keys))}");
                    }
                }
                GUILayout.Label($"Intents (lifetime {snap.Intents.Lifetime}, producedByTick {(snap.Intents.ProducedByTick.HasValue ? snap.Intents.ProducedByTick.Value.ToString() : "null")}): " +
                                string.Join(", ", snap.Intents.Pools.Select(p => $"{p.Key}={p.Value.Count}")));
                GUILayout.Label("F2: dump complete snapshot");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static GUIStyle rich;
        private static GUIStyle Rich()
        {
            if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true };
            return rich;
        }
    }
}
