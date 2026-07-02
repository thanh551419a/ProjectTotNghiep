#pragma once
#include<vector>
#include "TrajectoryTestData.h"
#include "../Config/Match/TrajectoryConfig.h"
#include "../Config/Match/MatchEntityConfig.h"
#include <utility>
class TestTrajectory
{
private:
    int x = 0;
    TrajectoryTestData data;
    int y = 0;
    std::vector<std::vector<TrajectoryTestData>> Table;
   

public:
    static TestTrajectory& getInstance()
    {
        static TestTrajectory instance;
        return instance;
    }
    bool Change               = false;
    GameConfig::Entity source = GameConfig::NONE;
    float GotoX                 = 0;
    int step                      = 0;
    float AttackPower           = 180.0f;
    TestTrajectory() {

        int maxX = TrajectoryConfig::DISTANCE_COUNT;
        int maxY = TrajectoryConfig::ANGLE_COUNT;
        
        Table.resize(maxX);
        for (int i = 0; i < maxX; i++)
        {
            Table[i].resize(maxY);
        }
        float baseX = TrajectoryConfig::MIN_DISTANCE + 1.0f;
        float baseY = TrajectoryConfig::MIN_ANGLE + 1.0f;

        float stepX = TrajectoryConfig::DISTANCE_STEP;
        float stepY = TrajectoryConfig::ANGLE_STEP;
        for (int i = 0; i < maxX; i++)
        {
            for (int j = 0; j < maxY; j++)
            {
                data.distance = baseX + i * stepX;
                data.angle    = baseY + j * stepY;
                Table[i][j]   = data;
            }
        }
    }
    std::pair<int, int> GetXY() {
        std::pair<int, int> result = {x, y};
        y++;
        if (y == TrajectoryConfig::ANGLE_COUNT)
        {
            y = 0;
            x++;
        }
        if (x == TrajectoryConfig::DISTANCE_COUNT)
        {
            x = 0;
            AttackPower == 180.0f ? AttackPower = 150.0f : AttackPower = 180.0f;
        }
        return result;
    }
    std::pair<int, int> GetXYNoUpdate() { return {x, y};
    }
    TrajectoryTestData getData(int x, int y) {
        return Table[x][y];
    }
};

