#pragma once
#include <cmath>

// ==============================================================
// SimMath::Vec2
// Thay the ax::Vec2 trong Simulation Core.
// Khong phu thuoc Axmol - chi dung <cmath>.
//
// API suy ra truc tiep tu cac method ax::Vec2 dang duoc
// goi trong ECSCore, Config, Gameplay va System/Trajectory.
// ==============================================================

namespace SimMath
{

struct Size; // forward declaration

struct Vec2
{
    // Anonymous union: cho phep truy cap cung mot field bang
    // ca .x lan .width (tuong thich ApplyIntentToComponent.h:356,360)
    union { float x; float width;  };
    union { float y; float height; };

    // --- CONSTRUCTORS ---

    constexpr Vec2() : x(0.0f), y(0.0f) {}
    constexpr Vec2(float x_, float y_) : x(x_), y(y_) {}

    // Chuyen doi tu Size (dung o GetData.h:33: Vec2(BallSize))
    constexpr explicit Vec2(const Size& s);

    // --- ARITHMETIC OPERATORS ---
    // operator+  : ApplyIntentToComponent.h:324 (avgPos = oldPos + newPos)
    // operator-  : TrajectoryMath.cpp:56 (dir = ballCenter - CharacterCenter)
    // operator*  : ApplyIntentToComponent.h:324 (* 0.5f)
    // operator/  : UpdateTrajectoryByCollision.cpp:47-48 (normalize)

    constexpr Vec2 operator+(const Vec2& rhs) const { return Vec2(x + rhs.x, y + rhs.y); }
    constexpr Vec2 operator-(const Vec2& rhs) const { return Vec2(x - rhs.x, y - rhs.y); }
    constexpr Vec2 operator-()                const { return Vec2(-x, -y); }
    constexpr Vec2 operator*(float s)         const { return Vec2(x * s, y * s); }
    constexpr Vec2 operator/(float s)         const { return Vec2(x / s, y / s); }

    Vec2& operator+=(const Vec2& rhs) { x += rhs.x; y += rhs.y; return *this; }
    Vec2& operator-=(const Vec2& rhs) { x -= rhs.x; y -= rhs.y; return *this; }
    Vec2& operator*=(float s)         { x *= s; y *= s; return *this; }
    Vec2& operator/=(float s)         { x /= s; y /= s; return *this; }

    constexpr bool operator==(const Vec2& rhs) const { return x == rhs.x && y == rhs.y; }
    constexpr bool operator!=(const Vec2& rhs) const { return !(*this == rhs); }

    // --- MATH METHODS ---
    // length()        : TrajectoryMath.cpp:59 (dir.length())
    // lengthSquared() : tinh khoang cach khong can sqrt
    // dot()           : UpdateTrajectoryByCollision.cpp:85,140

    float       length()            const { return std::sqrt(x * x + y * y); }
    constexpr float lengthSquared() const { return x * x + y * y; }
    constexpr float dot(const Vec2& rhs) const { return x * rhs.x + y * rhs.y; }
};
constexpr float PI      = 3.14159265358979323846f;
// scalar * Vec2
constexpr inline Vec2 operator*(float s, const Vec2& v) { return v * s; }

} // namespace SimMath
