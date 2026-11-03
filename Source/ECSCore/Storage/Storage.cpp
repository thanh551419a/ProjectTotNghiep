#include "Storage.h"

void Storage::Init()
{
    componentStorage.InitComponent();
    stateStorage.Init();
    intentStorage.clear();
}

void Storage::Reset()
{
    componentStorage.Reset();
    stateStorage.Reset();
    intentStorage.clear();
}
