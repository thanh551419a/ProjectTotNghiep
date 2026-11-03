// =====================================================================
// SimSnapshotSchema.cs
// Managed model of the machine-readable schema returned by
// Sim_GetSchemaJson() ("TheSpike.SimSnapshot", schemaVersion 1).
//
// The native schema describes every component/state/intent type field by
// field (generated in C++ from SimSchema.h with structured bindings, so a new
// C++ field cannot be omitted). The snapshot envelope sections that are not
// typed pools ("globals", "world", entity/match headers) are mirrored here in
// SimEnvelopeContract from SimSnapshotJson.cpp so that the decoder can also
// report unknown or missing envelope fields instead of silently ignoring them.
//
// No UnityEngine dependency (shared with the out-of-Unity harness).
// =====================================================================
using System;
using System.Collections.Generic;

namespace Native.Snapshot
{
    public sealed class SimFieldSchema
    {
        public string Name;
        public string Kind;        // float | int | uint | bool | enum | object | array | string
        public string CType;
        public string Enum;        // enum name for Kind == enum
        public string Transition;  // none | always | boundary
        public List<SimFieldSchema> Fields = new List<SimFieldSchema>(); // Kind == object
        public int Length;         // Kind == array (0 = any)
        public SimFieldSchema Element; // Kind == array

        public static SimFieldSchema Leaf(string name, string kind, string enumName = null) =>
            new SimFieldSchema { Name = name, Kind = kind, CType = kind, Enum = enumName, Transition = "none" };

        public static SimFieldSchema Obj(string name, params SimFieldSchema[] fields)
        {
            var f = new SimFieldSchema { Name = name, Kind = "object" };
            f.Fields.AddRange(fields);
            return f;
        }

        public static SimFieldSchema Arr(string name, int length, SimFieldSchema element) =>
            new SimFieldSchema { Name = name, Kind = "array", Length = length, Element = element };

        /// <summary>Number of scalar leaves described by this field (arrays expanded).</summary>
        public int LeafCount()
        {
            switch (Kind)
            {
                case "object":
                    int n = 0;
                    foreach (var f in Fields) n += f.LeafCount();
                    return n;
                case "array":
                    return Math.Max(Length, 0) * (Element != null ? Element.LeafCount() : 1);
                default:
                    return 1;
            }
        }
    }

    public sealed class SimTypeSchema
    {
        public string Name;
        public int DeclaredFieldCount;
        public List<SimFieldSchema> Fields = new List<SimFieldSchema>();

        public int LeafCount()
        {
            int n = 0;
            foreach (var f in Fields) n += f.LeafCount();
            return n;
        }
    }

    public sealed class SimEnumSchema
    {
        public string Name;
        public string Underlying;
        public string[] Values = Array.Empty<string>();

        public string NameOf(long value) =>
            value >= 0 && value < Values.Length ? Values[value] : $"<unknown {value}>";
    }

    public sealed class SimPoolSchema
    {
        public string Name;
        public string Storage;
        public string Type;
        public string Key;       // entity | match
        public string Section;   // entities | matches | intents | matchData
        public string Lifetime;  // persistent | tick
    }

    public sealed class SimSnapshotSchema
    {
        public const string ExpectedSchemaName = "TheSpike.SimSnapshot";
        public const int ExpectedSchemaVersion = 1;
        public const int ExpectedApiVersion = 2;

        public string Name;
        public int SchemaVersion;
        public int ApiVersion;
        public string CoordinateSystem;
        public string NonFiniteFloatEncoding;
        public string RawJson;

        public readonly Dictionary<string, SimTypeSchema> Types = new Dictionary<string, SimTypeSchema>(StringComparer.Ordinal);
        public readonly List<SimTypeSchema> TypeList = new List<SimTypeSchema>();
        public readonly Dictionary<string, SimEnumSchema> Enums = new Dictionary<string, SimEnumSchema>(StringComparer.Ordinal);
        public readonly List<SimPoolSchema> Pools = new List<SimPoolSchema>();
        public readonly Dictionary<string, SimPoolSchema> PoolsByName = new Dictionary<string, SimPoolSchema>(StringComparer.Ordinal);
        public readonly List<KeyValuePair<string, string>> Excluded = new List<KeyValuePair<string, string>>();

        public SimTypeSchema TypeOfPool(string poolName) =>
            PoolsByName.TryGetValue(poolName, out var p) && Types.TryGetValue(p.Type, out var t) ? t : null;

