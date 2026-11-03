// ==============================================================
// SimDiagRunner.cpp
// Headless runner for the REAL simulation (same SimulationRuntime / systems /
// storages as the Unity DLL). No gameplay rule is duplicated here: the runner
// only submits button masks through the public C API, exactly like Unity.
//
// The "reactive controller" decides which buttons a human would press from the
// observed snapshot (e.g. "ball is coming to my side -> hold Bump"). Whether a
// hit actually happens is decided solely by the simulation systems.
//
// Usage: SimDiagRunner <outputDir> [scenarioName|all]
// ==============================================================

#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <functional>
#include <map>
#include <string>
#include <vector>

#include "../SimulationRuntime.h"
#include "../SpikeNativeApi.h"

namespace fs = std::filesystem;

namespace
{
    constexpr float kDt = 1.0f / 60.0f;
    std::string g_outDir = "Diagnostics";

    SimulationRuntime& RT() { return SimulationRuntime::GetInstance(); }
    const ComponentStorage& CS() { return RT().GetComponentStorage(); }

    std::string Path(const std::string& name) { return (fs::path(g_outDir) / name).string(); }

    std::string Q(const std::string& s) { return "\"" + s + "\""; }

    // ---------------- observation helpers (read-only) ----------------
    struct Obs
    {
        float px, py, pw, ph;
        float ax, ay;  // OPPONENT_1
        float bx, by;
        int stateFrame;
        float landingX;
        float speed, a, b, c;
        int trajType;
        int status0, status3;
        float remain0;
        int leftScore, rightScore, servingTeam;
        uint32_t lastTouch;
        int touchCount;
        bool finished;
        int jump0;
    };

    Obs Observe()
    {
        const auto& cs = CS();
        Obs o{};
        auto* p  = cs.characterPositionPool.get(0);
        auto* s  = cs.characterSizePool.get(0);
        auto* a  = cs.characterPositionPool.get(3);
        auto* b  = cs.ballPositionPool.get(7);
        auto* bs = cs.ballGameplayStatePool.get(0);
        auto* tr = cs.ballTrajectoryPool.get(7);
        auto* ms = cs.matchStatePool.get(0);
        auto* rs = cs.rallyStatePool.get(0);
        o.px = p->position.x; o.py = p->position.y; o.pw = s->size.x; o.ph = s->size.y;
        o.ax = a->position.x; o.ay = a->position.y;
        o.bx = b->position.x; o.by = b->position.y;
        o.stateFrame = bs->stateFrame; o.landingX = bs->landingX;
        o.speed = tr->speed; o.a = tr->a; o.b = tr->b; o.c = tr->c; o.trajType = static_cast<int>(tr->type);
        o.status0 = static_cast<int>(cs.characterActionStatePool.get(0)->status);
        o.status3 = static_cast<int>(cs.characterActionStatePool.get(3)->status);
        o.remain0 = cs.characterActionStatePool.get(0)->remainTime;
        o.leftScore = ms->leftScore; o.rightScore = ms->rightScore; o.servingTeam = static_cast<int>(ms->servingTeam);
        o.lastTouch = rs->lastTouch; o.touchCount = rs->touchCount; o.finished = ms->finished;
        o.jump0 = cs.jumpUpFramePool.get(0)->remainingFrames;
        return o;
    }

    // ---------------- statistics gathered from observed state between ticks ----------------
    struct Stats
    {
        std::map<std::string, int> playerActions;  // status0 transitions None->X
        std::map<std::string, int> aiActions;      // status3 transitions None->X
        int pointsLeft = 0, pointsRight = 0;
        int maxTouchCount = 0;
        int linearTrajectoryTicks = 0;
        int ticks = 0;
        int nanTicks = 0;
        int playerJumps = 0;
        bool finished = false;
        uint64_t finishedAtTick = 0;
    };

    const char* ActName(int s) { return SimSchema::kActionStateNames[s]; }

    void Accumulate(Stats& st, const Obs& before, const Obs& after)
    {
        st.ticks++;
        if (after.status0 != before.status0 && after.status0 != 0)
            st.playerActions[ActName(after.status0)]++;
        if (after.status3 != before.status3 && after.status3 != 0)
            st.aiActions[ActName(after.status3)]++;
        st.pointsLeft += after.leftScore - before.leftScore;
        st.pointsRight += after.rightScore - before.rightScore;
        if (after.touchCount > st.maxTouchCount)
            st.maxTouchCount = after.touchCount;
        if (after.trajType == 0)
            st.linearTrajectoryTicks++;
        if (std::isnan(after.bx) || std::isnan(after.by) || std::isnan(after.b))
            st.nanTicks++;
        if (after.jump0 > 0 && before.jump0 == 0)
            st.playerJumps++;
        if (after.finished && !st.finished)
        {
            st.finished       = true;
            st.finishedAtTick = RT().GetTick();
        }
    }

