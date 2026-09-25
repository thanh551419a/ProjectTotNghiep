#include "SimulationApi.h"

#include <cassert>
#include <iostream>
#include <string>

int main()
{
    using namespace Simulation;

    const bool initialized = Init();
    assert(initialized);

    const Input input{4.0f, true, false, false, false, false, false, false};
    const JsonData output = ReceiveData(input, TypeInput::Unity);

    assert(!output.value.empty());
    const std::string text = output.value;
    assert(text.find("\"typeInput\":\"Unity\"") != std::string::npos);
    assert(text.find("\"intent\":\"") != std::string::npos);

    std::cout << output.value << std::endl;
    return 0;
}