        public static SimSnapshotSchema Parse(string json)
        {
            var root = SimJson.Parse(json) as JsonObject;
            if (root == null) throw new JsonFormatException("schema root is not an object");

            var schema = new SimSnapshotSchema
            {
                RawJson = json,
                Name = SimJson.GetString(root, "schema"),
                SchemaVersion = (int)SimJson.GetLong(root, "schemaVersion", -1),
                ApiVersion = (int)SimJson.GetLong(root, "apiVersion", -1),
                CoordinateSystem = SimJson.GetString(root, "coordinateSystem"),
                NonFiniteFloatEncoding = SimJson.GetString(root, "nonFiniteFloatEncoding")
            };

            foreach (var t in SimJson.GetArray(root, "types") ?? new List<object>())
            {
                if (!(t is JsonObject to)) throw new JsonFormatException("schema type entry is not an object");
                var type = new SimTypeSchema
                {
                    Name = SimJson.GetString(to, "name"),
                    DeclaredFieldCount = (int)SimJson.GetLong(to, "declaredFieldCount", -1)
                };
                foreach (var f in SimJson.GetArray(to, "fields") ?? new List<object>())
                    type.Fields.Add(ParseField(f as JsonObject));
                schema.Types[type.Name] = type;
                schema.TypeList.Add(type);
            }

            foreach (var e in SimJson.GetArray(root, "enums") ?? new List<object>())
            {
                var eo = e as JsonObject;
                var en = new SimEnumSchema { Name = SimJson.GetString(eo, "name"), Underlying = SimJson.GetString(eo, "underlying") };
                var vals = SimJson.GetArray(eo, "values") ?? new List<object>();
                en.Values = new string[vals.Count];
                for (int i = 0; i < vals.Count; i++) en.Values[i] = vals[i] as string;
                schema.Enums[en.Name] = en;
            }

            foreach (var p in SimJson.GetArray(root, "pools") ?? new List<object>())
            {
                var po = p as JsonObject;
                var pool = new SimPoolSchema
                {
                    Name = SimJson.GetString(po, "name"),
                    Storage = SimJson.GetString(po, "storage"),
                    Type = SimJson.GetString(po, "type"),
                    Key = SimJson.GetString(po, "key"),
                    Section = SimJson.GetString(po, "section"),
                    Lifetime = SimJson.GetString(po, "lifetime")
                };
                schema.Pools.Add(pool);
                schema.PoolsByName[pool.Name] = pool;
            }

            foreach (var x in SimJson.GetArray(root, "excluded") ?? new List<object>())
            {
                var xo = x as JsonObject;
                schema.Excluded.Add(new KeyValuePair<string, string>(SimJson.GetString(xo, "path"), SimJson.GetString(xo, "reason")));
            }

            return schema;
        }

        private static SimFieldSchema ParseField(JsonObject fo)
        {
            if (fo == null) throw new JsonFormatException("schema field entry is not an object");
            var f = new SimFieldSchema
            {
                Name = SimJson.GetString(fo, "name"),
                Kind = SimJson.GetString(fo, "kind"),
                CType = SimJson.GetString(fo, "cType"),
                Enum = SimJson.GetString(fo, "enum"),
                Transition = SimJson.GetString(fo, "transition"),
                Length = (int)SimJson.GetLong(fo, "length", 0)
            };
            if (f.Kind == "object")
            {
                foreach (var c in SimJson.GetArray(fo, "fields") ?? new List<object>())
                    f.Fields.Add(ParseField(c as JsonObject));
            }
            else if (f.Kind == "array")
            {
                var el = SimJson.GetObject(fo, "element");
                if (el != null) f.Element = ParseField(el);
            }
            return f;
        }

        /// <summary>Consistency checks of the schema itself (pools reference known types, enums exist).</summary>
        public List<string> SelfCheck()
        {
            var problems = new List<string>();
            if (Name != ExpectedSchemaName) problems.Add($"schema name '{Name}' != '{ExpectedSchemaName}'");
            if (SchemaVersion != ExpectedSchemaVersion) problems.Add($"schemaVersion {SchemaVersion} != {ExpectedSchemaVersion}");
            if (ApiVersion != ExpectedApiVersion) problems.Add($"apiVersion {ApiVersion} != {ExpectedApiVersion}");
            foreach (var p in Pools)
                if (!Types.ContainsKey(p.Type)) problems.Add($"pool '{p.Name}' references unknown type '{p.Type}'");
            foreach (var t in TypeList)
            {
                if (t.DeclaredFieldCount != t.Fields.Count)
                    problems.Add($"type '{t.Name}' declaredFieldCount {t.DeclaredFieldCount} != described {t.Fields.Count}");
                CheckEnums(t.Name, t.Fields, problems);
            }
            return problems;
        }

