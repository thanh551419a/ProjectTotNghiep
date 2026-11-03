#include "SimulationRuntime.h"
#include "../Utils/GetData.h"
#include "../Utils/ApplyIntentToComponent.h"
#include "../Utils/SimLog.h"

SimulationRuntime::SimulationRuntime()
{
}

SimulationRuntime::~SimulationRuntime()
{
    Shutdown();
}

SimulationRuntime& SimulationRuntime::GetInstance()
{
    static SimulationRuntime instance;
    return instance;
}

SimStateView SimulationRuntime::MakeView() const
{
    SimStateView v;
    v.components                = &_storage;
    v.intents                   = &_intentStorage;
    v.statPool                  = &GetCharacterStatPool();
    v.tick                      = _frameNumber;
    v.intentsProducedByTick     = _intentsProducedByTick;
    v.statsLoaded               = _statsLoaded;
    v.lastUpdateFrozenByVictory = _lastUpdateFrozenByVictory;
    v.initialized               = _initialized;
    v.input                     = _currentInput;
    v.prevButtonMask            = _prevButtonMask;
    return v;
}

const std::string& SimulationRuntime::GetSnapshotJson()
{
    if (_snapshotJsonVersion != _stateVersion)
    {
        _snapshotJsonCache   = SimSnapshotJson::BuildSnapshot(MakeView());
        _snapshotJsonVersion = _stateVersion;
    }
    return _snapshotJsonCache;
}

const std::string& SimulationRuntime::GetSchemaJson()
{
    static const std::string schema = SimSnapshotJson::BuildSchema();
    return schema;
}

void SimulationRuntime::EmitInitialDiagnostics(const char* reason)
{
    _diag.Lifecycle(reason, _frameNumber);
    if (_diag.Has(SimDiagnostics::kSnapshotInitial) || _diag.SnapshotRequested(_frameNumber))
    {
        _diag.Snapshot(_frameNumber, reason, GetSnapshotJson());
    }
    _diag.Flush();
}

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif
#include <fstream>

static std::string GetModuleDirectory()
{
#if defined(_WIN32)
    HMODULE hm = NULL;
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | 
                           GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           (LPCSTR)&InputMapping::GetInstance, &hm))
    {
        char path[MAX_PATH] = {0};
        DWORD len = GetModuleFileNameA(hm, path, MAX_PATH);
        if (len > 0)
        {
            char* lastSlash = std::strrchr(path, '\\');
            if (!lastSlash) lastSlash = std::strrchr(path, '/');
            if (lastSlash)
            {
                *(lastSlash + 1) = '\0';
                return std::string(path);
            }
        }
    }
#endif
    return "";
}

