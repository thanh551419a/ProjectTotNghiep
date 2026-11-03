#include "SimSnapshotJson.h"

#include <set>
#include <vector>

#include "SimJsonWriter.h"
#include "SimSchema.h"
#include "../../Config/Match/BigRect.h"
#include "../../Config/Match/MatchEntityConfig.h"
#include "../../Config/Match/MatchObjectConfig.h"
#include "../../Config/Match/MatchRuleConfig.h"
#include "../../Config/System/SystemConf.h"
#include "../../Constants/BallGameplayConstants.h"
#include "../../System/TrajectoryTestConfig.h"

using namespace SimSchema;
using SimJson::Writer;

namespace
{
    const char* RoleName(uint32_t id)
    {
        switch (id)
        {
        case GameConfig::PLAYER: return "PLAYER";
        case GameConfig::TEAMMATE_1: return "TEAMMATE_1";
        case GameConfig::TEAMMATE_2: return "TEAMMATE_2";
        case GameConfig::OPPONENT_1: return "OPPONENT_1";
        case GameConfig::OPPONENT_2: return "OPPONENT_2";
        case GameConfig::OPPONENT_3: return "OPPONENT_3";
        case GameConfig::NET: return "NET";
        case GameConfig::BALL: return "BALL";
        default: return "UNKNOWN";
        }
    }

    const char* EntityTeamName(uint32_t id)
    {
        if (id <= GameConfig::TEAMMATE_2)
            return "LEFT";
        if (id >= GameConfig::OPPONENT_1 && id <= GameConfig::OPPONENT_3)
            return "RIGHT";
        return "NONE";
    }

    const char* ControllerName(uint32_t id)
    {
        if (id == GameConfig::PLAYER)
            return "human";
        if (id == GameConfig::OPPONENT_1)
            return MatchRuleConfig::ENABLE_BOT ? "ai" : "human2";
        if (id <= GameConfig::OPPONENT_3)
            return "none";
        return "simulation";
    }

    void WriteVec2(Writer& w, const char* key, const SimMath::Vec2& v)
    {
        w.Key(key);
        w.StartObject();
        w.Key("x");
        SimJson::WriteFloat(w, v.x);
        w.Key("y");
        SimJson::WriteFloat(w, v.y);
        w.EndObject();
    }

    void WriteNum(Writer& w, const char* key, float v)
    {
        w.Key(key);
        SimJson::WriteFloat(w, v);
    }

