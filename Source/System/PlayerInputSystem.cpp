#include "PlayerInputSystem.h"
#include <algorithm>
namespace InputBinding
{
    constexpr EventKeyboard::KeyCode MoveLeft  = EventKeyboard::KeyCode::KEY_A;
    constexpr EventKeyboard::KeyCode MoveRight = EventKeyboard::KeyCode::KEY_D;

    constexpr EventKeyboard::KeyCode JumpSpike = EventKeyboard::KeyCode::KEY_SPACE;
    constexpr EventKeyboard::KeyCode Bump      = EventKeyboard::KeyCode::KEY_LEFT_SHIFT;
    constexpr EventKeyboard::KeyCode Set       = EventKeyboard::KeyCode::KEY_S;
    constexpr EventKeyboard::KeyCode Serve     = EventKeyboard::KeyCode::KEY_T;
    constexpr EventKeyboard::KeyCode SpikeLight     = EventKeyboard::KeyCode::KEY_J;
    constexpr EventKeyboard::KeyCode SpikeMedium     = EventKeyboard::KeyCode::KEY_K;
    constexpr EventKeyboard::KeyCode SpikeStrong     = EventKeyboard::KeyCode::KEY_L;
}  // namespace InputBinding
inline bool IsHolding(const InputListener::InputFrame& input, EventKeyboard::KeyCode key)
{
    return std::find(input.holdingKeys.begin(), input.holdingKeys.end(), key) != input.holdingKeys.end();
}

inline bool IsPressed(const InputListener::InputFrame& input, EventKeyboard::KeyCode key)
{
    return std::find(input.pressedKeys.begin(), input.pressedKeys.end(), key) != input.pressedKeys.end();
}
PlayerInputSystem::PlayerInputSystem(IntentStorage* intentStorage , ComponentStorage* componentStorage)
{
    _componentStorage = componentStorage;
    _intentStorage = intentStorage;
    //AXLOG("[PlayerInputSystem] storage=%p", (void*)_storage);
}

void PlayerInputSystem::update(float delta, InputListener::InputFrame input)
{
    ComponentPool<CharacterIntent>& intentPool = _intentStorage->GetCharacterIntentPool();
    //ComponentPool < CharacterIntent > & intentPool = _intentStorage->GetCharacterIntentPool();
    // tạo intent mới mỗi frame
    CharacterIntent intent;

    //AXLOG("intentPool addr = %p", &intentPool);
    auto playerActionState = _componentStorage->GetCharacterActionStatePool().get(GameConfig::PLAYER);
    bool canGenerateIntent = playerActionState->remainTime == 0.0f;
   /* AXLOG("remainTime hien tai la : %f", playerActionState->remainTime);*/
    if (!canGenerateIntent)  // còn tồn tại dư thừa từ frame trước
    {
        return;
    }

   /* AXLOG("co nhan input dau vao cho Player");*/
    // reset
    intent.moveX = 0.0f;
    intent.finalIntent = FinalIntent::None;
    // ======================================================
    // INPUT DETECTION
    // ======================================================
    auto& posPool = _componentStorage->GetCharacterPositionPool();
    if (IsHolding(input, InputBinding::MoveLeft))
    {
        intent.moveX -= SystemConfig::SPEED;
    }

    if (IsHolding(input, InputBinding::MoveRight))
    {
        intent.moveX += SystemConfig::SPEED;
    }

    if (IsPressed(input, InputBinding::JumpSpike))
    {
        if (posPool.get(GameConfig::PLAYER)->position.y == SystemConfig::MIN_Y)
            intent.finalIntent = FinalIntent::Jump;
        else
            intent.finalIntent = FinalIntent::Spike;
    }

    if (IsHolding(input, InputBinding::Bump))
    {
        intent.finalIntent = FinalIntent::Bump;
    }

    if (IsPressed(input, InputBinding::Set))
    {
        intent.finalIntent = FinalIntent::Set;
    }

    if (IsPressed(input, InputBinding::Serve))
    {
        intent.finalIntent = FinalIntent::Serve;
    }
    if (IsPressed(input, InputBinding::SpikeLight))
    {
        intent.finalIntent = FinalIntent::SpikeLight;
    }

    if (IsPressed(input, InputBinding::SpikeMedium))
    {
        intent.finalIntent = FinalIntent::SpikeMedium;
    }

    if (IsPressed(input, InputBinding::SpikeStrong))
    {
        intent.finalIntent = FinalIntent::SpikeStrong;
    }
    
    if (intent.moveX != 0.0f || intent.finalIntent != None)    
    {
      /*  AXLOG("[PlayerInputSystem] Adding intent: moveX=%.2f, finalIntent=%d", intent.moveX,
              static_cast<int>(intent.finalIntent));*/
        intentPool.add(0, intent);
       // AXLOG("intentPool size = %d", intentPool.size());
    }
}
