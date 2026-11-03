using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace Simulation
{
    /// <summary>
    /// Loads and validates the native button mapping (button_mapping.json, Format B:
    /// {"button_1":"left","button_2":"right","button_3":"bump","button_4":"serve"}).
    ///
    /// The SAME file is handed to the native Sim_Initialize(path), so Unity and the native
    /// InputMapping always agree on the button IDs. A missing or invalid file is a hard error:
    /// there is no silent fallback to built-in defaults.
    ///
    /// Android: StreamingAssets live inside the APK (jar:file://...), which native fopen cannot
    /// read, so the file is read with UnityWebRequest and copied to persistentDataPath first.
    /// </summary>
    public sealed class ButtonMappingConfig
    {
        public const string FileName = "button_mapping.json";
        public static readonly string[] RequiredActions = { "left", "right", "bump", "serve" };

        /// <summary>Filesystem path that native code can open.</summary>
        public string NativePath { get; private set; }
        public string RawJson { get; private set; }
        public uint LeftId { get; private set; }
        public uint RightId { get; private set; }
        public uint BumpId { get; private set; }
        public uint ServeId { get; private set; }
        public IReadOnlyDictionary<string, uint> ActionToId => actionToId;

        private readonly Dictionary<string, uint> actionToId = new Dictionary<string, uint>(StringComparer.Ordinal);

        private static readonly Regex Entry = new Regex("\"button_(\\d+)\"\\s*:\\s*\"([A-Za-z_]+)\"", RegexOptions.Compiled);

        /// <summary>Parses + validates JSON text. Throws FormatException with a precise reason.</summary>
        public static ButtonMappingConfig Parse(string json, string nativePath)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("button mapping file is empty");
            var cfg = new ButtonMappingConfig { RawJson = json, NativePath = nativePath };
            var usedIds = new HashSet<uint>();
            foreach (Match m in Entry.Matches(json))
            {
                uint id = uint.Parse(m.Groups[1].Value);
                string action = m.Groups[2].Value.ToLowerInvariant();
                if (id == 0) throw new FormatException("button id 0 is reserved (ids start at 1)");
                if (!usedIds.Add(id)) throw new FormatException($"button id {id} is mapped twice");
                if (cfg.actionToId.ContainsKey(action)) throw new FormatException($"action '{action}' is mapped twice");
                cfg.actionToId[action] = id;
            }
            foreach (var a in RequiredActions)
            {
                if (!cfg.actionToId.ContainsKey(a)) throw new FormatException($"required action '{a}' is missing");
            }
            cfg.LeftId = cfg.actionToId["left"];
            cfg.RightId = cfg.actionToId["right"];
            cfg.BumpId = cfg.actionToId["bump"];
            cfg.ServeId = cfg.actionToId["serve"];
            return cfg;
        }

        public override string ToString() =>
            $"left={LeftId} right={RightId} bump={BumpId} serve={ServeId} (path {NativePath})";

        /// <summary>
        /// Resolves the mapping for the current platform. onDone receives (config, null) or (null, error).
        /// Runs synchronously on desktop/editor; uses a coroutine on Android.
        /// </summary>
        public static IEnumerator Load(Action<ButtonMappingConfig, string> onDone)
        {
            string streamingPath = Path.Combine(Application.streamingAssetsPath, FileName);
            string json = null;
            string nativePath = streamingPath;
            string error = null;

            if (streamingPath.Contains("://"))
            {
                // Android (jar:file://...) or WebGL: read via UnityWebRequest, then materialise a real file.
                using (var req = UnityWebRequest.Get(streamingPath))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        error = $"cannot read {streamingPath}: {req.error}";
                    }
                    else
                    {
                        json = req.downloadHandler.text;
                        try
                        {
                            nativePath = Path.Combine(Application.persistentDataPath, FileName);
                            File.WriteAllText(nativePath, json);
                        }
                        catch (Exception ex)
                        {
                            error = $"cannot write {nativePath}: {ex.Message}";
                        }
                    }
                }
            }
            else
            {
                try
                {
                    if (!File.Exists(streamingPath)) error = $"missing {streamingPath}";
                    else json = File.ReadAllText(streamingPath);
                }
                catch (Exception ex)
                {
                    error = $"cannot read {streamingPath}: {ex.Message}";
                }
            }

            if (error != null)
            {
                onDone(null, error);
                yield break;
            }

            ButtonMappingConfig cfg = null;
            try
            {
                cfg = Parse(json, nativePath);
            }
            catch (FormatException ex)
            {
                error = $"invalid {FileName}: {ex.Message}";
            }
            onDone(cfg, error);
        }
    }
}
