#include "Player2InputSystem.h"
#include <algorithm>
namespace InputBinding2
{
// Movement
    using Key = ax::EventKeyboard::KeyCode;
// Movement
    constexpr EventKeyboard::KeyCode MoveLeft  = EventKeyboard::KeyCode::KEY_LEFT_ARROW;
    constexpr EventKeyboard::KeyCode MoveRight = EventKeyboard::KeyCode::KEY_RIGHT_ARROW;

    // Gameplay (Numpad)
    constexpr EventKeyboard::KeyCode JumpSpike = EventKeyboard::KeyCode::KEY_UP_ARROW;  // 8

    constexpr EventKeyboard::KeyCode Bump  = EventKeyboard::KeyCode::KEY_DOWN_ARROW;  // 4
    constexpr EventKeyboard::KeyCode Set  = EventKeyboard::KeyCode::KEY_SLASH;       // 5

    constexpr EventKeyboard::KeyCode Serve = EventKeyboard::KeyCode::KEY_RIGHT_SHIFT;  // 0

    // Spike power (đổi sang cụm khác)
    constexpr EventKeyboard::KeyCode SpikeLight  = EventKeyboard::KeyCode::KEY_SEMICOLON;  // 7
    constexpr EventKeyboard::KeyCode SpikeMedium = EventKeyboard::KeyCode::KEY_APOSTROPHE;  // 8
    constexpr EventKeyboard::KeyCode SpikeStrong = EventKeyboard::KeyCode::KEY_ENTER;  // 9
    }
inline bool IsHolding(const InputListener::InputFrame& input, EventKeyboard::KeyCode key)
{
    return std::find(input.holdingKeys.begin(), input.holdingKeys.end(), key) != input.holdingKeys.end();
}

inline bool IsPressed(const InputListener::InputFrame& input, EventKeyboard::KeyCode key)
{
    return std::find(input.pressedKeys.begin(), input.pressedKeys.end(), key) != input.pressedKeys.end();
}
Player2InputSystem::Player2InputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    _componentStorage = componentStorage;
    _intentStorage    = intentStorage;
    // AXLOG("[PlayerInputSystem] storage=%p", (void*)_storage);
}

void Player2InputSystem::update(float delta, InputListener::InputFrame input)
{
    ComponentPool<CharacterIntent>& intentPool = _intentStorage->GetCharacterIntentPool();
    // ComponentPool < CharacterIntent > & intentPool = _intentStorage->GetCharacterIntentPool();
    //  tạo intent mới mỗi frame
    CharacterIntent intent;

    // AXLOG("intentPool addr = %p", &intentPool);
    auto playerActionState = _componentStorage->GetCharacterActionStatePool().get(GameConfig::OPPONENT_1);
    bool canGenerateIntent = (playerActionState->remainTime == 0.0f);
    /* AXLOG("remainTime hien tai la : %f", playerActionState->remainTime);*/
    if (!canGenerateIntent)  // còn tồn tại dư thừa từ frame trước
    {
        return;
    }

    /* AXLOG("co nhan input dau vao cho Player");*/
    // reset
    intent.moveX       = 0.0f;
    intent.finalIntent = FinalIntent::None;
    // ======================================================
    // INPUT DETECTION
    // ======================================================
    auto& posPool = _componentStorage->GetCharacterPositionPool();
    if (IsHolding(input, InputBinding2::MoveLeft))
    {
        intent.moveX -= SystemConfig::SPEED;
    }

    if (IsHolding(input, InputBinding2::MoveRight))
    {
        intent.moveX += SystemConfig::SPEED;
    }

    if (IsPressed(input, InputBinding2::JumpSpike))
    {
        if (posPool.get(GameConfig::PLAYER)->position.y == SystemConfig::MIN_Y)
            intent.finalIntent = FinalIntent::Jump;
        else
            intent.finalIntent = FinalIntent::Spike;
    }

    if (IsHolding(input, InputBinding2::Bump))
    {
        intent.finalIntent = FinalIntent::Bump;
    }

    if (IsPressed(input, InputBinding2::Set))
    {
        intent.finalIntent = FinalIntent::Set;
    }

    if (IsPressed(input, InputBinding2::Serve))
    {
        intent.finalIntent = FinalIntent::Serve;
    }
    if (IsPressed(input, InputBinding2::SpikeLight))
    {
        intent.finalIntent = FinalIntent::SpikeLight;
    }

    if (IsPressed(input, InputBinding2::SpikeMedium))
    {
        intent.finalIntent = FinalIntent::SpikeMedium;
    }

    if (IsPressed(input, InputBinding2::SpikeStrong))
    {
        intent.finalIntent = FinalIntent::SpikeStrong;
    }

    if (intent.moveX != 0.0f || intent.finalIntent != None)
    {
        /*  AXLOG("[PlayerInputSystem] Adding intent: moveX=%.2f, finalIntent=%d", intent.moveX,
                static_cast<int>(intent.finalIntent));*/
        intentPool.add(GameConfig::OPPONENT_1, intent);
        // AXLOG("intentPool size = %d", intentPool.size());
    }
}
