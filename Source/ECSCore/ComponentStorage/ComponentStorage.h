#pragma once
#include "../ComponentPools/ComponentPool.h"
#include "../Components/PositionComponent.h"
#include "../Components/SizeComponent.h"
#include "../Components/JumpUpFrameComponent.h"
#include "../Components/BallTrajectoryComponent.h"
#include "../Components/CharacterComponents.h"
#include "../States/ActionState.h"
#include "../States/AirborneState.h"
#include "../States/BallGamePlayState.h"
#include "../States/MatchState.h"
#include "../States/RallyState.h"
#include "../../Gameplay/MatchData/CharacterStats.h"

#include "../../Config/Match/MatchEntityConfig.h"
#include "../../Config/Match/MatchObjectConfig.h"
#include "../../Config/ECS/ChunkConfig.h"
#include "../../Config/Match/BigRect.h"
#include "../../Config/System/SystemConf.h"

using namespace ChunkConfig;
using namespace BigRect;
using namespace SystemConfig;
using namespace MatchObjectConfig;

class ComponentStorage
{
public:
    ComponentPool<PositionComponent> characterPositionPool;
    ComponentPool<SizeComponent> characterSizePool;
    ComponentPool<PositionComponent> ballPositionPool;
    ComponentPool<JumpUpFrameComponent> jumpUpFramePool;
    ComponentPool<BallTrajectoryComponent> ballTrajectoryPool;
    ComponentPool<CharacterActionState> characterActionStatePool;
    ComponentPool<CharacterAirborneState> characterAirborneStatePool;

    ComponentPool<BallGamePlayState> ballGameplayStatePool;
    ComponentPool<MatchState> matchStatePool;
    ComponentPool<RallyState> rallyStatePool;

public:
    ComponentPool<PositionComponent>& GetCharacterPositionPool() { return characterPositionPool; }
    ComponentPool<PositionComponent>& GetBallPositionPool() { return ballPositionPool; }
    ComponentPool<SizeComponent>& GetCharacterSizePool() { return characterSizePool; }
    ComponentPool<JumpUpFrameComponent>& GetJumpUpFramePool() { return jumpUpFramePool; }
    ComponentPool<BallTrajectoryComponent>& GetBallTrajectoryPool() { return ballTrajectoryPool; }
    ComponentPool<CharacterActionState>& GetCharacterActionStatePool() { return characterActionStatePool; }
    ComponentPool<CharacterAirborneState>& GetCharacterAirborneStatePool() { return characterAirborneStatePool; }

    ComponentPool<BallGamePlayState>& GetBallGameplayStatePool() { return ballGameplayStatePool; }
    ComponentPool<MatchState>& GetMatchGamePlayStatePool() { return matchStatePool; }
    ComponentPool<RallyState>& GetRallyStatePool() { return rallyStatePool; }

    float GetCharacterAttackStat(int entityIndex) const
    {
        return RuntimeCharacterStats::GetInstance().GetStat(entityIndex).AttackPower;
    }
    float GetCharacterDefenseStat(int entityIndex) const
    {
        return RuntimeCharacterStats::GetInstance().GetStat(entityIndex).DefensePower;
    }
    float GetCharacterSpeedStat(int entityIndex) const
    {
        return RuntimeCharacterStats::GetInstance().GetStat(entityIndex).Speed;
    }
    float GetCharacterJumpingStat(int entityIndex) const
    {
        return RuntimeCharacterStats::GetInstance().GetStat(entityIndex).JumpPower;
    }
    bool SetCharacterStats(uint32_t entityId, float attack, float defense, float speed, float jump)
    {
        return RuntimeCharacterStats::GetInstance().SetStat(entityId, attack, defense, speed, jump);
    }

    SizeComponent sizeTemp;
    int _slowMoFramesRemaining = 0;
    int _pendingSlowMoDelayFrames = -1;
    int _pendingSlowMoDurationFrames = 0;
    float _lastSpikeContactHeight = 0.0f;

    int GetSlowMoFramesRemaining() const { return _slowMoFramesRemaining; }
    int GetPendingSlowMoDelayFrames() const { return _pendingSlowMoDelayFrames; }
    void TriggerSlowMotion(int frames)
    {
        _slowMoFramesRemaining = frames;
        _pendingSlowMoDelayFrames = -1;
        _pendingSlowMoDurationFrames = 0;
    }
    void ScheduleDelayedSlowMotion(int delayFrames, int durationFrames)
    {
        if (delayFrames <= 0)
        {
            TriggerSlowMotion(durationFrames);
        }
        else
        {
            _pendingSlowMoDelayFrames = delayFrames;
            _pendingSlowMoDurationFrames = durationFrames;
        }
    }
    void TickPendingSlowMotion()
    {
        if (_pendingSlowMoDelayFrames > 0)
        {
            --_pendingSlowMoDelayFrames;
            if (_pendingSlowMoDelayFrames == 0)
            {
                const int duration = _pendingSlowMoDurationFrames;
                _pendingSlowMoDelayFrames = -1;
                _pendingSlowMoDurationFrames = 0;
                _slowMoFramesRemaining = duration;
            }
        }
    }
    int DecrementSlowMoFrames()
    {
        if (_slowMoFramesRemaining > 0)
            --_slowMoFramesRemaining;
        return _slowMoFramesRemaining;
    }

    float GetLastSpikeContactHeight() const { return _lastSpikeContactHeight; }
    void SetLastSpikeContactHeight(float h) { _lastSpikeContactHeight = h; }

    SimMath::Vec2 _lastBallContactPos{0.0f, 0.0f};
    SimMath::Vec2 GetLastBallContactPos() const { return _lastBallContactPos; }
    void SetLastBallContactPos(const SimMath::Vec2& pos) { _lastBallContactPos = pos; }

    void Reset()
    {
        _slowMoFramesRemaining = 0;
        _pendingSlowMoDelayFrames = -1;
        _pendingSlowMoDurationFrames = 0;
        _lastSpikeContactHeight = 0.0f;
        _lastBallContactPos = SimMath::Vec2{0.0f, 0.0f};
        characterPositionPool.clear();
        characterSizePool.clear();
        ballPositionPool.clear();
        jumpUpFramePool.clear();
        ballTrajectoryPool.clear();
        characterActionStatePool.clear();
        characterAirborneStatePool.clear();
        ballGameplayStatePool.clear();
        matchStatePool.clear();
        rallyStatePool.clear();
    }

    void InitComponent();
    void InitState();
};
