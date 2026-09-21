// ==============================================================
// Tests/SimMath/test_simmath.cpp
//
// Unit test cho SimMath::Vec2 va SimMath::Size.
// Chay doc lap, khong can Axmol, khong can framework test.
//
// Compile tren Windows (MSVC):
//   cl /EHsc /I..\..\Source test_simmath.cpp /Fe:test_simmath.exe
//
// Compile tren Linux/Mac (GCC/Clang):
//   g++ -std=c++17 -I../../Source -o test_simmath test_simmath.cpp
// ==============================================================

#include "SimMath/Vec2.h"
#include "SimMath/Size.h"

#include <cstdio>
#include <cmath>
#include <cassert>

// ------------------------------------------------------------------
// Kiem tra float voi nguong sai so tuyet doi 1e-5
// Tuong ung voi muc ax::Vec2 su dung float 32-bit.
// ------------------------------------------------------------------
static constexpr float kEps = 1e-5f;

static bool nearEq(float a, float b) { return std::fabs(a - b) < kEps; }
static bool vecEq(SimMath::Vec2 a, SimMath::Vec2 b)
{
    return nearEq(a.x, b.x) && nearEq(a.y, b.y);
}

#define CHECK(expr)                                              \
    do {                                                         \
        if (!(expr)) {                                           \
            std::printf("FAIL  [line %d]: %s\n", __LINE__, #expr); \
            failures++;                                          \
        } else {                                                 \
            std::printf("PASS  %s\n", #expr);                    \
        }                                                        \
    } while(0)

// ------------------------------------------------------------------
// Nhom test Vec2
// ------------------------------------------------------------------
static int testVec2()
{
    using namespace SimMath;
    int failures = 0;

    // Constructor mac dinh
    Vec2 zero;
    CHECK(nearEq(zero.x, 0.0f) && nearEq(zero.y, 0.0f));

    // Constructor 2 tham so
    Vec2 a(3.0f, 4.0f);
    CHECK(nearEq(a.x, 3.0f) && nearEq(a.y, 4.0f));

    // Anonymous union: .width = .x, .height = .y
    CHECK(nearEq(a.width,  a.x));
    CHECK(nearEq(a.height, a.y));

    // operator+
    Vec2 b(1.0f, 2.0f);
    CHECK(vecEq(a + b, Vec2(4.0f, 6.0f)));

    // operator-
    CHECK(vecEq(a - b, Vec2(2.0f, 2.0f)));

    // unary operator-
    CHECK(vecEq(-b, Vec2(-1.0f, -2.0f)));

    // operator* (vec * scalar)
    CHECK(vecEq(a * 2.0f, Vec2(6.0f, 8.0f)));

    // operator* (scalar * vec)
    CHECK(vecEq(2.0f * a, Vec2(6.0f, 8.0f)));

    // operator/
    CHECK(vecEq(a / 2.0f, Vec2(1.5f, 2.0f)));

    // operator+=
    Vec2 c = a;
    c += b;
    CHECK(vecEq(c, Vec2(4.0f, 6.0f)));

    // operator-=
    c -= b;
    CHECK(vecEq(c, a));

    // operator*=
    c *= 2.0f;
    CHECK(vecEq(c, Vec2(6.0f, 8.0f)));

    // operator/=
    c /= 2.0f;
    CHECK(vecEq(c, a));

    // length(): 3-4-5 triangle
    CHECK(nearEq(a.length(), 5.0f));

    // lengthSquared()
    CHECK(nearEq(a.lengthSquared(), 25.0f));

    // dot()
    // a=(3,4), b=(1,2): dot = 3*1 + 4*2 = 11
    CHECK(nearEq(a.dot(b), 11.0f));

    // operator==
    Vec2 a2(3.0f, 4.0f);
    CHECK(a == a2);
    CHECK(!(a == b));
    CHECK(a != b);

    return failures;
}

// ------------------------------------------------------------------
// Nhom test Size
// ------------------------------------------------------------------
static int testSize()
{
    using namespace SimMath;
    int failures = 0;

    Size sz;
    CHECK(nearEq(sz.x, 0.0f) && nearEq(sz.y, 0.0f));

    Size s(10.0f, 20.0f);
    CHECK(nearEq(s.width,  10.0f)); // .width == .x
    CHECK(nearEq(s.height, 20.0f)); // .height == .y

    Size s2(10.0f, 20.0f);
    CHECK(s == s2);
    CHECK(!(s != s2));

    return failures;
}

// ------------------------------------------------------------------
// Cross-type conversion constructors
// ------------------------------------------------------------------
static int testConversion()
{
    using namespace SimMath;
    int failures = 0;

    // Vec2 tao tu Size
    Size s(5.0f, 6.0f);
    Vec2 v(s);
    CHECK(nearEq(v.x, 5.0f) && nearEq(v.y, 6.0f));

    // Size tao tu Vec2
    Vec2 v2(7.0f, 8.0f);
    Size s2(v2);
    CHECK(nearEq(s2.x, 7.0f) && nearEq(s2.y, 8.0f));

    return failures;
}

// ------------------------------------------------------------------
// ConstExpr compile-time verification
// (Neu compile thanh cong la pass — khong can runtime check)
// ------------------------------------------------------------------
static void testConstexpr()
{
    using namespace SimMath;

    // Gia lap BallSize va NETSIZE trong MatchObjectConfig.h
    constexpr Vec2 BallPos(100.0f, 200.0f);
    constexpr Size BallSize(24.0f, 24.0f);

    // Kiem tra constexpr conversion
    constexpr Vec2 fromSize(BallSize);

    // Kiem tra constexpr arithmetic
    constexpr Vec2 doubled = BallPos * 2.0f;
    static_assert(doubled.x == 200.0f, "constexpr Vec2 operator* failed");
    static_assert(doubled.y == 400.0f, "constexpr Vec2 operator* failed");

    std::printf("PASS  constexpr compile-time checks (static_assert passed)\n");
}

// ------------------------------------------------------------------
// main
// ------------------------------------------------------------------
int main()
{
    std::printf("\n=== SimMath Unit Tests ===\n\n");

    int total_failures = 0;

    std::printf("--- Vec2 ---\n");
    total_failures += testVec2();

    std::printf("\n--- Size ---\n");
    total_failures += testSize();

    std::printf("\n--- Cross-type conversion ---\n");
    total_failures += testConversion();

    std::printf("\n--- Constexpr (compile-time) ---\n");
    testConstexpr();

    std::printf("\n=== Result: %d failure(s) ===\n\n", total_failures);
    return total_failures == 0 ? 0 : 1;
}
