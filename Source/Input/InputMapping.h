#pragma once

#include <cstdint>
#include <string>
#include <unordered_map>
#include "../Gameplay/FinalIntent.h"

// ==============================================================
// InputMapping
// Data-driven input mapping for The Spike Cross Remastered.
// Decouples physical input / abstract button IDs from Gameplay Actions.
// ==============================================================

enum class SimButton : uint32_t
{
    None        = 0,
    MoveLeft    = 1 << 0,  // 1
    MoveRight   = 1 << 1,  // 2
    JumpSpike   = 1 << 2,  // 4
    Bump        = 1 << 3,  // 8
    Set         = 1 << 4,  // 16
    Serve       = 1 << 5,  // 32
    SpikeLight  = 1 << 6,  // 64
    SpikeMedium = 1 << 7,  // 128
    SpikeStrong = 1 << 8,  // 256
    Jump        = 1 << 9   // 512
};

enum class GameAction : uint32_t
{
    None = 0,
    MoveLeft,
    MoveRight,
    JumpSpike,
    Bump,
    Set,
    Serve,
    SpikeLight,
    SpikeMedium,
    SpikeStrong,
    Jump
};

enum class SimInputState : int32_t
{
    Released = 0,
    Pressed  = 1,
    Held     = 2
};

struct SimInputFrame
{
    uint32_t holdingButtons = 0; // Currently held buttons bitmask
    uint32_t pressedButtons = 0; // Buttons pressed this frame bitmask
    float moveAxisX         = 0.0f; // Optional analog axis [-1.0f, 1.0f]
};

class InputMapping
{
public:
    InputMapping();

    bool LoadFromFile(const std::string& filePath);
    bool LoadFromString(const std::string& jsonContent);

    // Queries whether an action is triggered by the given button bitmask
    bool IsActionTriggered(uint32_t buttonMask, GameAction action) const;

    // Maps an abstract button ID (received from Unity) to a native GameAction
    bool MapButtonToAction(int32_t buttonId, GameAction& outAction) const;
    uint32_t ActionToButtonMask(GameAction action) const;
    static const char* ActionToString(GameAction action);
    static GameAction StringToAction(const std::string& name);
    static const char* InputStateToString(int32_t inputState);

    // Configurable gameplay tuning loaded from JSON
    float GetPlayerSpeed() const { return _playerSpeed; }
    float GetAISpeed() const { return _aiSpeed; }
    float GetAIAttackHeightThreshold() const { return _aiAttackHeightThreshold; }

    static InputMapping& GetInstance();

private:
    void SetDefaults();

    std::unordered_map<GameAction, uint32_t> _actionToButtonMask;
    std::unordered_map<int32_t, GameAction> _buttonIdToAction;
    float _playerSpeed               = 7.0f;
    float _aiSpeed                   = 7.0f;
    float _aiAttackHeightThreshold   = 300.0f;
};
