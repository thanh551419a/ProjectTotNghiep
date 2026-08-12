#pragma once
#include "TrajectoryMath.h"
#include "../../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../../Config/Match/TrajectoryData.h"

// Cập nhật landingX dựa trên trajectory hiện tại của bóng.
// Dùng lại ở nhiều nơi: sau khi update trajectory, hoặc khi
// character/AI cần biết bóng sẽ rơi ở đâu.
void UpdateLandingX(ComponentStorage* componentStorage);

// Áp trajectory mới (a, b, c, speed) cho bóng.
// Hàm lõi, được gọi từ tất cả các loại hành động
// (Serve, Bump, Set, Spike, SpikeLight/Medium/Strong).
void UpdateNewTrajectory(const TrajectoryData& trajectory,
                         ComponentStorage* componentStorage,
                         int reverseDirection,
                         int decreaseC,
                         int powerSpike,
                         TrajectoryBoost trajectoryBoost);

// Gộp pattern lặp lại nhiều lần: nếu khoảng cách nằm trong
// ngưỡng cho phép thì set lastEvent + áp trajectory mới.
// inclusive = true  -> so sánh "<=" (dùng cho Bump, Set)
// inclusive = false -> so sánh "<"  (dùng cho SpikeLight/Medium/Strong)
// Trả về true nếu đã áp dụng trajectory (có sự kiện xảy ra).
bool TryApplyTrajectory(ActionState state,
                        float distancePercent,
                        float distanceThreshold,
                        bool inclusive,
                        const TrajectoryData& trajectoryData,
                        TrajectoryBoost boost,
                        int decreaseC,
                        int powerSpike,
                        int direction,
                        ComponentStorage* componentStorage,
                        ActionState& lastEvent);