    std::string StatsJson(const Stats& st)
    {
        auto mapJson = [](const std::map<std::string, int>& m) {
            std::string s = "{";
            bool first    = true;
            for (auto& [k, v] : m)
            {
                if (!first)
                    s += ",";
                first = false;
                s += Q(k) + ":" + std::to_string(v);
            }
            return s + "}";
        };
        Obs o = Observe();
        char buf[512];
        std::snprintf(buf, sizeof(buf),
                      "\"ticks\":%d,\"pointsLeft\":%d,\"pointsRight\":%d,\"maxTouchCount\":%d,"
                      "\"linearTrajectoryTicks\":%d,\"nanTicks\":%d,\"finished\":%s,\"finishedAtTick\":%llu,"
                      "\"finalScore\":[%d,%d],\"finalTick\":%llu",
                      st.ticks, st.pointsLeft, st.pointsRight, st.maxTouchCount, st.linearTrajectoryTicks, st.nanTicks,
                      st.finished ? "true" : "false", static_cast<unsigned long long>(st.finishedAtTick), o.leftScore,
                      o.rightScore, static_cast<unsigned long long>(RT().GetTick()));
        return std::string(buf) + ",\"playerJumps\":" + std::to_string(st.playerJumps) +
               ",\"playerActions\":" + mapJson(st.playerActions) +
               ",\"aiActions\":" + mapJson(st.aiActions);
    }

    // ---------------- reactive input controller ----------------
    enum class Plan
    {
        ServeOnly,
        Bump,
        Set,
        SpikeLight,
        SpikeMedium,
        SpikeStrong,
        JumpSpike,
        Mixed,
        // Two-phase combos: phase A = Set the incoming ball, phase B = act on our own set ball.
        SetThenSpikeStrong,
        SetRepeat,
        SetThenJumpSpike,
        JumpSpikeIntercept
    };

    bool IsCombo(Plan p)
    {
        return p == Plan::SetThenSpikeStrong || p == Plan::SetRepeat || p == Plan::SetThenJumpSpike;
    }

    struct Controller
    {
        Plan plan;
        bool toggle    = false;
        int rallyIndex = 0;
        int lastPoints = 0;
        float prevBy   = 0.0f;  // previous observed ball y (a human sees the ball's motion)

        Plan Current(const Obs& o)
        {
            if (plan != Plan::Mixed)
                return plan;
            int points = o.leftScore + o.rightScore;
            if (points != lastPoints)
            {
                lastPoints = points;
                rallyIndex++;
            }
            static const Plan cycle[] = {Plan::Bump, Plan::Set, Plan::SpikeLight, Plan::SpikeMedium, Plan::SpikeStrong,
                                         Plan::JumpSpike};
            return cycle[rallyIndex % 6];
        }

