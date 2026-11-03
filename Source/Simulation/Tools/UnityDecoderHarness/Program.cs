// =====================================================================
// UnityDecoderHarness - Program.cs
// Verifies the Unity-side managed decoder (linked C# sources) against the
// real native library, outside Unity. Writes Diagnostics/U01_managed_decoder_contract.json.
//
// Usage: UnityDecoderHarness <repoRoot>
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Native;
using Native.Snapshot;
using Assets.Table;
using Simulation;

internal static class Program
{
    private const float Dt = 1f / 60f;
    private static readonly List<string> Failures = new List<string>();
    private static readonly StringBuilder Report = new StringBuilder();
    private static SimSnapshotSchema schema;

    private static int Main(string[] args)
    {
        string repo = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", ".."));
        string diagDir = Path.Combine(repo, "Diagnostics");
        string mapping = Path.Combine(repo, "Unity", "TheSpike", "My project", "Assets", "StreamingAssets", "InputMapping.json");
        Console.WriteLine($"repo={repo}");

        var results = new List<string>();

        // ---------------- schema ----------------
        if (SpikeNativeJson.GetSchemaJson(out string schemaJson) != SpikeNativeBridge.SIM_OK) { Console.WriteLine("schema read failed"); return 2; }
        schema = SimSnapshotSchema.Parse(schemaJson);
        var selfCheck = schema.SelfCheck();
        results.Add(Section("schema", selfCheck.Count == 0,
            $"\"types\":{schema.TypeList.Count},\"pools\":{schema.Pools.Count},\"enums\":{schema.Enums.Count},\"problems\":{JArr(selfCheck)}"));

        bool probe = SpikeNativeJson.ProbeApiV2(out string probeDetail);
        results.Add(Section("apiProbe", probe, $"\"detail\":{J(probeDetail)}"));

        // ---------------- init ----------------
        SpikeNativeBridge.Sim_SetLegacyLogEnabled(0);
        if (!SpikeNativeBridge.Sim_Initialize(File.Exists(mapping) ? mapping : null)) { Console.WriteLine("init failed"); return 2; }
        SpikeNativeBridge.Sim_SeedRandom(1);

        string initJson = Fetch();
        var initDecoded = SimSnapshotDecoder.Decode(initJson, schema);
        results.Add(Section("initialSnapshot", initDecoded.Validation.Ok && initDecoded.Tick == 0,
            $"\"bytes\":{Encoding.UTF8.GetByteCount(initJson)},\"validation\":{J(initDecoded.Validation.Summary())},\"producedByTickIsNull\":{B(initDecoded.Intents.ProducedByTick == null)},\"intentEntries\":{initDecoded.Intents.TotalEntries}"));

        // ---------------- Get_data P/Invoke Contract Test ----------------
        var testInput = NativeSimInput.Empty;
        NativeSimData snapData = SpikeNativeBridge.Get_data(testInput);
        bool getDataOk = snapData.status == 0 &&
                         snapData.entityCount == 4 &&
                         snapData.entity0.entityId == 0 &&
                         snapData.entity1.entityId == 3 &&
                         snapData.entity2.entityId == 7 &&
                         snapData.entity3.entityId == 6 &&
                         snapData.entity0.flags.HasPosition &&
                         snapData.entity0.RoleName == "PLAYER" &&
                         snapData.entity2.RoleName == "BALL";
        Console.WriteLine($"[TEST] Get_data returned status={snapData.status}, tick={snapData.tick}, entities={snapData.entityCount}, E0={snapData.entity0.RoleName}(id={snapData.entity0.entityId}, pos={snapData.entity0.position.position.x},{snapData.entity0.position.position.y}), E2={snapData.entity2.RoleName}(id={snapData.entity2.entityId}, pos={snapData.entity2.position.position.x},{snapData.entity2.position.position.y})");
        results.Add(Section("getDataContract", getDataOk, $"\"status\":{snapData.status},\"tick\":{snapData.tick},\"entityCount\":{snapData.entityCount},\"p0Role\":{J(snapData.entity0.RoleName)},\"ballRole\":{J(snapData.entity2.RoleName)}"));
        SpikeNativeBridge.Sim_Reset();

        // ---------------- buffer protocol ----------------
        results.Add(BufferProtocolTest());

        // ---------------- scenarios through the real API ----------------
        var stats = new Stats();
        results.Add(RunScenario("A_scripted_and_reactive", ScenarioReactive, 2400, stats, out string hashA));
        SpikeNativeBridge.Sim_Reset();
        string afterReset = Fetch();
        results.Add(Section("resetConsistency", string.Equals(afterReset, initJson, StringComparison.Ordinal),
            $"\"byteIdenticalToInitial\":{B(string.Equals(afterReset, initJson, StringComparison.Ordinal))}"));
        SpikeNativeBridge.Sim_SeedRandom(1);
        results.Add(RunScenario("A_repeat_for_determinism", ScenarioReactive, 2400, new Stats(), out string hashA2));
        results.Add(Section("determinism", hashA == hashA2, $"\"sha256_run1\":{J(hashA)},\"sha256_run2\":{J(hashA2)}"));
        SpikeNativeBridge.Sim_Reset();
        results.Add(RunScenario("B_serve_only_until_victory_and_frozen", ScenarioServeOnly, 3600, stats, out _));
        results.Add(Section("observedStates", stats.Finished && stats.StateFrames.Contains("alive") && stats.StateFrames.Contains("countdown") && stats.StateFrames.Contains("waitServe"),
            stats.ToJson()));

        // ---------------- corpus: every snapshot record of the Phase 2 runs ----------------
        results.Add(CorpusTest(diagDir));

        // ---------------- mutation tests (validator must detect) ----------------
        results.Add(MutationTests(initJson, afterReset));

        // ---------------- Mandatory Data Table & Entity Pipeline Tests (A - E) ----------------
        RunDataTableIntegrationTests(results);

        // ---------------- shutdown contract ----------------
        SpikeNativeBridge.Sim_Shutdown();
        int rcAfter = SpikeNativeBridge.Sim_GetSnapshotJson(null, 0, out int reqAfter);
        string err = SpikeNativeJson.GetLastError();
        results.Add(Section("notInitializedAfterShutdown", rcAfter == SpikeNativeBridge.SIM_ERR_NOT_INITIALIZED && reqAfter == 0 && err.Length > 0,
            $"\"rc\":{rcAfter},\"required\":{reqAfter},\"lastError\":{J(err)}"));

        bool pass = Failures.Count == 0;
        string json = "{\n  \"test\":\"U01_managed_decoder_contract\",\n  \"decoderSources\":\"Unity/TheSpike/My project/Assets/Scripts/Native/Snapshot/*.cs (linked, C# 9)\",\n" +
                      $"  \"nativeLibrary\":\"MyGame/build_native/Release/SpikeCrossCore.dll\",\n  \"pass\":{B(pass)},\n  \"failures\":{JArr(Failures.Take(100))},\n  \"sections\":[\n    " +
                      string.Join(",\n    ", results) + "\n  ]\n}\n";
        Directory.CreateDirectory(diagDir);
        File.WriteAllText(Path.Combine(diagDir, "U01_managed_decoder_contract.json"), json, new UTF8Encoding(false));
        Console.WriteLine(json.Length > 6000 ? json.Substring(0, 6000) + "..." : json);
        Console.WriteLine(pass ? "U01 PASS" : $"U01 FAIL ({Failures.Count})");
        return pass ? 0 : 1;
    }

