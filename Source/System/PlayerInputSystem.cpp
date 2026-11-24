#include "PlayerInputSystem.h"
#include "../Utils/SimLog.h"
#include <algorithm>

PlayerInputSystem::PlayerInputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    _componentStorage = componentStorage;
    _intentStorage    = intentStorage;
}

void PlayerInputSystem::update(float delta, const SimInputFrame& input)
{
    ComponentPool<CharacterIntent>& intentPool = _intentStorage->GetCharacterIntentPool();
    auto playerActionState = _componentStorage->GetCharacterActionStatePool().get(GameConfig::PLAYER);
    if (!playerActionState)
        return;

    auto& posPool    = _componentStorage->GetCharacterPositionPool();
    auto* playerPos  = posPool.get(GameConfig::PLAYER);
    auto* playerJump = _componentStorage->GetJumpUpFramePool().get(GameConfig::PLAYER);
    const float playerJumpStat = _componentStorage ? _componentStorage->GetCharacterJumpingStat(GameConfig::PLAYER)
                                                   : SystemConfig::BASE_JUMP_STAT;
    const float fullJumpDuration = SystemConfig::ComputeJumpUpDurationSeconds(playerJumpStat);
    const float maxRemainingJumpTimeForUnlock =
        (1.0f - SystemConfig::MIN_SPIKE_HEIGHT_RATIO) * fullJumpDuration;
    const bool isGrounded = (playerPos && playerPos->position.y <= SystemConfig::MIN_Y + 0.1f);
    // Trong 80% thời gian bay lên đầu tiên (đang trong Cooldown của ActionState::Jump hoặc jumpUpTime > 20% fullJumpDuration):
    // Cho phép tạo intent di chuyển trái/phải, nhưng chặn toàn bộ các hành động khác do đang trong thời gian Cooldown nhảy.
    const auto& mapping   = InputMapping::GetInstance();
    const bool isAscendingBelow80Percent =
        !isGrounded && (playerJump != nullptr) && (playerJump->jumpUpTime > maxRemainingJumpTimeForUnlock + 1e-5f);
    const bool isOnActionCooldown = (playerActionState->remainTime > 0.0f);
    const bool allowMovementDuringCooldown =
        (playerActionState->status == ActionState::Jump) ||
        (playerActionState->status == ActionState::None && isAscendingBelow80Percent);

    if (isOnActionCooldown && !allowMovementDuringCooldown)
    {
        if (input.holdingButtons != 0 || input.pressedButtons != 0 || input.moveAxisX != 0.0f)
        {
            SIM_LOG("[NATIVE_INPUT][CHECKPOINT_D] Player input rejected by gameplay condition: ActionState=%d, remainTime=%.1fms > 0",
                    static_cast<int>(playerActionState->status), playerActionState->remainTime);
        }
        return;
    }

    CharacterIntent intent;
    intent.moveX       = 0.0f;
    intent.finalIntent = FinalIntent::None;

    const float baseSpeed = DefaultCharacterStats::kReferenceBaseSpeed[GameConfig::PLAYER];
    const float statSpeed = _componentStorage ? _componentStorage->GetCharacterSpeedStat(GameConfig::PLAYER) : baseSpeed;
    float speed           = mapping.GetPlayerSpeed() * (baseSpeed > 0.0f ? (statSpeed / baseSpeed) : 1.0f);

    // Analog axis input takes precedence if provided, otherwise check digital buttons
    if (input.moveAxisX != 0.0f)
    {
        intent.moveX = input.moveAxisX * speed;
    }
    else
    {
        if (mapping.IsActionTriggered(input.holdingButtons, GameAction::MoveLeft))
        {
            intent.moveX -= speed;
        }
        if (mapping.IsActionTriggered(input.holdingButtons, GameAction::MoveRight))
        {
            intent.moveX += speed;
        }
    }

    const bool canTriggerGroundedAction = isGrounded && !isOnActionCooldown;
    const bool canTriggerAirborneAction = !isGrounded && !isOnActionCooldown && !isAscendingBelow80Percent;

    auto* ballState  = _componentStorage ? _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH) : nullptr;
    auto* matchState = _componentStorage ? _componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH) : nullptr;
    const bool isPlayerServeReset = (ballState != nullptr && ballState->stateFrame == BallStateFrame::Reset &&
                                     matchState != nullptr && matchState->servingTeam == Team::LEFT);
    if (!isPlayerServeReset && _componentStorage != nullptr)
    {
        _componentStorage->SetPreparingAttackServe(false);
    }

    if (canTriggerGroundedAction)
    {
        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::JumpSpike))
        {
            intent.finalIntent = FinalIntent::Jump;
        }

        // Independent Jump is evaluated after JumpSpike, so it wins that one conflict.
        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::Jump))
        {
            intent.finalIntent = FinalIntent::Jump;
        }

        if (mapping.IsActionTriggered(input.holdingButtons, GameAction::Bump))
        {
            intent.finalIntent = FinalIntent::Bump;
        }

        if (isPlayerServeReset)
        {
            if (mapping.IsActionTriggered(input.releasedButtons, GameAction::Bump))
            {
                intent.finalIntent = FinalIntent::LaunchAttackServe;
            }
            else if (mapping.IsActionTriggered(input.holdingButtons, GameAction::Bump) ||
                     mapping.IsActionTriggered(input.pressedButtons, GameAction::Bump))
            {
                intent.finalIntent = FinalIntent::PrepareAttackServe;
            }
        }

        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::Set))
        {
            intent.finalIntent = FinalIntent::Set;
        }

        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::Serve))
        {
            intent.finalIntent = FinalIntent::Serve;
        }

        // Grounded SpikeLight / SpikeMedium / SpikeStrong: allowed whenever !isOnActionCooldown
        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::SpikeLight))
        {
            intent.finalIntent = FinalIntent::SpikeLight;
        }

        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::SpikeMedium))
        {
            intent.finalIntent = FinalIntent::SpikeMedium;
        }

        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::SpikeStrong))
        {
            intent.finalIntent = FinalIntent::SpikeStrong;
        }
    }
    else if (canTriggerAirborneAction)
    {
        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::JumpSpike))
        {
            intent.finalIntent = FinalIntent::Spike;
        }

        // Independent Jump is evaluated after JumpSpike, so it wins that one conflict.
        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::Jump))
        {
            intent.finalIntent = FinalIntent::Jump;
        }

        if (mapping.IsActionTriggered(input.holdingButtons, GameAction::Bump))
        {
            intent.finalIntent = FinalIntent::Bump;
        }

        if (isPlayerServeReset)
        {
            if (mapping.IsActionTriggered(input.releasedButtons, GameAction::Bump))
            {
                intent.finalIntent = FinalIntent::LaunchAttackServe;
            }
            else if (mapping.IsActionTriggered(input.holdingButtons, GameAction::Bump) ||
                     mapping.IsActionTriggered(input.pressedButtons, GameAction::Bump))
            {
                intent.finalIntent = FinalIntent::PrepareAttackServe;
            }
        }

        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::Set))
        {
            intent.finalIntent = FinalIntent::Set;
        }

        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::Serve))
        {
            intent.finalIntent = FinalIntent::Serve;
        }

        // Airborne SpikeLight / SpikeMedium / SpikeStrong: requires !isOnActionCooldown && !isAscendingBelow80Percent
        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::SpikeLight))
        {
            intent.finalIntent = FinalIntent::SpikeLight;
        }

        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::SpikeMedium))
        {
            intent.finalIntent = FinalIntent::SpikeMedium;
        }

        if (mapping.IsActionTriggered(input.pressedButtons, GameAction::SpikeStrong))
        {
            intent.finalIntent = FinalIntent::SpikeStrong;
        }
    }

    // Ensure 2-Phase Hold-Bump Attack Serve in Reset is 100% deterministic across hold -> release transitions,
    // even if a release frame coincides with an action cooldown or lacks an explicit releasedButtons bit after holding.
    if (isPlayerServeReset && _componentStorage != nullptr)
    {
        const bool bumpHeldOrPressed = mapping.IsActionTriggered(input.holdingButtons, GameAction::Bump) ||
                                       mapping.IsActionTriggered(input.pressedButtons, GameAction::Bump);
        const bool bumpReleased      = mapping.IsActionTriggered(input.releasedButtons, GameAction::Bump);
        if (bumpReleased || (_componentStorage->IsPreparingAttackServe() && !bumpHeldOrPressed))
        {
            _componentStorage->SetPreparingAttackServe(false);
            intent.finalIntent = FinalIntent::LaunchAttackServe;
        }
        else if (bumpHeldOrPressed && (canTriggerGroundedAction || canTriggerAirborneAction || _componentStorage->IsPreparingAttackServe()))
        {
            _componentStorage->SetPreparingAttackServe(true);
            if (intent.finalIntent == FinalIntent::None || intent.finalIntent == FinalIntent::Bump ||
                intent.finalIntent == FinalIntent::PrepareAttackServe)
            {
                intent.finalIntent = FinalIntent::PrepareAttackServe;
            }
        }
    }

    if (intent.moveX != 0.0f || intent.finalIntent != FinalIntent::None)
    {
        if (auto* existing = intentPool.get(GameConfig::PLAYER))
        {
            *existing = intent;
        }
        else
        {
            intentPool.add(GameConfig::PLAYER, intent);
        }
        SIM_LOG("[NATIVE_INPUT][CHECKPOINT_D] Player intent generated: moveX=%.2f, finalIntent=%d. Action accepted.",
                intent.moveX, static_cast<int>(intent.finalIntent));
    }
}

