#include "RectSystem.h"
#include <cstdlib> // rand
#include <ctime>   // time
#include <iostream>
#include "../Utils/GetData.h"
#include "../Utils/TrajectoryDebugDraw.h"
using namespace ax;
RectSystem::RectSystem(Scene* scene, ComponentStorage* storage)
{
    _scene = scene;
    _storage = storage;

    _node = DrawNode::create();
    _node2 = DrawNode::create();
    _scene->addChild(_node);
    _scene->addChild(_node2);

    DrawTrajectoryDebugInfo(_node2, _storage);

}
struct RectRenderData
{
    Entity entity;

    Vec2 position;

    Vec2 size;
};
void RectSystem::update(float dt)
{

    const ObjectData* data = GetPositionData(_storage);
    ClearDrawNode(_node);
    Draw8Rect(_node, data, _storage);
    auto& trajectory = TestTrajectory::getInstance().tempTrajectory;

    const ax::Vector<ax::Node*>& children = _node2->getChildren();

    AXLOG("Node2 children count = %d", children.size());

    // ============================================================
    // NODE STRUCTURE
    //
    // 0 = Label A
    // 1 = EditBox A
    //
    // 2 = Label B
    // 3 = EditBox B
    //
    // 4 = Label C
    // 5 = EditBox C
    //
    // 6 = Label V0
    // 7 = EditBox V0
    //
    // 8  = Label Trajectory Type
    // 9  = Linear Button
    // 10 = Parabolic Button
    // ============================================================

    if (children.size() < 8)
        return;

    // ============================================================
    // GET EDIT BOX
    // ============================================================

    auto* editA = dynamic_cast<ax::ui::EditBox*>(children[1]);

    auto* editB = dynamic_cast<ax::ui::EditBox*>(children[3]);

    auto* editC = dynamic_cast<ax::ui::EditBox*>(children[5]);

    auto* editV0 = dynamic_cast<ax::ui::EditBox*>(children[7]);

    // ============================================================
    // BUFFER
    // ============================================================

    char buffer[64];

    // ============================================================
    // A
    // ============================================================

    if (editA && !editA->isFocused())
    {
        std::snprintf(buffer, sizeof(buffer), "%.3f", trajectory.a);

        editA->setText(buffer);
    }

    // ============================================================
    // B
    // ============================================================

    if (editB && !editB->isFocused())
    {
        std::snprintf(buffer, sizeof(buffer), "%.3f", trajectory.b);

        editB->setText(buffer);
    }

    // ============================================================
    // C
    // ============================================================

    if (editC && !editC->isFocused())
    {
        std::snprintf(buffer, sizeof(buffer), "%.3f", trajectory.c);

        editC->setText(buffer);
    }

    // ============================================================
    // V0 / SPEED
    // ============================================================

    if (editV0 && !editV0->isFocused())
    {
        std::snprintf(buffer, sizeof(buffer), "%.3f", trajectory.speed);

        editV0->setText(buffer);
    }
}


