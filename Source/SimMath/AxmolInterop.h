#pragma once

// ==============================================================
// SimMath::AxmolInterop
// Conversion helpers tai bien gioi Presentation / Simulation.
//
// QUY TAC:
//   - Cac ham nay CHI duoc #include trong Presentation layer
//     (RectSystem.cpp, Draw8Rect.cpp, v.v.).
//   - Simulation Core khong bao gio #include file nay.
//   - File nay phu thuoc ca hai: Axmol va SimMath.
//
// BIEN GIOI:
//   Simulation ---[SimMath::Vec2/Size]---> AxmolInterop ---[ax::Vec2/Size]---> Presentation
// ==============================================================

#include "SimMath/Vec2.h"
#include "SimMath/Size.h"

// Bao dam chi compile khi co Axmol trong include path
#include "axmol.h"

namespace SimMath
{

// Vec2 <-> ax::Vec2
inline ax::Vec2 ToAxmol(const Vec2& v)  { return ax::Vec2(v.x, v.y); }
inline Vec2     FromAxmol(const ax::Vec2& v) { return Vec2(v.x, v.y); }

// Size <-> ax::Size
inline ax::Size ToAxmolSize(const Size& s)  { return ax::Size(s.x, s.y); }
inline Size     FromAxmolSize(const ax::Size& s) { return Size(s.x, s.y); }

} // namespace SimMath
