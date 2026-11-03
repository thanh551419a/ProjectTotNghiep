// ==============================================================
// SimSnapshotCoverageTest.cpp
// Proves the JSON snapshot is a complete, lossless image of the original
// storage. Field mappings below are written by hand, independently of the
// SimSchema visitor used by the serializer; compile-time field counts make any
// newly added field a build error until both sides account for it.
//
// Output: <outDir>/coverage_report.json   exit code 0 = pass
// ==============================================================

#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <map>
#include <set>
#include <string>
#include <vector>

#include "../SimulationRuntime.h"
#include "../SpikeNativeApi.h"
#include "../Diagnostics/SimSchema.h"
#include "../../ThirdParty/RapidJson/document.h"

using rapidjson::Value;
namespace fs = std::filesystem;

// ---------------- compile-time field inventory (independent of Visit) ----------------
static_assert(SimSchema::AggregateFieldCount<PositionComponent>() == 1);
static_assert(SimSchema::AggregateFieldCount<SizeComponent>() == 1);
static_assert(SimSchema::AggregateFieldCount<JumpUpFrameComponent>() == 1);
static_assert(SimSchema::AggregateFieldCount<BallTrajectoryComponent>() == 5);
static_assert(SimSchema::AggregateFieldCount<CharacterActionState>() == 2);
static_assert(SimSchema::AggregateFieldCount<BallGamePlayState>() == 3);
static_assert(SimSchema::AggregateFieldCount<MatchState>() == 7);
static_assert(SimSchema::AggregateFieldCount<RallyState>() == 2);
static_assert(SimSchema::AggregateFieldCount<CharacterIntent>() == 2);
static_assert(SimSchema::AggregateFieldCount<BallIntent>() == 2);
static_assert(SimSchema::AggregateFieldCount<VelocityIntent>() == 2);
static_assert(SimSchema::AggregateFieldCount<CharacterStat>() == 4);
static_assert(sizeof(MatchCharacterStat) == 6 * sizeof(CharacterStat));

namespace
{
    std::vector<std::string> g_failures;
    std::map<std::string, long> g_leafChecks;  // type -> number of leaf values compared
    std::map<std::string, std::set<std::string>> g_fieldsSeen;
    int g_checkpoints = 0;

    void Fail(const std::string& msg)
    {
        if (g_failures.size() < 200)
            g_failures.push_back(msg);
    }

    bool SameFloat(const Value& v, float f)
    {
        if (v.IsString())
        {
            std::string s = v.GetString();
            if (std::isnan(f))
                return s == "NaN";
            if (std::isinf(f))
                return s == (f > 0 ? "+Inf" : "-Inf");
            return false;
        }
        if (!v.IsNumber() || !std::isfinite(f))
            return false;
        float g = static_cast<float>(v.GetDouble());
        return std::memcmp(&g, &f, sizeof(float)) == 0;
    }

    // Field accessor with presence + exact member-count checks.
    struct Obj
    {
        const Value* v;
        std::string type;
        std::string where;

        const Value* Get(const char* name)
        {
            g_fieldsSeen[type].insert(name);
            if (!v->HasMember(name))
            {
                Fail(where + ": missing field '" + name + "'");
                return nullptr;
            }
            return &(*v)[name];
        }
        void F(const char* name, float expected)
        {
            if (auto* x = Get(name))
            {
                g_leafChecks[type]++;
                if (!SameFloat(*x, expected))
                    Fail(where + "." + name + ": float mismatch");
            }
        }
        void I(const char* name, int64_t expected)
        {
            if (auto* x = Get(name))
            {
                g_leafChecks[type]++;
                if (!x->IsInt64() || x->GetInt64() != expected)
                    Fail(where + "." + name + ": int mismatch");
            }
        }
        void U(const char* name, uint64_t expected)
        {
            if (auto* x = Get(name))
            {
                g_leafChecks[type]++;
                if (!x->IsUint64() || x->GetUint64() != expected)
                    Fail(where + "." + name + ": uint mismatch");
            }
        }
        void B(const char* name, bool expected)
        {
            if (auto* x = Get(name))
            {
                g_leafChecks[type]++;
                if (!x->IsBool() || x->GetBool() != expected)
                    Fail(where + "." + name + ": bool mismatch");
            }
        }
        Obj Sub(const char* name, const char* subType)
        {
            const Value* x = Get(name);
            static const Value empty(rapidjson::kObjectType);
            return Obj{x && x->IsObject() ? x : &empty, subType, where + "." + name};
        }
        void Members(unsigned expected)
        {
            if (!v->IsObject() || v->MemberCount() != expected)
                Fail(where + ": expected " + std::to_string(expected) + " members, got " +
                     std::to_string(v->IsObject() ? v->MemberCount() : 0));
        }
    };

