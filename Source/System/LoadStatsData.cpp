#include "LoadStatsData.h"
#include<filesystem>
#include <fstream>
LoadStatsData::LoadStatsData(CharacterStatStorage* characterStatStorage)
{
    _characterStatStorage = characterStatStorage;
}
void LoadStatsData::LoadData()
{
    
    std::ifstream ifs("../Source/Data/CharacterStats.json");
    if (!ifs.is_open())
    {
        AXLOG("Cannot open file");
        return;
    }

    rapidjson::IStreamWrapper isw(ifs);

    rapidjson::Document doc;
    doc.ParseStream(isw);

    AXLOG("Load JSON Success");
    // Load 1 data test , chỉ cần mảng đầu vào là 
    auto& matchStatPool = _characterStatStorage->GetMatchStatPool();
    auto match         = matchStatPool.get(0);  // Take entity 0

    LoadCharacterStat(doc, match);
    for (int i = 0; i < ChunkConfig::CHARACTER_PER_MATCH; i++)
    {
        AXLOG("Character %d", i + 1);
        AXLOG("AttackPower  : %.2f", match->stats[i].AttackPower);
        AXLOG("DefensePower : %.2f", match->stats[i].DefensePower);
        AXLOG("Speed        : %.2f", match->stats[i].Speed);
        AXLOG("JumpPower    : %.2f", match->stats[i].JumpPower);
        AXLOG("----------------------------");
    }
}
