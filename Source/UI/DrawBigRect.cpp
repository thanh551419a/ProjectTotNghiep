#include "DrawBigRect.h"
Rect rect(BigRect::RECT_X, BigRect::RECT_Y, BigRect::RECT_WIDTH, BigRect::RECT_HEIGHT);

DrawBigRect::DrawBigRect(Scene* scene, float offsetX, float offsetY)
{
    _BigRect = Rect(
        offsetX,
        offsetY,
        BigRect::RECT_WIDTH,
        BigRect::RECT_HEIGHT
    );

    auto drawNode = DrawNode::create();

    Vec2 vertices[4] =
    {
        Vec2(_BigRect.getMinX(), _BigRect.getMinY()),
        Vec2(_BigRect.getMaxX(), _BigRect.getMinY()),
        Vec2(_BigRect.getMaxX(), _BigRect.getMaxY()),
        Vec2(_BigRect.getMinX(), _BigRect.getMaxY())
    };

    // true = khép kín hình
    drawNode->drawPoly(vertices, 4, true, Color4F::RED);

    // =========================================================
    // LEFT 3M LINE
    // =========================================================

    drawNode->drawLine(Vec2(BigRect::LEFT_3M_LINE_X, rect.getMinY()), Vec2(BigRect::LEFT_3M_LINE_X, rect.getMaxY()),
                       Color4F::GREEN);

    // =========================================================
    // RIGHT 3M LINE
    // =========================================================

    drawNode->drawLine(Vec2(BigRect::RIGHT_3M_LINE_X, rect.getMinY()), Vec2(BigRect::RIGHT_3M_LINE_X, rect.getMaxY()),
                       Color4F::GREEN);

    // =========================================================
    // LEFT 9M LINE
    // =========================================================

    drawNode->drawLine(Vec2(BigRect::LEFT_9M_LINE_X, rect.getMinY()), Vec2(BigRect::LEFT_9M_LINE_X, rect.getMaxY()),
                       Color4F::BLUE);

    // =========================================================
    // RIGHT 9M LINE
    // =========================================================

    drawNode->drawLine(Vec2(BigRect::RIGHT_9M_LINE_X, rect.getMinY()), Vec2(BigRect::RIGHT_9M_LINE_X, rect.getMaxY()),
                       Color4F::BLUE);
    scene->addChild(drawNode);
}
