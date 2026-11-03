#include "InputMapping.h"
#include "../ThirdParty/RapidJson/document.h"
#include "../ThirdParty/RapidJson/istreamwrapper.h"
#include "../Utils/SimLog.h"
#include <fstream>
#include <cctype>
#include <cstring>
#include <set>
#include <iterator>
#include <algorithm>

InputMapping::InputMapping()
{
    SetDefaults();
}

InputMapping& InputMapping::GetInstance()
{
    static InputMapping instance;
    return instance;
}

void InputMapping::SetDefaults()
{
    _actionToButtonMask[GameAction::MoveLeft]    = static_cast<uint32_t>(SimButton::MoveLeft);
    _actionToButtonMask[GameAction::MoveRight]   = static_cast<uint32_t>(SimButton::MoveRight);
    _actionToButtonMask[GameAction::JumpSpike]   = static_cast<uint32_t>(SimButton::JumpSpike);
    _actionToButtonMask[GameAction::Bump]        = static_cast<uint32_t>(SimButton::Bump);
    _actionToButtonMask[GameAction::Set]         = static_cast<uint32_t>(SimButton::Set);
    _actionToButtonMask[GameAction::Serve]       = static_cast<uint32_t>(SimButton::Serve);
    _actionToButtonMask[GameAction::SpikeLight]  = static_cast<uint32_t>(SimButton::SpikeLight);
    _actionToButtonMask[GameAction::SpikeMedium] = static_cast<uint32_t>(SimButton::SpikeMedium);
    _actionToButtonMask[GameAction::SpikeStrong] = static_cast<uint32_t>(SimButton::SpikeStrong);

    _buttonIdToAction.clear();
    // Default 0-based button IDs matching Unity SimButtonId & StreamingAssets/InputMapping.json
    _buttonIdToAction[0] = GameAction::MoveLeft;
    _buttonIdToAction[1] = GameAction::MoveRight;
    _buttonIdToAction[2] = GameAction::JumpSpike;
    _buttonIdToAction[3] = GameAction::Bump;
    _buttonIdToAction[4] = GameAction::Set;
    _buttonIdToAction[5] = GameAction::Serve;
    _buttonIdToAction[6] = GameAction::SpikeLight;
    _buttonIdToAction[7] = GameAction::SpikeMedium;
    _buttonIdToAction[8] = GameAction::SpikeStrong;

    _playerSpeed             = 7.0f;
    _aiSpeed                 = 7.0f;
    _aiAttackHeightThreshold = 300.0f;
}

bool InputMapping::LoadFromFile(const std::string& filePath)
{
    std::ifstream ifs(filePath);
    if (!ifs.is_open())
    {
        SIM_LOG("InputMapping: Unable to open '%s'.", filePath.c_str());
        return false;
    }

    std::string content((std::istreambuf_iterator<char>(ifs)),
                        std::istreambuf_iterator<char>());
    if (!LoadFromString(content))
    {
        SIM_LOG("InputMapping: Failed to parse/validate mapping from '%s'.", filePath.c_str());
        return false;
    }

    SIM_LOG("InputMapping: Successfully loaded configuration from '%s'.", filePath.c_str());
    return true;
}

