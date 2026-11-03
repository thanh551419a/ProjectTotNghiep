// =====================================================================
// SimSnapshotComparer.cs
// Field-by-field verification that the managed (decoded) snapshot equals
// the native output:
//   1. Round trip: the decoded tree re-serialized with the original number
//      tokens must be byte-identical to the native UTF-8 text (nothing lost,
//      re-ordered or altered by decoding).
//   2. Typed views vs raw tokens: every value exposed by the typed accessors
//      must equal (bit-exact float32 / exact integer) the native token at the
//      same path.
//   3. Cross-check with the independently marshalled v1 binary struct
//      (Sim_GetSnapshot) captured right after the same Sim_Update.
//
// No UnityEngine dependency (shared with the out-of-Unity harness).
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Native.Snapshot
{
    public sealed class SimComparisonResult
    {
        public int Checked;
        public readonly List<string> Mismatches = new List<string>();
        public bool Ok => Mismatches.Count == 0;

        public void Fail(string m)
        {
            if (Mismatches.Count < 200) Mismatches.Add(m);
            else if (Mismatches.Count == 200) Mismatches.Add("... (further mismatches truncated in report only)");
        }

        public override string ToString() => $"{(Ok ? "PASS" : "FAIL")} checked={Checked} mismatches={Mismatches.Count}";
    }

    public static class SimSnapshotComparer
    {
        // ------------------------------------------------------------------
        // 1. Canonical compact serializer (mirrors rapidjson::Writer output)
        // ------------------------------------------------------------------
        public static string Serialize(object node)
        {
            var sb = new StringBuilder(8192);
            Write(node, sb);
            return sb.ToString();
        }

        private static void Write(object node, StringBuilder sb)
        {
            switch (node)
            {
                case null: sb.Append("null"); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case JsonNumber n: sb.Append(n.Raw); return;
                case string s: WriteString(s, sb); return;
                case List<object> a:
                    sb.Append('[');
                    for (int i = 0; i < a.Count; i++) { if (i > 0) sb.Append(','); Write(a[i], sb); }
                    sb.Append(']');
                    return;
                case JsonObject o:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in o)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(kv.Key, sb);
                        sb.Append(':');
                        Write(kv.Value, sb);
                    }
                    sb.Append('}');
                    return;
                default:
                    throw new InvalidOperationException("unsupported node " + node.GetType());
            }
        }

        private static void WriteString(string s, StringBuilder sb)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static void CompareRoundTrip(SimSnapshotData d, SimComparisonResult r)
        {
            r.Checked++;
            if (d.Root == null) { r.Fail("roundtrip: no decoded root"); return; }
            string again = Serialize(d.Root);
            if (!string.Equals(again, d.RawJson, StringComparison.Ordinal))
            {
                int i = 0;
                int n = Math.Min(again.Length, d.RawJson.Length);
                while (i < n && again[i] == d.RawJson[i]) i++;
                string ctx(string s) => s.Substring(Math.Max(0, i - 40), Math.Min(80, s.Length - Math.Max(0, i - 40)));
                r.Fail($"roundtrip: re-serialized decoded tree differs from native text at offset {i}: native='{ctx(d.RawJson)}' decoded='{ctx(again)}'");
            }
        }

        // ------------------------------------------------------------------
        // 2. Typed views vs raw native tokens
        // ------------------------------------------------------------------
        public static void CompareTypedViews(SimSnapshotData d, SimComparisonResult r)
        {
            if (d.Root == null) { r.Fail("typed: no decoded root"); return; }
            var leaves = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in SimJson.Flatten(d.Root)) leaves[kv.Key] = kv.Value;

            for (int i = 0; i < d.Entities.Count; i++)
            {
                var e = d.Entities[i];
                string p = $"entities[{i}].components.";
                if (d.TryGetCharacter(e.Id, out var c))
                {
                    if (c.HasPosition) { F(r, leaves, p + "characterPositionPool.position.x", c.Position.X); F(r, leaves, p + "characterPositionPool.position.y", c.Position.Y); }
                    if (c.HasSize) { F(r, leaves, p + "characterSizePool.size.x", c.Size.X); F(r, leaves, p + "characterSizePool.size.y", c.Size.Y); }
                    if (c.HasJumpUpFrame) L(r, leaves, p + "jumpUpFramePool.remainingFrames", c.JumpRemainingFrames);
                    if (c.HasAction) { L(r, leaves, p + "characterActionStatePool.status", c.ActionStatus); F(r, leaves, p + "characterActionStatePool.remainTime", c.ActionRemainTimeMs); }
                    // presence flags must match the raw component keys exactly (absent != zero)
                    Presence(r, e, SimPools.CharacterPosition, c.HasPosition);
                    Presence(r, e, SimPools.CharacterSize, c.HasSize);
                    Presence(r, e, SimPools.JumpUpFrame, c.HasJumpUpFrame);
                    Presence(r, e, SimPools.CharacterActionState, c.HasAction);
                    if (c.HasIntent) CompareIntent(d, r, leaves, SimPools.CharacterIntent, e.Id, "moveX", c.IntentMoveX, "finalIntent", c.IntentFinal);
                    if (c.HasVelocityIntent) CompareIntentF(d, r, leaves, SimPools.VelocityIntent, e.Id, "vx", c.VelocityVx, "vy", c.VelocityVy);
                }
                if (d.TryGetBall(e.Id, out var b))
                {
                    F(r, leaves, p + "ballPositionPool.position.x", b.Position.X);
                    F(r, leaves, p + "ballPositionPool.position.y", b.Position.Y);
                    if (b.HasTrajectory)
                    {
                        L(r, leaves, p + "ballTrajectoryPool.type", b.TrajectoryType);
                        F(r, leaves, p + "ballTrajectoryPool.speed", b.Speed);
                        F(r, leaves, p + "ballTrajectoryPool.a", b.A);
                        F(r, leaves, p + "ballTrajectoryPool.b", b.B);
                        F(r, leaves, p + "ballTrajectoryPool.c", b.C);
                    }
                    Presence(r, e, SimPools.BallTrajectory, b.HasTrajectory);
                }
            }

            for (int i = 0; i < d.Matches.Count; i++)
            {
                var m = d.Matches[i];
                if (!d.TryGetMatchView(m.MatchId, out var v)) { r.Fail($"typed: match {m.MatchId} not resolvable"); continue; }
                string p = $"matches[{i}].states.";
                if (v.HasBallGameplay)
                {
                    F(r, leaves, p + "ballGameplayStatePool.landingX", v.LandingX);
                    L(r, leaves, p + "ballGameplayStatePool.stateFrame", v.StateFrame);
                    L(r, leaves, p + "ballGameplayStatePool.collisionState", v.CollisionState);
                }
                if (v.HasMatch)
                {
                    L(r, leaves, p + "matchStatePool.leftScore", v.LeftScore);
                    L(r, leaves, p + "matchStatePool.rightScore", v.RightScore);
                    L(r, leaves, p + "matchStatePool.servingTeam", v.ServingTeam);
                    L(r, leaves, p + "matchStatePool.leftServingEntity", v.LeftServingEntity);
                    L(r, leaves, p + "matchStatePool.rightServingEntity", v.RightServingEntity);
                    Bo(r, leaves, p + "matchStatePool.finished", v.Finished);
                    L(r, leaves, p + "matchStatePool.matchWinner", v.MatchWinner);
                }
                if (v.HasRally)
                {
                    L(r, leaves, p + "rallyStatePool.lastTouch", v.LastTouch);
                    L(r, leaves, p + "rallyStatePool.touchCount", v.TouchCount);
                }
            }

            var w = d.GetWorldView();
            F(r, leaves, "world.netPosition.x", w.NetPosition.X);
            F(r, leaves, "world.netPosition.y", w.NetPosition.Y);
            F(r, leaves, "world.netSize.x", w.NetSize.X);
            F(r, leaves, "world.netSize.y", w.NetSize.Y);
            F(r, leaves, "world.ballSize.x", w.BallSize.X);
            F(r, leaves, "world.ballSize.y", w.BallSize.Y);
            F(r, leaves, "world.court.floorY", w.FloorY);
            F(r, leaves, "world.court.netX", w.NetX);
            F(r, leaves, "world.physics.minY", w.MinY);
            F(r, leaves, "world.physics.pixelsPerMeter", w.PixelsPerMeter);
            L(r, leaves, "world.physics.tickRate", w.TickRate);
            L(r, leaves, "world.rules.frameFlyUntilReset", w.FrameFlyUntilReset);
            L(r, leaves, "world.rules.scoreToWin", w.ScoreToWin);
            L(r, leaves, "world.rules.maxTouchPerTeam", w.MaxTouchPerTeam);
            for (int i = 0; i < (w.ActionCooldownMs?.Length ?? 0); i++)
                F(r, leaves, $"world.actionCooldownMs[{i}]", w.ActionCooldownMs[i]);
            L(r, leaves, "tick", (long)d.Tick);
        }

        private static void CompareIntent(SimSnapshotData d, SimComparisonResult r, Dictionary<string, object> leaves, string pool, int entity,
            string fName, float fVal, string lName, long lVal)
        {
            int idx = IntentIndex(d, pool, entity);
            if (idx < 0) { r.Fail($"typed: intent {pool}/{entity} not found"); return; }
            F(r, leaves, $"intents.pools.{pool}[{idx}].value.{fName}", fVal);
            L(r, leaves, $"intents.pools.{pool}[{idx}].value.{lName}", lVal);
        }

        private static void CompareIntentF(SimSnapshotData d, SimComparisonResult r, Dictionary<string, object> leaves, string pool, int entity,
            string aName, float aVal, string bName, float bVal)
        {
            int idx = IntentIndex(d, pool, entity);
            if (idx < 0) { r.Fail($"typed: intent {pool}/{entity} not found"); return; }
            F(r, leaves, $"intents.pools.{pool}[{idx}].value.{aName}", aVal);
            F(r, leaves, $"intents.pools.{pool}[{idx}].value.{bName}", bVal);
        }

        private static int IntentIndex(SimSnapshotData d, string pool, int entity)
        {
            if (!d.Intents.Pools.TryGetValue(pool, out var list)) return -1;
            for (int i = 0; i < list.Count; i++) if (list[i].Entity == entity) return i;
            return -1;
        }

        private static void Presence(SimComparisonResult r, SimEntityRecord e, string pool, bool typedHas)
        {
            r.Checked++;
            if (e.Has(pool) != typedHas) r.Fail($"presence: entity {e.Id} {pool} raw={e.Has(pool)} typed={typedHas}");
        }

        private static void F(SimComparisonResult r, Dictionary<string, object> leaves, string path, float typed)
        {
            r.Checked++;
            if (!leaves.TryGetValue(path, out object raw)) { r.Fail($"{path}: missing in native text"); return; }
            float native;
            if (raw is JsonNumber n)
            {
                if (!float.TryParse(n.Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out native)) { r.Fail($"{path}: unparsable token {n.Raw}"); return; }
            }
            else if (!SimJson.TryGetFloat(raw, out native)) { r.Fail($"{path}: not a float token ({SimJson.LeafToString(raw)})"); return; }
            if (!SameBits(native, typed)) r.Fail($"{path}: native={SimJson.LeafToString(raw)} typed={typed.ToString("R", CultureInfo.InvariantCulture)}");
        }

        private static void L(SimComparisonResult r, Dictionary<string, object> leaves, string path, long typed)
        {
            r.Checked++;
            if (!leaves.TryGetValue(path, out object raw)) { r.Fail($"{path}: missing in native text"); return; }
            if (!SimJson.TryGetLong(raw, out long native) || native != typed) r.Fail($"{path}: native={SimJson.LeafToString(raw)} typed={typed}");
        }

        private static void Bo(SimComparisonResult r, Dictionary<string, object> leaves, string path, bool typed)
        {
            r.Checked++;
            if (!leaves.TryGetValue(path, out object raw) || !(raw is bool b) || b != typed) r.Fail($"{path}: native={SimJson.LeafToString(raw)} typed={typed}");
        }

        public static bool SameBits(float a, float b) =>
            BitConverter.ToInt32(BitConverter.GetBytes(a), 0) == BitConverter.ToInt32(BitConverter.GetBytes(b), 0);

        // ------------------------------------------------------------------
        // 3. Cross-check against the v1 binary struct (independent marshalling)
        //    Derivations below are exactly those of SimulationRuntime::BuildSnapshot.
        // ------------------------------------------------------------------
        public static void CompareWithLegacyStruct(SimSnapshotData d, SimSnapshot legacy, SimComparisonResult r)
        {
            Eq(r, "frameNumber==tick", legacy.frameNumber, d.Tick);
            var w = d.GetWorldView();
            for (int i = 0; i < 6; i++)
            {
                var e = legacy.GetEntity(i);
                d.TryGetCharacter(i, out var c);
                string p = $"entity{i}.";
                Fe(r, p + "posX", e.posX, c.HasPosition ? c.Position.X : 0f);
                Fe(r, p + "posY", e.posY, c.HasPosition ? c.Position.Y : 0f);
                Fe(r, p + "sizeX", e.sizeX, c.HasSize ? c.Size.X : 0f);
                Fe(r, p + "sizeY", e.sizeY, c.HasSize ? c.Size.Y : 0f);
                Eq(r, p + "actionState", e.actionState, c.HasAction ? c.ActionStatus : 0);
                Fe(r, p + "actionRemainTime", e.actionRemainTime, c.HasAction ? c.ActionRemainTimeMs : 0f);
                Fe(r, p + "moveX", e.moveX, c.HasIntent ? c.IntentMoveX : 0f);
                float posY = c.HasPosition ? c.Position.Y : 0f;
                Eq(r, p + "isGrounded", e.isGrounded, posY <= w.MinY + 0.1f ? 1 : 0);
                float mx = c.HasIntent ? c.IntentMoveX : 0f;
                int facing = i < 3 ? (mx < -0.1f ? -1 : 1) : (mx > 0.1f ? 1 : -1);
                Eq(r, p + "facingDirection", e.facingDirection, facing);
            }

            Fe(r, "net.posX", legacy.net.posX, w.NetPosition.X);
            Fe(r, "net.posY", legacy.net.posY, w.NetPosition.Y);
            Fe(r, "net.sizeX", legacy.net.sizeX, w.NetSize.X);
            Fe(r, "net.sizeY", legacy.net.sizeY, w.NetSize.Y);

            int ballId = d.FindBallEntityId();
            d.TryGetBall(ballId, out var b);
            d.TryGetMatchView(d.MatchId, out var m);
            Fe(r, "ball.posX", legacy.ball.posX, b.HasPosition ? b.Position.X : 0f);
            Fe(r, "ball.posY", legacy.ball.posY, b.HasPosition ? b.Position.Y : 0f);
            Fe(r, "ball.speed", legacy.ball.speed, b.HasTrajectory ? b.Speed : 0f);
            Eq(r, "ball.trajectoryType", legacy.ball.trajectoryType, b.HasTrajectory ? b.TrajectoryType : 0);
            Fe(r, "ball.landingX", legacy.ball.landingX, m.HasBallGameplay ? m.LandingX : 0f);
            Eq(r, "ball.stateFrame", legacy.ball.stateFrame, m.HasBallGameplay ? m.StateFrame : 0);

            Eq(r, "match.leftScore", legacy.match.leftScore, m.LeftScore);
            Eq(r, "match.rightScore", legacy.match.rightScore, m.RightScore);
            Eq(r, "match.servingTeam", legacy.match.servingTeam, m.ServingTeam);
            Eq(r, "match.servingEntity", legacy.match.servingEntity, (int)m.ServingEntity);
            Eq(r, "match.finished", legacy.match.finished, m.Finished ? 1 : 0);
            Eq(r, "match.matchWinner", legacy.match.matchWinner, m.MatchWinner);
            Eq(r, "match.lastTouchEntity", legacy.match.lastTouchEntity, (int)m.LastTouch);
            Eq(r, "match.touchCount", legacy.match.touchCount, (int)m.TouchCount);
        }

        private static void Fe(SimComparisonResult r, string name, float legacy, float decoded)
        {
            r.Checked++;
            if (!SameBits(legacy, decoded)) r.Fail($"v1 {name}: struct={legacy.ToString("R", CultureInfo.InvariantCulture)} decoded={decoded.ToString("R", CultureInfo.InvariantCulture)}");
        }

        private static void Eq<T>(SimComparisonResult r, string name, T legacy, T decoded) where T : IEquatable<T>
        {
            r.Checked++;
            if (!legacy.Equals(decoded)) r.Fail($"v1 {name}: struct={legacy} decoded={decoded}");
        }
    }
}
