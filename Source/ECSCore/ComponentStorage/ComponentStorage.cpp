#include "ComponentStorage.h"
#include "../../Config/System/SystemConf.h"
#include "../../Config/Match/BigRect.h"
#include "axmol.h"

void ComponentStorage::InitDemo()
{
    for (int i = 0; i < 8; i++)
    {
        PositionComponent posTemp;

        float left  = SystemConfig::offsetX;
        float right = SystemConfig::offsetX + BigRect::RECT_WIDTH;

        float bottom = SystemConfig::offsetY;
        float top    = SystemConfig::offsetY + BigRect::RECT_HEIGHT;

        float netX   = left + BigRect::RECT_WIDTH * 0.5f;
        float floorY = bottom;

        SizeComponent sizeTemp;  // 👉 move lên trước để fix dependency

        // =========================================================
        // SIZE FIRST (giữ nguyên logic gốc)
        // =========================================================

        if (i == GameConfig::PLAYER)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 2.0f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::TEAMMATE_1)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.9f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::TEAMMATE_2)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 2.0f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::OPPONENT_1)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.5f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::OPPONENT_2)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.6f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::OPPONENT_3)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.7f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::BALL)
        {
            sizeTemp.size = Vec2(0.21f * SystemConfig::PIXELS_PER_METER, 0.21f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::NET)
        {
            sizeTemp.size = Vec2(0.1f * SystemConfig::PIXELS_PER_METER, 2.43f * SystemConfig::PIXELS_PER_METER);
        }

        // =========================================================
        // POSITION (GIỮ NGUYÊN LOGIC GỐC)
        // =========================================================

        if (i == GameConfig::PLAYER || i == GameConfig::TEAMMATE_1 || i == GameConfig::TEAMMATE_2)
        {
            float spacing = 180.0f;

           float x = netX - 400.0f + i * spacing;
            float y = floorY;

            posTemp.position = Vec2(x, y);
        }
        else if (i == GameConfig::OPPONENT_1 || i == GameConfig::OPPONENT_2 || i == GameConfig::OPPONENT_3)
        {
            float spacing = 180.0f;
            float x       = netX + 120.0f + (i - 3) * spacing;
            float y       = floorY;

            posTemp.position = Vec2(x, y);
        }
        else if (i == GameConfig::BALL)
        {
            posTemp.position = Vec2(netX, floorY + 350.0f);
        }
        else if (i == GameConfig::NET)
        {
            posTemp.position = Vec2(netX - sizeTemp.size.x * 0.5f, floorY);
        }

        positionPool.add(i, posTemp);
        sizePool.add(i, sizeTemp);
    }
}
