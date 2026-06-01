#include "ComponentStorage.h"
#include "../../Config/System/SystemConf.h"
#include "../../Config/Match/BigRect.h"
#include "axmol.h"
void ComponentStorage::InitDemo()
    {
        for (int i = 0; i < 8; i++)
        {
            PositionComponent posTemp;

            // =========================================================
            // BIG RECT
            // =========================================================

            float left  = SystemConfig::offsetX;
            float right = SystemConfig::offsetX + BigRect::RECT_WIDTH;

            float bottom = SystemConfig::offsetY;
            float top    = SystemConfig::offsetY + BigRect::RECT_HEIGHT;

            // =========================================================
            // NET
            // =========================================================

            float netX = left + BigRect::RECT_WIDTH * 0.5f;

            // =========================================================
            // FLOOR
            // =========================================================

            float floorY = bottom;

            // =========================================================
            // TEAM 1 (LEFT SIDE)
            // =========================================================

            if (i == 0 || i == 1 || i == 2)
            {
                float spacing = 180.0f;

                float x = netX - 400.0f + i * spacing;

                // đứng trên sàn
                float y = floorY;

                // trừ chiều cao để đáy rect nằm trên sàn
                y -= sizeTemp.size.y;

                posTemp.position = Vec2(x, y);
            }

            // =========================================================
            // TEAM 2 (RIGHT SIDE)
            // =========================================================

            else if (i == 3 || i == 4 || i == 5)
            {
                float spacing = 180.0f;

                float x = netX + 120.0f + (i - 3) * spacing;

                float y = floorY;

                y -= sizeTemp.size.y;

                posTemp.position = Vec2(x, y);
            }

            // =========================================================
            // BALL
            // =========================================================

            else if (i == 6)
            {
                posTemp.position = Vec2(netX, floorY + 350.0f);
            }

            // =========================================================
            // NET
            // =========================================================

            else if (i == 7)
            {
                posTemp.position = Vec2(netX - sizeTemp.size.x * 0.5f, floorY - sizeTemp.size.y);
            }

            positionPool.add(i, posTemp);
            SizeComponent sizeTemp;

            // =========================================================
            // TEAM 1
            // entity 0 1 2
            // =========================================================

            if (i == 0)
            {
                // 1m80
                sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 2.0f * SystemConfig::PIXELS_PER_METER);
            }
            else if (i == 1)
            {
                // 1m90
                sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.9f * SystemConfig::PIXELS_PER_METER);
            }
            else if (i == 2)
            {
                // 2m00
                sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 2.0f * SystemConfig::PIXELS_PER_METER);
            }

            // =========================================================
            // TEAM 2
            // entity 3 4 5
            // =========================================================

            else if (i == 3)
            {
                // 1m50
                sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.5f * SystemConfig::PIXELS_PER_METER);
            }
            else if (i == 4)
            {
                // 1m60
                sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.6f * SystemConfig::PIXELS_PER_METER);
            }
            else if (i == 5)
            {
                // 1m70
                sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.7f * SystemConfig::PIXELS_PER_METER);
            }

            // =========================================================
            // BALL
            // =========================================================

            else if (i == 6)
            {
                // bóng chuyền ~21cm
                sizeTemp.size = Vec2(0.21f * SystemConfig::PIXELS_PER_METER, 0.21f * SystemConfig::PIXELS_PER_METER);
            }

            // =========================================================
            // NET
            // =========================================================

            else if (i == 7)
            {
                // lưới
                sizeTemp.size = Vec2(0.1f * SystemConfig::PIXELS_PER_METER, 2.43f * SystemConfig::PIXELS_PER_METER);
            }

            sizePool.add(i, sizeTemp);
        }
        //PositionComponent* pos = positionPool.get(1);

        //AXLOG("[InitDemo] Entity=%d | x=%.2f y=%.2f | component=%p", 1, pos->position.x, pos->position.y, (void*)pos);
    }
