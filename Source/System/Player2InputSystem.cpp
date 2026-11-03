#include "Player2InputSystem.h"
#include <algorithm>

Player2InputSystem::Player2InputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    _componentStorage = componentStorage;
    _intentStorage    = intentStorage;
}

void Player2InputSystem::update(float delta, const SimInputFrame& input)
{
    ComponentPool<CharacterIntent>& intentPool = _intentStorage->GetCharacterIntentPool();
    auto playerActionState = _componentStorage->GetCharacterActionStatePool().get(GameConfig::OPPONENT_1);
    if (!playerActionState)
        return;

    bool canGenerateIntent = (playerActionState->remainTime == 0.0f);
    if (!canGenerateIntent)
    {
        return;
    }

    CharacterIntent intent;
    intent.moveX       = 0.0f;
    intent.finalIntent = FinalIntent::None;

    const auto& mapping = InputMapping::GetInstance();
    float speed         = mapping.GetPlayerSpeed();

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
    auto* opponentPos = posPool.get(GameConfig::OPPONENT_1);

    if (mapping.IsActionTriggered(input.pressedButtons, GameAction::JumpSpike))
    {
        if (opponentPos && opponentPos->position.y == SystemConfig::MIN_Y)
            intent.finalIntent = FinalIntent::Jump;
        else
            intent.finalIntent = FinalIntent::Spike;
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
        intentPool.add(GameConfig::OPPONENT_1, intent);
    }
}

#if defined(AXMOL_ENABLED)
void Player2InputSystem::update(float delta, const InputListener::InputFrame& input)
{
    SimInputFrame simFrame;

    auto isHolding = [&](EventKeyboard::KeyCode key) {
        return std::find(input.holdingKeys.begin(), input.holdingKeys.end(), key) != input.holdingKeys.end();
    };

    auto isPressed = [&](EventKeyboard::KeyCode key) {
        return std::find(input.pressedKeys.begin(), input.pressedKeys.end(), key) != input.pressedKeys.end();
    };

    if (isHolding(EventKeyboard::KeyCode::KEY_LEFT_ARROW))
        simFrame.holdingButtons |= static_cast<uint32_t>(SimButton::MoveLeft);
    if (isHolding(EventKeyboard::KeyCode::KEY_RIGHT_ARROW))
        simFrame.holdingButtons |= static_cast<uint32_t>(SimButton::MoveRight);
    if (isHolding(EventKeyboard::KeyCode::KEY_DOWN_ARROW))
        simFrame.holdingButtons |= static_cast<uint32_t>(SimButton::Bump);

    if (isPressed(EventKeyboard::KeyCode::KEY_UP_ARROW))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::JumpSpike);
    if (isPressed(EventKeyboard::KeyCode::KEY_SLASH))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::Set);
    if (isPressed(EventKeyboard::KeyCode::KEY_RIGHT_SHIFT))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::Serve);
    if (isPressed(EventKeyboard::KeyCode::KEY_SEMICOLON))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::SpikeLight);
    if (isPressed(EventKeyboard::KeyCode::KEY_APOSTROPHE))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::SpikeMedium);
    if (isPressed(EventKeyboard::KeyCode::KEY_ENTER))
        simFrame.pressedButtons |= static_cast<uint32_t>(SimButton::SpikeStrong);

    update(delta, simFrame);
}
#endif
