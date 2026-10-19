#pragma once
#include <cstdint>

#include "ActionState.h"


struct RallyState
{
    Entity lastTouch = GameConfig::NONE;

    uint8_t touchCount = 0;

    // LOẠI cú chạm gần nhất (Set / Bump / SpikeLight / ... ). Chỉ để GHI NHẬN, không
    // tham gia vào bất kỳ quy tắc nào của game - mọi luật vẫn chỉ dùng lastTouch và
    // touchCount như trước.
    //
    // Vì sao cần: model AI phải biết "cú chạm trước đó là gì" mới học được chuỗi phối
    // hợp Set -> Jump -> Spike. Đo được ngày 12/10/2026: chuỗi này xuất hiện 0 lần trong
    // game vì model không có cách nào biết mình vừa Set. 21 feature của schema v1 chỉ có
    // lastTouch (AI) và touchCount (mấy lần), không có loại cú.
    ActionState lastTouchKind = ActionState::None;

    void Reset() { *this = RallyState{}; }
};
