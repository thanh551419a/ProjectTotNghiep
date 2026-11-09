#pragma once

#include <cstdint>

#if defined(_WIN32)
#   if defined(SIM_BUILD_DLL)
#       define SIM_API __declspec(dllexport)
#   else
#       define SIM_API __declspec(dllimport)
#   endif
#else
#   define SIM_API __attribute__((visibility("default")))
#endif

// ==============================================================
// BUTTON BITMASK CONSTANTS (Matches SimButton)
// ==============================================================
#define SIM_BUTTON_NONE         0
#define SIM_BUTTON_MOVE_LEFT    (1 << 0) // 1
#define SIM_BUTTON_MOVE_RIGHT   (1 << 1) // 2
#define SIM_BUTTON_JUMP_SPIKE   (1 << 2) // 4
#define SIM_BUTTON_BUMP         (1 << 3) // 8
#define SIM_BUTTON_SET          (1 << 4) // 16
#define SIM_BUTTON_SERVE        (1 << 5) // 32
#define SIM_BUTTON_SPIKE_LIGHT  (1 << 6) // 64
#define SIM_BUTTON_SPIKE_MEDIUM (1 << 7) // 128
#define SIM_BUTTON_SPIKE_STRONG (1 << 8) // 256
#define SIM_BUTTON_JUMP         (1 << 9) // 512

// ==============================================================
// ACTION STATE ENUMERATION
// ==============================================================
enum SimActionState : int32_t
{
    SIM_ACTION_NONE         = 0,
    SIM_ACTION_JUMP         = 1,
    SIM_ACTION_SPIKE        = 2,
    SIM_ACTION_SLIDE        = 3,
    SIM_ACTION_SERVE        = 4,
    SIM_ACTION_BUMP         = 5,
    SIM_ACTION_SET          = 6,
    SIM_ACTION_SPIKE_LIGHT  = 7,
    SIM_ACTION_SPIKE_MEDIUM = 8,
    SIM_ACTION_SPIKE_STRONG = 9
};

// ==============================================================
// BLITTABLE SNAPSHOT STRUCTURES (Explicit 4-byte packing)
// ==============================================================
#pragma pack(push, 4)

typedef struct
{
    float posX;              // World horizontal position (pixels)
    float posY;              // World vertical position (pixels)
    float sizeX;             // Hitbox width
    float sizeY;             // Hitbox height
    int32_t actionState;     // SimActionState
    float actionRemainTime;  // Cooldown remaining
    float moveX;             // Horizontal velocity / intent
    int32_t isGrounded;      // 1 if on floor (posY <= MIN_Y), else 0
    int32_t facingDirection; // -1: Left, 1: Right
} SimEntitySnapshot;

typedef struct
{
    float posX;              // World horizontal position
    float posY;              // World vertical position
    float speed;             // Horizontal trajectory speed
    int32_t trajectoryType;  // 0: Linear, 1: Parabolic
    float landingX;          // Predicted floor landing coordinate
    int32_t stateFrame;      // -1: Alive, 0: Reset, >0: Resetting countdown
} SimBallSnapshot;

typedef struct
{
    int32_t leftScore;
    int32_t rightScore;
    int32_t servingTeam;       // 0: Left, 1: Right
    int32_t servingEntity;     // Entity index currently serving
    int32_t finished;          // 1 if score reached win threshold
    int32_t matchWinner;       // 0: Left, 1: Right, -1: None
    int32_t lastTouchEntity;   // Last entity index to touch the ball
    int32_t touchCount;        // Consecutive touches on current side
} SimMatchSnapshot;

typedef struct
{
    uint64_t frameNumber;
    SimEntitySnapshot entities[6]; // 0: Player 1, 1: Teammate 1, 2: Teammate 2, 3: Opponent 1 (AI), 4: Opponent 2, 5: Opponent 3
    SimEntitySnapshot net;         // Entity 6: Net
    SimBallSnapshot ball;          // Entity 7: Ball
    SimMatchSnapshot match;        // Match score & rules
} SimSnapshot;

#pragma pack(pop)