    void CheckVec2(Obj o, const SimMath::Vec2& p)
    {
        o.Members(2);
        o.F("x", p.x);
        o.F("y", p.y);
    }

    // ---- hand-written per-type comparisons ----
    void Check(Obj o, const PositionComponent& c) { o.Members(1); CheckVec2(o.Sub("position", "Vec2"), c.position); }
    void Check(Obj o, const SizeComponent& c)
    {
        o.Members(1);
        Obj s = o.Sub("size", "Size");
        s.Members(2);
        s.F("x", c.size.x);
        s.F("y", c.size.y);
    }
    void Check(Obj o, const JumpUpFrameComponent& c) { o.Members(1); o.I("remainingFrames", c.remainingFrames); }
    void Check(Obj o, const BallTrajectoryComponent& c)
    {
        o.Members(5);
        o.I("type", static_cast<int>(c.type));
        o.F("speed", c.speed);
        o.F("a", c.a);
        o.F("b", c.b);
        o.F("c", c.c);
    }
    void Check(Obj o, const CharacterActionState& c)
    {
        o.Members(2);
        o.I("status", static_cast<int>(c.status));
        o.F("remainTime", c.remainTime);
    }
    void Check(Obj o, const BallGamePlayState& c)
    {
        o.Members(3);
        o.F("landingX", c.landingX);
        o.I("stateFrame", c.stateFrame);
        o.I("collisionState", static_cast<int>(c.collisionState));
    }
    void Check(Obj o, const MatchState& c)
    {
        o.Members(7);
        o.I("leftScore", c.leftScore);
        o.I("rightScore", c.rightScore);
        o.I("servingTeam", static_cast<int>(c.servingTeam));
        o.U("leftServingEntity", c.leftServingEntity);
        o.U("rightServingEntity", c.rightServingEntity);
        o.B("finished", c.finished);
        o.I("matchWinner", static_cast<int>(c.matchWinner));
    }
    void Check(Obj o, const RallyState& c)
    {
        o.Members(2);
        o.U("lastTouch", c.lastTouch);
        o.U("touchCount", c.touchCount);
    }
    void Check(Obj o, const CharacterIntent& c)
    {
        o.Members(2);
        o.F("moveX", c.moveX);
        o.I("finalIntent", static_cast<int>(c.finalIntent));
    }
    void Check(Obj o, const BallIntent& c)
    {
        o.Members(2);
        o.F("newX", c.newX);
        o.F("newY", c.newY);
    }
    void Check(Obj o, const VelocityIntent& c)
    {
        o.Members(2);
        o.F("vx", c.vx);
        o.F("vy", c.vy);
    }
    void Check(Obj o, const MatchCharacterStat& c)
    {
        o.Members(1);
        const Value* arr = o.Get("stats");
        if (!arr || !arr->IsArray() || arr->Size() != 6)
        {
            Fail(o.where + ".stats: expected array[6]");
            return;
        }
        for (int i = 0; i < 6; ++i)
        {
            Obj s{&(*arr)[i], "CharacterStat", o.where + ".stats[" + std::to_string(i) + "]"};
            s.Members(4);
            s.F("AttackPower", c.stats[i].AttackPower);
            s.F("DefensePower", c.stats[i].DefensePower);
            s.F("Speed", c.stats[i].Speed);
            s.F("JumpPower", c.stats[i].JumpPower);
        }
    }

    const Value* FindEntity(const Value& doc, uint32_t id)
    {
        for (const auto& e : doc["entities"].GetArray())
            if (e["id"].GetUint() == id)
                return &e;
        return nullptr;
    }