bool SimulationRuntime::Initialize(const char* configJsonPath)
{
    if (_initialized)
    {
        return true;
    }

    if (configJsonPath && configJsonPath[0] != '\0')
    {
        if (!InputMapping::GetInstance().LoadFromFile(configJsonPath))
        {
            SIM_LOG("SimulationRuntime: Failed to load specified config from '%s'.", configJsonPath);
            return false;
        }
    }
    else
    {
        // Deterministic discovery independent of working directory
        std::string modDir = GetModuleDirectory();
        std::string candidate = modDir + "button_mapping.json";
        std::ifstream test(candidate);
        if (test.good())
        {
            test.close();
            if (!InputMapping::GetInstance().LoadFromFile(candidate))
            {
                SIM_LOG("SimulationRuntime: Failed to load deterministic config '%s'.", candidate.c_str());
                return false;
            }
        }
        else
        {
            std::string fallback = "SimulationDllTest/button_mapping.json";
            std::ifstream test2(fallback);
            if (test2.good())
            {
                test2.close();
                if (!InputMapping::GetInstance().LoadFromFile(fallback))
                {
                    SIM_LOG("SimulationRuntime: Failed to load fallback config '%s'.", fallback.c_str());
                    return false;
                }
            }
        }
    }

    _storage.InitComponent();
    _intentStorage.clear();
    // Process-global test singleton: restart its counters with the runtime (no gameplay effect;
    // AttackPower is always assigned before it is read).
    TestTrajectory::getInstance().Reset();

    _framePreparationSystem = std::make_unique<FramePreparationSystem>(&_storage, &_intentStorage);
    _playerInputSystem      = std::make_unique<PlayerInputSystem>(&_intentStorage, &_storage);
    _aiInputSystem          = std::make_unique<AIInputSystem>(&_intentStorage, &_storage);
    _updateCharacterActionStateSystem =
        std::make_unique<UpdateCharacterActionStateSystem>(&_storage);
    _jumpStartSystem = std::make_unique<JumpStartSystem>(&_intentStorage, &_storage);
    _updateTrajectoryFromIntent  = std::make_unique<UpdateTrajectoryFromIntent>(&_intentStorage, &_storage);
    _updateTrajectoryByCollision = std::make_unique<UpdateTrajectoryByCollision>(&_intentStorage, &_storage);
    _legacyUpdateTrajectory      = std::make_unique<UpdateTrajectory>(&_intentStorage, &_storage);
    _velocityIntentSystem = std::make_unique<VelocityIntentSystem>(&_intentStorage);
    _gameRuleEvaluationSystem =
        std::make_unique<GameRuleEvaluationSystem>(&_storage, &_intentStorage);
    _loadStatsData = std::make_unique<LoadStatsData>(&_characterStatStorage);

    _frameNumber     = 0;
    _prevButtonMask  = 0;
    _latchedPressedButtons = 0;
    _currentInput    = SimInputFrame{};
    _intentsProducedByTick     = -1;
    _lastUpdateFrozenByVictory = false;
    _initialized     = true;
    ++_stateVersion;

    SIM_LOG("SimulationRuntime: Initialized successfully.");
    EmitInitialDiagnostics("initialize");
    return true;
}

void SimulationRuntime::Reset()
{
    _storage.Reset();
    _storage.InitComponent();
    _intentStorage.clear();
    _characterStatStorage.reset();
    TestTrajectory::getInstance().Reset();
    _frameNumber    = 0;
    _prevButtonMask = 0;
    _latchedPressedButtons = 0;
    _currentInput   = SimInputFrame{};
    _intentsProducedByTick     = -1;
    _lastUpdateFrozenByVictory = false;
    ++_stateVersion;

    SIM_LOG("SimulationRuntime: State reset.");
    EmitInitialDiagnostics("reset");
}

void SimulationRuntime::SetInput(uint32_t entityId, uint32_t buttonMask, float moveAxisX)
{
    if (entityId == GameConfig::PLAYER)
    {
        _currentInput.holdingButtons = buttonMask;
        _currentInput.pressedButtons = buttonMask & (~_prevButtonMask);
        _prevButtonMask              = buttonMask;
        _currentInput.moveAxisX      = moveAxisX;
        ++_stateVersion;
    }
    _diag.Input(_frameNumber, entityId, _currentInput.holdingButtons, _currentInput.pressedButtons,
                _currentInput.moveAxisX, _prevButtonMask);
}