        uint32_t Decide()
        {
            const Obs o = Observe();
            toggle      = !toggle;
            uint32_t m  = 0;
            const Plan p = Current(o);
            const bool descending = o.by < prevBy;
            prevBy                = o.by;

            // Serve when it is our serve and the ball is attached (stateFrame == Reset).
            if (o.stateFrame == 0 && o.servingTeam == 0)
                return toggle ? SIM_BUTTON_SERVE : 0u;
            if (o.stateFrame != -1 || p == Plan::ServeOnly)
                return 0;

            const float pcx  = o.px + o.pw * 0.5f;
            const float pcy  = o.py + o.ph * 0.5f;
            const float bcx  = o.bx + MatchObjectConfig::BallSize.x * 0.5f;
            const float bcy  = o.by + MatchObjectConfig::BallSize.y * 0.5f;
            const float dist = std::hypot(bcx - pcx, bcy - pcy);
            const bool grounded = o.py == SystemConfig::MIN_Y;

            // Phase B of combo plans: our team touched last and the ball will come down on our side.
            const bool ownBall = o.lastTouch == 0 && o.landingX > 0.0f && o.landingX < BigRect::NET_X &&
                                 o.bx < BigRect::NET_X;
            if (IsCombo(p) && ownBall)
            {
                const float tx = (p == Plan::SetRepeat) ? (o.landingX + MatchObjectConfig::BallSize.x * 0.5f) : bcx;
                const float ddx = tx - pcx;
                if (ddx > 7.0f)
                    m |= SIM_BUTTON_MOVE_RIGHT;
                else if (ddx < -7.0f)
                    m |= SIM_BUTTON_MOVE_LEFT;
                switch (p)
                {
                case Plan::SetRepeat:
                    if (descending && dist < 220.0f && o.status0 == 0 && toggle)
                        m |= SIM_BUTTON_SET;
                    break;
                case Plan::SetThenSpikeStrong:
                    if (descending && dist < 75.0f && o.status0 == 0 && toggle)
                        m |= SIM_BUTTON_SPIKE_STRONG;
                    break;
                case Plan::SetThenJumpSpike:
                    if (grounded && descending && (bcy - pcy) < 330.0f && (bcy - pcy) > 0.0f && toggle)
                        m |= SIM_BUTTON_JUMP_SPIKE;  // grounded -> Jump
                    else if (!grounded && dist < 90.0f && toggle)
                        m |= SIM_BUTTON_JUMP_SPIKE;  // airborne -> Spike
                    break;
                default: break;
                }
                return m;
            }

            // Jump-spike intercept: read the observed parabola y = a(x+b)^2 + c (what a human "sees"), stand under
            // the point where the ball will be at our jump peak, take off ~29 ticks before the ball arrives and
            // press JumpSpike while airborne. Whether a Spike happens is decided by the simulation.
            if (p == Plan::JumpSpikeIntercept && o.speed < 0.0f && o.trajType == 1 && o.a < 0.0f &&
                o.lastTouch != 0)
            {
                const float half   = MatchObjectConfig::BallSize.x * 0.5f;
                const float peakY  = SystemConfig::MIN_Y + 304.0f;               // jump apex (S03 evidence)
                const float ty     = peakY + o.ph * 0.5f - half;                 // ball pos.y at our peak centre
                const float val    = (ty - o.c) / o.a;
                if (val >= 0.0f)
                {
                    float xt = -o.b - std::sqrt(val);                            // descending side (ball moves left)
                    if (xt > BigRect::NET_X - 60.0f)
                        xt = BigRect::NET_X - 60.0f;
                    const float targetPx = xt + half - o.pw * 0.5f;
                    const float ddx      = targetPx - o.px;
                    if (grounded)
                    {
                        if (ddx > 7.0f)
                            m |= SIM_BUTTON_MOVE_RIGHT;
                        else if (ddx < -7.0f)
                            m |= SIM_BUTTON_MOVE_LEFT;
                        const float ticksAway = (o.bx - xt) / -o.speed;
                        if (ticksAway > 0.0f && ticksAway <= 30.0f && toggle)
                            m |= SIM_BUTTON_JUMP_SPIKE;  // grounded -> Jump
                    }
                    else if (dist < o.ph * 0.58f && toggle)
                        m |= SIM_BUTTON_JUMP_SPIKE;      // airborne -> Spike
                    return m;
                }
            }

            const bool incoming = o.speed < 0.0f && o.landingX > 0.0f && o.landingX < BigRect::NET_X;
            if (!incoming)
                return 0;

            const float dx   = (o.landingX + MatchObjectConfig::BallSize.x * 0.5f) - pcx;

            if (p != Plan::Bump)
            {
                if (dx > 7.0f)
                    m |= SIM_BUTTON_MOVE_RIGHT;
                else if (dx < -7.0f)
                    m |= SIM_BUTTON_MOVE_LEFT;
            }

            switch (p)
            {
            case Plan::Bump: m |= SIM_BUTTON_BUMP; break;  // held: the simulation decides range & movement
            case Plan::SetThenSpikeStrong:
            case Plan::SetRepeat:
            case Plan::SetThenJumpSpike:
            case Plan::Set:
                if (dist < 220.0f && toggle)
                    m |= SIM_BUTTON_SET;
                break;
            case Plan::SpikeLight:
                if (dist < 140.0f && toggle)
                    m |= SIM_BUTTON_SPIKE_LIGHT;
                break;
            case Plan::SpikeMedium:
                if (dist < 140.0f && toggle)
                    m |= SIM_BUTTON_SPIKE_MEDIUM;
                break;
            case Plan::SpikeStrong:
                if (dist < 140.0f && toggle)
                    m |= SIM_BUTTON_SPIKE_STRONG;
                break;
            case Plan::JumpSpike:
                if (grounded && dist < 420.0f && o.by > 380.0f && toggle)
                    m |= SIM_BUTTON_JUMP_SPIKE;  // Jump
                else if (!grounded && dist < 130.0f && toggle)
                    m |= SIM_BUTTON_JUMP_SPIKE;  // pressed while airborne -> Spike
                break;
            default: break;
            }
            return m;
        }
    };

