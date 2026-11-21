#include "RallyStateUtils.h"
#include "../../Utils/SimLog.h"

void MoveCharacterTowardLanding(IntentStorage* intentStorage,
                                ComponentStorage* componentStorage,
                                int index,
                                float landingX,
                                float speed)
{
    auto characterPos = componentStorage->GetCharacterPositionPool().get(index);
    if (!characterPos)
        return;

    // Chỉ tự động di chuyển đón bóng khi điểm rơi (landingX) nằm bên phần sân của đội đó.
    const bool isLeftTeam = (index < ChunkConfig::CHARACTER_PER_MATCH / 2);
    if (isLeftTeam)
    {
        if (landingX <= 0.0f || landingX >= BigRect::NET_X)
            return;
        landingX = clampf(SystemConfig::MIN_X_LEFT, landingX, SystemConfig::MAX_X_LEFT);
    }
    else
    {
        if (landingX < BigRect::NET_X || landingX > SystemConfig::MAX_X_RIGHT)
            return;
        landingX = clampf(SystemConfig::MIN_X_RIGHT, landingX, SystemConfig::MAX_X_RIGHT);
    }

    float dx = landingX - characterPos->position.x;

    if (std::abs(dx) <= speed)  // trong vùng detect, không cần di chuyển
        return;

    int direction1 = (characterPos->position.x < landingX) ? 1 : -1;

    auto& characterIntentPool = intentStorage->GetCharacterIntentPool();
    auto characterIntent      = characterIntentPool.get(index);

    if (characterIntent == nullptr)
    {
        CharacterIntent c;
        c.moveX = speed * direction1;
        characterIntentPool.add(index, c);
    }
    else
    {
        characterIntent->moveX = speed * direction1;
    }
}

void UpdateRallyTouch(ComponentStorage* componentStorage, int index, int maxCharacterPerSideCourt)
{
    auto rallyState = componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
    if (!rallyState)
        return;

    if (rallyState->lastTouch / maxCharacterPerSideCourt == index / maxCharacterPerSideCourt)
    {
        rallyState->touchCount++;
    }
    else
    {
        rallyState->touchCount = 1;
    }

    rallyState->lastTouch = index;  // lưu lại entity vừa đánh bóng
}

void ApplyCharacterActionState(ComponentStorage* componentStorage, int index, ActionState lastEvent)
{
    auto charState        = componentStorage->GetCharacterActionStatePool().get(index);
    charState->status     = lastEvent;
    charState->remainTime = ActionStateInfo::Cooldown[static_cast<size_t>(lastEvent)];
}

void LogTrajectoryEvent(std::ofstream* logFile,
                        int frame,
                        int index,
                        ActionState lastEvent,
                        ComponentStorage* componentStorage)
{
    if (!logFile || !logFile->is_open())
        return;

    auto playerPos   = componentStorage->GetCharacterPositionPool().get(GameConfig::PLAYER);
    auto opponentPos = componentStorage->GetCharacterPositionPool().get(GameConfig::OPPONENT_1);

    const char* playerEvent   = (index == GameConfig::PLAYER) ? ActionStateToString(lastEvent) : "None";
    const char* opponentEvent = (index == GameConfig::OPPONENT_1) ? ActionStateToString(lastEvent) : "None";

    (*logFile) << frame << ',' << playerPos->position.x << ',' << playerPos->position.y << ',' << playerEvent << ','
               << opponentPos->position.x << ',' << opponentPos->position.y << ',' << opponentEvent << '\n';
}