int32_t SimulationRuntime::SubmitButtonInput(uint32_t entityId, int32_t buttonId, int32_t inputState)
{
    const char* stateStr = InputMapping::InputStateToString(inputState);
    if (entityId != GameConfig::PLAYER)
    {
        SIM_LOG("[NATIVE_INPUT][CHECKPOINT_B] Native API received button input: Entity=%u, ButtonId=%d, State=%s, Tick=%llu, Valid=NO (only Player 0 accepts input), Status=REJECTED",
                entityId, buttonId, stateStr, (unsigned long long)_frameNumber);
        return SIM_ERR_INVALID_ARGUMENT;
    }
    if (inputState < 0 || inputState > 2)
    {
        SIM_LOG("[NATIVE_INPUT][CHECKPOINT_B] Native API received button input: Entity=%u, ButtonId=%d, State=%d, Tick=%llu, Valid=NO (invalid input state), Status=REJECTED",
                entityId, buttonId, inputState, (unsigned long long)_frameNumber);
        return SIM_ERR_INVALID_ARGUMENT;
    }

    SIM_LOG("[NATIVE_INPUT][CHECKPOINT_B] Native API received button input: Entity=%u, ButtonId=%d, State=%s, Tick=%llu, Valid=YES, Status=ACCEPTED",
            entityId, buttonId, stateStr, (unsigned long long)_frameNumber);

    GameAction mappedAction = GameAction::None;
    bool mapped = InputMapping::GetInstance().MapButtonToAction(buttonId, mappedAction);
    if (!mapped || mappedAction == GameAction::None)
    {
        SIM_LOG("[NATIVE_INPUT][CHECKPOINT_C] Button mapping lookup: ButtonId=%d -> MappedAction=None. Reason: buttonId not mapped. Result: REJECTED",
                buttonId);
        return SIM_ERR_INVALID_ARGUMENT;
    }

    uint32_t buttonMask = InputMapping::GetInstance().ActionToButtonMask(mappedAction);
    const char* actionName = InputMapping::ActionToString(mappedAction);
    SIM_LOG("[NATIVE_INPUT][CHECKPOINT_C] Button mapping lookup: ButtonId=%d -> MappedAction=%s (mask=0x%X). Result: SUCCESS",
            buttonId, actionName, buttonMask);

    if (inputState == static_cast<int32_t>(SimInputState::Pressed))
    {
        _currentInput.holdingButtons |= buttonMask;
        _latchedPressedButtons |= buttonMask;
    }
    else if (inputState == static_cast<int32_t>(SimInputState::Held))
    {
        _currentInput.holdingButtons |= buttonMask;
    }
    else if (inputState == static_cast<int32_t>(SimInputState::Released))
    {
        _currentInput.holdingButtons &= ~buttonMask;
        _latchedPressedButtons &= ~buttonMask;
    }

    bool leftHeld = (_currentInput.holdingButtons & static_cast<uint32_t>(SimButton::MoveLeft)) != 0;
    bool rightHeld = (_currentInput.holdingButtons & static_cast<uint32_t>(SimButton::MoveRight)) != 0;
    if (leftHeld && !rightHeld)
    {
        _currentInput.moveAxisX = -1.0f;
    }
    else if (rightHeld && !leftHeld)
    {
        _currentInput.moveAxisX = 1.0f;
    }
    else
    {
        _currentInput.moveAxisX = 0.0f;
    }

    ++_stateVersion;
    return SIM_OK;
}