        private void CheckEnums(string path, List<SimFieldSchema> fields, List<string> problems)
        {
            foreach (var f in fields)
            {
                if (f.Kind == "enum" && (f.Enum == null || !Enums.ContainsKey(f.Enum)))
                    problems.Add($"{path}.{f.Name} references unknown enum '{f.Enum}'");
                if (f.Kind == "object") CheckEnums(path + "." + f.Name, f.Fields, problems);
                if (f.Kind == "array" && f.Element != null && f.Element.Kind == "object")
                    CheckEnums(path + "." + f.Name + "[]", f.Element.Fields, problems);
            }
        }
    }

    /// <summary>
    /// Mirror of the non-pool parts of the snapshot written by
    /// SimSnapshotJson::BuildSnapshot (MyGame/Source/Simulation/Diagnostics/SimSnapshotJson.cpp).
    /// Any key the native side adds is reported as "unknown" until this
    /// contract (and THE_SPIKE_NATIVE_API.md) is updated.
    /// </summary>
    public static class SimEnvelopeContract
    {
        public static readonly string[] TopLevelKeys =
        {
            "schema", "schemaVersion", "apiVersion", "initialized", "tick", "chunk", "matchId",
            "entities", "matches", "intents", "matchData", "pools", "globals", "world"
        };

        public static readonly string[] EntityKeys = { "id", "role", "team", "controller", "components" };
        public static readonly string[] MatchKeys = { "matchId", "states" };
        public static readonly string[] IntentSectionKeys = { "lifetime", "producedByTick", "pools" };
        public static readonly string[] IntentEntryKeys = { "entity", "value" };
        public static readonly string[] MatchDataKeys = { "characterStatStorage" };
        public static readonly string[] StatStorageKeys = { "loaded", "readBySimulation", "matchStatPool" };
        public static readonly string[] StatEntryKeys = { "matchId", "value" };
        public static readonly string[] PoolTableKeys = { "name", "storage", "type", "key", "section", "lifetime", "count", "keys" };

        private static SimFieldSchema F(string n) => SimFieldSchema.Leaf(n, "float");
        private static SimFieldSchema I(string n) => SimFieldSchema.Leaf(n, "int");
        private static SimFieldSchema U(string n) => SimFieldSchema.Leaf(n, "uint");
        private static SimFieldSchema B(string n) => SimFieldSchema.Leaf(n, "bool");
        private static SimFieldSchema V2(string n) => SimFieldSchema.Obj(n, F("x"), F("y"));

        public static readonly SimFieldSchema Globals = SimFieldSchema.Obj("globals",
            SimFieldSchema.Obj("testTrajectory", I("frame"), F("AttackPower")),
            SimFieldSchema.Obj("input", U("entity"), U("holdingButtons"), U("pressedButtons"), F("moveAxisX"), U("prevButtonMask")),
            SimFieldSchema.Obj("inputMapping", F("playerSpeed"), F("aiSpeed"), F("aiAttackHeightThreshold")),
            SimFieldSchema.Obj("runtime", B("lastUpdateFrozenByVictory")));

        /// <summary>ActionState has 10 real values (Count excluded) -> 10 cooldown entries.</summary>
        public const int ActionCooldownCount = 10;

        public static readonly SimFieldSchema World = SimFieldSchema.Obj("world",
            V2("netPosition"), V2("netSize"), V2("ballSize"), U("netEntity"), B("netHasComponents"),
            SimFieldSchema.Obj("court", F("rectX"), F("rectY"), F("rectWidth"), F("rectHeight"), F("floorY"), F("netX"),
                F("left3mLineX"), F("right3mLineX"), F("left9mLineX"), F("right9mLineX"),
                F("leftPositionResetX"), F("rightPositionResetX")),
            SimFieldSchema.Obj("physics", F("minY"), F("minXLeft"), F("maxXLeft"), F("minXRight"), F("maxXRight"),
                F("gravity"), I("jumpFrame"), F("pixelsPerMeter"), I("tickRate"), F("virtualWidth"), F("virtualHeight")),
            SimFieldSchema.Obj("rules", I("scoreToWin"), I("maxTouchPerTeam"), B("enableBot"), I("frameFlyUntilReset"),
                I("ballStateFrameAlive"), I("ballStateFrameReset")),
            SimFieldSchema.Arr("actionCooldownMs", ActionCooldownCount, F(null)));
    }
}