    void WriteWorld(Writer& w)
    {
        w.Key("world");
        w.StartObject();
        WriteVec2(w, "netPosition", MatchObjectConfig::NETPOSITION);
        WriteVec2(w, "netSize", SimMath::Vec2(MatchObjectConfig::NETSIZE));
        WriteVec2(w, "ballSize", SimMath::Vec2(MatchObjectConfig::BallSize));
        w.Key("netEntity");
        w.Uint(GameConfig::NET);
        w.Key("netHasComponents");
        w.Bool(false);
        w.Key("court");
        w.StartObject();
        WriteNum(w, "rectX", BigRect::RECT_X);
        WriteNum(w, "rectY", BigRect::RECT_Y);
        WriteNum(w, "rectWidth", BigRect::RECT_WIDTH);
        WriteNum(w, "rectHeight", BigRect::RECT_HEIGHT);
        WriteNum(w, "floorY", BigRect::FLOOR_Y);
        WriteNum(w, "netX", BigRect::NET_X);
        WriteNum(w, "left3mLineX", BigRect::LEFT_3M_LINE_X);
        WriteNum(w, "right3mLineX", BigRect::RIGHT_3M_LINE_X);
        WriteNum(w, "left9mLineX", BigRect::LEFT_9M_LINE_X);
        WriteNum(w, "right9mLineX", BigRect::RIGHT_9M_LINE_X);
        WriteNum(w, "leftPositionResetX", BigRect::LEFT_POSITION_RESET_X);
        WriteNum(w, "rightPositionResetX", BigRect::RIGHT_POSITION_RESET_X);
        w.EndObject();
        w.Key("physics");
        w.StartObject();
        WriteNum(w, "minY", SystemConfig::MIN_Y);
        WriteNum(w, "minXLeft", SystemConfig::MIN_X_LEFT);
        WriteNum(w, "maxXLeft", SystemConfig::MAX_X_LEFT);
        WriteNum(w, "minXRight", SystemConfig::MIN_X_RIGHT);
        WriteNum(w, "maxXRight", SystemConfig::MAX_X_RIGHT);
        WriteNum(w, "gravity", SystemConfig::GRAVITY);
        w.Key("jumpFrame");
        w.Int(SystemConfig::JUMP_FRAME);
        WriteNum(w, "pixelsPerMeter", SystemConfig::PIXELS_PER_METER);
        w.Key("tickRate");
        w.Int(SystemConfig::TICK_RATE);
        WriteNum(w, "virtualWidth", SystemConfig::VIRTUAL_WIDTH);
        WriteNum(w, "virtualHeight", SystemConfig::VIRTUAL_HEIGHT);
        w.EndObject();
        w.Key("rules");
        w.StartObject();
        w.Key("scoreToWin");
        w.Int(MatchRuleConfig::SCORE_TO_WIN);
        w.Key("maxTouchPerTeam");
        w.Int(MatchRuleConfig::MAX_TOUCH_PER_TEAM);
        w.Key("enableBot");
        w.Bool(MatchRuleConfig::ENABLE_BOT);
        w.Key("frameFlyUntilReset");
        w.Int(BallConfig::FrameFlyUntilReset);
        w.Key("ballStateFrameAlive");
        w.Int(BallStateFrame::Alive);
        w.Key("ballStateFrameReset");
        w.Int(BallStateFrame::Reset);
        w.EndObject();
        w.Key("actionCooldownMs");
        w.StartArray();
        for (float cd : ActionStateInfo::Cooldown)
            SimJson::WriteFloat(w, cd);
        w.EndArray();
        w.EndObject();
    }

    // ---------------- schema visitor ----------------
    const char* TransitionName(TransitionMode m)
    {
        switch (m)
        {
        case TransitionMode::Always: return "always";
        case TransitionMode::Boundary: return "boundary";
        default: return "none";
        }
    }

    struct SchemaVisitor
    {
        Writer& w;

        void Leaf(const char* name, const char* kind, const char* cType, const char* enumName, TransitionMode m)
        {
            w.StartObject();
            w.Key("name");
            w.String(name ? name : "");
            w.Key("kind");
            w.String(kind);
            w.Key("cType");
            w.String(cType);
            if (enumName)
            {
                w.Key("enum");
                w.String(enumName);
            }
            w.Key("transition");
            w.String(TransitionName(m));
            w.EndObject();
        }
        void Float(const char* n, float, TransitionMode m) { Leaf(n, "float", "float", nullptr, m); }
        void Int(const char* n, int64_t, TransitionMode m) { Leaf(n, "int", "int32", nullptr, m); }
        void UInt(const char* n, uint64_t, TransitionMode m) { Leaf(n, "uint", "uint32", nullptr, m); }
        void Bool(const char* n, bool, TransitionMode m) { Leaf(n, "bool", "bool", nullptr, m); }
        void Enum(const char* n, int64_t, const char* e, TransitionMode m) { Leaf(n, "enum", "enum", e, m); }

        int arrayDepthElement = 0;
        void BeginObject(const char* name)
        {
            if (name == nullptr)
            {
                // array element: describe element once (first element only)
                if (arrayDepthElement++ > 0)
                {
                    skipping = true;
                    return;
                }
                w.Key("element");
                w.StartObject();
                w.Key("kind");
                w.String("object");
                w.Key("fields");
                w.StartArray();
                return;
            }
            w.StartObject();
            w.Key("name");
            w.String(name);
            w.Key("kind");
            w.String("object");
            w.Key("fields");
            w.StartArray();
        }
        bool skipping = false;
        void EndObject()
        {
            if (skipping)
            {
                skipping = false;
                return;
            }
            w.EndArray();
            w.EndObject();
        }
        void BeginArray(const char* name)
        {
            arrayDepthElement = 0;
            w.StartObject();
            w.Key("name");
            w.String(name);
            w.Key("kind");
            w.String("array");
            w.Key("length");
            w.Int(ChunkConfig::CHARACTER_PER_MATCH);
        }
        void EndArray() { w.EndObject(); }
    };

