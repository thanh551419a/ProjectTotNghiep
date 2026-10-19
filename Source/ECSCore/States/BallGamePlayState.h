#pragma once
#include "../../Constants/BallGameplayConstants.h"
using Entity = unsigned int;
using namespace BallStateFrame;
using namespace BallConfig;
enum class BallCollisionState : uint8_t
{
    None = 0,
    NetTop,
    NetMiddle,
    NetBottom,
};
struct BallGamePlayState
{
    static constexpr float DefaultLandingX = 9999.0f;

    float landingX = DefaultLandingX;

    // Bắt đầu ở Reset = "đang chờ giao bóng", không phải Alive.
    // Alive nghĩa là bóng đang bay, nhưng lúc khởi tạo bóng chưa có quỹ đạo nên nó đứng một chỗ
    // và landingX giữ nguyên DefaultLandingX -> không ai thấy bóng thuộc sân mình. Đồng thời
    // UpdateTrajectoryFromIntent chặn giao bóng khi stateFrame != Reset, nên không cách nào mở
    // được pha bóng đầu trận. Để Reset thì đội được quyền giao sẽ giao như sau mỗi lần ghi điểm.
    int stateFrame = BallStateFrame::Reset;

    BallCollisionState collisionState = BallCollisionState::None;

    void Reset() { *this = BallGamePlayState{}; }
};
