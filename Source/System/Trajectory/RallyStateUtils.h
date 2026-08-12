#pragma once
#include "TrajectoryMath.h"
#include "../../ECSCore/IntentStorage/IntentStorage.h"
#include "../../ECSCore/ComponentStorage/ComponentStorage.h"
#include <fstream>

// Nếu character ở ngoài vùng detect so với điểm rơi bóng (landingX)
// thì set moveX để AI/character di chuyển tới đó.
void MoveCharacterTowardLanding(IntentStorage* intentStorage,
                                ComponentStorage* componentStorage,
                                int index,
                                float landingX,
                                float speed);

// Cập nhật touchCount / lastTouch của rally sau khi có
// character chạm bóng (cùng team -> tăng, khác team -> reset về 1).
void UpdateRallyTouch(ComponentStorage* componentStorage, int index, int maxCharacterPerSideCourt);

// Set trạng thái hành động + thời gian hồi (cooldown) cho character
// sau khi có sự kiện (Spike, Bump, Set, Serve, ...).
void ApplyCharacterActionState(ComponentStorage* componentStorage, int index, ActionState lastEvent);

// Ghi log CSV mỗi khi có sự kiện xảy ra (dùng cho debug/test).
void LogTrajectoryEvent(std::ofstream* logFile,
                        int frame,
                        int index,
                        ActionState lastEvent,
                        ComponentStorage* componentStorage);
