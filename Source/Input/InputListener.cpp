// =========================================================
// InputListener.cpp
// =========================================================

#include "InputListener.h"
#include <iostream>
// =========================================================
// CREATE
// =========================================================

InputListener* InputListener::Create(Scene* scene)
{
    auto listener = new InputListener();

    if (listener && listener->init())
    {
        listener->autorelease();

        scene->addChild(listener);
        
        listener->InitKeyboard();

        return listener;
    }

    AX_SAFE_DELETE(listener);

    return nullptr;
}

// =========================================================
// INIT
// =========================================================

bool InputListener::init()
{
    if (!Node::init())
    {
        return false;
    }

    return true;
}

// =========================================================
// GET INPUT
// =========================================================

const InputListener::InputFrame& InputListener::GetFrameInput() const
{
    return _frame;
}

// =========================================================
// INIT KEYBOARD
// =========================================================

void InputListener::InitKeyboard()
{
    auto keyboard = EventListenerKeyboard::create();
    std::cout << "InitKeyboard: " << keyboard << std::endl;
    keyboard->onKeyPressed = AX_CALLBACK_2(InputListener::OnKeyPressed, this);

    keyboard->onKeyReleased = AX_CALLBACK_2(InputListener::OnKeyReleased, this);

    _eventDispatcher->addEventListenerWithSceneGraphPriority(keyboard, this);

    scheduleUpdate();
}

// =========================================================
// KEY PRESSED
// =========================================================

void InputListener::OnKeyPressed(EventKeyboard::KeyCode keyCode, Event* event)
{
    // tránh repeat spam
    //AXLOG("Cos phim được nhấn");

    for (auto key : _frame.holdingKeys)
    {
        if (key == keyCode)
        {
            return;
        }
    }

    _frame.holdingKeys.push_back(keyCode);

    _pressedThisFrame.push_back(keyCode);
}

// =========================================================
// KEY RELEASED
// =========================================================

void InputListener::OnKeyReleased(EventKeyboard::KeyCode keyCode, Event* event)
{
    //AXLOG("Cos phim được thả");
    for (int i = 0; i < _frame.holdingKeys.size(); i++)
    {
        if (_frame.holdingKeys[i] == keyCode)
        {
            _frame.holdingKeys.erase(_frame.holdingKeys.begin() + i);

            break;
        }
    }
}

// =========================================================
// UPDATE
// =========================================================

void InputListener::update(float dt)
{
    _frame.pressedKeys = _pressedThisFrame;

    // =====================================================
    // EVENT OUTPUT
    // =====================================================

    //
    // gọi gameplay system tại đây
    //
    // ví dụ:
    //
    // GameplayInputSystem::HandleInput(_frame);
    //
    // =====================================================

    _pressedThisFrame.clear();
}
