#pragma once
#include <axmol.h>
#include "ObjectData.h"
#include "../System/TrajectoryTestConfig.h"
inline std::pair<int, int> GetPreviousXY(int x, int y)
{
    y--;

    if (y == -1)
    {
        y = 4;
        x--;
    }

    if (x == -1)
    {
        x = 4;
    }

    return {x, y};
}
inline void Draw8Rect(ax::DrawNode* _node, const ObjectData* data, ComponentStorage* componentStorage)
{
    for (int i = 0; i < 8; i++)
    {
        Vec2 p = data[i].pos;
        Vec2 s = data[i].size;

        if (i != 0 && i != 6 && i!= 7 && i != 3)
            continue;
        // =====================================================
        // RECT
        // =====================================================

        Vec2 v[4] = {{p.x, p.y}, {p.x + s.x, p.y}, {p.x + s.x, p.y + s.y}, {p.x, p.y + s.y}};

        _node->drawPoly(v, 4, true, Color4F::RED);

        // =====================================================
        // DIAGONAL LINE
        // =====================================================

        constexpr float LINE_LENGTH = 30.0f;

        Vec2 start = {p.x + s.x, p.y + s.y};

        Vec2 end = {start.x + LINE_LENGTH, start.y + LINE_LENGTH};

        _node->drawLine(start, end, Color4F::GREEN);

        // =====================================================
        // LABEL
        // =====================================================

        char buffer[128];

        snprintf(buffer, sizeof(buffer), "ID:%d\nX: %.0f\nY: %.0f", i, p.x, p.y);

        auto label = Label::createWithTTF(buffer, "fonts/arial.ttf", 24);

        label->setTextColor(Color4B::WHITE);

        label->setAnchorPoint(Vec2(0.0f, 0.5f));

        label->setPosition(end + Vec2(20.0f, 0.0f));

        _node->addChild(label, 9999);
    }
    //TestTrajectory
    TestTrajectory& trajectoryTest = TestTrajectory::getInstance();
    auto trajectoryXY              = trajectoryTest.GetXYNoUpdate();
    trajectoryXY                   = GetPreviousXY(trajectoryXY.first, trajectoryXY.second);
    auto visibleSize               = Director::getInstance()->getVisibleSize();
    char trajectoryBuffer[128];
    snprintf(trajectoryBuffer, sizeof(trajectoryBuffer), "Testing Trajectory: %d %d", trajectoryXY.first,
             trajectoryXY.second);

    auto trajectoryLabel = Label::createWithTTF(trajectoryBuffer, "fonts/arial.ttf", 40);
    trajectoryLabel->setTextColor(Color4B::YELLOW);
    trajectoryLabel->setAnchorPoint(Vec2(0.0f, 1.0f));
    trajectoryLabel->setPosition(Vec2(visibleSize.width * 0.5f-200.0f, visibleSize.height * 0.5f + 200.0f));  // chỉnh lại nếu cần
    _node->addChild(trajectoryLabel, 9999);
        // =====================================================
    // MATCH SCORE
    // =====================================================

    auto& matchStatePool = componentStorage->GetMatchGamePlayStatePool();
    auto matchState      = matchStatePool.get(DEFAULT_MATCH);

    if (matchState)
    {
        float centerX = visibleSize.width * 0.5f;
        float topY    = visibleSize.height - 40.0f;

        // LEFT TEAM
        auto leftLabel =
            Label::createWithTTF(StringUtils::format("LEFT TEAM\n%d", matchState->leftScore), "fonts/arial.ttf", 40);

        leftLabel->setTextColor(Color4B::WHITE);
        leftLabel->setAlignment(TextHAlignment::CENTER);
        leftLabel->setAnchorPoint(Vec2(0.5f, 1.0f));
        leftLabel->setPosition(Vec2(centerX - 180.0f, topY));

        _node->addChild(leftLabel, 9999);

        // RIGHT TEAM
        auto rightLabel =
            Label::createWithTTF(StringUtils::format("RIGHT TEAM\n%d", matchState->rightScore), "fonts/arial.ttf", 40);

        rightLabel->setTextColor(Color4B::WHITE);
        rightLabel->setAlignment(TextHAlignment::CENTER);
        rightLabel->setAnchorPoint(Vec2(0.5f, 1.0f));
        rightLabel->setPosition(Vec2(centerX + 180.0f, topY));

        _node->addChild(rightLabel, 9999);
        auto label = Label::createWithTTF(
            "Controls\n\n"
            "<-  ->   Move\n"
            "Z         Bump\n"
            "T         Serve",
            "fonts/Marker Felt.ttf", 24);

        label->setAnchorPoint(Vec2(0, 1));
        label->setPosition(Vec2(20, Director::getInstance()->getVisibleSize().height - 20));
        _node->addChild(label);

        // =====================================================
        // PLAYER CONTROLS
        // =====================================================

        auto visibleSize = Director::getInstance()->getVisibleSize();

        // ===============================
        // LEFT PLAYER CONTROL
        // ===============================

        auto leftControl = Label::createWithTTF(
            "PLAYER LEFT\n\n"
            "A / D       Move\n"
            "SPACE       Jump Spike\n"
            "SHIFT       Bump\n"
            "S           Set\n"
            "T           Serve\n"
            "J K L       Spike Power",
            "fonts/arial.ttf", 24);

        leftControl->setTextColor(Color4B::WHITE);
        leftControl->setAnchorPoint(Vec2(0, 1));

        leftControl->setPosition(Vec2(20, visibleSize.height - 150));

        _node->addChild(leftControl, 9999);

        // ===============================
        // RIGHT PLAYER CONTROL
        // ===============================

        auto rightControl = Label::createWithTTF(
            "PLAYER RIGHT\n\n"
            "<- / ->      Move\n"
            "UP           Jump Spike\n"
            "DOWN         Bump\n"
            "NUM 5        Set\n"
            "NUM 6        Spike\n"
            "SHIFT        Serve\n"
            "; ' ENTER    Spike Power",
            "fonts/arial.ttf", 24);

        rightControl->setTextColor(Color4B::WHITE);
        rightControl->setAnchorPoint(Vec2(1, 1));

        rightControl->setPosition(Vec2(visibleSize.width - 20, visibleSize.height - 150));

        _node->addChild(rightControl, 9999);
    }

}
