#pragma once

#include <cstdint>
#include <string>

#if defined(_WIN32)
#   if defined(SIM_BUILD_DLL)
#       define SIM_API __declspec(dllexport)
#   else
#       define SIM_API __declspec(dllimport)
#   endif
#else
#   define SIM_API __attribute__((visibility("default")))
#endif

#pragma pack(push, 4)

struct SimVec2Data
{
    float x = 0.0f;
    float y = 0.0f;
};

struct SimPositionComponentData
{
    SimVec2Data position;
};

struct SimSizeComponentData
{
    SimVec2Data size;
};

struct SimJumpUpFrameComponentData
{
    int32_t remainingFrames = 0;
};

struct SimCharacterActionStateData
{
    int32_t status = 0;       // ActionState enum: 0=None, 1=Jump, 2=Spike, etc.
    float remainTime = 0.0f;  // Remaining cooldown in milliseconds
};

struct SimCharacterIntentData
{
    float moveX = 0.0f;
    int32_t finalIntent = 0;  // FinalIntent enum
};

struct SimVelocityIntentData
{
    float vx = 0.0f;
    float vy = 0.0f;
};

struct SimBallTrajectoryComponentData
{
    int32_t type = 0;         // 0: Linear, 1: Parabolic
    float speed = 0.0f;
    float a = 0.0f;
    float b = 0.0f;
    float c = 0.0f;
};

struct SimBallGamePlayStateData
{
    float landingX = 0.0f;
    int32_t stateFrame = 0;
    int32_t collisionState = 0; // BallCollisionState
};

struct SimMatchStateData
{
    int32_t leftScore = 0;
    int32_t rightScore = 0;
    int32_t servingTeam = 0;
    uint32_t leftServingEntity = 0;
    uint32_t rightServingEntity = 0;
    int32_t finished = 0;
    int32_t matchWinner = 0;
};

struct SimRallyStateData
{
    uint32_t lastTouch = 8;    // GameConfig::NONE
    uint8_t touchCount = 0;
    uint8_t _padding[3] = {0, 0, 0};
};

struct SimComponentFlags
{
    int32_t hasPosition = 0;
    int32_t hasSize = 0;
    int32_t hasJumpUpFrame = 0;
    int32_t hasCharacterActionState = 0;
    int32_t hasCharacterIntent = 0;
    int32_t hasVelocityIntent = 0;
    int32_t hasBallTrajectory = 0;
    int32_t hasBallGameplayState = 0;
};

struct SimEntityData
{
    uint32_t entityId = 0;
    char roleName[32] = {0};   // "PLAYER", "OPPONENT_1", "BALL", "NET"
    int32_t team = -1;         // 0: LEFT, 1: RIGHT, -1: NONE
    int32_t isGrounded = 0;
    int32_t facingDirection = 0; // -1: Left, 1: Right

    SimComponentFlags flags;

    SimPositionComponentData position;
    SimSizeComponentData size;
    SimJumpUpFrameComponentData jumpFrame;
    SimCharacterActionStateData actionState;
    SimCharacterIntentData characterIntent;
    SimVelocityIntentData velocityIntent;
    SimBallTrajectoryComponentData ballTrajectory;
    SimBallGamePlayStateData ballGameplayState;
};

struct data
{
    int32_t status = 0;        // 0 = SIM_OK
    uint64_t tick = 0;         // Authoritative simulation tick
    int32_t entityCount = 4;   // Exactly 4 entities
    SimEntityData entities[4]; // [0]=PLAYER, [1]=OPPONENT_1, [2]=BALL, [3]=NET

    SimMatchStateData matchState;
    SimRallyStateData rallyState;

    char formattedJson[4096] = {0}; // Structured snapshot representation
};

static constexpr uint32_t MAX_INPUT_BUTTONS = 8;

struct Input
{
    uint32_t buttonCount = 0;
    uint32_t buttonIds[MAX_INPUT_BUTTONS] = {0};

    bool IsEmpty() const
    {
        return buttonCount == 0;
    }
};

#pragma pack(pop)

// Required C-ABI entry points
extern "C" {

SIM_API data Get_data(Input inputSend);

} // extern "C"

namespace Simulation
{
    using ::Input;
    using ::data;
    using ::Get_data;

    enum class TypeInput
    {
        Keyboard,
        AI,
        Replay,
        Unity,
        Unknown
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

    SIM_API bool Init();
    SIM_API void Reset();
    SIM_API void Shutdown();
    SIM_API JsonData Update(const UpdateParameters& parameters);
    SIM_API JsonData ReceiveData(const Input& input, TypeInput typeInput);

}  // namespace Simulation
