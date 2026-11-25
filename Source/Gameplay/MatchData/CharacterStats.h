#pragma once
#include <vector>
#include "../../Config/ECS/ChunkConfig.h"
struct CharacterStat
{
    float AttackPower;
    float DefensePower;
    float Speed;
    float JumpPower;
};

struct MatchCharacterStat
{
    CharacterStat stats[ChunkConfig::CHARACTER_PER_MATCH];
};

namespace DefaultCharacterStats
{
    // Bảng chỉ số mặc định của 6 nhân vật (khớp với MyGame/Source/Data/CharacterStats.json)
    inline constexpr CharacterStat kTable[ChunkConfig::CHARACTER_PER_MATCH] = {
        {180.0f, 80.0f, 90.0f, 95.0f},   // Character1 (PLAYER = 0)
        {92.0f,  88.0f, 96.0f, 90.0f},   // Character2 (TEAMMATE_1 = 1)
        {180.0f, 75.0f, 84.0f, 98.0f},   // Character3 (TEAMMATE_2 = 2)
        {180.0f, 95.0f, 82.0f, 88.0f},   // Character4 (OPPONENT_1 = 3)
        {97.0f,  86.0f, 93.0f, 92.0f},   // Character5 (OPPONENT_2 = 4)
        {175.0f, 78.0f, 88.0f, 100.0f}   // Character6 (OPPONENT_3 = 5)
    };

    inline constexpr float kReferenceBaseSpeed[ChunkConfig::CHARACTER_PER_MATCH] = {
        90.0f, 96.0f, 84.0f, 82.0f, 93.0f, 88.0f
    };

    inline constexpr float GetJumpingStat(int entityIndex)
    {
        if (entityIndex >= 0 && entityIndex < ChunkConfig::CHARACTER_PER_MATCH)
        {
            return kTable[entityIndex].JumpPower;
        }
        return 100.0f;
    }
}

// ==============================================================
// RuntimeCharacterStats
// Lưu trữ chỉ số nhân vật trong RAM (cho phép đọc từ file rồi
// chỉnh sửa trực tiếp trong RAM khi đang chạy game).
// ==============================================================
class RuntimeCharacterStats
{
public:
    static RuntimeCharacterStats& GetInstance()
    {
        static RuntimeCharacterStats instance;
        return instance;
    }

    const CharacterStat& GetStat(int entityIndex) const
    {
        if (entityIndex >= 0 && entityIndex < ChunkConfig::CHARACTER_PER_MATCH)
        {
            return _activeStats[entityIndex];
        }
        return _activeStats[0];
    }

    bool GetStat(uint32_t entityId, float* outAttack, float* outDefense, float* outSpeed, float* outJump) const
    {
        if (entityId >= static_cast<uint32_t>(ChunkConfig::CHARACTER_PER_MATCH))
            return false;
        const CharacterStat& s = _activeStats[entityId];
        if (outAttack)  *outAttack  = s.AttackPower;
        if (outDefense) *outDefense = s.DefensePower;
        if (outSpeed)   *outSpeed   = s.Speed;
        if (outJump)    *outJump    = s.JumpPower;
        return true;
    }

    bool SetStat(uint32_t entityId, float attack, float defense, float speed, float jump)
    {
        if (entityId >= static_cast<uint32_t>(ChunkConfig::CHARACTER_PER_MATCH))
            return false;
        _activeStats[entityId].AttackPower  = attack;
        _activeStats[entityId].DefensePower = defense;
        _activeStats[entityId].Speed        = speed;
        _activeStats[entityId].JumpPower    = jump;
        return true;
    }

    void SetLoadedStats(const CharacterStat loaded[ChunkConfig::CHARACTER_PER_MATCH])
    {
        for (int i = 0; i < ChunkConfig::CHARACTER_PER_MATCH; ++i)
        {
            _loadedStats[i] = loaded[i];
            _activeStats[i] = loaded[i];
        }
    }

    void ResetToLoaded()
    {
        for (int i = 0; i < ChunkConfig::CHARACTER_PER_MATCH; ++i)
        {
            _activeStats[i] = _loadedStats[i];
        }
    }

private:
    RuntimeCharacterStats()
    {
        for (int i = 0; i < ChunkConfig::CHARACTER_PER_MATCH; ++i)
        {
            _loadedStats[i] = DefaultCharacterStats::kTable[i];
            _activeStats[i] = DefaultCharacterStats::kTable[i];
        }
    }

    CharacterStat _loadedStats[ChunkConfig::CHARACTER_PER_MATCH]{};
    CharacterStat _activeStats[ChunkConfig::CHARACTER_PER_MATCH]{};
};
