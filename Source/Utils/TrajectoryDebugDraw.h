#pragma once
#include <axmol.h>
#include "../System/TrajectoryTestConfig.h"

#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#pragma once

#include <axmol.h>

#include <cerrno>
#include <cstdlib>
#include <cstdio>
#include <string>

#include "../System/TrajectoryTestConfig.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
// ============================================================
// FLOAT EDIT BOX DELEGATE
// ============================================================

class FloatEditBoxDelegate final : public ax::ui::EditBoxDelegate
{
public:
    explicit FloatEditBoxDelegate(float* value) : _value(value) {}

    // --------------------------------------------------------
    // TEXT CHANGED
    // --------------------------------------------------------

    void editBoxTextChanged(ax::ui::EditBox* editBox, std::string_view text) override
    {
        if (_value == nullptr)
            return;

        if (text.empty())
            return;

        std::string str(text);

        char* end = nullptr;

        errno = 0;

        const float result = std::strtof(str.c_str(), &end);

        // Không parse được
        if (end == str.c_str())
            return;

        // Overflow / underflow
        if (errno == ERANGE)
            return;

        // Cho phép khoảng trắng ở cuối
        while (*end != '\0')
        {
            if (!std::isspace(static_cast<unsigned char>(*end)))
            {
                return;
            }

            ++end;
        }

        *_value = result;
    }

    // --------------------------------------------------------
    // RETURN
    // --------------------------------------------------------

    void editBoxReturn(ax::ui::EditBox* editBox) override {}

    // --------------------------------------------------------
    // BEGIN
    // --------------------------------------------------------

    void editBoxEditingDidBegin(ax::ui::EditBox* editBox) override {}

    // --------------------------------------------------------
    // END
    // --------------------------------------------------------

    void editBoxEditingDidEndWithAction(ax::ui::EditBox* editBox,
                                        ax::ui::EditBoxDelegate::EditBoxEndAction action) override
    {}

private:
    float* _value = nullptr;
};

// ============================================================
// CREATE FLOAT EDIT BOX
// ============================================================

inline ax::ui::EditBox* CreateFloatEditBox(ax::Node* parent, const char* title, float* value, const ax::Vec2& position)
{
    if (parent == nullptr || value == nullptr)
        return nullptr;

    // ========================================================
    // LABEL
    // ========================================================

    auto* label = ax::Label::createWithTTF(title, "fonts/arial.ttf", 24);

    if (label == nullptr)
        return nullptr;

    label->setAnchorPoint(ax::Vec2(0.0f, 0.5f));

    label->setPosition(position);

    parent->addChild(label, 9999);

    // ========================================================
    // BACKGROUND
    // ========================================================

    auto* background = ax::ui::Scale9Sprite::create();

    if (background == nullptr)
        return nullptr;

    // ========================================================
    // EDIT BOX
    // ========================================================

    auto* editBox = ax::ui::EditBox::create(ax::Size(150.0f, 45.0f), background);

    if (editBox == nullptr)
        return nullptr;

    // ========================================================
    // INITIAL VALUE
    // ========================================================

    char buffer[64];

    std::snprintf(buffer, sizeof(buffer), "%.3f", *value);

    editBox->setText(buffer);

    // ========================================================
    // FONT
    // ========================================================

    editBox->setFont("fonts/arial.ttf", 24);

    // ========================================================
    // INPUT MODE
    // ========================================================

    editBox->setInputMode(ax::ui::EditBox::InputMode::DECIMAL);

    // ========================================================
    // INPUT FLAG
    // ========================================================

    editBox->setInputFlag(ax::ui::EditBox::InputFlag::SENSITIVE);

    // ========================================================
    // POSITION
    // ========================================================

    editBox->setAnchorPoint(ax::Vec2(0.0f, 0.5f));

    editBox->setPosition(position + ax::Vec2(100.0f, 0.0f));

    // ========================================================
    // DELEGATE
    // ========================================================

    auto* delegate = new FloatEditBoxDelegate(value);

    editBox->setDelegate(delegate);

    // ========================================================
    // ADD TO PARENT
    // ========================================================

    parent->addChild(editBox, 9999);

    return editBox;
}

// ============================================================
// TRAJECTORY TYPE UI
// ============================================================

