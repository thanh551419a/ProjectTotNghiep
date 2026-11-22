#pragma once
#include <array>
#include <cstdint>
#include "../../Config/System/SystemConf.h"

enum class ActionState : uint8_t
{
    None = 0,

    Jump,
    Spike,
    Slide,
    Serve,
    Bump,
    Set,
    SpikeLight,
    SpikeMedium,
    SpikeStrong,
    Count
};

namespace ActionStateInfo
{
constexpr std::array<float, static_cast<size_t>(ActionState::Count)> CooldownRatio = {
    0.0f,                                // None
    SystemConfig::COOLDOWN_JUMP_RATIO,   // Jump        (0.80f * jumpTime)
    SystemConfig::COOLDOWN_SPIKE_RATIO,  // Spike       (1.00f * jumpTime)
    SystemConfig::COOLDOWN_SLIDE_RATIO,  // Slide       (1.75f * jumpTime)
    SystemConfig::COOLDOWN_SERVE_RATIO,  // Serve       (2/3   * jumpTime)
    SystemConfig::COOLDOWN_BUMP_RATIO,   // Bump        (2/3   * jumpTime)
    SystemConfig::COOLDOWN_SET_RATIO,    // Set         (0.50f * jumpTime)
    SystemConfig::COOLDOWN_SPIKE_RATIO,  // SpikeLight  (1.00f * jumpTime)
    SystemConfig::COOLDOWN_SPIKE_RATIO,  // SpikeMedium (1.00f * jumpTime)
    SystemConfig::COOLDOWN_SPIKE_RATIO   // SpikeStrong (1.00f * jumpTime)
};

inline constexpr float GetCooldownRatio(ActionState state)
{
    const size_t idx = static_cast<size_t>(state);
    return (idx < CooldownRatio.size()) ? CooldownRatio[idx] : 0.0f;
}

inline constexpr float ComputeCooldownMsFromJumpTime(ActionState state, float jumpTimeSeconds)
{
    return (jumpTimeSeconds * 1000.0f) * GetCooldownRatio(state);
}

inline constexpr float ComputeCooldownMsForJumpStat(ActionState state, float rawJumpStat)
{
    // Bước 1: Tính thời gian nhảy jumpTime = baseTime * (jump / baseJump)
    const float jumpTimeSeconds = SystemConfig::ComputeJumpUpDurationSeconds(rawJumpStat);
    // Bước 2: Tính thời gian cooldown từ jumpTime theo hệ số từng hành động
    return ComputeCooldownMsFromJumpTime(state, jumpTimeSeconds);
}

// Bảng Cooldown tham chiếu tại mốc chuẩn BASE_JUMP_STAT (170 -> jumpTime = 0.45s = 450ms)
constexpr std::array<float, static_cast<size_t>(ActionState::Count)> Cooldown = {
    ComputeCooldownMsForJumpStat(ActionState::None,        SystemConfig::BASE_JUMP_STAT),
    ComputeCooldownMsForJumpStat(ActionState::Jump,        SystemConfig::BASE_JUMP_STAT),
    ComputeCooldownMsForJumpStat(ActionState::Spike,       SystemConfig::BASE_JUMP_STAT),
    ComputeCooldownMsForJumpStat(ActionState::Slide,       SystemConfig::BASE_JUMP_STAT),
    ComputeCooldownMsForJumpStat(ActionState::Serve,       SystemConfig::BASE_JUMP_STAT),
    ComputeCooldownMsForJumpStat(ActionState::Bump,        SystemConfig::BASE_JUMP_STAT),
    ComputeCooldownMsForJumpStat(ActionState::Set,         SystemConfig::BASE_JUMP_STAT),
    ComputeCooldownMsForJumpStat(ActionState::SpikeLight,  SystemConfig::BASE_JUMP_STAT),
    ComputeCooldownMsForJumpStat(ActionState::SpikeMedium, SystemConfig::BASE_JUMP_STAT),
    ComputeCooldownMsForJumpStat(ActionState::SpikeStrong, SystemConfig::BASE_JUMP_STAT)
};
}

struct CharacterActionState
{
    ActionState status = ActionState::None;
    float remainTime   = 0.0f;
};
