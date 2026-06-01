#pragma once
#include <axmol.h>
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "ObjectData.h"
#include "GetData.h"
#include "../input/InputListener.h"
#include "../Config/System/SystemConf.h"
#include "../Config/Match/BigRect.h"
USING_NS_AX;
class ProcessInput
{
private:
    ComponentStorage* _storage = nullptr;
    
    public:
        ProcessInput(ComponentStorage* storage);
        void update(float delta , InputListener::InputFrame input);
};
