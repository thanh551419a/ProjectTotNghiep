#pragma once
#include <axmol.h>
#include "../Gameplay/MatchData/CharacterStatStorage.h"
#include <rapidjson/document.h>
#include <rapidjson/istreamwrapper.h>
#include <rapidjson/prettywriter.h>
#include <string>
#include <fstream>
class LoadStatsData
{
    private:
        CharacterStatStorage* _characterStatStorage = nullptr;

    public:
        LoadStatsData(CharacterStatStorage* characterStatStorage);  // khởi tạo con trỏ _characterStatStorage là nullptr
        inline bool LoadCharacterStat(const rapidjson::Document& doc, MatchCharacterStat* matchStat)
        {
            for (int i = 0; i < ChunkConfig::CHARACTER_PER_MATCH; i++)
            {
                std::string key = "Character" + std::to_string(i + 1);

                if (!doc.HasMember(key.c_str()))
                    return false;

                const auto& character = doc[key.c_str()];

                matchStat->stats[i].AttackPower  = character["AttackPower"].GetFloat();
                matchStat->stats[i].DefensePower = character["DefensePower"].GetFloat();
                matchStat->stats[i].Speed        = character["Speed"].GetFloat();
                matchStat->stats[i].JumpPower    = character["JumpPower"].GetFloat();
            }

            return true;
        }
        void LoadData(); // Load 1 data test , chỉ cần mảng đầu vào làm
};
