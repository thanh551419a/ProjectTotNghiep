#pragma once

#include "axmol.h"
using namespace ax;
#include "../Config/System/SystemConf.h"
#include "../UI/RectSystem.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Input/InputListener.h"
#include "../Utils/GenerateIntent.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../Utils/ApplyIntentToComponent.h"
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

    RectSystem* _rectSystem = nullptr;
    CREATE_FUNC(MainScene);

private:
    ComponentStorage* _storage;
    IntentStorage* _intentStorage;
    GenerateIntent* _generateIntent = nullptr;
    RectRenderSystem* _render;
    InputListener* _input = nullptr;
};