// ==============================================================
// EXPORTED C FUNCTIONS
// ==============================================================
extern "C" {

/**
 * Initializes the authoritative simulation instance.
 * @param configJsonPath Path to InputMapping.json (can be NULL or empty for defaults).
 * @return True if initialized successfully.
 */
SIM_API bool Sim_Initialize(const char* configJsonPath);

/**
 * Resets the simulation to the initial match state (scores 0-0, default positions).
 */
SIM_API void Sim_Reset();

/**
 * Checks SCORE_TO_WIN without advancing simulation.
 * Returns 0 if no reset is needed, 1 after a verified reset,
 * SIM_ERR_NOT_INITIALIZED or SIM_ERR_INVALID_STATE on failure.
 */
SIM_API int32_t Sim_CheckAndResetMatch();

/**
 * Submits player input for a given entity.
 * @param entityId Entity index (typically 0 for Player 1).
 * @param buttonMask Bitwise combination of SIM_BUTTON_* flags.
 * @param moveAxisX Continuous analog movement in [-1.0f, 1.0f], or 0.0f.
 */
SIM_API void Sim_SetInput(uint32_t entityId, uint32_t buttonMask, float moveAxisX);

/**
 * Submits abstract button input for an entity (Unity sends button ID only).
 * The native C++ layer maps buttonId to its configured action and updates input state.
 * @param entityId Entity index (typically 0 for Player).
 * @param buttonId Abstract button ID (0..N).
 * @param inputState 1 = Pressed, 2 = Held, 0 = Released.
 * @return SIM_OK on success, negative error code on failure.
 */
SIM_API int32_t Sim_SubmitButtonInput(uint32_t entityId, int32_t buttonId, int32_t inputState);

/**
 * Advances the simulation by exactly one time step dt.
 * Single-threaded and deterministic.
 * @param dt Delta time in seconds (typically fixed 1/60s).
 */
SIM_API void Sim_Update(float dt);

/**
 * Retrieves the current authoritative snapshot for rendering / presentation.
 * @param outSnapshot Pointer to client-allocated SimSnapshot struct.
 */
SIM_API void Sim_GetSnapshot(SimSnapshot* outSnapshot);

/**
 * Shuts down the simulation instance and frees resources.
 */
SIM_API void Sim_Shutdown();

// ==============================================================
// COMPLETE AUTHORITATIVE SNAPSHOT CONTRACT (API v2)
// --------------------------------------------------------------
// Format : UTF-8 JSON, schema "TheSpike.SimSnapshot", schemaVersion 1.
// Content: every valid entity, every component of every entity, every field,
//          all match/rally/ball state, tick-scoped intents (with producedByTick),
//          match data, pool table, globals and world config. See THE_SPIKE_NATIVE_API.md.
// Buffer : caller-owned. outRequiredBytes receives the size INCLUDING the
//          terminating NUL. If capacity < required, nothing is written and
//          SIM_ERR_BUFFER_TOO_SMALL is returned (never truncated). Pass
//          buffer=NULL/capacity=0 to query the size.
// Consistency: the snapshot is built from one tick (single-threaded runtime);
//          the same bytes are returned until the state changes.
// Threading: all functions must be called from one thread.
// ==============================================================
#define SIM_OK                     0
#define SIM_ERR_NOT_INITIALIZED   -1
#define SIM_ERR_BUFFER_TOO_SMALL  -2
#define SIM_ERR_INVALID_ARGUMENT  -3
#define SIM_ERR_IO                -4
#define SIM_ERR_INVALID_STATE     -5

#define SIM_DIAG_SYSTEM_TRACE        (1u << 0)
#define SIM_DIAG_FIELD_CHANGES       (1u << 1)
#define SIM_DIAG_TRANSITIONS         (1u << 2)
#define SIM_DIAG_INPUT               (1u << 3)
#define SIM_DIAG_SNAPSHOT_INITIAL    (1u << 4)
#define SIM_DIAG_SNAPSHOT_EVERY_TICK (1u << 5)
#define SIM_DIAG_INTENTS             (1u << 6)
#define SIM_DIAG_LIFECYCLE           (1u << 7)
#define SIM_DIAG_SYSTEM_TRACE_CHANGED_ONLY (1u << 8)  // with SYSTEM_TRACE: only systems that changed data

/** Native API version (2 = JSON snapshot contract). */
SIM_API int32_t Sim_GetApiVersion();

/** Snapshot schema version (1). */
SIM_API int32_t Sim_GetSnapshotSchemaVersion();

/** Number of executed simulation ticks since Initialize/Reset. */
SIM_API uint64_t Sim_GetTick();

/** Complete authoritative snapshot as UTF-8 JSON (see contract above). */
SIM_API int32_t Sim_GetSnapshotJson(char* buffer, int32_t capacity, int32_t* outRequiredBytes);

/** Machine-readable schema (types, fields, enums, pools, exclusions) as UTF-8 JSON. */
SIM_API int32_t Sim_GetSchemaJson(char* buffer, int32_t capacity, int32_t* outRequiredBytes);

/** Last error message (UTF-8, empty if none). Same buffer contract. */
SIM_API int32_t Sim_GetLastError(char* buffer, int32_t capacity, int32_t* outRequiredBytes);

/**
 * Development diagnostics (JSON Lines). flags = SIM_DIAG_* bitmask; outputPath = file to
 * (over)write. flags = 0 or outputPath = NULL disables. Read-only: does not alter gameplay.
 */
SIM_API int32_t Sim_SetDiagnostics(uint32_t flags, const char* outputPath);

/** Emit a full snapshot record in the diagnostics stream when the tick counter reaches 'tick'. */
SIM_API int32_t Sim_RequestDiagnosticSnapshot(uint64_t tick);

/** Enables/disables legacy text logging (e.g. per-tick "Ball Position"). Default: enabled. */
SIM_API void Sim_SetLegacyLogEnabled(int32_t enabled);

/** Seeds the PRNG used by the AI attack choice (std::rand). Optional; for reproducible runs. */
SIM_API void Sim_SeedRandom(uint32_t seed);

}
