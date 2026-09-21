#pragma once

#include "Vec2.h"

// ==============================================================
// SimMath::Size
// Thay the ax::Size trong Simulation Core.
// Khong phu thuoc Axmol.
//
// Dung anonymous union {x/width} va {y/height} de tuong thich
// voi ca hai cach truy cap dang co trong source:
//   - .x  : ComponentStorage.cpp:103 (BallSize.x)
//   - .width : ApplyIntentToComponent.h:356 (BallSize.width)
// ==============================================================

namespace SimMath
{

struct Size
{
    union { float x; float width;  };
    union { float y; float height; };

    constexpr Size() : x(0.0f), y(0.0f) {}
    constexpr Size(float w, float h) : x(w), y(h) {}

    // Chuyen doi tu Vec2 (cho phep TrajectoryMath.cpp dung Size{0,0})
    constexpr explicit Size(const Vec2& v);

    constexpr bool operator==(const Size& rhs) const { return x == rhs.x && y == rhs.y; }
    constexpr bool operator!=(const Size& rhs) const { return !(*this == rhs); }
};

// --- Inline implementation cua cross-type conversion constructors ---
// Dat sau ca hai dinh nghia struct de tranh forward declaration gap loi.

constexpr inline Vec2::Vec2(const Size& s) : x(s.x), y(s.y) {}
constexpr inline Size::Size(const Vec2& v) : x(v.x), y(v.y) {}

} // namespace SimMath
