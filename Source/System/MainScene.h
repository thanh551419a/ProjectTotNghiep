#pragma once

#include "axmol.h"
using namespace ax;
#include "../Config/System/SystemConf.h"
#include "../UI/RectSystem.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Input/InputListener.h"
#include "../Utils/ProcessInput.h"
class ComponentStorage;
class RectRenderSystem;
class MainScene : public ax::Scene
{
public:
    bool init() override;

    void update(float delta) override;

    Point RecLT;
    Rect BigRect;
    Point ArchorPointBigRect;
    ProcessInput* _processInput = nullptr;
    RectSystem* _rectSystem = nullptr;
    CREATE_FUNC(MainScene);

private:
    ComponentStorage* _storage;
    RectRenderSystem* _render;
    InputListener* _input = nullptr;
};