    // While skipping repeated array elements, leaves must not be written.
    struct SkippingSchemaVisitor : SchemaVisitor
    {
        void Float(const char* n, float f, TransitionMode m)
        {
            if (!skipping)
                SchemaVisitor::Float(n, f, m);
        }
        void Int(const char* n, int64_t i, TransitionMode m)
        {
            if (!skipping)
                SchemaVisitor::Int(n, i, m);
        }
        void UInt(const char* n, uint64_t u, TransitionMode m)
        {
            if (!skipping)
                SchemaVisitor::UInt(n, u, m);
        }
        void Bool(const char* n, bool b, TransitionMode m)
        {
            if (!skipping)
                SchemaVisitor::Bool(n, b, m);
        }
        void Enum(const char* n, int64_t v, const char* e, TransitionMode m)
        {
            if (!skipping)
                SchemaVisitor::Enum(n, v, e, m);
        }
    };

    template <class T>
    void WriteTypeSchema(Writer& w)
    {
        w.StartObject();
        w.Key("name");
        w.String(TypeName<T>());
        w.Key("declaredFieldCount");
        w.Uint(static_cast<unsigned>(AggregateFieldCount<T>()));
        w.Key("fields");
        w.StartArray();
        SkippingSchemaVisitor v{{w}};
        T instance{};
        Visit(v, instance);
        w.EndArray();
        w.EndObject();
    }
}  // namespace

