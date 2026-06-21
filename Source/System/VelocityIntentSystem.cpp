#include "VelocityIntentSystem.h"
VelocityIntentSystem::VelocityIntentSystem(IntentStorage* intentStorage)
{
    _intentStorage = intentStorage;
}
inline void PrintObjectData(ObjectData* data)
{
    if (!data)
    {
        AXLOG("Data is null");
        return;
    }

    for (int i = 0; i < 8; i++)
    {
        AXLOG("[%d] Pos(%.2f, %.2f) Size(%.2f, %.2f)", i, data[i].pos.x, data[i].pos.y, data[i].size.x, data[i].size.y);
    }
}
void VelocityIntentSystem::update(float delta, ObjectData* data)
{  // sinh Intent cua velocity
    // Lấy VelocityIntent

    auto& velocityIntentStorage = _intentStorage->GetVelocityIntentPool();
    // sau khi có intent , kiểm tra trong toàn bộ data , nếu như có 1 phần tử nào > 265 thì giảm y xuống
    for (auto e = GameConfig::PLAYER; e <= GameConfig::OPPONENT_3; ++e)
    {
        VelocityIntent intent;
        if (data[e].pos.y > SystemConfig::MIN_Y){ // entity nay khong o tren ground
            intent.vy = SystemConfig::GRAVITY;
            //PrintObjectData(data);
        }
        if (intent.vy || intent.vx)
        {
            AXLOG("Add velocity intent entity=%d vy=%.2f", e, intent.vy);
            velocityIntentStorage.add(e, intent);
        }
    }
}
