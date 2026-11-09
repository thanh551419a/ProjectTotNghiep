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

    bool canGenerateIntent = (playerActionState->remainTime == 0.0f);
    if (!canGenerateIntent)
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

    const auto& mapping = InputMapping::GetInstance();
    float speed         = mapping.GetPlayerSpeed();

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

    auto& posPool   = _componentStorage->GetCharacterPositionPool();
    auto* playerPos = posPool.get(GameConfig::PLAYER);

    if (mapping.IsActionTriggered(input.pressedButtons, GameAction::JumpSpike))
    {
        if (playerPos && playerPos->position.y == SystemConfig::MIN_Y)
            intent.finalIntent = FinalIntent::Jump;
        else
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

    if (mapping.IsActionTriggered(input.pressedButtons, GameAction::Set))
    {
        intent.finalIntent = FinalIntent::Set;
    }

    if (mapping.IsActionTriggered(input.pressedButtons, GameAction::Serve))
    {
        intent.finalIntent = FinalIntent::Serve;
    }

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

    if (intent.moveX != 0.0f || intent.finalIntent != FinalIntent::None)
    {
        intentPool.add(GameConfig::PLAYER, intent);
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