    // ---------------- scenario plumbing ----------------
    constexpr uint32_t kDetailFlags = SIM_DIAG_SYSTEM_TRACE | SIM_DIAG_FIELD_CHANGES | SIM_DIAG_TRANSITIONS |
                                      SIM_DIAG_INPUT | SIM_DIAG_SNAPSHOT_INITIAL | SIM_DIAG_INTENTS |
                                      SIM_DIAG_LIFECYCLE;
    constexpr uint32_t kRallyFlags = SIM_DIAG_SYSTEM_TRACE | SIM_DIAG_SYSTEM_TRACE_CHANGED_ONLY | SIM_DIAG_TRANSITIONS |
                                     SIM_DIAG_SNAPSHOT_INITIAL | SIM_DIAG_INTENTS | SIM_DIAG_LIFECYCLE |
                                     SIM_DIAG_INPUT;
    constexpr uint32_t kLongFlags = SIM_DIAG_TRANSITIONS | SIM_DIAG_SNAPSHOT_INITIAL | SIM_DIAG_LIFECYCLE;

    struct Scenario
    {
        std::string name;
        Stats stats;

        Scenario(const std::string& n, const std::string& description, uint32_t flags, uint32_t seed,
                 std::vector<uint64_t> snapshotTicks = {})
            : name(n)
        {
            Sim_SetDiagnostics(flags, Path(n + ".jsonl").c_str());
            for (uint64_t t : snapshotTicks)
                Sim_RequestDiagnosticSnapshot(t);
            RT().Diagnostics().Marker("scenario", "\"name\":" + Q(n) + ",\"description\":" + Q(description) +
                                                      ",\"seed\":" + std::to_string(seed) +
                                                      ",\"flags\":" + std::to_string(flags));
            Sim_SeedRandom(seed);
            Sim_Reset();
        }

        void Step(uint32_t mask, float axis = 0.0f)
        {
            Obs before = Observe();
            Sim_SetInput(0, mask, axis);
            Sim_Update(kDt);
            Accumulate(stats, before, Observe());
        }

        void Hold(uint32_t mask, int ticks, const char* label)
        {
            RT().Diagnostics().Marker("step", "\"tick\":" + std::to_string(RT().GetTick()) + ",\"label\":" + Q(label) +
                                                  ",\"mask\":" + std::to_string(mask) +
                                                  ",\"ticks\":" + std::to_string(ticks));
            for (int i = 0; i < ticks; ++i)
                Step(mask);
        }

        void Press(uint32_t mask, const char* label)
        {
            Hold(mask, 1, label);
        }

        void RunController(Controller& c, int maxTicks, bool stopWhenFinished = false)
        {
            RT().Diagnostics().Marker("step", "\"tick\":" + std::to_string(RT().GetTick()) +
                                                  ",\"label\":\"controller\",\"ticks\":" + std::to_string(maxTicks));
            for (int i = 0; i < maxTicks; ++i)
            {
                Step(c.Decide());
                if (stopWhenFinished && stats.finished)
                    break;
            }
        }

        ~Scenario()
        {
            RT().Diagnostics().Snapshot(RT().GetTick(), "final", RT().GetSnapshotJson());
            RT().Diagnostics().Marker("summary", "\"name\":" + Q(name) + "," + StatsJson(stats));
            std::printf("[%s] %s\n", name.c_str(), StatsJson(stats).c_str());
            Sim_SetDiagnostics(0, nullptr);
        }
    };

    // ---------------- scenarios ----------------
    void S01_Idle()
    {
        Scenario s("S01_idle", "No input for 120 ticks: tick-1 ball attach, no movement", kDetailFlags, 1, {1, 120});
        s.Hold(0, 120, "idle");
    }

