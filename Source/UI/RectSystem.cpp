#include "RectSystem.h"
#include <cstdlib> // rand
#include <ctime>   // time
#include <iostream>
#include "../Utils/GetData.h"
using namespace ax;
RectSystem::RectSystem(Scene* scene, ComponentStorage* storage)
{
    _scene = scene;
    _storage = storage;

    _node = DrawNode::create();
    _scene->addChild(_node);
    AXLOG("[RectSystem] storage=%p", (void*)_storage);
}
struct RectRenderData
{
    Entity entity;

    Vec2 position;


    Vec2 size;
};
void RectSystem::update(float dt)
{

    const ObjectData* data = GetData(_storage);
    ClearDrawNode(_node);
    Draw8Rect(_node, _storage, data);
}


