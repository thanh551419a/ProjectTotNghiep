#pragma once

#include <cstddef>
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

#ifndef SIM_ANIM_ID_DEFINED
#define SIM_ANIM_ID_DEFINED
enum SimAnimId : int32_t
{
    SIM_ANIM_NONE         = 0,
    SIM_ANIM_IDLE         = 10,
    SIM_ANIM_MOVE         = 20,
    SIM_ANIM_JUMP_UP      = 30,
    SIM_ANIM_FALL         = 31,
    SIM_ANIM_SPIKE        = 40,
    SIM_ANIM_BUMP         = 50,
    SIM_ANIM_SPIKE_LIGHT  = 60,
    SIM_ANIM_SPIKE_MEDIUM = 70,
    SIM_ANIM_SPIKE_STRONG = 80,
    SIM_ANIM_SERVE        = 90
};
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

struct SimCharacterPresentationData
{
    int32_t animId;              // SimAnimId (byte offset 124 in SimEntityData)
    int32_t cooldownAction;      // SimActionState of active skill cooldown (byte offset 128)
    float cooldownRemainMs;      // Remaining skill cooldown in ms (byte offset 132)
    float cooldownDurationMs;    // Total skill cooldown duration in ms (byte offset 136)
    int32_t reserved;            // Zero-padded to 20 bytes (byte offset 140)
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
    uint8_t slowMoFramesRemaining = 0; // Active full-world slow-motion countdown (30..0 frames = 0.5s at 60Hz)
    uint16_t spikeContactHeight = 0;   // Ball Y coordinate (Sim px) at the moment of last Spike contact (byte offset 674 of SimData)
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
    int16_t isGrounded = 0;
    int16_t airborneState = 0;
    int32_t facingDirection = 0; // -1: Left, 1: Right

    SimComponentFlags flags;

    SimPositionComponentData position;
    SimSizeComponentData size;
    SimJumpUpFrameComponentData jumpFrame;
    SimCharacterActionStateData actionState;
    SimCharacterIntentData characterIntent;
    SimVelocityIntentData velocityIntent;
    union
    {
        SimBallTrajectoryComponentData ballTrajectory{};
        SimCharacterPresentationData characterPresentation;
    };
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

static_assert(sizeof(Input) == 36, "ABI size mismatch: sizeof(Input) must be 36 bytes");
static_assert(sizeof(SimCharacterPresentationData) == 20, "ABI size mismatch: sizeof(SimCharacterPresentationData) must be 20 bytes");
static_assert(sizeof(SimEntityData) == 156, "ABI size mismatch: sizeof(SimEntityData) must be 156 bytes");
static_assert(sizeof(data) == 4772, "ABI size mismatch: sizeof(data) must be 4772 bytes");
static_assert(offsetof(SimEntityData, ballTrajectory) == 124, "ABI offset mismatch: ballTrajectory must be at byte 124");
static_assert(offsetof(SimEntityData, characterPresentation) == 124, "ABI offset mismatch: characterPresentation must be at byte 124");
static_assert(offsetof(SimCharacterPresentationData, animId) == 0, "ABI offset mismatch: animId must be at +0 (byte 124)");
static_assert(offsetof(SimCharacterPresentationData, cooldownAction) == 4, "ABI offset mismatch: cooldownAction must be at +4 (byte 128)");
static_assert(offsetof(SimCharacterPresentationData, cooldownRemainMs) == 8, "ABI offset mismatch: cooldownRemainMs must be at +8 (byte 132)");
static_assert(offsetof(SimCharacterPresentationData, cooldownDurationMs) == 12, "ABI offset mismatch: cooldownDurationMs must be at +12 (byte 136)");
static_assert(offsetof(SimEntityData, ballGameplayState) == 144, "ABI offset mismatch: ballGameplayState must be at byte 144");

#ifndef SPIKE_API
#define SPIKE_API SIM_API
#endif

namespace SystemConfig
{
    struct SimAttackServeConfig;
}

// Required C-ABI entry points
#if defined(__clang__)
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wreturn-type-c-linkage"
#endif
extern "C" {

SIM_API data Get_data(Input inputSend);

SIM_API int32_t Sim_GetCharacterStats(uint32_t entityId, float* outAttack, float* outDefense, float* outSpeed, float* outJump);
SIM_API int32_t Sim_SetCharacterStats(uint32_t entityId, float attack, float defense, float speed, float jump);
SIM_API int32_t Sim_ResetCharacterStats();
SIM_API int32_t Sim_GetSlowMoFramesRemaining();
SIM_API float Sim_GetLastSpikeContactHeight();
SPIKE_API SystemConfig::SimAttackServeConfig* Sim_GetAttackServeConfigPtr();

} // extern "C"
#if defined(__clang__)
#pragma clang diagnostic pop
#endif

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
