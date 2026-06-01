// =========================================================
// InputListener.h
// =========================================================

#pragma once

#include "axmol.h"
#include <vector>

using namespace ax;

class InputListener : public Node
{
public:
    // =====================================================
    // INPUT FRAME
    // =====================================================

    struct InputFrame
    {
        std::vector<EventKeyboard::KeyCode> holdingKeys;

        std::vector<EventKeyboard::KeyCode> pressedKeys;
    };

public:
    static InputListener* Create(Scene* scene);
    void update(float dt) override;
    const InputFrame& GetFrameInput() const;

private:
    bool init() override;

    void InitKeyboard();

    void OnKeyPressed(EventKeyboard::KeyCode keyCode, Event* event);

    void OnKeyReleased(EventKeyboard::KeyCode keyCode, Event* event);

    

private:
    InputFrame _frame;

    std::vector<EventKeyboard::KeyCode> _pressedThisFrame;
};