    void S02_Move()
    {
        Scenario s("S02_move", "Hold right 60 (pre-serve clamp to LEFT_9M=650), left 120 (clamp to 10), idle 20",
                   kDetailFlags, 2, {60, 180});
        s.Hold(SIM_BUTTON_MOVE_RIGHT, 60, "hold MoveRight");
        s.Hold(SIM_BUTTON_MOVE_LEFT, 120, "hold MoveLeft");
        s.Hold(0, 20, "release");
    }

    void S03_Jump()
    {
        Scenario s("S03_jump", "Jump in place, then jump while holding right (half air speed)", kDetailFlags, 3,
                   {1, 30, 31, 62, 63});
        s.Press(SIM_BUTTON_JUMP_SPIKE, "press JumpSpike (grounded -> Jump)");
        s.Hold(0, 79, "release, observe arc");
        s.Press(SIM_BUTTON_JUMP_SPIKE | SIM_BUTTON_MOVE_RIGHT, "press JumpSpike + MoveRight");
        s.Hold(SIM_BUTTON_MOVE_RIGHT, 30, "hold MoveRight in air");
        s.Hold(0, 50, "release");
    }

    void S04_ServePoint()
    {
        Scenario s("S04_serve_point", "Player serve, no further player input: trajectory, AI response, point, reset",
                   kDetailFlags | SIM_DIAG_SNAPSHOT_EVERY_TICK, 4);
        s.Press(SIM_BUTTON_SERVE, "press Serve");
        s.Hold(0, 420, "idle");
    }

    void ControllerScenario(const char* name, const char* desc, Plan plan, uint32_t seed, int ticks)
    {
        Scenario s(name, desc, kRallyFlags | SIM_DIAG_FIELD_CHANGES, seed);
        Controller c{plan};
        s.RunController(c, ticks);
    }

    void S09_Victory()
    {
        Scenario s("S09_victory", "Serve-only player vs AI until a team reaches 15, freeze, then reset", kLongFlags, 9);
        Controller c{Plan::ServeOnly};
        s.RunController(c, 60000, true);
        s.Hold(0, 5, "after victory (frozen)");
        RT().Diagnostics().Snapshot(RT().GetTick(), "beforeReset", RT().GetSnapshotJson());
        Sim_Reset();
        RT().Diagnostics().Marker("step", "\"tick\":0,\"label\":\"after reset (2 idle ticks)\",\"mask\":0,\"ticks\":2");
        for (int i = 0; i < 2; ++i)
        {
            Sim_SetInput(0, 0, 0.0f);
            Sim_Update(kDt);
        }
    }

    void S10_Mixed()
    {
        Scenario s("S10_mixed_long", "Controller cycles Bump/Set/SpikeL/M/S/JumpSpike per rally (net collision search)",
                   kRallyFlags, 10);
        Controller c{Plan::Mixed};
        s.RunController(c, 20000, true);
    }

    // ---------------- tests ----------------
    bool T01_ResetConsistency()
    {
        Sim_SetDiagnostics(0, nullptr);
        Sim_SeedRandom(77);
        Sim_Reset();
        const std::string a = RT().GetSnapshotJson();
        Controller c{Plan::Bump};
        for (int i = 0; i < 300; ++i)
        {
            Sim_SetInput(0, c.Decide(), 0.0f);
            Sim_Update(kDt);
        }
        const std::string mid = RT().GetSnapshotJson();
        Sim_Reset();
        const std::string b = RT().GetSnapshotJson();
        const bool ok = (a == b) && (a != mid);
        FILE* f = std::fopen(Path("T01_reset_consistency.json").c_str(), "wb");
        if (f)
        {
            std::fprintf(f, "{\"test\":\"T01_reset_consistency\",\"pass\":%s,\"initialEqualsAfterReset\":%s,"
                            "\"midDiffers\":%s,\"bytes\":%zu}\n",
                         ok ? "true" : "false", a == b ? "true" : "false", a != mid ? "true" : "false", a.size());
            std::fclose(f);
        }
        std::printf("[T01_reset_consistency] %s\n", ok ? "PASS" : "FAIL");
        return ok;
    }