#if defined(AXMOL_ENABLED)
void PlayerInputSystem::update(float delta, const InputListener::InputFrame& input)
{
    SimInputFrame simFrame;

    auto isHolding = [&](EventKeyboard::KeyCode key) {
        return std::find(input.holdingKeys.begin(), input.holdingKeys.end(), key) != input.holdingKeys.end();
    };

    auto isPressed = [&](EventKeyboard::KeyCode key) {
        return std::find(input.pressedKeys.begin(), input.pressedKeys.end(), key) != input.pressedKeys.end();
    };

    if (isHolding(EventKeyboard::KeyCode::KEY_A))
        simFrame.holdingButtons |= static_cast<uint32_t>(SimButton::MoveLeft);
    if (isHolding(EventKeyboard::KeyCode::KEY_D))
        simFrame.holdingButtons |= static_cast<uint32_t>(SimButton::MoveRight);
    if (isHolding(EventKeyboard::KeyCode::KEY_LEFT_SHIFT))
        simFrame.holdingButtons |= static_cast<uint32_t>(SimButton::Bump);

    if (isPressed(EventKeyboard::KeyCode::KEY_SPACE))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::JumpSpike);
    if (isPressed(EventKeyboard::KeyCode::KEY_S))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::Set);
    if (isPressed(EventKeyboard::KeyCode::KEY_T))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::Serve);
    if (isPressed(EventKeyboard::KeyCode::KEY_J))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::SpikeLight);
    if (isPressed(EventKeyboard::KeyCode::KEY_K))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::SpikeMedium);
    if (isPressed(EventKeyboard::KeyCode::KEY_L))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::SpikeStrong);

    update(delta, simFrame);
}
#endif