inline void CreateTrajectoryTypeUI(ax::Node* parent)
{
    if (parent == nullptr)
        return;

    auto& trajectory = TestTrajectory::getInstance().tempTrajectory;

    // ========================================================
    // TITLE
    // ========================================================

    auto* title = ax::Label::createWithTTF("Trajectory Type", "fonts/arial.ttf", 24);

    if (title == nullptr)
        return;

    title->setAnchorPoint(ax::Vec2(0.0f, 0.5f));

    title->setPosition(ax::Vec2(300.0f, 460.0f));

    parent->addChild(title, 9999);

    // ========================================================
    // LINEAR BUTTON
    // ========================================================

    auto* linearButton = ax::ui::Button::create();

    if (linearButton == nullptr)
        return;

    linearButton->setTitleText("Linear");

    linearButton->setTitleFontSize(24);

    linearButton->setContentSize(ax::Size(150.0f, 50.0f));

    linearButton->setPosition(ax::Vec2(380.0f, 400.0f));

    // linearButton->addClickEventListener([&trajectory](ax::Ref*) { trajectory.type = TrajectoryType::Linear; });
    linearButton->addClickEventListener([&trajectory](ax::Object*) { trajectory.type = TrajectoryType::Linear; });
    parent->addChild(linearButton, 9999);

    // ========================================================
    // PARABOLIC BUTTON
    // ========================================================

    auto* parabolicButton = ax::ui::Button::create();

    if (parabolicButton == nullptr)
        return;

    parabolicButton->setTitleText("Parabolic");

    parabolicButton->setTitleFontSize(24);

    parabolicButton->setContentSize(ax::Size(150.0f, 50.0f));

    parabolicButton->setPosition(ax::Vec2(380.0f, 340.0f));

    // parabolicButton->addClickEventListener([&trajectory](ax::Ref*) { trajectory.type = TrajectoryType::Parabolic; });
    parabolicButton->addClickEventListener([&trajectory](ax::Object*) { trajectory.type = TrajectoryType::Parabolic; });
    parent->addChild(parabolicButton, 9999);
}

// ============================================================
// CREATE COMPLETE TRAJECTORY TEST UI
// ============================================================

inline void CreateTrajectoryTestUI(ax::Node* parent)
{
    if (parent == nullptr)
        return;

    auto& trajectory = TestTrajectory::getInstance().tempTrajectory;

    // ========================================================
    // MAIN TITLE
    // ========================================================

    auto* title = ax::Label::createWithTTF("TRAJECTORY TEST", "fonts/arial.ttf", 32);

    if (title == nullptr)
        return;

    title->setAnchorPoint(ax::Vec2(0.0f, 0.5f));

    title->setPosition(ax::Vec2(100.0f, 600.0f));

    parent->addChild(title, 9999);

    float yOffset = 800.0f;
    // ========================================================
    // SPEED
    // ========================================================

    CreateFloatEditBox(parent, "Speed", &trajectory.speed, ax::Vec2(yOffset, 540.0f));

    // ========================================================
    // A
    // ========================================================

    CreateFloatEditBox(parent, "A", &trajectory.a, ax::Vec2(yOffset, 480.0f));

    // ========================================================
    // B
    // ========================================================

    CreateFloatEditBox(parent, "B", &trajectory.b, ax::Vec2(yOffset, 420.0f));

    // ========================================================
    // C
    // ========================================================

    CreateFloatEditBox(parent, "C", &trajectory.c, ax::Vec2(yOffset, 360.0f));

    // ========================================================
    // TRAJECTORY TYPE
    // ========================================================

    CreateTrajectoryTypeUI(parent);
}

// ============================================================
// CREATE COMPLETE TRAJECTORY TEST UI
// ============================================================
inline void DrawTrajectoryDebugInfo(ax::Node* _node, ComponentStorage* componentStorage)
{
    auto& testTrajectory = TestTrajectory::getInstance();

    CreateFloatEditBox(_node, "a", &testTrajectory.tempTrajectory.a, ax::Vec2(100, 400));

    CreateFloatEditBox(_node, "b", &testTrajectory.tempTrajectory.b, ax::Vec2(100, 340));

    CreateFloatEditBox(_node, "c", &testTrajectory.tempTrajectory.c, ax::Vec2(100, 280));
    CreateFloatEditBox(_node, "v0", &testTrajectory.tempTrajectory.speed, ax::Vec2(100, 220));

    CreateTrajectoryTypeUI(_node);
}
