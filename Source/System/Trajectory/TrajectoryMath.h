#pragma once
#include "../../Utils/ObjectData.h"
#include "../../Config/Match/TrajectoryConfig.h"
#include "../../Config/Match/TrajectoryData.h"
#include "../../ECSCore/States/ActionState.h"
#include "../../ECSCore/States/MatchState.h"
#include "../../ECSCore/Components/BallTrajectoryComponent.h"
#include "axmol.h"

USING_NS_AX;
using namespace TrajectoryConfig;

// =====================================================
// STRUCTS
// =====================================================

struct DetectionResult
{
    float distancePercent;  // khoảng cách = ? % chiều cao player
    float angle;            // độ, ngược chiều kim đồng hồ từ trục Ox
};

struct TrajectoryBoost
{
    float v0Multiplier = 1.0f;
    float aFlattenFactor;
};

// =====================================================
// ENUM -> STRING
// =====================================================
const char* ActionStateToString(ActionState state);

// =====================================================
// TEAM HELPER
// =====================================================
Team GetEntityTeam1(Entity entity);

// =====================================================
// DETECT PLAYER <-> BALL (khoảng cách % chiều cao player + góc)
// =====================================================
DetectionResult DetectPlayerBall(const ObjectData* data, int characterIndex, int ballIndex, Size PercentChange);

// =====================================================
// TRAJECTORY MATH (a, b, c của parabol)
// =====================================================
float CalculateC(float v0);
float CalculateB(float a, float c, float x0, float y0, int direction, int isSpike);
Vec2 CalculateLandingPoint(const Vec2& startPos, BallTrajectoryComponent* trajectory, float groundY);
