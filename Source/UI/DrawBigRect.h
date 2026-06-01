#pragma once
#include <axmol.h>
#include "../Config/Match/BigRect.h"
USING_NS_AX;
class DrawBigRect
{
private:
    Rect _BigRect;
    static Point ArchorPointBigRect;

public:
    DrawBigRect(Scene* scene, float offsetX, float offsetY);
};