    // ------------------------------------------------------------------
    private static string Fetch()
    {
        int rc = SpikeNativeJson.GetSnapshotJson(out string json);
        if (rc != SpikeNativeBridge.SIM_OK) throw new Exception($"Sim_GetSnapshotJson rc={rc} err={SpikeNativeJson.GetLastError()}");
        return json;
    }

    private delegate (uint mask, float axis) Controller(int tick, SimSnapshotData prev);

    private sealed class Stats
    {
        public readonly SortedSet<string> StateFrames = new SortedSet<string>();
        public readonly SortedSet<string> Statuses = new SortedSet<string>();
        public readonly SortedSet<string> FinalIntents = new SortedSet<string>();
        public int MaxTouch;
        public int MaxLeft, MaxRight;
        public bool Finished;
        public string Winner = "NONE";
        public int VelocityIntents;
        public int NonFinite;
        public int Snapshots;
        public long Leaves;
        public int Warnings;

        public void Observe(SimSnapshotData d)
        {
            Snapshots++;
            Leaves += d.Validation.LeafFieldsValidated;
            NonFinite += d.Validation.NonFiniteFloats;
            Warnings += d.Validation.Warnings.Count;
            if (d.TryGetMatchView(d.MatchId, out var m))
            {
                StateFrames.Add(m.StateFrame == -1 ? "alive" : m.StateFrame == 0 ? "waitServe" : "countdown");
                MaxTouch = Math.Max(MaxTouch, (int)m.TouchCount);
                MaxLeft = Math.Max(MaxLeft, m.LeftScore);
                MaxRight = Math.Max(MaxRight, m.RightScore);
                if (m.Finished) { Finished = true; Winner = schema.Enums["Team"].NameOf(m.MatchWinner); }
            }
            for (int i = 0; i < 6; i++)
            {
                if (!d.TryGetCharacter(i, out var c)) continue;
                if (c.HasAction) Statuses.Add($"e{i}:{schema.Enums["ActionState"].NameOf(c.ActionStatus)}");
                if (c.HasIntent) FinalIntents.Add($"e{i}:{schema.Enums["FinalIntent"].NameOf(c.IntentFinal)}");
                if (c.HasVelocityIntent) VelocityIntents++;
            }
        }