    std::vector<std::string> RunRecorded(Plan plan, int ticks, uint32_t seed, uint32_t diagFlags, bool legacy)
    {
        Sim_SetDiagnostics(diagFlags, diagFlags ? Path("T02_neutrality_diag_on.jsonl").c_str() : nullptr);
        RT().SetLegacyPipelineForTest(legacy);
        Sim_SeedRandom(seed);
        Sim_Reset();
        std::vector<std::string> out;
        out.reserve(ticks + 1);
        out.push_back(RT().GetSnapshotJson());
        Controller c{plan};
        for (int i = 0; i < ticks; ++i)
        {
            Sim_SetInput(0, c.Decide(), 0.0f);
            Sim_Update(kDt);
            out.push_back(RT().GetSnapshotJson());
        }
        RT().SetLegacyPipelineForTest(false);
        Sim_SetDiagnostics(0, nullptr);
        return out;
    }

    bool T02_Neutrality()
    {
        // Two input plans: Mixed (net collision / long rallies) and JumpSpikeIntercept (LEFT scoring + serve rotation).
        struct Case { const char* name; Plan plan; int ticks; };
        const Case cases[] = {{"Mixed", Plan::Mixed, 1500}, {"JumpSpikeIntercept", Plan::JumpSpikeIntercept, 2400}};
        bool allOk = true;
        std::string results;
        for (const auto& cs : cases)
        {
            auto off    = RunRecorded(cs.plan, cs.ticks, 4242, 0, false);
            auto on     = RunRecorded(cs.plan, cs.ticks, 4242, 0xFFu, false);
            auto legacy = RunRecorded(cs.plan, cs.ticks, 4242, 0, true);
            long firstDiffOn = -1, firstDiffLegacy = -1;
            for (size_t i = 0; i < off.size(); ++i)
            {
                if (firstDiffOn < 0 && off[i] != on[i])
                    firstDiffOn = static_cast<long>(i);
                if (firstDiffLegacy < 0 && off[i] != legacy[i])
                    firstDiffLegacy = static_cast<long>(i);
            }
            const bool ok = firstDiffOn < 0 && firstDiffLegacy < 0;
            allOk         = allOk && ok;
            char buf[512];
            std::snprintf(buf, sizeof(buf),
                          "%s{\"plan\":\"%s\",\"pass\":%s,\"ticks\":%d,\"snapshotsCompared\":%zu,"
                          "\"diagOffVsAllFlags_firstDiffTick\":%ld,\"tracedVsLegacyPipeline_firstDiffTick\":%ld,"
                          "\"finalSnapshotBytes\":%zu}",
                          results.empty() ? "" : ",", cs.name, ok ? "true" : "false", cs.ticks, off.size(), firstDiffOn,
                          firstDiffLegacy, off.back().size());
            results += buf;
            std::printf("[T02_neutrality:%s] %s (diff on=%ld legacy=%ld)\n", cs.name, ok ? "PASS" : "FAIL", firstDiffOn,
                        firstDiffLegacy);
        }
        FILE* f = std::fopen(Path("T02_neutrality.json").c_str(), "wb");
        if (f)
        {
            std::fprintf(f, "{\"test\":\"T02_neutrality\",\"pass\":%s,\"cases\":[%s]}\n", allOk ? "true" : "false",
                         results.c_str());
            std::fclose(f);
        }
        return allOk;
    }

    bool T03_SeedDeterminism()
    {
        struct Case { const char* name; Plan plan; int ticks; };
        const Case cases[] = {{"Mixed", Plan::Mixed, 1800}, {"JumpSpikeIntercept", Plan::JumpSpikeIntercept, 3600}};
        bool allSame = true;
        std::string results;
        for (const auto& cs : cases)
        {
            auto a = RunRecorded(cs.plan, cs.ticks, 99, 0, false);
            auto b = RunRecorded(cs.plan, cs.ticks, 99, 0, false);
            auto c = RunRecorded(cs.plan, cs.ticks, 100, 0, false);
            const bool same = a == b;
            allSame         = allSame && same;
            long firstDiffSeed = -1;
            for (size_t i = 0; i < a.size(); ++i)
                if (a[i] != c[i])
                {
                    firstDiffSeed = static_cast<long>(i);
                    break;
                }
            char buf[256];
            std::snprintf(buf, sizeof(buf), "%s{\"plan\":\"%s\",\"ticks\":%d,\"sameSeedIdentical\":%s,\"differentSeed_firstDiffTick\":%ld}",
                          results.empty() ? "" : ",", cs.name, cs.ticks, same ? "true" : "false", firstDiffSeed);
            results += buf;
            std::printf("[T03_seed_determinism:%s] %s (different seed diverges at tick %ld)\n", cs.name,
                        same ? "PASS" : "FAIL", firstDiffSeed);
        }
        FILE* f = std::fopen(Path("T03_seed_determinism.json").c_str(), "wb");
        if (f)
        {
            std::fprintf(f,
                         "{\"test\":\"T03_seed_determinism\",\"pass\":%s,\"cases\":[%s],"
                         "\"note\":\"AI attack choice uses std::rand only when >1 (or 0) predicted-in-court options; "
                         "differentSeed_firstDiffTick=-1 means that branch never changed the outcome in this run\"}\n",
                         allSame ? "true" : "false", results.c_str());
            std::fclose(f);
        }
        return allSame;
    }

