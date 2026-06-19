#pragma once
#include <axmol.h>
#include "ObjectData.h"

inline void Draw8Rect(ax::DrawNode* _node, const ObjectData* data)
{
    for (int i = 0; i < 8; i++)
    {
        Vec2 p = data[i].pos;
        Vec2 s = data[i].size;

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
}