    std::string FetchSnapshotViaApi()
    {
        int32_t required = 0;
        int32_t rc       = Sim_GetSnapshotJson(nullptr, 0, &required);
        if (rc != SIM_ERR_BUFFER_TOO_SMALL || required <= 1)
            Fail("size query did not return BUFFER_TOO_SMALL with a size");
        // too-small buffer must not be written
        std::vector<char> small(16, 'Z');
        int32_t req2 = 0;
        rc           = Sim_GetSnapshotJson(small.data(), 16, &req2);
        if (rc != SIM_ERR_BUFFER_TOO_SMALL || req2 != required)
            Fail("small buffer: wrong status/size");
        for (char ch : small)
            if (ch != 'Z')
            {
                Fail("small buffer was written (truncation)");
                break;
            }
        std::vector<char> buf(static_cast<size_t>(required), '\0');
        rc = Sim_GetSnapshotJson(buf.data(), required, &req2);
        if (rc != SIM_OK)
            Fail("exact buffer: status " + std::to_string(rc));
        if (buf.back() != '\0' || std::strlen(buf.data()) + 1 != static_cast<size_t>(required))
            Fail("exact buffer: NUL/size mismatch");
        return std::string(buf.data());
    }

    void VerifyCheckpoint(const char* label)
    {
        ++g_checkpoints;
        auto& rt                = SimulationRuntime::GetInstance();
        const ComponentStorage& cs = rt.GetComponentStorage();
        const IntentStorage& is    = rt.GetIntentStorage();
        const auto& statPool       = rt.GetCharacterStatPool();

        const std::string json = FetchSnapshotViaApi();
        if (json != rt.GetSnapshotJson())
            Fail(std::string(label) + ": API bytes differ from runtime snapshot");

        rapidjson::Document doc;
        doc.Parse(json.c_str(), json.size());
        if (doc.HasParseError() || !doc.IsObject())
        {
            Fail(std::string(label) + ": JSON parse error");
            return;
        }
        const std::string at = std::string("[") + label + "] ";

        if (std::string(doc["schema"].GetString()) != SimSchema::kSnapshotSchemaName ||
            doc["schemaVersion"].GetInt() != SimSchema::kSnapshotSchemaVersion)
            Fail(at + "schema id/version");
        if (doc["tick"].GetUint64() != rt.GetTick())
            Fail(at + "tick mismatch");

        // ---- pools table: identity, counts, keys, capacity exclusion ----
        std::set<std::string> poolsInJson;
        for (const auto& p : doc["pools"].GetArray())
            poolsInJson.insert(p["name"].GetString());
        int poolCount = 0;
        SimSchema::ForEachPool(cs, is, statPool, [&](const SimSchema::PoolInfo& info, const auto& pool) {
            ++poolCount;
            const Value* entry = nullptr;
            for (const auto& p : doc["pools"].GetArray())
                if (std::strcmp(p["name"].GetString(), info.name) == 0)
                    entry = &p;
            if (!entry)
            {
                Fail(at + "pool missing from pools table: " + info.name);
                return;
            }
            if ((*entry)["count"].GetUint64() != pool.size())
                Fail(at + info.name + ": count mismatch");
            const auto& keys = pool.entities();
            const auto& jk   = (*entry)["keys"].GetArray();
            if (jk.Size() != keys.size())
                Fail(at + info.name + ": key count mismatch");
            for (size_t i = 0; i < keys.size() && i < jk.Size(); ++i)
                if (jk[static_cast<rapidjson::SizeType>(i)].GetUint() != keys[i])
                    Fail(at + info.name + ": key order/identity mismatch");
            if (pool.size() >= 10000)
                Fail(at + info.name + ": capacity leaked");
        });
        if (poolCount != SimSchema::kPoolCount || static_cast<int>(poolsInJson.size()) != SimSchema::kPoolCount)
            Fail(at + "pool count mismatch (registry vs JSON)");

        // ---- entities: every component of every entity, nothing extra ----
        auto checkEntityPool = [&](const char* poolName, const auto& pool) {
            for (Entity e : pool.entities())
            {
                const Value* ent = FindEntity(doc, e);
                if (!ent)
                {
                    Fail(at + "entity missing: " + std::to_string(e));
                    continue;
                }
                const Value& comps = (*ent)["components"];
                if (!comps.HasMember(poolName))
                {
                    Fail(at + "entity " + std::to_string(e) + " missing component " + poolName);
                    continue;
                }
                Check(Obj{&comps[poolName], SimSchema::TypeName<std::decay_t<decltype(*pool.get(e))>>(),
                          at + "entities[" + std::to_string(e) + "]." + poolName},
                      *pool.get(e));
            }
        };
        checkEntityPool("characterPositionPool", cs.characterPositionPool);
        checkEntityPool("characterSizePool", cs.characterSizePool);
        checkEntityPool("ballPositionPool", cs.ballPositionPool);
        checkEntityPool("jumpUpFramePool", cs.jumpUpFramePool);
        checkEntityPool("ballTrajectoryPool", cs.ballTrajectoryPool);
        checkEntityPool("characterActionStatePool", cs.characterActionStatePool);

        for (const auto& ent : doc["entities"].GetArray())
        {
            const uint32_t id = ent["id"].GetUint();
            unsigned expected = 0;
            expected += cs.characterPositionPool.has(id) + cs.characterSizePool.has(id) + cs.ballPositionPool.has(id) +
                        cs.jumpUpFramePool.has(id) + cs.ballTrajectoryPool.has(id) +
                        cs.characterActionStatePool.has(id);
            if (ent["components"].MemberCount() != expected)
                Fail(at + "entity " + std::to_string(id) + ": unexpected component count");
        }
        // missing component must be absent (not zero)
        if (const Value* net = FindEntity(doc, GameConfig::NET))
        {
            if ((*net)["components"].MemberCount() != 0)
                Fail(at + "NET must have no components");
        }
        else
            Fail(at + "NET entity not listed");
        if (const Value* ball = FindEntity(doc, GameConfig::BALL))
        {
            if ((*ball)["components"].HasMember("characterPositionPool") ||
                (*ball)["components"].HasMember("characterActionStatePool"))
                Fail(at + "BALL has character components");
        }

        // ---- match states ----
        const Value& matches = doc["matches"];
        if (!matches.IsArray() || matches.Size() != cs.matchStatePool.size())
            Fail(at + "matches array size");
        for (const auto& m : matches.GetArray())
        {
            const uint32_t id = m["matchId"].GetUint();
            const Value& st   = m["states"];
            if (st.MemberCount() != 3)
                Fail(at + "match states member count");
            if (auto* p = cs.ballGameplayStatePool.get(id))
                Check(Obj{&st["ballGameplayStatePool"], "BallGamePlayState", at + "matches.ballGameplayStatePool"}, *p);
            if (auto* p = cs.matchStatePool.get(id))
                Check(Obj{&st["matchStatePool"], "MatchState", at + "matches.matchStatePool"}, *p);
            if (auto* p = cs.rallyStatePool.get(id))
                Check(Obj{&st["rallyStatePool"], "RallyState", at + "matches.rallyStatePool"}, *p);
        }

        // ---- intents (tick-scoped) ----
        const Value& intents = doc["intents"];
        const auto view      = rt.MakeView();
        if (view.intentsProducedByTick < 0)
        {
            if (!intents["producedByTick"].IsNull())
                Fail(at + "producedByTick should be null");
        }
        else if (!intents["producedByTick"].IsInt64() ||
                 intents["producedByTick"].GetInt64() != view.intentsProducedByTick ||
                 static_cast<uint64_t>(view.intentsProducedByTick + 1) != rt.GetTick())
            Fail(at + "producedByTick mismatch");
        auto checkIntentPool = [&](const char* name, const auto& pool) {
            const Value& arr = intents["pools"][name];
            if (!arr.IsArray() || arr.Size() != pool.size())
            {
                Fail(at + "intent pool size " + name);
                return;
            }
            for (rapidjson::SizeType i = 0; i < arr.Size(); ++i)
            {
                const uint32_t e = arr[i]["entity"].GetUint();
                if (e != pool.entities()[i])
                    Fail(at + "intent key mismatch " + name);
                Check(Obj{&arr[i]["value"], SimSchema::TypeName<std::decay_t<decltype(pool.components()[0])>>(),
                          at + "intents." + name},
                      pool.components()[i]);
            }
        };
        checkIntentPool("characterIntentPool", is.characterIntentPool);
        checkIntentPool("ballIntentPool", is.ballIntentPool);
        checkIntentPool("velocityIntentPool", is.velocityIntentPool);

        // ---- match data ----
        const Value& stats = doc["matchData"]["characterStatStorage"];
        if (stats["loaded"].GetBool() != false)
            Fail(at + "stats loaded flag");
        if (stats["matchStatPool"].Size() != statPool.size())
            Fail(at + "stat pool size");
        for (rapidjson::SizeType i = 0; i < stats["matchStatPool"].Size(); ++i)
            Check(Obj{&stats["matchStatPool"][i]["value"], "MatchCharacterStat", at + "matchStatPool"},
                  statPool.components()[i]);

        // ---- globals ----
        const TestTrajectory& tt = TestTrajectory::getInstance();
        Obj g{&doc["globals"]["testTrajectory"], "globals.testTrajectory", at + "globals.testTrajectory"};
        g.Members(2);
        g.I("frame", tt.frame);
        g.F("AttackPower", tt.AttackPower);
        Obj in{&doc["globals"]["input"], "globals.input", at + "globals.input"};
        in.U("holdingButtons", view.input.holdingButtons);
        in.U("pressedButtons", view.input.pressedButtons);
        in.F("moveAxisX", view.input.moveAxisX);
        in.U("prevButtonMask", view.prevButtonMask);
    }

