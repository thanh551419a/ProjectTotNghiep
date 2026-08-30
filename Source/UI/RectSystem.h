#pragma once
#include <axmol.h>
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Utils/ResetNode.h"
#include "../Utils/Draw8Rect.h"
#include "../Utils/ObjectData.h"
#include "../Utils/GenerateIntent.h"
using namespace ax;

class RectSystem
{
public:
    

private:
    Scene* _scene   = nullptr;
    DrawNode* _node = nullptr;
    DrawNode* _node2           = nullptr;
    ComponentStorage* _storage = nullptr;
    ObjectData _cache[8];
    int temp = 0;
    ax::ui::EditBox* _aEditBox     = nullptr;
    ax::ui::EditBox* _bEditBox     = nullptr;
    ax::ui::EditBox* _cEditBox     = nullptr;
    ax::ui::EditBox* _speedEditBox = nullptr;

public:
    RectSystem(Scene* scene, ComponentStorage* storage);

    void update(float dt);

};
