using UnityEngine;

namespace Spike.Diagnostics
{
    /// <summary>
    /// Development-only run control shared by the runtime test components and the Editor batch
    /// runner. Supports several consecutive Play Mode cycles (repeated Play/Stop verification).
    /// </summary>
    public static class DevRunControl
    {
        public const string KeyCycleIndex = "Spike.DevRun.CycleIndex";
        public const string KeyCyclesRemaining = "Spike.DevRun.CyclesRemaining";
        public const string KeyWorstCode = "Spike.DevRun.WorstCode";

        /// <summary>1-based index of the current Play Mode cycle (1 when not run by the batch runner).</summary>
        public static int CycleIndex
        {
            get
            {
#if UNITY_EDITOR
                return Mathf.Max(1, UnityEditor.SessionState.GetInt(KeyCycleIndex, 1));
#else
                return 1;
#endif
            }
        }

        public static bool MoreCyclesPending
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.SessionState.GetInt(KeyCyclesRemaining, 0) > 0;
#else
                return false;
#endif
            }
        }

        /// <summary>Ends the current run. In the Editor either stops Play Mode (more cycles) or exits.</summary>
        public static void Finish(int exitCode, string who)
        {
            Debug.Log($"[DevRunControl] {who} finished with code {exitCode} (cycle {CycleIndex}).");
#if UNITY_EDITOR
            int worst = Mathf.Max(UnityEditor.SessionState.GetInt(KeyWorstCode, 0), exitCode);
            UnityEditor.SessionState.SetInt(KeyWorstCode, worst);
            if (MoreCyclesPending)
            {
                UnityEditor.EditorApplication.ExitPlaymode();
                return;
            }
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(worst);
            else UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit(exitCode);
#endif
        }
    }
}