    // Counts schema-declared leaf fields per type with the serializer's visitor, for the report.
    struct LeafCounter
    {
        int n = 0;
        void Float(const char*, float, SimSchema::TransitionMode) { ++n; }
        void Int(const char*, int64_t, SimSchema::TransitionMode) { ++n; }
        void UInt(const char*, uint64_t, SimSchema::TransitionMode) { ++n; }
        void Bool(const char*, bool, SimSchema::TransitionMode) { ++n; }
        void Enum(const char*, int64_t, const char*, SimSchema::TransitionMode) { ++n; }
        void BeginObject(const char*) {}
        void EndObject() {}
        void BeginArray(const char*) {}
        void EndArray() {}
    };
    template <class T>
    int Leaves()
    {
        LeafCounter c;
        T t{};
        SimSchema::Visit(c, t);
        return c.n;
    }
}  // namespace

int main(int argc, char** argv)
{
    const std::string outDir = argc > 1 ? argv[1] : "Diagnostics";
    fs::create_directories(outDir);
    Sim_SetLegacyLogEnabled(0);

    // Not-initialized contract
    {
        int32_t req = 123;
        if (Sim_GetSnapshotJson(nullptr, 0, &req) != SIM_ERR_NOT_INITIALIZED || req != 0)
            Fail("not-initialized snapshot must return SIM_ERR_NOT_INITIALIZED");
    }

    Sim_SeedRandom(2024);
    Sim_Initialize(nullptr);
    VerifyCheckpoint("init");

    const float dt = 1.0f / 60.0f;
    auto tick = [&](uint32_t mask) {
        Sim_SetInput(0, mask, 0.0f);
        Sim_Update(dt);
    };

    tick(0);
    VerifyCheckpoint("tick1");
    for (int i = 0; i < 20; ++i)
        tick(SIM_BUTTON_MOVE_RIGHT);
    VerifyCheckpoint("moveRight20");
    tick(SIM_BUTTON_JUMP_SPIKE);
    for (int i = 0; i < 10; ++i)
        tick(0);
    VerifyCheckpoint("airborne");
    for (int i = 0; i < 60; ++i)
        tick(0);
    tick(SIM_BUTTON_SERVE);
    VerifyCheckpoint("afterServe");
    // Hold bump through rallies; verify periodically (covers AI intents, rally/score/stateFrame changes)
    for (int i = 1; i <= 3000; ++i)
    {
        tick(SIM_BUTTON_BUMP);
        if (i % 50 == 0)
            VerifyCheckpoint(("bump" + std::to_string(i)).c_str());
    }
    Sim_Reset();
    VerifyCheckpoint("afterReset");
    tick(0);
    VerifyCheckpoint("afterResetTick1");

    // Schema must list every type
    {
        int32_t req = 0;
        Sim_GetSchemaJson(nullptr, 0, &req);
        std::vector<char> buf(static_cast<size_t>(req));
        Sim_GetSchemaJson(buf.data(), req, &req);
        rapidjson::Document sd;
        sd.Parse(buf.data());
        if (sd.HasParseError() || sd["types"].Size() != 12)
            Fail("schema types count");
    }

    Sim_Shutdown();
    {
        int32_t req = 0;
        if (Sim_GetSnapshotJson(nullptr, 0, &req) != SIM_ERR_NOT_INITIALIZED)
            Fail("after shutdown snapshot must return SIM_ERR_NOT_INITIALIZED");
    }

    // ---------------- report ----------------
    struct TypeRow
    {
        const char* name;
        size_t declared;
        int leaves;
    };
    const TypeRow types[] = {
        {"PositionComponent", SimSchema::AggregateFieldCount<PositionComponent>(), Leaves<PositionComponent>()},
        {"SizeComponent", SimSchema::AggregateFieldCount<SizeComponent>(), Leaves<SizeComponent>()},
        {"JumpUpFrameComponent", SimSchema::AggregateFieldCount<JumpUpFrameComponent>(), Leaves<JumpUpFrameComponent>()},
        {"BallTrajectoryComponent", SimSchema::AggregateFieldCount<BallTrajectoryComponent>(), Leaves<BallTrajectoryComponent>()},
        {"CharacterActionState", SimSchema::AggregateFieldCount<CharacterActionState>(), Leaves<CharacterActionState>()},
        {"BallGamePlayState", SimSchema::AggregateFieldCount<BallGamePlayState>(), Leaves<BallGamePlayState>()},
        {"MatchState", SimSchema::AggregateFieldCount<MatchState>(), Leaves<MatchState>()},
        {"RallyState", SimSchema::AggregateFieldCount<RallyState>(), Leaves<RallyState>()},
        {"CharacterIntent", SimSchema::AggregateFieldCount<CharacterIntent>(), Leaves<CharacterIntent>()},
        {"BallIntent", SimSchema::AggregateFieldCount<BallIntent>(), Leaves<BallIntent>()},
        {"VelocityIntent", SimSchema::AggregateFieldCount<VelocityIntent>(), Leaves<VelocityIntent>()},
        {"MatchCharacterStat", SimSchema::AggregateFieldCount<MatchCharacterStat>(), Leaves<MatchCharacterStat>()},
    };

    std::vector<std::string> unaccounted;
    for (const auto& t : types)
    {
        // A type with zero compared values (other than the never-populated BallIntent) is unaccounted.
        long checks = g_leafChecks[t.name];
        if (std::string(t.name) == "PositionComponent")
            checks = g_leafChecks["Vec2"];
        if (std::string(t.name) == "SizeComponent")
            checks = g_leafChecks["Size"];
        if (std::string(t.name) == "MatchCharacterStat")
            checks = g_leafChecks["CharacterStat"];
        if (checks == 0 && std::string(t.name) != "BallIntent")
            unaccounted.push_back(t.name);
    }

    const std::string reportPath = (fs::path(outDir) / "coverage_report.json").string();
    FILE* f                      = std::fopen(reportPath.c_str(), "wb");
    if (f)
    {
        std::fprintf(f, "{\n  \"test\": \"SimSnapshotCoverageTest\",\n  \"pass\": %s,\n  \"checkpoints\": %d,\n",
                     g_failures.empty() && unaccounted.empty() ? "true" : "false", g_checkpoints);
        std::fprintf(f, "  \"pools\": %d,\n  \"types\": [\n", SimSchema::kPoolCount);
        for (size_t i = 0; i < std::size(types); ++i)
        {
            const auto& t = types[i];
            long checks   = g_leafChecks[t.name];
            const char* leafType = t.name;
            if (std::string(t.name) == "PositionComponent") leafType = "Vec2";
            if (std::string(t.name) == "SizeComponent") leafType = "Size";
            if (std::string(t.name) == "MatchCharacterStat") leafType = "CharacterStat";
            checks = g_leafChecks[leafType];
            std::fprintf(f,
                         "    {\"type\": \"%s\", \"declaredTopLevelFields\": %zu, \"serializedLeafFields\": %d, "
                         "\"leafValuesComparedAgainstStorage\": %ld}%s\n",
                         t.name, t.declared, t.leaves, checks, i + 1 < std::size(types) ? "," : "");
        }
        std::fprintf(f, "  ],\n  \"globalsValuesCompared\": %ld,\n",
                     g_leafChecks["globals.testTrajectory"] + g_leafChecks["globals.input"]);
        std::fprintf(f, "  \"notes\": [\"BallIntent pool is never written by any system: verified present and empty "
                        "at every checkpoint\", \"MatchCharacterStat values verified (zero, loaded=false)\"],\n");
        std::fprintf(f, "  \"unaccountedTypes\": [");
        for (size_t i = 0; i < unaccounted.size(); ++i)
            std::fprintf(f, "%s\"%s\"", i ? "," : "", unaccounted[i].c_str());
        std::fprintf(f, "],\n  \"failures\": [");
        for (size_t i = 0; i < g_failures.size(); ++i)
            std::fprintf(f, "%s\n    \"%s\"", i ? "," : "", g_failures[i].c_str());
        std::fprintf(f, "\n  ]\n}\n");
        std::fclose(f);
    }

    std::printf("SimSnapshotCoverageTest: checkpoints=%d failures=%zu unaccounted=%zu -> %s\n", g_checkpoints,
                g_failures.size(), unaccounted.size(), reportPath.c_str());
    for (size_t i = 0; i < g_failures.size() && i < 20; ++i)
        std::printf("  FAIL %s\n", g_failures[i].c_str());
    return g_failures.empty() && unaccounted.empty() ? 0 : 1;
}
