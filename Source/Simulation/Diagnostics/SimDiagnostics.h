#pragma once
// ==============================================================
// SimDiagnostics.h
// Development diagnostics for the native simulation runtime.
//
// Read-only instrumentation: it observes storages before/after each system
// call (snapshot diff) and writes deterministic JSON Lines. It never writes to
// simulation storage and does not change system order. With flags == 0 the
// instrumented call path is a direct function call (no flattening at all).
// ==============================================================

#include <cstdint>
#include <cstdio>
#include <map>
#include <set>
#include <string>

#include "SimSnapshotJson.h"
#include "SimSchema.h"

class SimDiagnostics
{
public:
    enum Flags : uint32_t
    {
        kSystemTrace       = 1u << 0,  // one record per system call (order, name, #changes)
        kFieldChanges      = 1u << 1,  // every changed field (old/new/system/cause)
        kTransitions       = 1u << 2,  // discrete state transitions only
        kInput             = 1u << 3,  // Sim_SetInput reception
        kSnapshotInitial   = 1u << 4,  // full snapshot after Initialize / Reset
        kSnapshotEveryTick = 1u << 5,  // full snapshot after every tick (short runs only)
        kIntentDump        = 1u << 6,  // intents produced by each tick
        kLifecycle         = 1u << 7,  // init / reset / shutdown / victory-freeze records
        kSystemTraceChangedOnly = 1u << 8,  // modifier: only emit system records whose call changed data
        kAll               = 0xFFu
    };

    struct FlatValue
    {
        uint8_t kind = 0;  // 0 float, 1 int, 2 uint, 3 bool, 4 enum
        double num   = 0.0;
        const char* enumName       = nullptr;
        SimSchema::TransitionMode mode = SimSchema::TransitionMode::None;
    };

    struct FlatEntry
    {
        std::string pool;
        uint32_t key = 0;
        bool matchKey = false;
        std::string field;
        FlatValue value;
    };

    using FlatMap = std::map<std::string, FlatEntry>;

    ~SimDiagnostics() { Close(); }

    // path == nullptr or flags == 0 disables diagnostics.
    bool Configure(uint32_t flags, const char* path);
    void Close();

    uint32_t GetFlags() const { return _flags; }
    bool Active() const { return _flags != 0 && _out != nullptr; }
    bool Has(uint32_t f) const { return Active() && (_flags & f) != 0; }
    bool NeedsDiff() const { return Has(kSystemTrace | kFieldChanges | kTransitions); }

    void RequestSnapshotAtTick(uint64_t tick) { _snapshotTicks.insert(tick); }
    bool SnapshotRequested(uint64_t tick) const { return _snapshotTicks.count(tick) != 0; }

    // ---------------- records ----------------
    void Lifecycle(const char* type, uint64_t tick, const char* detail = nullptr);
    void Input(uint64_t tick, uint32_t entity, uint32_t holding, uint32_t pressed, float axis, uint32_t prevMask);
    void Snapshot(uint64_t tick, const char* reason, const std::string& json);
    void Intents(uint64_t tick, const SimStateView& view);
    void Marker(const char* type, const std::string& rawJsonObjectBody);  // for runners: {"type":..., <body>}
    void Flush();

    // Runs one system with before/after observation. 'order' is the position in the tick.
    template <class Fn>
    void RunSystem(uint64_t tick, int order, const char* name, const SimStateView& view, Fn&& fn)
    {
        if (!NeedsDiff())
        {
            fn();
            return;
        }
        FlatMap before = Flatten(view);
        fn();
        FlatMap after = Flatten(view);
        EmitDiff(tick, order, name, view, before, after);
    }

    static FlatMap Flatten(const SimStateView& view);

private:
    void EmitDiff(uint64_t tick, int order, const char* name, const SimStateView& view, const FlatMap& before,
                  const FlatMap& after);
    void WriteLine(const std::string& line);

    uint32_t _flags = 0;
    FILE* _out      = nullptr;
    std::set<uint64_t> _snapshotTicks;
};
