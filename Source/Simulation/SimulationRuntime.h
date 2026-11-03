#pragma once

#include "SpikeNativeApi.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../Gameplay/MatchData/CharacterStatStorage.h"
#include "../Input/InputMapping.h"
#include "../System/FramePreparationSystem.h"
#include "../System/PlayerInputSystem.h"
#include "../System/AIInputSystem.h"
#include "../System/UpdateCharacterActionStateSystem.h"
#include "../System/JumpStartSystem.h"
#include "../System/UpdateTrajectory.h"
#include "../System/VelocityIntentSystem.h"
#include "../System/GameRuleEvaluationSystem.h"
#include "../System/LoadStatsData.h"
#include "Diagnostics/SimDiagnostics.h"
#include "Diagnostics/SimSnapshotJson.h"

#include <memory>
#include <string>

class SimulationRuntime
{
public:
    SimulationRuntime();
    ~SimulationRuntime();

    bool Initialize(const char* configJsonPath);
    void Reset();
    void SetInput(uint32_t entityId, uint32_t buttonMask, float moveAxisX);
    int32_t SubmitButtonInput(uint32_t entityId, int32_t buttonId, int32_t inputState);
    void Update(float dt);
    void GetSnapshot(SimSnapshot* outSnapshot) const;
    void Shutdown();

    static SimulationRuntime& GetInstance();

    // ---------------- complete data contract (read-only) ----------------
    bool IsInitialized() const { return _initialized; }
    uint64_t GetTick() const { return _frameNumber; }
    uint64_t GetStateVersion() const { return _stateVersion; }
    SimStateView MakeView() const;
    const std::string& GetSnapshotJson();      // cached per state version
    static const std::string& GetSchemaJson();  // static schema

    const ComponentStorage& GetComponentStorage() const { return _storage; }
    const IntentStorage& GetIntentStorage() const { return _intentStorage; }
    const ComponentPool<MatchCharacterStat>& GetCharacterStatPool() const
    {
        return const_cast<CharacterStatStorage&>(_characterStatStorage).GetMatchStatPool();
    }

    SimDiagnostics& Diagnostics() { return _diag; }

    // Test-only: run the original, untraced call path (UpdateTrajectory wrapper +
    // ApplyIntentToComponent) to prove the traced path is behaviour-identical.
    void SetLegacyPipelineForTest(bool enabled) { _useLegacyPipeline = enabled; }

private:
    void BuildSnapshot(SimSnapshot* outSnapshot) const;
    void EmitInitialDiagnostics(const char* reason);

    bool _initialized = false;
    uint64_t _frameNumber = 0;
    uint64_t _stateVersion = 1;
    int64_t _intentsProducedByTick = -1;
    bool _lastUpdateFrozenByVictory = false;
    bool _statsLoaded = false;

    ComponentStorage _storage;
    IntentStorage _intentStorage;
    CharacterStatStorage _characterStatStorage;

    std::unique_ptr<FramePreparationSystem> _framePreparationSystem;
    std::unique_ptr<PlayerInputSystem> _playerInputSystem;
    std::unique_ptr<AIInputSystem> _aiInputSystem;
    std::unique_ptr<UpdateCharacterActionStateSystem> _updateCharacterActionStateSystem;
    std::unique_ptr<JumpStartSystem> _jumpStartSystem;
    // Same two sub-systems the UpdateTrajectory wrapper calls (FromIntent, then ByCollision),
    // owned directly so each step can be traced individually.
    std::unique_ptr<UpdateTrajectoryFromIntent> _updateTrajectoryFromIntent;
    std::unique_ptr<UpdateTrajectoryByCollision> _updateTrajectoryByCollision;
    std::unique_ptr<VelocityIntentSystem> _velocityIntentSystem;
    std::unique_ptr<GameRuleEvaluationSystem> _gameRuleEvaluationSystem;
    std::unique_ptr<LoadStatsData> _loadStatsData;

    SimInputFrame _currentInput;
    uint32_t _prevButtonMask = 0;
    uint32_t _latchedPressedButtons = 0;

    SimDiagnostics _diag;
    bool _useLegacyPipeline = false;
    std::unique_ptr<UpdateTrajectory> _legacyUpdateTrajectory;
    std::string _snapshotJsonCache;
    uint64_t _snapshotJsonVersion = 0;
};