void SimulationRuntime::Update(float dt)
{
    if (!_initialized)
    {
        Initialize(nullptr);
    }

    const uint64_t tick = _frameNumber;
    const SimStateView view = MakeView();
    int order = 0;

    // 0. Victory score check (frozen match: returns before any system runs)
    bool frozen = false;
    _diag.RunSystem(tick, order++, "VictoryCheck", view, [&] {
        auto* matchState = _storage.GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
        if (matchState != nullptr)
        {
            if (matchState->leftScore >= MatchRuleConfig::SCORE_TO_WIN ||
                matchState->rightScore >= MatchRuleConfig::SCORE_TO_WIN)
            {
                matchState->finished = true;
                matchState->matchWinner = (matchState->leftScore >= MatchRuleConfig::SCORE_TO_WIN) ? Team::LEFT : Team::RIGHT;
                frozen = true;
            }
        }
    });
    if (frozen)
    {
        _lastUpdateFrozenByVictory = true;
        ++_stateVersion;
        _diag.Lifecycle("victoryFreeze", tick);
        _diag.Flush();
        return;
    }
    _lastUpdateFrozenByVictory = false;

    if (_useLegacyPipeline)
    {
        // Original SimulationRuntime::Update body (pre-instrumentation), kept verbatim for equivalence tests.
        _intentStorage.clear();
        _intentsProducedByTick = static_cast<int64_t>(tick);
        _framePreparationSystem->update();
        _playerInputSystem->update(dt, _currentInput);
        if (MatchRuleConfig::ENABLE_BOT)
        {
            _aiInputSystem->update();
        }
        _updateCharacterActionStateSystem->update(dt * 1000.0f);
        _jumpStartSystem->update(dt);
        ObjectData* legacyData = GetPositionData(&_storage);
        _legacyUpdateTrajectory->update(legacyData, dt, nullptr);
        _velocityIntentSystem->update(dt, legacyData);
        ApplyIntentToComponent(&_intentStorage, &_storage, nullptr);
        _gameRuleEvaluationSystem->update();
        _frameNumber++;
        ++_stateVersion;
        return;
    }

    // Combine any latched pressed edges from SubmitButtonInput
    _currentInput.pressedButtons |= _latchedPressedButtons;
    _latchedPressedButtons       = 0;

    auto* playerPosComp = _storage.GetCharacterPositionPool().get(GameConfig::PLAYER);
    auto* playerActionComp = _storage.GetCharacterActionStatePool().get(GameConfig::PLAYER);
    float beforeX = playerPosComp ? playerPosComp->position.x : 0.0f;
    float beforeY = playerPosComp ? playerPosComp->position.y : 0.0f;
    int32_t beforeAction = playerActionComp ? static_cast<int32_t>(playerActionComp->status) : 0;

    // 1. Clear transient intents
    _diag.RunSystem(tick, order++, "IntentStorage.clear", view, [&] { _intentStorage.clear(); });
    _intentsProducedByTick = static_cast<int64_t>(tick);

    // 2. Frame preparation (serves, positions)
    _diag.RunSystem(tick, order++, "FramePreparationSystem", view, [&] { _framePreparationSystem->update(); });

    // 3. Input processing -> intents
    _diag.RunSystem(tick, order++, "PlayerInputSystem", view, [&] { _playerInputSystem->update(dt, _currentInput); });

    if (MatchRuleConfig::ENABLE_BOT)
    {
        _diag.RunSystem(tick, order++, "AIInputSystem", view, [&] { _aiInputSystem->update(); });
    }

    // 4. Cooldown timers
    _diag.RunSystem(tick, order++, "UpdateCharacterActionStateSystem", view,
                    [&] { _updateCharacterActionStateSystem->update(dt * 1000.0f); });

    // 5. Jump initiation
    _diag.RunSystem(tick, order++, "JumpStartSystem", view, [&] { _jumpStartSystem->update(dt); });

    // 6. Ball trajectory & collision physics (body of UpdateTrajectory::update)
    ObjectData* data = nullptr;
    _diag.RunSystem(tick, order++, "GetPositionData", view, [&] { data = GetPositionData(&_storage); });
    _diag.RunSystem(tick, order++, "UpdateTrajectoryFromIntent", view,
                    [&] { _updateTrajectoryFromIntent->update(data, dt, nullptr); });
    _diag.RunSystem(tick, order++, "UpdateTrajectoryByCollision", view,
                    [&] { _updateTrajectoryByCollision->update(data, dt, nullptr); });

    // 7. Gravity intent
    _diag.RunSystem(tick, order++, "VelocityIntentSystem", view, [&] { _velocityIntentSystem->update(dt, data); });

    // 8. Integration of intents into component storage (body of ApplyIntentToComponent)
    _diag.RunSystem(tick, order++, "ApplyJumpFrames", view, [&] { ApplyJumpFrames(&_intentStorage, &_storage); });
    _diag.RunSystem(tick, order++, "ApplyHorizontalMovement", view,
                    [&] { ApplyHorizontalMovement(&_intentStorage, &_storage, nullptr); });
    _diag.RunSystem(tick, order++, "ApplyVerticalVelocity", view,
                    [&] { ApplyVerticalVelocity(&_intentStorage, &_storage); });
    _diag.RunSystem(tick, order++, "ApplyBallTrajectory", view,
                    [&] { ApplyBallTrajectory(&_intentStorage, &_storage); });

    // 9. Rules & Scoring
    _diag.RunSystem(tick, order++, "GameRuleEvaluationSystem", view, [&] { _gameRuleEvaluationSystem->update(); });

    // Checkpoint E: State before and after simulation processing
    float afterX = playerPosComp ? playerPosComp->position.x : 0.0f;
    float afterY = playerPosComp ? playerPosComp->position.y : 0.0f;
    int32_t afterAction = playerActionComp ? static_cast<int32_t>(playerActionComp->status) : 0;
    auto* playerIntent = _intentStorage.GetCharacterIntentPool().get(GameConfig::PLAYER);
    float intentMoveX = playerIntent ? playerIntent->moveX : 0.0f;

    if (_currentInput.holdingButtons != 0 || _currentInput.pressedButtons != 0 || beforeX != afterX || beforeY != afterY)
    {
        SIM_LOG("[NATIVE_INPUT][CHECKPOINT_E] State before/after tick %llu: Entity=0(PLAYER), PosBefore=(%.2f, %.2f), PosAfter=(%.2f, %.2f), Delta=(%.2f, %.2f), IntentMoveX=%.2f, ActionBefore=%d, ActionAfter=%d, StateChanged=%s",
                (unsigned long long)tick, beforeX, beforeY, afterX, afterY, afterX - beforeX, afterY - beforeY,
                intentMoveX, beforeAction, afterAction, (beforeX != afterX || beforeY != afterY ? "YES" : "NO"));
    }

    // Advance frame counter
    _frameNumber++;
    ++_stateVersion;

    _diag.Intents(tick, view);
    if (_diag.Has(SimDiagnostics::kSnapshotEveryTick) || _diag.SnapshotRequested(_frameNumber))
    {
        _diag.Snapshot(_frameNumber, _diag.SnapshotRequested(_frameNumber) ? "requested" : "tick", GetSnapshotJson());
    }
    _diag.Flush();
}