namespace SimSnapshotJson
{
    std::string BuildSnapshot(const SimStateView& view)
    {
        SimJson::Buffer buf;
        Writer w(buf);

        const ComponentStorage& cs = *view.components;
        const IntentStorage& is    = *view.intents;
        const auto& statPool       = *view.statPool;

        w.StartObject();
        w.Key("schema");
        w.String(kSnapshotSchemaName);
        w.Key("schemaVersion");
        w.Int(kSnapshotSchemaVersion);
        w.Key("apiVersion");
        w.Int(kNativeApiVersion);
        w.Key("initialized");
        w.Bool(view.initialized);
        w.Key("tick");
        w.Uint64(view.tick);
        w.Key("chunk");
        w.Int(0);
        w.Key("matchId");
        w.Int(ChunkConfig::DEFAULT_MATCH);

        // ---------------- entities ----------------
        std::set<uint32_t> entityIds;
        for (uint32_t e = GameConfig::PLAYER; e <= GameConfig::BALL; ++e)
            entityIds.insert(e);
        std::set<uint32_t> matchIds;
        ForEachPool(cs, is, statPool, [&](const PoolInfo& info, const auto& pool) {
            for (Entity k : pool.entities())
            {
                if (info.section == Section::Entities)
                    entityIds.insert(k);
                else if (info.section == Section::Matches)
                    matchIds.insert(k);
            }
        });

        w.Key("entities");
        w.StartArray();
        for (uint32_t id : entityIds)
        {
            w.StartObject();
            w.Key("id");
            w.Uint(id);
            w.Key("role");
            w.String(RoleName(id));
            w.Key("team");
            w.String(EntityTeamName(id));
            w.Key("controller");
            w.String(ControllerName(id));
            w.Key("components");
            w.StartObject();
            ForEachPool(cs, is, statPool, [&](const PoolInfo& info, const auto& pool) {
                if (info.section != Section::Entities || !pool.has(id))
                    return;
                w.Key(info.name);
                SimJson::WriteValueObject(w, *pool.get(id));
            });
            w.EndObject();
            w.EndObject();
        }
        w.EndArray();

        // ---------------- matches (state pools) ----------------
        w.Key("matches");
        w.StartArray();
        for (uint32_t m : matchIds)
        {
            w.StartObject();
            w.Key("matchId");
            w.Uint(m);
            w.Key("states");
            w.StartObject();
            ForEachPool(cs, is, statPool, [&](const PoolInfo& info, const auto& pool) {
                if (info.section != Section::Matches || !pool.has(m))
                    return;
                w.Key(info.name);
                SimJson::WriteValueObject(w, *pool.get(m));
            });
            w.EndObject();
            w.EndObject();
        }
        w.EndArray();

        // ---------------- intents (per tick) ----------------
        w.Key("intents");
        w.StartObject();
        w.Key("lifetime");
        w.String("tick");
        w.Key("producedByTick");
        if (view.intentsProducedByTick >= 0)
            w.Int64(view.intentsProducedByTick);
        else
            w.Null();
        w.Key("pools");
        w.StartObject();
        ForEachPool(cs, is, statPool, [&](const PoolInfo& info, const auto& pool) {
            if (info.section != Section::Intents)
                return;
            w.Key(info.name);
            w.StartArray();
            const auto& keys = pool.entities();
            const auto& vals = pool.components();
            for (size_t i = 0; i < keys.size(); ++i)
            {
                w.StartObject();
                w.Key("entity");
                w.Uint(keys[i]);
                w.Key("value");
                SimJson::WriteValueObject(w, vals[i]);
                w.EndObject();
            }
            w.EndArray();
        });
        w.EndObject();
        w.EndObject();

        // ---------------- match data ----------------
        w.Key("matchData");
        w.StartObject();
        w.Key("characterStatStorage");
        w.StartObject();
        w.Key("loaded");
        w.Bool(view.statsLoaded);
        w.Key("readBySimulation");
        w.Bool(false);
        w.Key("matchStatPool");
        w.StartArray();
        {
            const auto& keys = statPool.entities();
            const auto& vals = statPool.components();
            for (size_t i = 0; i < keys.size(); ++i)
            {
                w.StartObject();
                w.Key("matchId");
                w.Uint(keys[i]);
                w.Key("value");
                SimJson::WriteValueObject(w, vals[i]);
                w.EndObject();
            }
        }
        w.EndArray();
        w.EndObject();
        w.EndObject();

        // ---------------- pools table (coverage / identity) ----------------
        w.Key("pools");
        w.StartArray();
        ForEachPool(cs, is, statPool, [&](const PoolInfo& info, const auto& pool) {
            w.StartObject();
            w.Key("name");
            w.String(info.name);
            w.Key("storage");
            w.String(info.storage);
            w.Key("type");
            w.String(info.typeName);
            w.Key("key");
            w.String(KeyKindName(info.key));
            w.Key("section");
            w.String(SectionName(info.section));
            w.Key("lifetime");
            w.String(info.lifetime);
            w.Key("count");
            w.Uint64(pool.size());
            w.Key("keys");
            w.StartArray();
            for (Entity k : pool.entities())
                w.Uint(k);
            w.EndArray();
            w.EndObject();
        });
        w.EndArray();

        // ---------------- globals ----------------
        const TestTrajectory& tt = TestTrajectory::getInstance();
        const InputMapping& im   = InputMapping::GetInstance();
        w.Key("globals");
        w.StartObject();
        w.Key("testTrajectory");
        w.StartObject();
        w.Key("frame");
        w.Int(tt.frame);
        WriteNum(w, "AttackPower", tt.AttackPower);
        w.EndObject();
        w.Key("input");
        w.StartObject();
        w.Key("entity");
        w.Uint(GameConfig::PLAYER);
        w.Key("holdingButtons");
        w.Uint(view.input.holdingButtons);
        w.Key("pressedButtons");
        w.Uint(view.input.pressedButtons);
        WriteNum(w, "moveAxisX", view.input.moveAxisX);
        w.Key("prevButtonMask");
        w.Uint(view.prevButtonMask);
        w.EndObject();
        w.Key("inputMapping");
        w.StartObject();
        WriteNum(w, "playerSpeed", im.GetPlayerSpeed());
        WriteNum(w, "aiSpeed", im.GetAISpeed());
        WriteNum(w, "aiAttackHeightThreshold", im.GetAIAttackHeightThreshold());
        w.EndObject();
        w.Key("runtime");
        w.StartObject();
        w.Key("lastUpdateFrozenByVictory");
        w.Bool(view.lastUpdateFrozenByVictory);
        w.EndObject();
        w.EndObject();

        WriteWorld(w);

        w.EndObject();
        return std::string(buf.GetString(), buf.GetSize());
    }

