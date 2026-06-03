#pragma once
#include<axmol.h>
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "ObjectData.h"
#include <vector>
using Entity = uint32_t;
inline void Draw8Rect(ax::DrawNode* _node, ComponentStorage* _storage, const ObjectData* data)
{
    auto& posPool = _storage->GetPositionPool();
    PositionComponent* pos = posPool.get(1);
    //AXLOG("Position nhan duoc o RectSystem: x=%.2f y=%.2f | dia chi component=%p | dia chi position=%p",
      //    pos->position.x, pos->position.y, (void*)pos, (void*)&pos->position);
    const auto& entities = posPool.entities();

    for (int i = 0; i < entities.size(); i++)
    {
       /* if (i != 1 && i != 6 && i != 7)
            continue;*/
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
        // từ phải -> trái
        // dài 30px
        // mô tả vị trí entity

        constexpr float LINE_LENGTH = 30.0f;

        Vec2 start = {p.x + s.x, p.y + s.y};

        Vec2 end = {start.x + LINE_LENGTH, start.y + LINE_LENGTH};

        _node->drawLine(start, end, Color4F::GREEN);

        // =====================================================
        // COORDINATE AXIS
        // =====================================================

        char buffer[128];

        snprintf(buffer, sizeof(buffer), "X: %.0f\nY: %.0f", p.x, p.y);

        auto label = Label::createWithTTF(buffer, "fonts/arial.ttf", 24);

        label->setTextColor(Color4B::WHITE);

        // label->enableOutline(Color4B::GREEN, 1);

        label->setAnchorPoint(Vec2(0.0f, 0.5f));

        label->setPosition(end + Vec2(20.0f, 0.0f));

        _node->addChild(label, 9999);
    }
}
