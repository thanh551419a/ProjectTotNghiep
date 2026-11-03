using System;
using Spike.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spike.EditorTools
{
    /// <summary>
    /// Batchmode entry point that opens the real gameplay scene and enters actual Editor Play Mode.
    /// Runtime development components (SceneCaptureProbe / GameplaySelfTestController) are activated
    /// by environment variables and end the run through DevRunControl.Finish. A watchdog exits with
    /// code 124 if nothing finishes in SPIKE_PLAYMODE_TIMEOUT seconds (default 300).
    ///
    /// SPIKE_PLAYMODE_CYCLES (default 1): number of consecutive Play Mode sessions in the same Editor
    /// process (repeated Play/Stop: verifies native shutdown/re-init, no stale table, no duplicates).
    ///
    /// Usage:
    ///   Unity.exe -batchmode -projectPath ... -executeMethod Spike.EditorTools.PlayModeBatchRunner.Run -logFile log.txt
    /// (do NOT pass -quit)
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeBatchRunner
    {
        public const string ScenePath = "Assets/Resources/New Scene.unity";
        private const string SessionKey = "Spike.PlayModeBatchRunner.Deadline";
        private const string ActiveKey = "Spike.PlayModeBatchRunner.Active";

        static PlayModeBatchRunner()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        private static void Hook()
        {
            EditorApplication.update -= Watchdog;
            EditorApplication.update += Watchdog;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static void Run()
        {
            string t = Environment.GetEnvironmentVariable("SPIKE_PLAYMODE_TIMEOUT");
            if (!float.TryParse(t, out float timeout) || timeout <= 0) timeout = 300f;
            string c = Environment.GetEnvironmentVariable("SPIKE_PLAYMODE_CYCLES");
            if (!int.TryParse(c, out int cycles) || cycles < 1) cycles = 1;

            SessionState.SetFloat(SessionKey, (float)EditorApplication.timeSinceStartup + timeout);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(DevRunControl.KeyCycleIndex, 1);
            SessionState.SetInt(DevRunControl.KeyCyclesRemaining, cycles - 1);
            SessionState.SetInt(DevRunControl.KeyWorstCode, 0);
            Hook();

            Debug.Log($"[PlayModeBatchRunner] Opening {ScenePath} and entering Play Mode (timeout {timeout}s, cycles {cycles})");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            int remaining = SessionState.GetInt(DevRunControl.KeyCyclesRemaining, 0);
            if (remaining > 0)
            {
                SessionState.SetInt(DevRunControl.KeyCyclesRemaining, remaining - 1);
                int next = SessionState.GetInt(DevRunControl.KeyCycleIndex, 1) + 1;
                SessionState.SetInt(DevRunControl.KeyCycleIndex, next);
                Debug.Log($"[PlayModeBatchRunner] Play Mode stopped; starting cycle {next}");
                EditorApplication.delayCall += EditorApplication.EnterPlaymode;
            }
            else if (Application.isBatchMode)
            {
                int worst = SessionState.GetInt(DevRunControl.KeyWorstCode, 0);
                SessionState.SetBool(ActiveKey, false);
                Debug.Log($"[PlayModeBatchRunner] All cycles done; exiting with {worst}");
                EditorApplication.Exit(worst);
            }
        }

        private static void Watchdog()
        {
            float deadline = SessionState.GetFloat(SessionKey, 0f);
            if (deadline <= 0f) return;
            if (EditorApplication.timeSinceStartup > deadline)
            {
                SessionState.EraseFloat(SessionKey);
                Debug.LogError("[PlayModeBatchRunner] TIMEOUT - no runtime test component finished in time.");
                EditorApplication.Exit(124);
            }
        }
    }
}