        public string ToJson() =>
            $"\"snapshots\":{Snapshots},\"leavesValidated\":{Leaves},\"stateFrames\":{JArr(StateFrames)},\"actionStatuses\":{JArr(Statuses)}," +
            $"\"finalIntents\":{JArr(FinalIntents)},\"maxTouchCount\":{MaxTouch},\"maxLeftScore\":{MaxLeft},\"maxRightScore\":{MaxRight}," +
            $"\"finished\":{B(Finished)},\"winner\":{J(Winner)},\"velocityIntentEntries\":{VelocityIntents},\"nonFiniteFloats\":{NonFinite},\"validationWarnings\":{Warnings}";
    }

    /// <summary>Serve-only player (same plan as Phase 2 S09): press Serve when the LEFT team may serve.</summary>
    private static (uint, float) ScenarioServeOnly(int t, SimSnapshotData prev)
    {
        if (prev == null || !prev.TryGetMatchView(prev.MatchId, out var m)) return ((t & 1) == 0 ? SimButton.Serve : SimButton.None, 0f);
        if (m.StateFrame == 0 && m.ServingTeam == SimTeam.Left) return ((t & 1) == 0 ? SimButton.Serve : SimButton.None, 0f);
        return (SimButton.None, 0f);
    }

    /// <summary>
    /// Input controller acting like a player who reads the (decoded) snapshot:
    /// scripted movement/jump first, then serve when allowed and play the ball
    /// with Bump / Set / SpikeMedium / JumpSpike in rotation. Valid game input only.
    /// </summary>
    private static (uint, float) ScenarioReactive(int t, SimSnapshotData prev)
    {
        if (t < 10) return (SimButton.None, 0f);
        if (t < 40) return (SimButton.MoveRight, 1f);
        if (t == 40) return (SimButton.JumpSpike, 0f);
        if (t < 75) return (SimButton.None, 0f);
        if (t < 100) return (SimButton.MoveLeft, -1f);
        if (prev == null || !prev.TryGetMatchView(prev.MatchId, out var m)) return (SimButton.None, 0f);
        if (m.StateFrame == 0 && m.ServingTeam == SimTeam.Left) return ((t & 1) == 0 ? SimButton.Serve : SimButton.None, 0f);
        if (m.StateFrame != -1) return (SimButton.None, 0f);
        if (!prev.TryGetCharacter(0, out var p) || !prev.TryGetBall(prev.FindBallEntityId(), out var b)) return (SimButton.None, 0f);
        float playerCenter = p.Position.X + p.Size.X * 0.5f;
        float ballCenter = b.Position.X + 8.4f;
        uint mask = SimButton.None;
        float axis = 0f;
        if (b.Position.X < 1370f && m.LandingX > 0f && m.LandingX < 1370f)
        {
            float target = m.LandingX - 20f;
            if (target < playerCenter - 10f) { mask |= SimButton.MoveLeft; axis = -1f; }
            else if (target > playerCenter + 10f) { mask |= SimButton.MoveRight; axis = 1f; }
        }
        bool near = Math.Abs(ballCenter - playerCenter) < 90f && b.Position.Y < p.Position.Y + p.Size.Y * 1.6f;
        if (near && (t & 1) == 0)
        {
            switch ((t / 2) % 4)
            {
                case 0: mask |= SimButton.Bump; break;
                case 1: mask |= SimButton.Set; break;
                case 2: mask |= SimButton.SpikeMedium; break;
                default: mask |= SimButton.JumpSpike; break;
            }
        }
        return (mask, axis);
    }

