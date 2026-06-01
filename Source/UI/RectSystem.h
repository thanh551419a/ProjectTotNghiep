#pragma once
#include <axmol.h>
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Utils/ResetNode.h"
#include "../Utils/Draw8Rect.h"
#include "../Utils/ObjectData.h"
#include "../Utils/ProcessInput.h"
using namespace ax;

class RectSystem
{
public:
    

private:
    Scene* _scene   = nullptr;
    DrawNode* _node = nullptr;
    ComponentStorage* _storage = nullptr;
    ObjectData _cache[8];
    int temp = 0;

public:
    RectSystem(Scene* scene, ComponentStorage* storage);

    void update(float dt);

};
