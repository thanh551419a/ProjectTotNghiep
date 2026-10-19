#pragma once
#include <axmol.h>

struct Color4F : public ax::Color {
    using ax::Color::Color;
    Color4F(const ax::Color& c) : ax::Color(c) {}
    static inline const ax::Color RED{1.0f, 0.0f, 0.0f, 1.0f};
    static inline const ax::Color GREEN{0.0f, 1.0f, 0.0f, 1.0f};
    static inline const ax::Color BLUE{0.0f, 0.0f, 1.0f, 1.0f};
    static inline const ax::Color WHITE{1.0f, 1.0f, 1.0f, 1.0f};
    static inline const ax::Color YELLOW{1.0f, 1.0f, 0.0f, 1.0f};
    static inline const ax::Color BLACK{0.0f, 0.0f, 0.0f, 1.0f};
};

struct Color4B : public ax::Color32 {
    using ax::Color32::Color32;
    Color4B(const ax::Color32& c) : ax::Color32(c) {}
    static inline const ax::Color32 RED{255, 0, 0, 255};
    static inline const ax::Color32 GREEN{0, 255, 0, 255};
    static inline const ax::Color32 BLUE{0, 0, 255, 255};
    static inline const ax::Color32 WHITE{255, 255, 255, 255};
    static inline const ax::Color32 YELLOW{255, 255, 0, 255};
    static inline const ax::Color32 BLACK{0, 0, 0, 255};
};
