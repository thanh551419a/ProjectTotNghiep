#pragma once
// ==============================================================
// SimSnapshotJson.h
// Builds the complete authoritative snapshot (TheSpike.SimSnapshot v1)
// and the machine-readable schema as UTF-8 JSON.
// Read-only: never mutates simulation state.
// ==============================================================

#include <cstdint>
#include <string>

#include "../../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../../ECSCore/IntentStorage/IntentStorage.h"
#include "../../Gameplay/MatchData/CharacterStatStorage.h"
#include "../../Input/InputMapping.h"

struct SimStateView
{
    const ComponentStorage* components = nullptr;
    const IntentStorage* intents       = nullptr;
    const ComponentPool<MatchCharacterStat>* statPool = nullptr;

    uint64_t tick                  = 0;   // number of executed ticks (_frameNumber)
    int64_t intentsProducedByTick  = -1;  // tick index that produced current IntentStorage, -1 = none since init/reset
    bool statsLoaded               = false;
    bool lastUpdateFrozenByVictory = false;
    bool initialized               = false;

    SimInputFrame input{};
    uint32_t prevButtonMask = 0;
};

namespace SimSnapshotJson
{
    // Complete snapshot. Deterministic: same state -> byte-identical output.
    std::string BuildSnapshot(const SimStateView& view);

    // Schema: types, fields, enums, pools, sections, exclusions.
    std::string BuildSchema();
}  // namespace SimSnapshotJson
