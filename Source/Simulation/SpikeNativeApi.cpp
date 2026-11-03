#include "SpikeNativeApi.h"
#include "SimulationRuntime.h"

#include <cstdlib>
#include <cstring>
#include <string>

namespace
{
    std::string& LastError()
    {
        static std::string err;
        return err;
    }

    // Copies a string into a caller buffer. Never truncates.
    int32_t CopyOut(const std::string& s, char* buffer, int32_t capacity, int32_t* outRequiredBytes)
    {
        const size_t required = s.size() + 1;
        if (required > static_cast<size_t>(INT32_MAX))
        {
            LastError() = "payload exceeds int32 range";
            return SIM_ERR_INVALID_ARGUMENT;
        }
        if (outRequiredBytes)
            *outRequiredBytes = static_cast<int32_t>(required);
        if (buffer == nullptr || capacity < static_cast<int32_t>(required))
        {
            return SIM_ERR_BUFFER_TOO_SMALL;
        }
        std::memcpy(buffer, s.data(), s.size());
        buffer[s.size()] = '\0';
        return SIM_OK;
    }
}  // namespace

extern "C" {

SIM_API bool Sim_Initialize(const char* configJsonPath)
{
    return SimulationRuntime::GetInstance().Initialize(configJsonPath);
}

SIM_API void Sim_Reset()
{
    SimulationRuntime::GetInstance().Reset();
}

SIM_API void Sim_SetInput(uint32_t entityId, uint32_t buttonMask, float moveAxisX)
{
    SimulationRuntime::GetInstance().SetInput(entityId, buttonMask, moveAxisX);
}

SIM_API int32_t Sim_SubmitButtonInput(uint32_t entityId, int32_t buttonId, int32_t inputState)
{
    return SimulationRuntime::GetInstance().SubmitButtonInput(entityId, buttonId, inputState);
}

SIM_API void Sim_Update(float dt)
{
    SimulationRuntime::GetInstance().Update(dt);
}

SIM_API void Sim_GetSnapshot(SimSnapshot* outSnapshot)
{
    SimulationRuntime::GetInstance().GetSnapshot(outSnapshot);
    if (outSnapshot && (outSnapshot->frameNumber % 60 == 0 || outSnapshot->entities[0].moveX != 0.0f))
    {
        SIM_LOG("[NATIVE_OUTPUT][CHECKPOINT_F] Native snapshot retrieved (struct): Tick=%llu, PlayerPos=(%.2f, %.2f), BallPos=(%.2f, %.2f), Valid=YES",
                (unsigned long long)outSnapshot->frameNumber, outSnapshot->entities[0].posX, outSnapshot->entities[0].posY,
                outSnapshot->ball.posX, outSnapshot->ball.posY);
    }
}

SIM_API void Sim_Shutdown()
{
    SimulationRuntime::GetInstance().Shutdown();
}

SIM_API int32_t Sim_GetApiVersion()
{
    return SimSchema::kNativeApiVersion;
}

SIM_API int32_t Sim_GetSnapshotSchemaVersion()
{
    return SimSchema::kSnapshotSchemaVersion;
}

SIM_API uint64_t Sim_GetTick()
{
    return SimulationRuntime::GetInstance().GetTick();
}

SIM_API int32_t Sim_GetSnapshotJson(char* buffer, int32_t capacity, int32_t* outRequiredBytes)
{
    auto& rt = SimulationRuntime::GetInstance();
    if (!rt.IsInitialized())
    {
        if (outRequiredBytes)
            *outRequiredBytes = 0;
        LastError() = "Sim_GetSnapshotJson: simulation not initialized";
        return SIM_ERR_NOT_INITIALIZED;
    }
    if (capacity < 0)
    {
        LastError() = "Sim_GetSnapshotJson: negative capacity";
        return SIM_ERR_INVALID_ARGUMENT;
    }
    return CopyOut(rt.GetSnapshotJson(), buffer, capacity, outRequiredBytes);
}

SIM_API int32_t Sim_GetSchemaJson(char* buffer, int32_t capacity, int32_t* outRequiredBytes)
{
    if (capacity < 0)
    {
        LastError() = "Sim_GetSchemaJson: negative capacity";
        return SIM_ERR_INVALID_ARGUMENT;
    }
    return CopyOut(SimulationRuntime::GetSchemaJson(), buffer, capacity, outRequiredBytes);
}

SIM_API int32_t Sim_GetLastError(char* buffer, int32_t capacity, int32_t* outRequiredBytes)
{
    if (capacity < 0)
        return SIM_ERR_INVALID_ARGUMENT;
    return CopyOut(LastError(), buffer, capacity, outRequiredBytes);
}

SIM_API int32_t Sim_SetDiagnostics(uint32_t flags, const char* outputPath)
{
    if (!SimulationRuntime::GetInstance().Diagnostics().Configure(flags, outputPath))
    {
        LastError() = std::string("Sim_SetDiagnostics: cannot open '") + (outputPath ? outputPath : "") + "'";
        return SIM_ERR_IO;
    }
    return SIM_OK;
}

SIM_API int32_t Sim_RequestDiagnosticSnapshot(uint64_t tick)
{
    SimulationRuntime::GetInstance().Diagnostics().RequestSnapshotAtTick(tick);
    return SIM_OK;
}

SIM_API void Sim_SetLegacyLogEnabled(int32_t enabled)
{
    SimLogControl::Enabled() = (enabled != 0);
}

SIM_API void Sim_SeedRandom(uint32_t seed)
{
    std::srand(seed);
}

}
