#pragma once
#include <axmol.h>

inline void ClearDrawNode(ax::DrawNode* node)
{
    node->removeAllChildren();
    node->clear();
}