    std::string BuildSchema()
    {
        SimJson::Buffer buf;
        Writer w(buf);
        w.StartObject();
        w.Key("schema");
        w.String(kSnapshotSchemaName);
        w.Key("schemaVersion");
        w.Int(kSnapshotSchemaVersion);
        w.Key("apiVersion");
        w.Int(kNativeApiVersion);
        w.Key("coordinateSystem");
        w.String("world pixels, origin bottom-left, +x right, +y up; positions are AABB bottom-left corners");
        w.Key("nonFiniteFloatEncoding");
        w.String("strings: NaN, +Inf, -Inf");

        w.Key("types");
        w.StartArray();
        WriteTypeSchema<PositionComponent>(w);
        WriteTypeSchema<SizeComponent>(w);
        WriteTypeSchema<JumpUpFrameComponent>(w);
        WriteTypeSchema<BallTrajectoryComponent>(w);
        WriteTypeSchema<CharacterActionState>(w);
        WriteTypeSchema<BallGamePlayState>(w);
        WriteTypeSchema<MatchState>(w);
        WriteTypeSchema<RallyState>(w);
        WriteTypeSchema<CharacterIntent>(w);
        WriteTypeSchema<BallIntent>(w);
        WriteTypeSchema<VelocityIntent>(w);
        WriteTypeSchema<MatchCharacterStat>(w);
        w.EndArray();

        w.Key("enums");
        w.StartArray();
        for (const auto& e : kEnums)
        {
            w.StartObject();
            w.Key("name");
            w.String(e.name);
            w.Key("underlying");
            w.String(e.underlying);
            w.Key("values");
            w.StartArray();
            for (int i = 0; i < e.count; ++i)
                w.String(e.names[i]);
            w.EndArray();
            w.EndObject();
        }
        w.EndArray();

        // Pool table from a throwaway empty storage (metadata only).
        w.Key("pools");
        w.StartArray();
        {
            static const ComponentStorage emptyCs;
            static const IntentStorage emptyIs;
            static const ComponentPool<MatchCharacterStat> emptyStats;
            ForEachPool(emptyCs, emptyIs, emptyStats, [&](const PoolInfo& info, const auto&) {
                w.StartObject();
                w.Key("name");
                w.String(info.name);
                w.Key("storage");
                w.String(info.storage);
                w.Key("type");
                w.String(info.typeName);
                w.Key("key");
                w.String(KeyKindName(info.key));
                w.Key("section");
                w.String(SectionName(info.section));
                w.Key("lifetime");
                w.String(info.lifetime);
                w.EndObject();
            });
        }
        w.EndArray();

        w.Key("excluded");
        w.StartArray();
        auto excl = [&](const char* path, const char* reason) {
            w.StartObject();
            w.Key("path");
            w.String(path);
            w.Key("reason");
            w.String(reason);
            w.EndObject();
        };
        excl("ComponentStorage::sizeTemp", "public scratch member; never read or written by any system");
        excl("ComponentPool<T>::sparse / vector capacity", "index structure and unused capacity, not simulation data");
        excl("StateStorage, Storage", "classes are never instantiated by any runtime; state lives in ComponentStorage");
        excl("TestTrajectory::{Table,x,y,data,tempTrajectory,Change,source,GotoX,step}",
             "Axmol trajectory test tool only; not read by native systems");
        excl("GetPositionData static cache[8]", "derived per tick from positions/sizes + world config");
        excl("Vec2/Size width,height", "anonymous-union aliases of x,y (same storage)");
        w.EndArray();

        w.EndObject();
        return std::string(buf.GetString(), buf.GetSize());
    }
}  // namespace SimSnapshotJson
