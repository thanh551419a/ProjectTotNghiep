#pragma once
#include <axmol.h>
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Utils/ObjectData.h"
#include "../Utils/GetData.h"
#include "../input/InputListener.h"
#include "../Config/System/SystemConf.h"
#include "../Config/Match/BigRect.h"
#include "../System/PlayerInputSystem.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
class Player2InputSystem  // class này cần phải lấy input và xử lý cập nhật vào conmponentStorage
{
private:
    IntentStorage* _intentStorage       = nullptr;
    ComponentStorage* _componentStorage = nullptr;

public:
    Player2InputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage);
    void update(float delta, InputListener::InputFrame input);

    // Có phím Player 2 nào đang giữ hoặc vừa bấm không. Dùng cho DAgger: chạm phím là
    // người giành quyền điều khiển sân phải (xem MatchRuleConfig::DAGGER_SESSION).
    static bool AnyKeyHeld(const InputListener::InputFrame& input);
};
