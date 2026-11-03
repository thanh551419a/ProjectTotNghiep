// =====================================================================
// SimSnapshotDecoder.cs
// Decodes and validates the complete authoritative snapshot returned by
// Sim_GetSnapshotJson() against the schema returned by Sim_GetSchemaJson().
//
// Guarantees:
//  - The complete parsed tree (Root) and the raw UTF-8 text (RawJson) are
//    kept; nothing is discarded. Typed views are convenience accessors only.
//  - Every field of every component/state/intent instance is validated
//    against the schema (presence, kind, enum range). Keys not described by
//    the schema/envelope are reported in Validation.UnknownFields; declared
//    fields that are absent are reported in Validation.MissingFields.
//  - The pool table (`pools`) is cross-checked against the actual presence
//    of components, states, intents and match data (identity preservation).
//  - Absent components are distinguishable from zero values (Has* flags).
//
// No UnityEngine dependency (shared with the out-of-Unity harness).
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Native.Snapshot
{
    public struct SimVec2
    {
        public float X;
        public float Y;

        public SimVec2(float x, float y) { X = x; Y = y; }
        public override string ToString() => $"({X.ToString("R", CultureInfo.InvariantCulture)}, {Y.ToString("R", CultureInfo.InvariantCulture)})";
    }

    /// <summary>Snapshot pool names (must match SimSchema::ForEachPool).</summary>
    public static class SimPools
    {
        public const string CharacterPosition = "characterPositionPool";
        public const string CharacterSize = "characterSizePool";
        public const string BallPosition = "ballPositionPool";
        public const string JumpUpFrame = "jumpUpFramePool";
        public const string BallTrajectory = "ballTrajectoryPool";
        public const string CharacterActionState = "characterActionStatePool";
        public const string BallGameplayState = "ballGameplayStatePool";
        public const string MatchState = "matchStatePool";
        public const string RallyState = "rallyStatePool";
        public const string CharacterIntent = "characterIntentPool";
        public const string BallIntent = "ballIntentPool";
        public const string VelocityIntent = "velocityIntentPool";
        public const string MatchStat = "matchStatPool";
    }

    public sealed class SimEntityRecord
    {
        public int Id;
        public string Role;
        public string Team;
        public string Controller;
        public JsonObject Components = new JsonObject();

        public bool Has(string pool) => Components.ContainsKey(pool);
        public JsonObject Get(string pool) => Components[pool] as JsonObject;
    }

    public sealed class SimMatchRecord
    {
        public int MatchId;
        public JsonObject States = new JsonObject();

        public bool Has(string pool) => States.ContainsKey(pool);
        public JsonObject Get(string pool) => States[pool] as JsonObject;
    }

    public struct SimIntentEntry
    {
        public int Entity;
        public JsonObject Value;
    }

    public sealed class SimIntentSection
    {
        public string Lifetime;
        /// <summary>Tick index that produced the intents; null after Initialize/Reset (no tick run yet).</summary>
        public long? ProducedByTick;
        public readonly Dictionary<string, List<SimIntentEntry>> Pools = new Dictionary<string, List<SimIntentEntry>>(StringComparer.Ordinal);

        public bool TryGet(string pool, int entity, out JsonObject value)
        {
            value = null;
            if (!Pools.TryGetValue(pool, out var list)) return false;
            foreach (var e in list)
                if (e.Entity == entity) { value = e.Value; return true; }
            return false;
        }

        public int TotalEntries => Pools.Values.Sum(l => l.Count);
    }

    public struct SimCharacterState
    {
        public int Id;
        public string Role, Team, Controller;
        public bool HasPosition; public SimVec2 Position;         // AABB bottom-left, px
        public bool HasSize; public SimVec2 Size;                 // hitbox w/h, px
        public bool HasJumpUpFrame; public int JumpRemainingFrames;
        public bool HasAction; public int ActionStatus; public float ActionRemainTimeMs;
        public bool HasIntent; public float IntentMoveX; public int IntentFinal;          // tick-scoped
        public bool HasVelocityIntent; public float VelocityVx, VelocityVy;               // tick-scoped
    }

    public struct SimBallState
    {
        public int Id;
        public bool HasPosition; public SimVec2 Position;
        public bool HasTrajectory; public int TrajectoryType; public float Speed, A, B, C;
    }

    public struct SimMatchView
    {
        public int MatchId;
        public bool HasBallGameplay; public float LandingX; public int StateFrame; public int CollisionState;
        public bool HasMatch; public int LeftScore, RightScore, ServingTeam; public uint LeftServingEntity, RightServingEntity; public bool Finished; public int MatchWinner;
        public bool HasRally; public uint LastTouch, TouchCount;

        public uint ServingEntity => ServingTeam == SimTeam.Left ? LeftServingEntity : RightServingEntity;
    }

    public struct SimWorldView
    {
        public SimVec2 NetPosition, NetSize, BallSize;
        public int NetEntity;
        public float FloorY, NetX, MinY, PixelsPerMeter, Gravity;
        public float MinXLeft, MaxXLeft, MinXRight, MaxXRight, VirtualWidth, VirtualHeight;
        public float Left3m, Right3m, Left9m, Right9m;
        public int TickRate, JumpFrame, ScoreToWin, MaxTouchPerTeam, FrameFlyUntilReset, BallStateFrameAlive, BallStateFrameReset;
        public bool EnableBot;
        public float[] ActionCooldownMs;
    }

    /// <summary>Team enum values (schema enum "Team").</summary>
    public static class SimTeam
    {
        public const int Left = 0;
        public const int Right = 1;
        public const int None = 2;
    }

    public sealed class SimValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> UnknownFields = new List<string>();
        public readonly List<string> MissingFields = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public int EntityCount;
        public int ComponentInstances;
        public int StateInstances;
        public int IntentEntries;
        public int MatchDataEntries;
        public int LeafFieldsValidated;
        public int NonFiniteFloats;

        public bool Ok => Errors.Count == 0 && UnknownFields.Count == 0 && MissingFields.Count == 0;

        public string Summary() =>
            $"{(Ok ? "OK" : "INVALID")} entities={EntityCount} components={ComponentInstances} states={StateInstances} " +
            $"intents={IntentEntries} matchData={MatchDataEntries} leaves={LeafFieldsValidated} nonFinite={NonFiniteFloats} " +
            $"errors={Errors.Count} unknown={UnknownFields.Count} missing={MissingFields.Count} warnings={Warnings.Count}";
    }

    public sealed class SimSnapshotData
    {
        // ---- complete data ----
        public string RawJson;
        public JsonObject Root;

        // ---- header ----
        public string Schema;
        public int SchemaVersion = -1;
        public int ApiVersion = -1;
        public bool Initialized;
        public ulong Tick;
        public int Chunk;
        public int MatchId;

        // ---- sections (complete, untyped) ----
        public readonly List<SimEntityRecord> Entities = new List<SimEntityRecord>();
        public readonly List<SimMatchRecord> Matches = new List<SimMatchRecord>();
        public readonly SimIntentSection Intents = new SimIntentSection();
        public JsonObject MatchData;
        public List<object> PoolTable;
        public JsonObject Globals;
        public JsonObject World;

        public readonly SimValidationReport Validation = new SimValidationReport();

        private readonly Dictionary<int, SimEntityRecord> byId = new Dictionary<int, SimEntityRecord>();
        private readonly Dictionary<int, SimMatchRecord> matchById = new Dictionary<int, SimMatchRecord>();

        internal void Index()
        {
            byId.Clear();
            foreach (var e in Entities) byId[e.Id] = e;
            matchById.Clear();
            foreach (var m in Matches) matchById[m.MatchId] = m;
        }

        public bool IsUsable => Root != null && Validation.Errors.Count == 0;

        public SimEntityRecord GetEntity(int id) => byId.TryGetValue(id, out var e) ? e : null;
        public SimMatchRecord GetMatch(int id) => matchById.TryGetValue(id, out var m) ? m : null;

        // ---- typed views ----
        public bool TryGetCharacter(int id, out SimCharacterState c)
        {
            c = default;
            var e = GetEntity(id);
            if (e == null) return false;
            c.Id = id; c.Role = e.Role; c.Team = e.Team; c.Controller = e.Controller;

            var pos = e.Get(SimPools.CharacterPosition);
            if (pos != null) { c.HasPosition = true; c.Position = ReadVec2(SimJson.GetObject(pos, "position")); }
            var size = e.Get(SimPools.CharacterSize);
            if (size != null) { c.HasSize = true; c.Size = ReadVec2(SimJson.GetObject(size, "size")); }
            var jump = e.Get(SimPools.JumpUpFrame);
            if (jump != null) { c.HasJumpUpFrame = true; c.JumpRemainingFrames = (int)SimJson.GetLong(jump, "remainingFrames"); }
            var act = e.Get(SimPools.CharacterActionState);
            if (act != null) { c.HasAction = true; c.ActionStatus = (int)SimJson.GetLong(act, "status"); c.ActionRemainTimeMs = SimJson.GetFloat(act, "remainTime"); }

            if (Intents.TryGet(SimPools.CharacterIntent, id, out var ci))
            { c.HasIntent = true; c.IntentMoveX = SimJson.GetFloat(ci, "moveX"); c.IntentFinal = (int)SimJson.GetLong(ci, "finalIntent"); }
            if (Intents.TryGet(SimPools.VelocityIntent, id, out var vi))
            { c.HasVelocityIntent = true; c.VelocityVx = SimJson.GetFloat(vi, "vx"); c.VelocityVy = SimJson.GetFloat(vi, "vy"); }

            return c.HasPosition;
        }

        public bool TryGetBall(int id, out SimBallState b)
        {
            b = default;
            var e = GetEntity(id);
            if (e == null) return false;
            b.Id = id;
            var pos = e.Get(SimPools.BallPosition);
            if (pos != null) { b.HasPosition = true; b.Position = ReadVec2(SimJson.GetObject(pos, "position")); }
            var tr = e.Get(SimPools.BallTrajectory);
            if (tr != null)
            {
                b.HasTrajectory = true;
                b.TrajectoryType = (int)SimJson.GetLong(tr, "type");
                b.Speed = SimJson.GetFloat(tr, "speed");
                b.A = SimJson.GetFloat(tr, "a");
                b.B = SimJson.GetFloat(tr, "b");
                b.C = SimJson.GetFloat(tr, "c");
            }
            return b.HasPosition;
        }

        /// <summary>First entity carrying a ball position component (the BALL, id 7 today).</summary>
        public int FindBallEntityId()
        {
            foreach (var e in Entities) if (e.Has(SimPools.BallPosition)) return e.Id;
            return -1;
        }

        public bool TryGetMatchView(int matchId, out SimMatchView v)
        {
            v = default;
            var m = GetMatch(matchId);
            if (m == null) return false;
            v.MatchId = matchId;
            var bg = m.Get(SimPools.BallGameplayState);
            if (bg != null)
            {
                v.HasBallGameplay = true;
                v.LandingX = SimJson.GetFloat(bg, "landingX");
                v.StateFrame = (int)SimJson.GetLong(bg, "stateFrame");
                v.CollisionState = (int)SimJson.GetLong(bg, "collisionState");
            }
            var ms = m.Get(SimPools.MatchState);
            if (ms != null)
            {
                v.HasMatch = true;
                v.LeftScore = (int)SimJson.GetLong(ms, "leftScore");
                v.RightScore = (int)SimJson.GetLong(ms, "rightScore");
                v.ServingTeam = (int)SimJson.GetLong(ms, "servingTeam");
                v.LeftServingEntity = (uint)SimJson.GetLong(ms, "leftServingEntity");
                v.RightServingEntity = (uint)SimJson.GetLong(ms, "rightServingEntity");
                v.Finished = SimJson.GetBool(ms, "finished");
                v.MatchWinner = (int)SimJson.GetLong(ms, "matchWinner");
            }
            var rs = m.Get(SimPools.RallyState);
            if (rs != null)
            {
                v.HasRally = true;
                v.LastTouch = (uint)SimJson.GetLong(rs, "lastTouch");
                v.TouchCount = (uint)SimJson.GetLong(rs, "touchCount");
            }
            return true;
        }

        public SimWorldView GetWorldView()
        {
            var w = new SimWorldView();
            if (World == null) return w;
            w.NetPosition = ReadVec2(SimJson.GetObject(World, "netPosition"));
            w.NetSize = ReadVec2(SimJson.GetObject(World, "netSize"));
            w.BallSize = ReadVec2(SimJson.GetObject(World, "ballSize"));
            w.NetEntity = (int)SimJson.GetLong(World, "netEntity", -1);
            var court = SimJson.GetObject(World, "court");
            w.FloorY = SimJson.GetFloat(court, "floorY");
            w.NetX = SimJson.GetFloat(court, "netX");
            w.Left3m = SimJson.GetFloat(court, "left3mLineX");
            w.Right3m = SimJson.GetFloat(court, "right3mLineX");
            w.Left9m = SimJson.GetFloat(court, "left9mLineX");
            w.Right9m = SimJson.GetFloat(court, "right9mLineX");
            var ph = SimJson.GetObject(World, "physics");
            w.MinY = SimJson.GetFloat(ph, "minY");
            w.MinXLeft = SimJson.GetFloat(ph, "minXLeft");
            w.MaxXLeft = SimJson.GetFloat(ph, "maxXLeft");
            w.MinXRight = SimJson.GetFloat(ph, "minXRight");
            w.MaxXRight = SimJson.GetFloat(ph, "maxXRight");
            w.Gravity = SimJson.GetFloat(ph, "gravity");
            w.JumpFrame = (int)SimJson.GetLong(ph, "jumpFrame");
            w.PixelsPerMeter = SimJson.GetFloat(ph, "pixelsPerMeter");
            w.TickRate = (int)SimJson.GetLong(ph, "tickRate");
            w.VirtualWidth = SimJson.GetFloat(ph, "virtualWidth");
            w.VirtualHeight = SimJson.GetFloat(ph, "virtualHeight");
            var rules = SimJson.GetObject(World, "rules");
            w.ScoreToWin = (int)SimJson.GetLong(rules, "scoreToWin");
            w.MaxTouchPerTeam = (int)SimJson.GetLong(rules, "maxTouchPerTeam");
            w.EnableBot = SimJson.GetBool(rules, "enableBot");
            w.FrameFlyUntilReset = (int)SimJson.GetLong(rules, "frameFlyUntilReset");
            w.BallStateFrameAlive = (int)SimJson.GetLong(rules, "ballStateFrameAlive");
            w.BallStateFrameReset = (int)SimJson.GetLong(rules, "ballStateFrameReset");
            var cds = SimJson.GetArray(World, "actionCooldownMs") ?? new List<object>();
            w.ActionCooldownMs = new float[cds.Count];
            for (int i = 0; i < cds.Count; i++) SimJson.TryGetFloat(cds[i], out w.ActionCooldownMs[i]);
            return w;
        }

        private static SimVec2 ReadVec2(JsonObject o) => new SimVec2(SimJson.GetFloat(o, "x"), SimJson.GetFloat(o, "y"));
    }

    public static class SimSnapshotDecoder
    {
        /// <summary>
        /// Decodes the raw snapshot text and validates it completely.
        /// Never throws for malformed data: problems are reported in Validation.Errors.
        /// </summary>
        public static SimSnapshotData Decode(string json, SimSnapshotSchema schema)
        {
            var d = new SimSnapshotData { RawJson = json };
            var rep = d.Validation;

            if (schema == null) { rep.Errors.Add("no schema available"); return d; }

            object parsed;
            try { parsed = SimJson.Parse(json); }
            catch (JsonFormatException ex) { rep.Errors.Add("parse error: " + ex.Message); return d; }

            d.Root = parsed as JsonObject;
            if (d.Root == null) { rep.Errors.Add("snapshot root is not an object"); return d; }
            var root = d.Root;

            // ---------------- header ----------------
            ReportEnvelopeKeys(root, SimEnvelopeContract.TopLevelKeys, "", rep);
            d.Schema = SimJson.GetString(root, "schema");
            d.SchemaVersion = (int)SimJson.GetLong(root, "schemaVersion", -1);
            d.ApiVersion = (int)SimJson.GetLong(root, "apiVersion", -1);
            d.Initialized = SimJson.GetBool(root, "initialized");
            d.Chunk = (int)SimJson.GetLong(root, "chunk", -1);
            d.MatchId = (int)SimJson.GetLong(root, "matchId", -1);
            if (!SimJson.TryGetULong(root["tick"], out d.Tick)) rep.Errors.Add("tick: not an unsigned integer");

            if (d.Schema != schema.Name) rep.Errors.Add($"schema '{d.Schema}' != schema document '{schema.Name}'");
            if (d.SchemaVersion != schema.SchemaVersion) rep.Errors.Add($"schemaVersion {d.SchemaVersion} != schema document {schema.SchemaVersion}");
            if (d.SchemaVersion != SimSnapshotSchema.ExpectedSchemaVersion) rep.Errors.Add($"schemaVersion {d.SchemaVersion} unsupported by this decoder (expects {SimSnapshotSchema.ExpectedSchemaVersion})");
            if (d.ApiVersion != SimSnapshotSchema.ExpectedApiVersion) rep.Errors.Add($"apiVersion {d.ApiVersion} unsupported by this decoder (expects {SimSnapshotSchema.ExpectedApiVersion})");
            if (!(root["initialized"] is bool)) rep.Errors.Add("initialized: not a bool");

            var presence = new Dictionary<string, SortedSet<long>>(StringComparer.Ordinal);
            foreach (var p in schema.Pools) presence[p.Name] = new SortedSet<long>();

            // ---------------- entities ----------------
            var entities = SimJson.GetArray(root, "entities");
            if (entities == null) rep.Errors.Add("entities: missing or not an array");
            else
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < entities.Count; i++)
                {
                    string path = $"entities[{i}]";
                    if (!(entities[i] is JsonObject eo)) { rep.Errors.Add(path + ": not an object"); continue; }
                    ReportEnvelopeKeys(eo, SimEnvelopeContract.EntityKeys, path + ".", rep);
                    var rec = new SimEntityRecord
                    {
                        Id = (int)SimJson.GetLong(eo, "id", -1),
                        Role = SimJson.GetString(eo, "role"),
                        Team = SimJson.GetString(eo, "team"),
                        Controller = SimJson.GetString(eo, "controller"),
                        Components = SimJson.GetObject(eo, "components") ?? new JsonObject()
                    };
                    if (!SimJson.TryGetULong(eo["id"], out _)) rep.Errors.Add(path + ".id: not an unsigned integer");
                    if (!seen.Add(rec.Id)) rep.Errors.Add($"{path}: duplicate entity id {rec.Id}");
                    if (rec.Role == null || rec.Team == null || rec.Controller == null) rep.Errors.Add(path + ": role/team/controller missing");
                    if (!(eo["components"] is JsonObject)) rep.Errors.Add(path + ".components: missing or not an object");

                    foreach (var kv in rec.Components)
                    {
                        string cpath = $"{path}.components.{kv.Key}";
                        if (!schema.PoolsByName.TryGetValue(kv.Key, out var pool)) { rep.UnknownFields.Add(cpath + " (unknown component pool)"); continue; }
                        if (pool.Section != "entities") { rep.Errors.Add($"{cpath}: pool belongs to section '{pool.Section}'"); continue; }
                        ValidateTyped(kv.Value, schema.TypeOfPool(kv.Key), schema, cpath, rep);
                        presence[kv.Key].Add(rec.Id);
                        rep.ComponentInstances++;
                    }
                    d.Entities.Add(rec);
                }
                rep.EntityCount = d.Entities.Count;
            }

            // ---------------- matches ----------------
            var matches = SimJson.GetArray(root, "matches");
            if (matches == null) rep.Errors.Add("matches: missing or not an array");
            else
            {
                for (int i = 0; i < matches.Count; i++)
                {
                    string path = $"matches[{i}]";
                    if (!(matches[i] is JsonObject mo)) { rep.Errors.Add(path + ": not an object"); continue; }
                    ReportEnvelopeKeys(mo, SimEnvelopeContract.MatchKeys, path + ".", rep);
                    var rec = new SimMatchRecord
                    {
                        MatchId = (int)SimJson.GetLong(mo, "matchId", -1),
                        States = SimJson.GetObject(mo, "states") ?? new JsonObject()
                    };
                    foreach (var kv in rec.States)
                    {
                        string spath = $"{path}.states.{kv.Key}";
                        if (!schema.PoolsByName.TryGetValue(kv.Key, out var pool)) { rep.UnknownFields.Add(spath + " (unknown state pool)"); continue; }
                        if (pool.Section != "matches") { rep.Errors.Add($"{spath}: pool belongs to section '{pool.Section}'"); continue; }
                        ValidateTyped(kv.Value, schema.TypeOfPool(kv.Key), schema, spath, rep);
                        presence[kv.Key].Add(rec.MatchId);
                        rep.StateInstances++;
                    }
                    d.Matches.Add(rec);
                }
            }

            // ---------------- intents (tick-scoped) ----------------
            var intents = SimJson.GetObject(root, "intents");
            if (intents == null) rep.Errors.Add("intents: missing or not an object");
            else
            {
                ReportEnvelopeKeys(intents, SimEnvelopeContract.IntentSectionKeys, "intents.", rep);
                d.Intents.Lifetime = SimJson.GetString(intents, "lifetime");
                if (d.Intents.Lifetime != "tick") rep.Errors.Add($"intents.lifetime '{d.Intents.Lifetime}' != 'tick'");
                object pbt = intents["producedByTick"];
                if (pbt == null) d.Intents.ProducedByTick = null;
                else if (SimJson.TryGetLong(pbt, out long t)) d.Intents.ProducedByTick = t;
                else rep.Errors.Add("intents.producedByTick: not an integer or null");

                var pools = SimJson.GetObject(intents, "pools");
                if (pools == null) rep.Errors.Add("intents.pools: missing");
                else
                {
                    foreach (var kv in pools)
                    {
                        string ppath = "intents.pools." + kv.Key;
                        if (!schema.PoolsByName.TryGetValue(kv.Key, out var pool)) { rep.UnknownFields.Add(ppath + " (unknown intent pool)"); continue; }
                        if (pool.Section != "intents") { rep.Errors.Add($"{ppath}: pool belongs to section '{pool.Section}'"); continue; }
                        var list = new List<SimIntentEntry>();
                        if (!(kv.Value is List<object> arr)) { rep.Errors.Add(ppath + ": not an array"); continue; }
                        for (int i = 0; i < arr.Count; i++)
                        {
                            string epath = $"{ppath}[{i}]";
                            if (!(arr[i] is JsonObject io)) { rep.Errors.Add(epath + ": not an object"); continue; }
                            ReportEnvelopeKeys(io, SimEnvelopeContract.IntentEntryKeys, epath + ".", rep);
                            if (!SimJson.TryGetULong(io["entity"], out ulong ent)) { rep.Errors.Add(epath + ".entity: not an unsigned integer"); continue; }
                            ValidateTyped(io["value"], schema.TypeOfPool(kv.Key), schema, epath + ".value", rep);
                            list.Add(new SimIntentEntry { Entity = (int)ent, Value = io["value"] as JsonObject });
                            presence[kv.Key].Add((long)ent);
                            rep.IntentEntries++;
                        }
                        d.Intents.Pools[kv.Key] = list;
                    }
                    foreach (var p in schema.Pools)
                        if (p.Section == "intents" && !pools.ContainsKey(p.Name)) rep.MissingFields.Add("intents.pools." + p.Name);
                }

                // Lifetime rule: intents were produced by the previous tick (or none yet).
                if (d.Intents.ProducedByTick.HasValue)
                {
                    long pb = d.Intents.ProducedByTick.Value;
                    if (pb < 0 || (ulong)pb >= d.Tick) rep.Errors.Add($"intents.producedByTick {pb} is not < tick {d.Tick}");
                    else if ((ulong)pb + 1 != d.Tick) rep.Warnings.Add($"intents.producedByTick {pb} != tick-1 ({d.Tick})");
                }
                else if (d.Intents.TotalEntries > 0)
                {
                    rep.Errors.Add("intents present but producedByTick is null");
                }
            }

            // ---------------- match data ----------------
            d.MatchData = SimJson.GetObject(root, "matchData");
            if (d.MatchData == null) rep.Errors.Add("matchData: missing or not an object");
            else
            {
                ReportEnvelopeKeys(d.MatchData, SimEnvelopeContract.MatchDataKeys, "matchData.", rep);
                var css = SimJson.GetObject(d.MatchData, "characterStatStorage");
                if (css == null) rep.MissingFields.Add("matchData.characterStatStorage");
                else
                {
                    ReportEnvelopeKeys(css, SimEnvelopeContract.StatStorageKeys, "matchData.characterStatStorage.", rep);
                    if (!(css["loaded"] is bool)) rep.Errors.Add("matchData.characterStatStorage.loaded: not a bool");
                    if (!(css["readBySimulation"] is bool)) rep.Errors.Add("matchData.characterStatStorage.readBySimulation: not a bool");
                    var arr = SimJson.GetArray(css, SimPools.MatchStat);
                    if (arr == null) rep.Errors.Add("matchData.characterStatStorage.matchStatPool: missing or not an array");
                    else
                    {
                        for (int i = 0; i < arr.Count; i++)
                        {
                            string epath = $"matchData.characterStatStorage.matchStatPool[{i}]";
                            if (!(arr[i] is JsonObject so)) { rep.Errors.Add(epath + ": not an object"); continue; }
                            ReportEnvelopeKeys(so, SimEnvelopeContract.StatEntryKeys, epath + ".", rep);
                            if (!SimJson.TryGetULong(so["matchId"], out ulong mid)) { rep.Errors.Add(epath + ".matchId: not an unsigned integer"); continue; }
                            ValidateTyped(so["value"], schema.TypeOfPool(SimPools.MatchStat), schema, epath + ".value", rep);
                            if (presence.ContainsKey(SimPools.MatchStat)) presence[SimPools.MatchStat].Add((long)mid);
                            rep.MatchDataEntries++;
                        }
                    }
                }
            }

            // ---------------- pool table (identity / coverage) ----------------
            d.PoolTable = SimJson.GetArray(root, "pools");
            if (d.PoolTable == null) rep.Errors.Add("pools: missing or not an array");
            else ValidatePoolTable(d.PoolTable, schema, presence, rep);

            // ---------------- globals / world (envelope contract) ----------------
            d.Globals = SimJson.GetObject(root, "globals");
            if (d.Globals == null) rep.Errors.Add("globals: missing or not an object");
            else ValidateObject(d.Globals, SimEnvelopeContract.Globals.Fields, schema, "globals", rep);

            d.World = SimJson.GetObject(root, "world");
            if (d.World == null) rep.Errors.Add("world: missing or not an object");
            else ValidateObject(d.World, SimEnvelopeContract.World.Fields, schema, "world", rep);

            d.Index();
            return d;
        }

        // ------------------------------------------------------------------
        private static void ReportEnvelopeKeys(JsonObject o, string[] expected, string prefix, SimValidationReport rep)
        {
            foreach (var k in o.Keys)
                if (Array.IndexOf(expected, k) < 0) rep.UnknownFields.Add(prefix + k);
            foreach (var k in expected)
                if (!o.ContainsKey(k)) rep.MissingFields.Add(prefix + k);
        }

        private static void ValidateTyped(object value, SimTypeSchema type, SimSnapshotSchema schema, string path, SimValidationReport rep)
        {
            if (type == null) { rep.Errors.Add(path + ": no schema type"); return; }
            if (!(value is JsonObject o)) { rep.Errors.Add(path + ": value is not an object"); return; }
            ValidateObject(o, type.Fields, schema, path, rep);
        }

        private static void ValidateObject(JsonObject o, List<SimFieldSchema> fields, SimSnapshotSchema schema, string path, SimValidationReport rep)
        {
            foreach (var f in fields)
            {
                string fpath = path + "." + f.Name;
                if (!o.TryGetValue(f.Name, out object v)) { rep.MissingFields.Add(fpath); continue; }
                ValidateValue(v, f, schema, fpath, rep);
            }
            foreach (var k in o.Keys)
            {
                bool known = false;
                foreach (var f in fields) if (f.Name == k) { known = true; break; }
                if (!known) rep.UnknownFields.Add(path + "." + k);
            }
        }

        private static void ValidateValue(object v, SimFieldSchema f, SimSnapshotSchema schema, string path, SimValidationReport rep)
        {
            switch (f.Kind)
            {
                case "float":
                    if (SimJson.IsNonFiniteToken(v)) rep.NonFiniteFloats++;
                    else if (!(v is JsonNumber)) rep.Errors.Add(path + ": expected float");
                    rep.LeafFieldsValidated++;
                    return;
                case "int":
                    if (!SimJson.TryGetLong(v, out long iv) || iv < int.MinValue || iv > int.MaxValue) rep.Errors.Add(path + ": expected int32");
                    rep.LeafFieldsValidated++;
                    return;
                case "uint":
                    if (!SimJson.TryGetULong(v, out ulong uv) || uv > uint.MaxValue) rep.Errors.Add(path + ": expected uint32");
                    rep.LeafFieldsValidated++;
                    return;
                case "bool":
                    if (!(v is bool)) rep.Errors.Add(path + ": expected bool");
                    rep.LeafFieldsValidated++;
                    return;
                case "enum":
                    if (!SimJson.TryGetLong(v, out long ev)) rep.Errors.Add(path + ": expected enum integer");
                    else if (f.Enum == null || !schema.Enums.TryGetValue(f.Enum, out var en)) rep.Errors.Add($"{path}: unknown enum '{f.Enum}'");
                    else if (ev < 0 || ev >= en.Values.Length) rep.Errors.Add($"{path}: value {ev} outside enum {f.Enum} [0,{en.Values.Length})");
                    rep.LeafFieldsValidated++;
                    return;
                case "string":
                    if (!(v is string)) rep.Errors.Add(path + ": expected string");
                    rep.LeafFieldsValidated++;
                    return;
                case "object":
                    if (!(v is JsonObject o)) { rep.Errors.Add(path + ": expected object"); return; }
                    ValidateObject(o, f.Fields, schema, path, rep);
                    return;
                case "array":
                    if (!(v is List<object> a)) { rep.Errors.Add(path + ": expected array"); return; }
                    if (f.Length > 0 && a.Count != f.Length) rep.Errors.Add($"{path}: length {a.Count} != {f.Length}");
                    if (f.Element == null) { rep.Errors.Add(path + ": schema has no element description"); return; }
                    for (int i = 0; i < a.Count; i++)
                        ValidateValue(a[i], f.Element, schema, $"{path}[{i}]", rep);
                    return;
                default:
                    rep.Errors.Add($"{path}: unsupported schema kind '{f.Kind}'");
                    return;
            }
        }

        private static void ValidatePoolTable(List<object> table, SimSnapshotSchema schema, Dictionary<string, SortedSet<long>> presence, SimValidationReport rep)
        {
            var listed = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < table.Count; i++)
            {
                string path = $"pools[{i}]";
                if (!(table[i] is JsonObject po)) { rep.Errors.Add(path + ": not an object"); continue; }
                ReportEnvelopeKeys(po, SimEnvelopeContract.PoolTableKeys, path + ".", rep);
                string name = SimJson.GetString(po, "name");
                if (name == null || !schema.PoolsByName.TryGetValue(name, out var ps)) { rep.UnknownFields.Add($"{path} (unknown pool '{name}')"); continue; }
                if (!listed.Add(name)) rep.Errors.Add($"{path}: pool '{name}' listed twice");

                if (SimJson.GetString(po, "storage") != ps.Storage) rep.Errors.Add($"{path}.storage mismatch for {name}");
                if (SimJson.GetString(po, "type") != ps.Type) rep.Errors.Add($"{path}.type mismatch for {name}");
                if (SimJson.GetString(po, "key") != ps.Key) rep.Errors.Add($"{path}.key mismatch for {name}");
                if (SimJson.GetString(po, "section") != ps.Section) rep.Errors.Add($"{path}.section mismatch for {name}");
                if (SimJson.GetString(po, "lifetime") != ps.Lifetime) rep.Errors.Add($"{path}.lifetime mismatch for {name}");

                var keys = SimJson.GetArray(po, "keys") ?? new List<object>();
                var keySet = new SortedSet<long>();
                foreach (var k in keys)
                {
                    if (SimJson.TryGetULong(k, out ulong kv)) { if (!keySet.Add((long)kv)) rep.Errors.Add($"{path}.keys: duplicate key {kv} in {name}"); }
                    else rep.Errors.Add($"{path}.keys: non-integer key in {name}");
                }
                if (!SimJson.TryGetULong(po["count"], out ulong count) || (int)count != keys.Count)
                    rep.Errors.Add($"{path}.count does not equal number of keys for {name}");

                var present = presence.TryGetValue(name, out var s) ? s : new SortedSet<long>();
                if (!keySet.SetEquals(present))
                    rep.Errors.Add($"{path}: keys [{string.Join(",", keySet)}] != present instances [{string.Join(",", present)}] for {name}");
            }
            foreach (var p in schema.Pools)
                if (!listed.Contains(p.Name)) rep.MissingFields.Add($"pools[{p.Name}]");
        }
    }
}
