#include "SimulationApi.h"

#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/IntentComponents/CharacterIntent.h"
#include "../Config/Match/MatchEntityConfig.h"
#include "../Config/ECS/ChunkConfig.h"

#include <sstream>
#include <string>

namespace Simulation
{
namespace
{
struct RuntimeState
{
    bool initialized = false;
    ComponentStorage storage;
    IntentStorage intentStorage;
    std::string lastJson;
};

RuntimeState& GetRuntime()
{
    static RuntimeState runtime;
    return runtime;
}

std::string GetTypeInputName(TypeInput typeInput)
{
    switch (typeInput)
    {
    case TypeInput::Keyboard:
        return "Keyboard";
    case TypeInput::AI:
        return "AI";
    case TypeInput::Replay:
        return "Replay";
    case TypeInput::Unity:
        return "Unity";
    default:
        return "Unknown";
    }
}

FinalIntent ResolveFinalIntent(const Input& input)
{
    if (input.spikeStrong)
        return FinalIntent::SpikeStrong;
    if (input.spikeMedium)
        return FinalIntent::SpikeMedium;
    if (input.spikeLight)
        return FinalIntent::SpikeLight;
    if (input.serve)
        return FinalIntent::Serve;
    if (input.set)
        return FinalIntent::Set;
    if (input.bump)
        return FinalIntent::Bump;
    if (input.jump)
        return FinalIntent::Jump;
    return FinalIntent::None;
}

}  // namespace

bool Init()
{
    auto& runtime = GetRuntime();
    if (!runtime.initialized)
    {
        runtime.storage.InitComponent();
        runtime.intentStorage.clear();
    }
    runtime.initialized = true;
    runtime.lastJson    = "{\"status\":\"ready\"}";
    return true;
}

JsonData Update(const UpdateParameters& parameters)
{
    auto& runtime = GetRuntime();
    if (!runtime.initialized)
    {
        Init();
    }

    CharacterIntent intent;
    intent.moveX       = parameters.input.moveX;
    intent.finalIntent = ResolveFinalIntent(parameters.input);

    runtime.intentStorage.characterIntentPool.add(GameConfig::PLAYER, intent);

    auto* playerPos = runtime.storage.GetCharacterPositionPool().get(GameConfig::PLAYER);
    if (playerPos != nullptr)
    {
        playerPos->position.x += parameters.input.moveX * 3.0f;
        if (parameters.input.jump)
        {
            playerPos->position.y += 20.0f;
        }
    }

    auto* ballPos = runtime.storage.GetBallPositionPool().get(GameConfig::BALL);
    if (ballPos != nullptr)
    {
        ballPos->position.x += 1.5f;
        ballPos->position.y += parameters.input.jump ? 5.0f : 0.0f;
    }

    auto* matchState = runtime.storage.GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    if (matchState != nullptr)
    {
        if (parameters.input.bump || parameters.input.set || parameters.input.serve)
        {
            matchState->leftScore = (matchState->leftScore + 1) % 25;
        }
    }

    std::ostringstream oss;
    oss << "{"
        << "\"typeInput\":\"" << GetTypeInputName(parameters.typeInput) << "\","
        << "\"player\":{\"x\":" << (playerPos ? playerPos->position.x : 0.0f)
        << ",\"y\":" << (playerPos ? playerPos->position.y : 0.0f) << "},"
        << "\"ball\":{\"x\":" << (ballPos ? ballPos->position.x : 0.0f)
        << ",\"y\":" << (ballPos ? ballPos->position.y : 0.0f) << "},"
        << "\"score\":{\"left\":" << (matchState ? matchState->leftScore : 0)
        << ",\"right\":" << (matchState ? matchState->rightScore : 0) << "},"
        << "\"intent\":\"" << static_cast<int>(intent.finalIntent) << "\""
        << "}";

    runtime.lastJson = oss.str();
    return JsonData{runtime.lastJson};
}

JsonData ReceiveData(const Input& input, TypeInput typeInput)
{
    return Update(UpdateParameters{input, typeInput});
}

}  // namespace Simulation
