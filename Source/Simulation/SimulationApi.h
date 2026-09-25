#pragma once

#include <string>

namespace Simulation
{
enum class TypeInput
{
    Keyboard,
    AI,
    Replay,
    Unity,
    Unknown
};

struct Input
{
    float moveX      = 0.0f;
    bool jump        = false;
    bool bump        = false;
    bool set         = false;
    bool serve       = false;
    bool spikeLight  = false;
    bool spikeMedium = false;
    bool spikeStrong = false;
};

struct UpdateParameters
{
    Input input;
    TypeInput typeInput = TypeInput::Unknown;
};

struct JsonData
{
    std::string value;
};

bool Init();
JsonData Update(const UpdateParameters& parameters);
JsonData ReceiveData(const Input& input, TypeInput typeInput);

}  // namespace Simulation