    private static string RunScenario(string name, Controller ctl, int ticks, Stats stats, out string sha)
    {
        var roundTrip = new SimComparisonResult();
        var typed = new SimComparisonResult();
        var legacy = new SimComparisonResult();
        int invalid = 0, unknown = 0, missing = 0, tickMismatch = 0, warnings = 0;
        var firstProblems = new List<string>();
        SimSnapshotData prev = null;
        using var hasher = SHA256.Create();
        int maxBytes = 0;

        for (int t = 0; t < ticks; t++)
        {
            var (mask, axis) = ctl(t, prev);
            SpikeNativeBridge.Sim_SetInput(0, mask, axis);
            SpikeNativeBridge.Sim_Update(Dt);
            string json = Fetch();
            SpikeNativeBridge.Sim_GetSnapshot(out SimSnapshot v1);
            ulong nativeTick = SpikeNativeBridge.Sim_GetTick();
            maxBytes = Math.Max(maxBytes, Encoding.UTF8.GetByteCount(json));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            hasher.TransformBlock(bytes, 0, bytes.Length, null, 0);

            var d = SimSnapshotDecoder.Decode(json, schema);
            if (!d.Validation.Ok)
            {
                invalid++;
                unknown += d.Validation.UnknownFields.Count;
                missing += d.Validation.MissingFields.Count;
                if (firstProblems.Count < 10)
                    firstProblems.AddRange(d.Validation.Errors.Concat(d.Validation.UnknownFields).Concat(d.Validation.MissingFields).Take(5).Select(s => $"t{t}: {s}"));
            }
            warnings += d.Validation.Warnings.Count;
            if (d.Tick != nativeTick) tickMismatch++;
            SimSnapshotComparer.CompareRoundTrip(d, roundTrip);
            SimSnapshotComparer.CompareTypedViews(d, typed);
            SimSnapshotComparer.CompareWithLegacyStruct(d, v1, legacy);
            stats.Observe(d);
            prev = d;
        }
        hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        sha = BitConverter.ToString(hasher.Hash).Replace("-", "");

        bool ok = invalid == 0 && tickMismatch == 0 && roundTrip.Ok && typed.Ok && legacy.Ok;
        return Section("scenario_" + name, ok,
            $"\"ticks\":{ticks},\"maxSnapshotBytes\":{maxBytes},\"invalidSnapshots\":{invalid},\"unknownFields\":{unknown},\"missingFields\":{missing}," +
            $"\"validationWarnings\":{warnings},\"tickMismatch\":{tickMismatch},\"roundTrip\":{J(roundTrip.ToString())},\"typedVsRaw\":{J(typed.ToString())}," +
            $"\"v1StructCrossCheck\":{J(legacy.ToString())},\"firstProblems\":{JArr(firstProblems.Concat(roundTrip.Mismatches).Concat(typed.Mismatches).Concat(legacy.Mismatches).Take(15))}");
    }

