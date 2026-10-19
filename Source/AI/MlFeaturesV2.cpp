#include "MlFeaturesV2.h"

#include "../Config/ECS/ChunkConfig.h"

namespace
{
// Một cú chạm có phải là cú đập hay không, theo enum ActionState.
// Khớp SPIKE_ACTION_STATES trong features.py: Spike / SpikeLight / SpikeMedium / SpikeStrong.
inline bool IsSpikeState(ActionState s)
{
    return s == ActionState::Spike || s == ActionState::SpikeLight || s == ActionState::SpikeMedium ||
           s == ActionState::SpikeStrong;
}
}  // namespace

bool MlFeaturesV2::Build(ComponentStorage* storage, GameConfig::Entity self, float* out)
{
    // 21 feature đầu: dùng lại y nguyên bản v1 đã được đối chiếu khớp với Python.
    if (!MlFeaturesV1::Build(storage, self, out))
        return false;

    auto* rally   = storage->GetRallyStatePool().get(DEFAULT_MATCH);
    auto* selfPos = storage->GetCharacterPositionPool().get(self);
    if (!rally || !selfPos)
        return false;

    const bool onLeft              = self < ChunkConfig::CHARACTER_PER_MATCH / 2;
    const GameConfig::Entity other = onLeft ? GameConfig::OPPONENT_1 : GameConfig::PLAYER;

    // CHÚ Ý: lastTouchKind dùng enum ActionState, KHÁC enum FinalIntent dùng ở cột
    // p_input_intent/o_input_intent của log. Ví dụ ActionState::Set = 6 nhưng
    // FinalIntent::Set = 7. Dùng lẫn hai bảng thì nhãn sai âm thầm.
    const ActionState kind = rally->lastTouchKind;
    const bool mine        = (rally->lastTouch == self);
    const bool theirs      = (rally->lastTouch == other);
    const bool isSpike     = IsSpikeState(kind);

    out[MY_LAST_SET]    = (mine && kind == ActionState::Set) ? 1.0f : 0.0f;
    out[MY_LAST_BUMP]   = (mine && kind == ActionState::Bump) ? 1.0f : 0.0f;
    out[MY_LAST_SPIKE]  = (mine && isSpike) ? 1.0f : 0.0f;
    out[MY_LAST_SERVE]  = (mine && kind == ActionState::Serve) ? 1.0f : 0.0f;
    out[OPP_LAST_SPIKE] = (theirs && isSpike) ? 1.0f : 0.0f;
    out[SELF_AIRBORNE]  = (selfPos->position.y > MIN_Y) ? 1.0f : 0.0f;

    return true;
}