bool InputMapping::LoadFromString(const std::string& jsonContent)
{
    rapidjson::Document doc;
    doc.Parse(jsonContent.c_str());
    if (doc.HasParseError() || !doc.IsObject())
    {
        SIM_LOG("InputMapping: Malformed JSON payload.");
        return false;
    }

    // Format A: Extended configuration with player/ai sections
    if (doc.HasMember("player") || doc.HasMember("buttonMapping") || doc.HasMember("ai"))
    {
        if (doc.HasMember("player") && doc["player"].IsObject())
        {
            const auto& playerObj = doc["player"];
            if (playerObj.HasMember("actions") && playerObj["actions"].IsObject())
            {
                const auto& actObj = playerObj["actions"];
                auto parseAction = [&](const char* name, GameAction action) {
                    if (actObj.HasMember(name) && actObj[name].IsUint())
                    {
                        _actionToButtonMask[action] = actObj[name].GetUint();
                    }
                };

                parseAction("MoveLeft", GameAction::MoveLeft);
                parseAction("MoveRight", GameAction::MoveRight);
                parseAction("JumpSpike", GameAction::JumpSpike);
                parseAction("Bump", GameAction::Bump);
                parseAction("Set", GameAction::Set);
                parseAction("Serve", GameAction::Serve);
                parseAction("SpikeLight", GameAction::SpikeLight);
                parseAction("SpikeMedium", GameAction::SpikeMedium);
                parseAction("SpikeStrong", GameAction::SpikeStrong);
            }

            if (playerObj.HasMember("speed") && playerObj["speed"].IsNumber())
            {
                _playerSpeed = playerObj["speed"].GetFloat();
            }
        }

        if (doc.HasMember("buttonMapping") && doc["buttonMapping"].IsObject())
        {
            const auto& bmObj = doc["buttonMapping"];
            std::unordered_map<int32_t, GameAction> tempMap;
            for (auto it = bmObj.MemberBegin(); it != bmObj.MemberEnd(); ++it)
            {
                if (it->name.IsString() && it->value.IsString())
                {
                    int32_t btnId = std::atoi(it->name.GetString());
                    GameAction act = StringToAction(it->value.GetString());
                    if (act != GameAction::None)
                    {
                        tempMap[btnId] = act;
                    }
                }
            }
            if (!tempMap.empty())
            {
                _buttonIdToAction = std::move(tempMap);
            }
        }

        if (doc.HasMember("ai") && doc["ai"].IsObject())
        {
            const auto& aiObj = doc["ai"];
            if (aiObj.HasMember("attackHeightThreshold") && aiObj["attackHeightThreshold"].IsNumber())
            {
                _aiAttackHeightThreshold = aiObj["attackHeightThreshold"].GetFloat();
            }
            if (aiObj.HasMember("speed") && aiObj["speed"].IsNumber())
            {
                _aiSpeed = aiObj["speed"].GetFloat();
            }
        }

        return true;
    }

    // Format B: Standalone button mapping: {"button_1": "left", "button_2": "right", ...}
    std::unordered_map<int32_t, GameAction> tempMap;
    std::set<int32_t> seenIds;
    std::set<GameAction> seenActions;

    for (auto it = doc.MemberBegin(); it != doc.MemberEnd(); ++it)
    {
        const char* keyName = it->name.GetString();
        int32_t btnId = -1;
        if (std::strncmp(keyName, "button_", 7) == 0)
        {
            const char* numPart = keyName + 7;
            if (*numPart == '\0')
            {
                SIM_LOG("InputMapping: Key '%s' lacks numeric ID.", keyName);
                return false;
            }
            btnId = std::atoi(numPart);
        }
        else if (std::isdigit(static_cast<unsigned char>(keyName[0])))
        {
            btnId = std::atoi(keyName);
        }
        else
        {
            SIM_LOG("InputMapping: Key '%s' is not a valid button identifier.", keyName);
            return false;
        }

        if (btnId <= 0)
        {
            SIM_LOG("InputMapping: Invalid button ID %d in key '%s'.", btnId, keyName);
            return false;
        }

        if (seenIds.count(btnId) > 0)
        {
            SIM_LOG("InputMapping: Duplicate button ID %d detected.", btnId);
            return false;
        }
        seenIds.insert(btnId);

        if (!it->value.IsString())
        {
            SIM_LOG("InputMapping: Button ID %d value is not a string.", btnId);
            return false;
        }

        GameAction act = StringToAction(it->value.GetString());
        if (act == GameAction::None)
        {
            SIM_LOG("InputMapping: Unknown/invalid action '%s' for button ID %d.", it->value.GetString(), btnId);
            return false;
        }

        tempMap[btnId] = act;
        seenActions.insert(act);
    }

    // Validate that required mappings exist (Left, Right, Bump, Serve)
    if (seenActions.count(GameAction::MoveLeft) == 0 ||
        seenActions.count(GameAction::MoveRight) == 0 ||
        seenActions.count(GameAction::Bump) == 0 ||
        seenActions.count(GameAction::Serve) == 0)
    {
        SIM_LOG("InputMapping: Missing required actions. Left, Right, Bump, and Serve must all be mapped.");
        return false;
    }

    _buttonIdToAction = std::move(tempMap);
    return true;
}

bool InputMapping::IsActionTriggered(uint32_t buttonMask, GameAction action) const
{
    auto it = _actionToButtonMask.find(action);
    if (it == _actionToButtonMask.end())
        return false;

    return (buttonMask & it->second) != 0;
}

bool InputMapping::MapButtonToAction(int32_t buttonId, GameAction& outAction) const
{
    auto it = _buttonIdToAction.find(buttonId);
    if (it != _buttonIdToAction.end())
    {
        outAction = it->second;
        return true;
    }
    outAction = GameAction::None;
    return false;
}

uint32_t InputMapping::ActionToButtonMask(GameAction action) const
{
    auto it = _actionToButtonMask.find(action);
    if (it != _actionToButtonMask.end())
    {
        return it->second;
    }
    return 0;
}

const char* InputMapping::ActionToString(GameAction action)
{
    switch (action)
    {
    case GameAction::None: return "None";
    case GameAction::MoveLeft: return "MoveLeft";
    case GameAction::MoveRight: return "MoveRight";
    case GameAction::JumpSpike: return "JumpSpike";
    case GameAction::Bump: return "Bump";
    case GameAction::Set: return "Set";
    case GameAction::Serve: return "Serve";
    case GameAction::SpikeLight: return "SpikeLight";
    case GameAction::SpikeMedium: return "SpikeMedium";
    case GameAction::SpikeStrong: return "SpikeStrong";
    default: return "Unknown";
    }
}

GameAction InputMapping::StringToAction(const std::string& rawName)
{
    std::string name = rawName;
    for (char& c : name)
    {
        c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    }

    if (name == "moveleft" || name == "left") return GameAction::MoveLeft;
    if (name == "moveright" || name == "right") return GameAction::MoveRight;
    if (name == "jumpspike" || name == "jump" || name == "spike") return GameAction::JumpSpike;
    if (name == "bump") return GameAction::Bump;
    if (name == "set") return GameAction::Set;
    if (name == "serve") return GameAction::Serve;
    if (name == "spikelight") return GameAction::SpikeLight;
    if (name == "spikemedium") return GameAction::SpikeMedium;
    if (name == "spikestrong") return GameAction::SpikeStrong;
    return GameAction::None;
}

const char* InputMapping::InputStateToString(int32_t inputState)
{
    switch (static_cast<SimInputState>(inputState))
    {
    case SimInputState::Released: return "Released";
    case SimInputState::Pressed: return "Pressed";
    case SimInputState::Held: return "Held";
    default: return "Invalid";
    }
}