void SimulationRuntime::BuildSnapshot(SimSnapshot* outSnapshot) const
{
    if (!outSnapshot)
        return;

    outSnapshot->frameNumber = _frameNumber;

    // Characters 0..5
    auto& posPool    = const_cast<ComponentStorage&>(_storage).GetCharacterPositionPool();
    auto& sizePool   = const_cast<ComponentStorage&>(_storage).GetCharacterSizePool();
    auto& actionPool = const_cast<ComponentStorage&>(_storage).GetCharacterActionStatePool();
    auto& intentPool = const_cast<IntentStorage&>(_intentStorage).GetCharacterIntentPool();

    for (int i = 0; i < 6; i++)
    {
        auto* pos    = posPool.get(i);
        auto* size   = sizePool.get(i);
        auto* action = actionPool.get(i);
        auto* intent = intentPool.get(i);

        SimEntitySnapshot& e = outSnapshot->entities[i];
        e.posX               = pos ? pos->position.x : 0.0f;
        e.posY               = pos ? pos->position.y : 0.0f;
        e.sizeX              = size ? size->size.x : 0.0f;
        e.sizeY              = size ? size->size.y : 0.0f;
        e.actionState        = action ? static_cast<int32_t>(action->status) : 0;
        e.actionRemainTime   = action ? action->remainTime : 0.0f;
        e.moveX              = intent ? intent->moveX : 0.0f;
        e.isGrounded         = (e.posY <= SystemConfig::MIN_Y + 0.1f) ? 1 : 0;

        // Facing direction: Team Left (0..2) faces right (1) unless moving left (-1).
        // Team Right (3..5) faces left (-1) unless moving right (1).
        if (i < 3)
        {
            e.facingDirection = (e.moveX < -0.1f) ? -1 : 1;
        }
        else
        {
            e.facingDirection = (e.moveX > 0.1f) ? 1 : -1;
        }
    }

    // Net (Entity 6)
    outSnapshot->net.posX            = MatchObjectConfig::NETPOSITION.x;
    outSnapshot->net.posY            = MatchObjectConfig::NETPOSITION.y;
    outSnapshot->net.sizeX           = MatchObjectConfig::NETSIZE.x;
    outSnapshot->net.sizeY           = MatchObjectConfig::NETSIZE.y;
    outSnapshot->net.actionState     = 0;
    outSnapshot->net.actionRemainTime= 0.0f;
    outSnapshot->net.moveX           = 0.0f;
    outSnapshot->net.isGrounded      = 1;
    outSnapshot->net.facingDirection = 1;

    // Ball (Entity 7)
    auto* ballPos        = const_cast<ComponentStorage&>(_storage).GetBallPositionPool().get(GameConfig::BALL);
    auto* ballTrajectory = const_cast<ComponentStorage&>(_storage).GetBallTrajectoryPool().get(GameConfig::BALL);
    auto* ballState      = const_cast<ComponentStorage&>(_storage).GetBallGameplayStatePool().get(DEFAULT_MATCH);

    outSnapshot->ball.posX           = ballPos ? ballPos->position.x : 0.0f;
    outSnapshot->ball.posY           = ballPos ? ballPos->position.y : 0.0f;
    outSnapshot->ball.speed          = ballTrajectory ? ballTrajectory->speed : 0.0f;
    outSnapshot->ball.trajectoryType = ballTrajectory ? static_cast<int32_t>(ballTrajectory->type) : 0;
    outSnapshot->ball.landingX       = ballState ? ballState->landingX : 0.0f;
    outSnapshot->ball.stateFrame     = ballState ? ballState->stateFrame : 0;

    // Match State
    auto* matchState = const_cast<ComponentStorage&>(_storage).GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    auto* rallyState = const_cast<ComponentStorage&>(_storage).GetRallyStatePool().get(DEFAULT_MATCH);

    if (matchState)
    {
        outSnapshot->match.leftScore       = matchState->leftScore;
        outSnapshot->match.rightScore      = matchState->rightScore;
        outSnapshot->match.servingTeam     = static_cast<int32_t>(matchState->servingTeam);
        outSnapshot->match.servingEntity   = static_cast<int32_t>(matchState->servingTeam == Team::LEFT ? matchState->leftServingEntity : matchState->rightServingEntity);
        outSnapshot->match.finished        = matchState->finished ? 1 : 0;
        outSnapshot->match.matchWinner     = static_cast<int32_t>(matchState->matchWinner);
    }
    else
    {
        outSnapshot->match = SimMatchSnapshot{};
    }

    if (rallyState)
    {
        outSnapshot->match.lastTouchEntity = static_cast<int32_t>(rallyState->lastTouch);
        outSnapshot->match.touchCount      = static_cast<int32_t>(rallyState->touchCount);
    }
}

void SimulationRuntime::GetSnapshot(SimSnapshot* outSnapshot) const
{
    BuildSnapshot(outSnapshot);
}

void SimulationRuntime::Shutdown()
{
    const bool wasInitialized = _initialized;
    _initialized = false;
    _framePreparationSystem.reset();
    _playerInputSystem.reset();
    _aiInputSystem.reset();
    _updateCharacterActionStateSystem.reset();
    _jumpStartSystem.reset();
    _updateTrajectoryFromIntent.reset();
    _updateTrajectoryByCollision.reset();
    _legacyUpdateTrajectory.reset();
    _velocityIntentSystem.reset();
    _gameRuleEvaluationSystem.reset();
    _loadStatsData.reset();
    _storage.Reset();
    _intentStorage.clear();
    _intentsProducedByTick = -1;
    ++_stateVersion;
    if (wasInitialized)
    {
        _diag.Lifecycle("shutdown", _frameNumber);
    }
    _diag.Close();
}