    private static string BufferProtocolTest()
    {
        var notes = new List<string>();
        bool ok = true;
        int rc = SpikeNativeBridge.Sim_GetSnapshotJson(null, 0, out int required);
        if (rc != SpikeNativeBridge.SIM_ERR_BUFFER_TOO_SMALL || required <= 1) { ok = false; notes.Add($"size query rc={rc} required={required}"); }

        var small = Enumerable.Repeat((byte)0xAA, required - 1).ToArray();
        rc = SpikeNativeBridge.Sim_GetSnapshotJson(small, small.Length, out int required2);
        if (rc != SpikeNativeBridge.SIM_ERR_BUFFER_TOO_SMALL || required2 != required || small.Any(x => x != 0xAA)) { ok = false; notes.Add($"capacity=required-1 rc={rc} touched={small.Any(x => x != 0xAA)}"); }

        var exact = new byte[required];
        rc = SpikeNativeBridge.Sim_GetSnapshotJson(exact, exact.Length, out int required3);
        if (rc != SpikeNativeBridge.SIM_OK || exact[required - 1] != 0 || exact.Take(required - 1).Any(x => x == 0)) { ok = false; notes.Add($"capacity=required rc={rc}"); }

        rc = SpikeNativeBridge.Sim_GetSnapshotJson(exact, -1, out _);
        if (rc != SpikeNativeBridge.SIM_ERR_INVALID_ARGUMENT) { ok = false; notes.Add($"negative capacity rc={rc}"); }

        string viaReader;
        byte[] tiny = new byte[8];
        int rr = SpikeNativeJson.Read(SpikeNativeBridge.Sim_GetSnapshotJson, ref tiny, out viaReader);
        string direct = Encoding.UTF8.GetString(exact, 0, required - 1);
        if (rr != SpikeNativeBridge.SIM_OK || viaReader != direct) { ok = false; notes.Add("retry reader result differs"); }

        return Section("bufferProtocol", ok, $"\"requiredBytes\":{required},\"notes\":{JArr(notes)}");
    }