    void WriteSchema()
    {
        int32_t req = 0;
        Sim_GetSchemaJson(nullptr, 0, &req);
        std::string buf(static_cast<size_t>(req), '\0');
        Sim_GetSchemaJson(buf.data(), req, &req);
        FILE* f = std::fopen(Path("schema.json").c_str(), "wb");
        if (f)
        {
            std::fwrite(buf.data(), 1, std::strlen(buf.c_str()), f);
            std::fclose(f);
        }
    }
}  // namespace

int main(int argc, char** argv)
{
    if (argc > 1)
        g_outDir = argv[1];
    const std::string which = argc > 2 ? argv[2] : "all";
    fs::create_directories(g_outDir);

    Sim_SetLegacyLogEnabled(0);  // silence legacy per-tick text ("Ball Position ...")
    if (!Sim_Initialize(nullptr))
    {
        std::printf("Sim_Initialize failed\n");
        return 2;
    }
    WriteSchema();

    const std::vector<std::pair<std::string, std::function<void()>>> scenarios = {
        {"S01_idle", S01_Idle},
        {"S02_move", S02_Move},
        {"S03_jump", S03_Jump},
        {"S04_serve_point", S04_ServePoint},
        {"S05_bump_rally", [] { ControllerScenario("S05_bump_rally", "Controller holds Bump when ball incoming", Plan::Bump, 5, 1800); }},
        {"S06_set_rally", [] { ControllerScenario("S06_set_rally", "Controller presses Set near ball", Plan::Set, 6, 1800); }},
        {"S07a_spike_light", [] { ControllerScenario("S07a_spike_light", "Controller presses SpikeLight near ball", Plan::SpikeLight, 71, 1800); }},
        {"S07b_spike_medium", [] { ControllerScenario("S07b_spike_medium", "Controller presses SpikeMedium near ball", Plan::SpikeMedium, 72, 1800); }},
        {"S07c_spike_strong", [] { ControllerScenario("S07c_spike_strong", "Controller presses SpikeStrong near ball", Plan::SpikeStrong, 73, 1800); }},
        {"S08_jump_spike", [] { ControllerScenario("S08_jump_spike", "Controller jumps then presses JumpSpike airborne", Plan::JumpSpike, 8, 1800); }},
        {"S09_victory", S09_Victory},
        {"S10_mixed_long", S10_Mixed},
        {"S11_set_spike_combo", [] { ControllerScenario("S11_set_spike_combo", "Set the incoming ball, then SpikeStrong our own set (2 touches)", Plan::SetThenSpikeStrong, 11, 3600); }},
        {"S12_four_touch_fault", [] { ControllerScenario("S12_four_touch_fault", "Keep setting our own ball: touchCount 1..4 > MAX_TOUCH_PER_TEAM", Plan::SetRepeat, 12, 1800); }},
        {"S13_set_jump_spike", [] { ControllerScenario("S13_set_jump_spike", "Set the incoming ball, jump under it and press JumpSpike airborne (Spike)", Plan::SetThenJumpSpike, 13, 3600); }},
        {"S14_jump_spike_intercept", [] { ControllerScenario("S14_jump_spike_intercept", "Time a jump at the incoming ball near the net and press JumpSpike airborne", Plan::JumpSpikeIntercept, 14, 3600); }},
    };

    for (const auto& [name, fn] : scenarios)
    {
        if (which == "all" || which == name)
            fn();
    }

    int failures = 0;
    if (which == "all" || which == "tests")
    {
        failures += T01_ResetConsistency() ? 0 : 1;
        failures += T02_Neutrality() ? 0 : 1;
        failures += T03_SeedDeterminism() ? 0 : 1;
    }

    Sim_Shutdown();
    std::printf("SimDiagRunner finished, failures=%d\n", failures);
    return failures == 0 ? 0 : 1;
}