    private static string CorpusTest(string diagDir)
    {
        int files = 0, records = 0, invalid = 0;
        long leaves = 0;
        int nonFinite = 0;
        var problems = new List<string>();
        foreach (var path in Directory.GetFiles(diagDir, "S*.jsonl").OrderBy(p => p, StringComparer.Ordinal))
        {
            files++;
            int lineNo = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNo++;
                if (line.IndexOf("\"type\":\"snapshot\"", StringComparison.Ordinal) < 0) continue;
                var rec = SimJson.Parse(line) as JsonObject;
                var data = rec?["data"];
                if (data == null) continue;
                records++;
                string text = SimSnapshotComparer.Serialize(data);
                var d = SimSnapshotDecoder.Decode(text, schema);
                leaves += d.Validation.LeafFieldsValidated;
                nonFinite += d.Validation.NonFiniteFloats;
                if (!d.Validation.Ok)
                {
                    invalid++;
                    if (problems.Count < 10)
                        problems.AddRange(d.Validation.Errors.Concat(d.Validation.UnknownFields).Concat(d.Validation.MissingFields).Take(3)
                            .Select(s => $"{Path.GetFileName(path)}:{lineNo}: {s}"));
                }
            }
        }
        return Section("phase2Corpus", records > 0 && invalid == 0,
            $"\"files\":{files},\"snapshotRecords\":{records},\"invalid\":{invalid},\"leavesValidated\":{leaves},\"nonFiniteFloats\":{nonFinite},\"problems\":{JArr(problems)}");
    }

    private static string MutationTests(string validJson, string afterReset)
    {
        var cases = new List<(string name, string text, Func<SimSnapshotData, bool> detected)>
        {
            ("unknownComponentField", ReplaceFirst(validJson, "\"characterPositionPool\":{", "\"characterPositionPool\":{\"bogus\":1,"),
                d => d.Validation.UnknownFields.Any(u => u.EndsWith("characterPositionPool.bogus", StringComparison.Ordinal))),
            ("missingStateField", ReplaceFirstRegex(validJson, ",\"remainTime\":[^,}]+", ""),
                d => d.Validation.MissingFields.Any(u => u.EndsWith("characterActionStatePool.remainTime", StringComparison.Ordinal))),
            ("enumOutOfRange", ReplaceFirstRegex(validJson, "\"status\":\\d+", "\"status\":99"),
                d => d.Validation.Errors.Any(e => e.Contains("outside enum ActionState"))),
            ("unknownPool", ReplaceFirst(validJson, "\"components\":{", "\"components\":{\"fooPool\":{\"x\":1},"),
                d => d.Validation.UnknownFields.Any(u => u.Contains("fooPool"))),
            ("absentComponentVsPoolTable", ReplaceFirstRegex(validJson, ",\"characterSizePool\":\\{\"size\":\\{\"x\":[^}]+\\}\\}", ""),
                d => d.Validation.Errors.Any(e => e.Contains("characterSizePool")) && d.TryGetCharacter(0, out var c) && !c.HasSize),
            ("unknownWorldField", ReplaceFirst(validJson, "\"physics\":{", "\"physics\":{\"extra\":1,"),
                d => d.Validation.UnknownFields.Any(u => u == "world.physics.extra")),
            ("schemaVersionMismatch", ReplaceFirst(validJson, "\"schemaVersion\":1", "\"schemaVersion\":2"),
                d => d.Validation.Errors.Any(e => e.Contains("schemaVersion"))),
            ("truncatedPayload", validJson.Substring(0, validJson.Length / 2),
                d => d.Validation.Errors.Any(e => e.StartsWith("parse error", StringComparison.Ordinal))),
            ("unknownTopLevel", ReplaceFirst(validJson, "{\"schema\":", "{\"newSection\":{},\"schema\":"),
                d => d.Validation.UnknownFields.Contains("newSection")),
            ("wrongKind", ReplaceFirst(validJson, "\"finished\":false", "\"finished\":0"),
                d => d.Validation.Errors.Any(e => e.EndsWith("matchStatePool.finished: expected bool", StringComparison.Ordinal))),
        };

        var lines = new List<string>();
        bool all = true;
        foreach (var (name, text, detected) in cases)
        {
            bool mutated = !string.Equals(text, validJson, StringComparison.Ordinal);
            var d = SimSnapshotDecoder.Decode(text, schema);
            bool hit = mutated && !d.Validation.Ok && detected(d);
            all &= hit;
            lines.Add($"{{\"case\":{J(name)},\"mutated\":{B(mutated)},\"detected\":{B(hit)},\"report\":{J(d.Validation.Summary())}}}");
        }
        var baseline = SimSnapshotDecoder.Decode(validJson, schema);
        all &= baseline.Validation.Ok;
        return Section("validatorMutationTests", all, $"\"baselineValid\":{B(baseline.Validation.Ok)},\"cases\":[{string.Join(",", lines)}]");
    }

    private static string ReplaceFirst(string s, string find, string repl)
    {
        int i = s.IndexOf(find, StringComparison.Ordinal);
        return i < 0 ? s : s.Substring(0, i) + repl + s.Substring(i + find.Length);
    }

    private static string ReplaceFirstRegex(string s, string pattern, string repl) => new Regex(pattern).Replace(s, repl, 1);

    // ------------------------------------------------------------------
    private static string Section(string name, bool ok, string body)
    {
        if (!ok) Failures.Add(name);
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
        return $"{{\"name\":{J(name)},\"pass\":{B(ok)},{body}}}";
    }

    private static string B(bool b) => b ? "true" : "false";

    private static string J(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    private static string JArr(IEnumerable<string> items) => "[" + string.Join(",", items.Select(J)) + "]";

    private static void RunDataTableIntegrationTests(List<string> results)
    {
        Console.WriteLine("\n========================================================");
        Console.WriteLine("RUNNING MANDATORY DATA TABLE INTEGRATION TESTS (A - E)");
        Console.WriteLine("========================================================");

        // ----------------------------------------------------
        // TEST A - Table Creation and Initialization
        // ----------------------------------------------------
        var table = SimulationDataTable.GetOrCreateInstance();
        bool testA_exists = table != null;
        table.ResetTable();
        bool testA_emptyBeforeInit = !table.IsInitialized && table.CurrentTick == 0 && table.EntityCount == 0;

        // Explicit handling of invalid snapshot
        var invalidSnap = new NativeSimData { status = -1, entityCount = 0 };
        bool testA_rejectInvalid = !table.UpdateFromNativeSnapshot(invalidSnap);

        bool testA_ok = testA_exists && testA_emptyBeforeInit && testA_rejectInvalid;
        Console.WriteLine($"[TEST A] Table Creation & Init: ok={testA_ok}");
        results.Add(Section("TestA_TableInit", testA_ok,
            $"\"tableCreated\":{B(testA_exists)},\"emptyBeforeInit\":{B(testA_emptyBeforeInit)},\"invalidSnapshotRejected\":{B(testA_rejectInvalid)}"));

        // ----------------------------------------------------
        // TEST B - Snapshot Ingestion via Real Native API
        // ----------------------------------------------------
        SpikeNativeBridge.Sim_Reset();
        var input0 = NativeSimInput.Empty;
        var snap0 = SpikeNativeBridge.Get_data(input0);

        bool testB_valid = snap0.status >= 0 && snap0.entityCount == 4;
        bool testB_ingested = table.UpdateFromNativeSnapshot(snap0);

        bool hasP0 = table.TryGetEntity(0, out var rec0);
        bool hasOpp = table.TryGetEntity(3, out var rec3);
        bool hasBall = table.TryGetEntity(7, out var rec7);
        bool hasNet = table.TryGetEntity(6, out var rec6);

        bool testB_mappingOk = hasP0 && rec0.roleName == "PLAYER" &&
                               hasOpp && rec3.roleName == "OPPONENT_1" &&
                               hasBall && rec7.roleName == "BALL" &&
                               hasNet && rec6.roleName == "NET";

        bool testB_flagsPreserved = rec0.hasPosition && rec0.hasSize && rec0.hasCharacterActionState &&
                                    rec7.hasPosition && rec7.hasBallTrajectory && rec7.hasBallGameplayState &&
                                    rec6.hasPosition && rec6.hasSize;

        bool testB_tickMatches = table.CurrentTick == snap0.tick;
        bool testB_matchMeta = table.MatchRecord.leftScore == snap0.matchState.leftScore &&
                               table.MatchRecord.rightScore == snap0.matchState.rightScore;

        bool testB_ok = testB_valid && testB_ingested && testB_mappingOk && testB_flagsPreserved && testB_tickMatches && testB_matchMeta;
        Console.WriteLine($"[TEST B] Snapshot Ingestion: ok={testB_ok}, Tick={table.CurrentTick}, Entities={table.EntityCount}");
        results.Add(Section("TestB_SnapshotIngestion", testB_ok,
            $"\"snapshotValid\":{B(testB_valid)},\"ingested\":{B(testB_ingested)},\"entitiesMapped\":{B(testB_mappingOk)},\"flagsPreserved\":{B(testB_flagsPreserved)},\"tickMatches\":{B(testB_tickMatches)}"));

        // ----------------------------------------------------
        // TEST C - Updated Values Replace Stale Values
        // ----------------------------------------------------
        float prevPlayerX = rec0.posX;
        float prevBallY = rec7.posY;
        ulong prevTick = table.CurrentTick;

        // Send MoveRight button input for 30 ticks to move player and advance game
        for (int i = 0; i < 30; i++)
        {
            var moveInput = NativeSimInput.FromButtons((uint)SimButtonId.MoveRight);
            var simStep = SpikeNativeBridge.Get_data(moveInput);
            table.UpdateFromNativeSnapshot(simStep);
        }

        table.TryGetEntity(0, out var rec0_after);
        table.TryGetEntity(7, out var rec7_after);
        table.TryGetEntity(6, out var rec6_after);

        bool testC_playerMoved = rec0_after.posX != prevPlayerX;
        bool testC_tickAdvanced = table.CurrentTick > prevTick;
        bool testC_netUncorrupted = rec6_after.hasPosition && rec6_after.posX == rec6.posX; // Net stays static

        // Test that reading from table does NOT advance simulation
        ulong tickBeforeRead = SpikeNativeBridge.Sim_GetTick();
        table.TryGetEntity(0, out _);
        table.TryGetEntity(7, out _);
        ulong tickAfterRead = SpikeNativeBridge.Sim_GetTick();
        bool testC_readDoesNotTick = (tickBeforeRead == tickAfterRead);

        bool testC_ok = testC_playerMoved && testC_tickAdvanced && testC_netUncorrupted && testC_readDoesNotTick;
        Console.WriteLine($"[TEST C] Stale Replacement: ok={testC_ok}, PlayerX: {prevPlayerX:F2} -> {rec0_after.posX:F2}, Tick: {prevTick} -> {table.CurrentTick}");
        results.Add(Section("TestC_StaleReplacement", testC_ok,
            $"\"playerMoved\":{B(testC_playerMoved)},\"tickAdvanced\":{B(testC_tickAdvanced)},\"netUncorrupted\":{B(testC_netUncorrupted)},\"readDoesNotTick\":{B(testC_readDoesNotTick)}"));

        // ----------------------------------------------------
        // TEST D - Table-to-Render Flow (Coordinate Conversion & Visual Verification)
        // ----------------------------------------------------
        // PPM = 80 -> simScale = 0.0125f
        var (pWorldX, pWorldY) = SimulationEntityReader.SimToWorld(rec0_after.posX, rec0_after.posY);
        var (bWorldX, bWorldY) = SimulationEntityReader.SimToWorld(rec7_after.posX, rec7_after.posY);
        var (oppWorldX, oppWorldY) = SimulationEntityReader.SimToWorld(rec3.posX, rec3.posY);
        var (netWorldX, netWorldY) = SimulationEntityReader.SimToWorld(rec6.posX, rec6.posY);

        // Verification of coordinate conversion logic
        float expectedPWorldX = (rec0_after.posX - 1370.0f) * 0.0125f;
        float expectedPWorldY = (rec0_after.posY - 265.0f) * 0.0125f;
        bool testD_coordsCorrect = Math.Abs(pWorldX - expectedPWorldX) < 1e-4f && Math.Abs(pWorldY - expectedPWorldY) < 1e-4f;
        bool testD_ballFromTable = rec7_after.hasPosition && bWorldX != 0f;

        bool testD_ok = testD_coordsCorrect && testD_ballFromTable;
        Console.WriteLine($"[TEST D] Table-to-Render Flow: ok={testD_ok}, PlayerWorld=({pWorldX:F3}, {pWorldY:F3}), BallWorld=({bWorldX:F3}, {bWorldY:F3})");
        results.Add(Section("TestD_TableToRenderFlow", testD_ok,
            $"\"coordsCorrect\":{B(testD_coordsCorrect)},\"ballFromTable\":{B(testD_ballFromTable)},\"pWorldX\":{pWorldX},\"pWorldY\":{pWorldY}"));

        // ----------------------------------------------------
        // TEST E - Lifecycle and Failure Handling
        // ----------------------------------------------------
        // 1. Invalid snapshot does NOT overwrite valid table
        var corruptSnap = new NativeSimData { status = -1, entityCount = 4 };
        bool rejected = !table.UpdateFromNativeSnapshot(corruptSnap);
        bool tableStillIntact = table.CurrentTick > 0 && table.EntityCount == 4;

        // 2. Table Reset clears records cleanly
        table.ResetTable();
        bool resetOk = !table.IsInitialized && table.CurrentTick == 0 && table.EntityCount == 0;

        bool testE_ok = rejected && tableStillIntact && resetOk;
        Console.WriteLine($"[TEST E] Lifecycle & Failure Handling: ok={testE_ok}");
        results.Add(Section("TestE_LifecycleAndFailureHandling", testE_ok,
            $"\"corruptSnapshotRejected\":{B(rejected)},\"tablePreserved\":{B(tableStillIntact)},\"resetClean\":{B(resetOk)}"));
        Console.WriteLine("========================================================\n");
    }
}
