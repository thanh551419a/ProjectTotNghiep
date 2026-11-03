#pragma once
// ==============================================================
// SimSchema.h
// Single source of truth for the logical field layout of every
// simulation data type exported by the native runtime.
//
// - Visit(v, value) walks every declared field of a type and reports it to a
//   visitor (JSON writer, schema writer, diff flattener, coverage counter).
// - Every Visit overload destructures the type with a structured binding,
//   which FAILS TO COMPILE if a field is added or removed. The serializer can
//   therefore never silently omit a newly declared field.
// - AggregateFieldCount<T>() independently counts declared fields at compile
//   time for the coverage report.
//
// This file is read-only with respect to simulation state.
// ==============================================================

#include <cstddef>
#include <cstdint>
#include <type_traits>
#include <utility>

#include "../../ECSCore/ComponentPools/ComponentPool.h"
#include "../../ECSCore/Components/PositionComponent.h"
#include "../../ECSCore/Components/SizeComponent.h"
#include "../../ECSCore/Components/JumpUpFrameComponent.h"
#include "../../ECSCore/Components/BallTrajectoryComponent.h"
#include "../../ECSCore/States/ActionState.h"
#include "../../ECSCore/States/BallGamePlayState.h"
#include "../../ECSCore/States/MatchState.h"
#include "../../ECSCore/States/RallyState.h"
#include "../../ECSCore/IntentComponents/CharacterIntent.h"
#include "../../ECSCore/IntentComponents/BallIntent.h"
#include "../../ECSCore/IntentComponents/VelocityIntent.h"
#include "../../Gameplay/MatchData/CharacterStats.h"
#include "../../SimMath/Vec2.h"
#include "../../SimMath/Size.h"

namespace SimSchema
{
    // ----------------------------------------------------------
    // Snapshot schema identity
    // ----------------------------------------------------------
    inline constexpr const char* kSnapshotSchemaName = "TheSpike.SimSnapshot";
    inline constexpr int kSnapshotSchemaVersion      = 1;
    inline constexpr int kNativeApiVersion           = 2;

    // ----------------------------------------------------------
    // Enum name tables (index == underlying value)
    // ----------------------------------------------------------
    inline constexpr const char* kActionStateNames[] = {"None", "Jump", "Spike", "Slide", "Serve", "Bump",
                                                        "Set", "SpikeLight", "SpikeMedium", "SpikeStrong", "Count"};
    inline constexpr const char* kTrajectoryTypeNames[] = {"Linear", "Parabolic"};
    inline constexpr const char* kBallCollisionStateNames[] = {"None", "NetTop", "NetMiddle", "NetBottom"};
    inline constexpr const char* kTeamNames[] = {"LEFT", "RIGHT", "NONE"};
    inline constexpr const char* kFinalIntentNames[] = {"None", "Jump", "Spike", "Feint", "Block", "Bump",
                                                        "Slide", "Set", "Serve", "SpikeLight", "SpikeMedium",
                                                        "SpikeStrong"};

    struct EnumInfo
    {
        const char* name;
        const char* underlying;
        const char* const* names;
        int count;
    };

    inline constexpr EnumInfo kEnums[] = {
        {"ActionState", "uint8", kActionStateNames, 11},
        {"TrajectoryType", "int32", kTrajectoryTypeNames, 2},
        {"BallCollisionState", "uint8", kBallCollisionStateNames, 4},
        {"Team", "uint8", kTeamNames, 3},
        {"FinalIntent", "int32", kFinalIntentNames, 12},
    };

    // Transition logging policy for a field (used by diagnostics only).
    enum class TransitionMode : uint8_t
    {
        None = 0,   // continuous value: only reported as a field change
        Always,     // discrete state: every change is a state transition
        Boundary    // counter/timer: transition when crossing <0 / 0 / >0 or when re-armed (increase)
    };

    // ----------------------------------------------------------
    // Visitor contract (duck-typed):
    //   v.Float(name, float, TransitionMode)
    //   v.Int(name, int64_t, TransitionMode)
    //   v.UInt(name, uint64_t, TransitionMode)
    //   v.Bool(name, bool, TransitionMode)
    //   v.Enum(name, int64_t value, const char* enumName, TransitionMode)
    //   v.BeginObject(name) / v.EndObject()
    //   v.BeginArray(name)  / v.EndArray()      (elements use BeginObject(nullptr))
    // ----------------------------------------------------------

    // ---- leaf math types (anonymous unions alias x/width and y/height; logical fields are x,y) ----
    static_assert(sizeof(SimMath::Vec2) == 2 * sizeof(float), "Vec2 layout changed: update SimSchema");
    static_assert(sizeof(SimMath::Size) == 2 * sizeof(float), "Size layout changed: update SimSchema");

    template <class V>
    void Visit(V& v, const SimMath::Vec2& p)
    {
        v.Float("x", p.x, TransitionMode::None);
        v.Float("y", p.y, TransitionMode::None);
    }

    template <class V>
    void Visit(V& v, const SimMath::Size& s)
    {
        v.Float("x", s.x, TransitionMode::None);
        v.Float("y", s.y, TransitionMode::None);
    }

    // ---- ComponentStorage component types ----
    template <class V>
    void Visit(V& v, const PositionComponent& c)
    {
        const auto& [position] = c;
        v.BeginObject("position");
        Visit(v, position);
        v.EndObject();
    }

    template <class V>
    void Visit(V& v, const SizeComponent& c)
    {
        const auto& [size] = c;
        v.BeginObject("size");
        Visit(v, size);
        v.EndObject();
    }

    template <class V>
    void Visit(V& v, const JumpUpFrameComponent& c)
    {
        const auto& [remainingFrames] = c;
        v.Int("remainingFrames", remainingFrames, TransitionMode::Boundary);
    }

    template <class V>
    void Visit(V& v, const BallTrajectoryComponent& c)
    {
        const auto& [type, speed, a, b, cc] = c;
        v.Enum("type", static_cast<int64_t>(type), "TrajectoryType", TransitionMode::Always);
        v.Float("speed", speed, TransitionMode::None);
        v.Float("a", a, TransitionMode::None);
        v.Float("b", b, TransitionMode::None);
        v.Float("c", cc, TransitionMode::None);
    }

    template <class V>
    void Visit(V& v, const CharacterActionState& c)
    {
        const auto& [status, remainTime] = c;
        v.Enum("status", static_cast<int64_t>(status), "ActionState", TransitionMode::Always);
        v.Float("remainTime", remainTime, TransitionMode::Boundary);
    }

    // ---- state types (stored in ComponentStorage, keyed by match id) ----
    template <class V>
    void Visit(V& v, const BallGamePlayState& c)
    {
        const auto& [landingX, stateFrame, collisionState] = c;
        v.Float("landingX", landingX, TransitionMode::None);
        v.Int("stateFrame", stateFrame, TransitionMode::Boundary);
        v.Enum("collisionState", static_cast<int64_t>(collisionState), "BallCollisionState", TransitionMode::Always);
    }

    template <class V>
    void Visit(V& v, const MatchState& c)
    {
        const auto& [leftScore, rightScore, servingTeam, leftServingEntity, rightServingEntity, finished, matchWinner] =
            c;
        v.Int("leftScore", leftScore, TransitionMode::Always);
        v.Int("rightScore", rightScore, TransitionMode::Always);
        v.Enum("servingTeam", static_cast<int64_t>(servingTeam), "Team", TransitionMode::Always);
        v.UInt("leftServingEntity", leftServingEntity, TransitionMode::Always);
        v.UInt("rightServingEntity", rightServingEntity, TransitionMode::Always);
        v.Bool("finished", finished, TransitionMode::Always);
        v.Enum("matchWinner", static_cast<int64_t>(matchWinner), "Team", TransitionMode::Always);
    }

    template <class V>
    void Visit(V& v, const RallyState& c)
    {
        const auto& [lastTouch, touchCount] = c;
        v.UInt("lastTouch", lastTouch, TransitionMode::Always);
        v.UInt("touchCount", touchCount, TransitionMode::Always);
    }

    // ---- intent types (IntentStorage, per tick) ----
    template <class V>
    void Visit(V& v, const CharacterIntent& c)
    {
        const auto& [moveX, finalIntent] = c;
        v.Float("moveX", moveX, TransitionMode::None);
        v.Enum("finalIntent", static_cast<int64_t>(finalIntent), "FinalIntent", TransitionMode::Always);
    }

    template <class V>
    void Visit(V& v, const BallIntent& c)
    {
        const auto& [newX, newY] = c;
        v.Float("newX", newX, TransitionMode::None);
        v.Float("newY", newY, TransitionMode::None);
    }

    template <class V>
    void Visit(V& v, const VelocityIntent& c)
    {
        const auto& [vx, vy] = c;
        v.Float("vx", vx, TransitionMode::None);
        v.Float("vy", vy, TransitionMode::None);
    }

    // ---- match data (CharacterStatStorage) ----
    template <class V>
    void Visit(V& v, const CharacterStat& c)
    {
        const auto& [AttackPower, DefensePower, Speed, JumpPower] = c;
        v.Float("AttackPower", AttackPower, TransitionMode::None);
        v.Float("DefensePower", DefensePower, TransitionMode::None);
        v.Float("Speed", Speed, TransitionMode::None);
        v.Float("JumpPower", JumpPower, TransitionMode::None);
    }

    template <class V>
    void Visit(V& v, const MatchCharacterStat& c)
    {
        const auto& [stats] = c;
        static_assert(std::extent_v<std::remove_reference_t<decltype(stats)>> == ChunkConfig::CHARACTER_PER_MATCH,
                      "MatchCharacterStat::stats size changed");
        v.BeginArray("stats");
        for (const auto& s : stats)
        {
            v.BeginObject(nullptr);
            Visit(v, s);
            v.EndObject();
        }
        v.EndArray();
    }

    // ----------------------------------------------------------
    // Type names (for schema / pools table)
    // ----------------------------------------------------------
    template <class T> constexpr const char* TypeName();
    template <> constexpr const char* TypeName<PositionComponent>() { return "PositionComponent"; }
    template <> constexpr const char* TypeName<SizeComponent>() { return "SizeComponent"; }
    template <> constexpr const char* TypeName<JumpUpFrameComponent>() { return "JumpUpFrameComponent"; }
    template <> constexpr const char* TypeName<BallTrajectoryComponent>() { return "BallTrajectoryComponent"; }
    template <> constexpr const char* TypeName<CharacterActionState>() { return "CharacterActionState"; }
    template <> constexpr const char* TypeName<BallGamePlayState>() { return "BallGamePlayState"; }
    template <> constexpr const char* TypeName<MatchState>() { return "MatchState"; }
    template <> constexpr const char* TypeName<RallyState>() { return "RallyState"; }
    template <> constexpr const char* TypeName<CharacterIntent>() { return "CharacterIntent"; }
    template <> constexpr const char* TypeName<BallIntent>() { return "BallIntent"; }
    template <> constexpr const char* TypeName<VelocityIntent>() { return "VelocityIntent"; }
    template <> constexpr const char* TypeName<MatchCharacterStat>() { return "MatchCharacterStat"; }
    template <> constexpr const char* TypeName<CharacterStat>() { return "CharacterStat"; }

    // ----------------------------------------------------------
    // Compile-time declared-field counter (independent of Visit)
    // ----------------------------------------------------------
    struct AnyField
    {
        template <class U>
        constexpr operator U() const noexcept
        {
            return U{};
        }
    };

    template <class T, class... A>
    concept BraceInitializable = requires { T{std::declval<A>()...}; };

    template <class T, class... A>
    constexpr std::size_t AggregateFieldCount()
    {
        if constexpr (sizeof...(A) > 16)
            return sizeof...(A);
        else if constexpr (BraceInitializable<T, A..., AnyField>)
            return AggregateFieldCount<T, A..., AnyField>();
        else
            return sizeof...(A);
    }

    // Arrays allow brace elision, which defeats the generic counter: fixed value.
    template <> constexpr std::size_t AggregateFieldCount<MatchCharacterStat>() { return 1; }

    // ----------------------------------------------------------
    // Pool registry
    // ----------------------------------------------------------
    enum class KeyKind : uint8_t
    {
        Entity,
        Match
    };

    enum class Section : uint8_t
    {
        Entities,   // persistent entity components (ComponentStorage, entity keys)
        Matches,    // persistent match state (ComponentStorage, match keys)
        Intents,    // per-tick transient (IntentStorage)
        MatchData   // CharacterStatStorage
    };

    inline const char* KeyKindName(KeyKind k) { return k == KeyKind::Entity ? "entity" : "match"; }
    inline const char* SectionName(Section s)
    {
        switch (s)
        {
        case Section::Entities: return "entities";
        case Section::Matches: return "matches";
        case Section::Intents: return "intents";
        case Section::MatchData: return "matchData";
        }
        return "unknown";
    }

    struct PoolInfo
    {
        const char* name;
        const char* storage;
        const char* typeName;
        KeyKind key;
        Section section;
        const char* lifetime;  // "persistent" | "tick"
    };

    // Calls f(const PoolInfo&, const ComponentPool<T>&) for every pool, in a fixed order.
    template <class ComponentStorageT, class IntentStorageT, class StatPoolT, class F>
    void ForEachPool(const ComponentStorageT& cs, const IntentStorageT& is, const StatPoolT& statPool, F&& f)
    {
        f(PoolInfo{"characterPositionPool", "ComponentStorage", TypeName<PositionComponent>(), KeyKind::Entity, Section::Entities, "persistent"}, cs.characterPositionPool);
        f(PoolInfo{"characterSizePool", "ComponentStorage", TypeName<SizeComponent>(), KeyKind::Entity, Section::Entities, "persistent"}, cs.characterSizePool);
        f(PoolInfo{"ballPositionPool", "ComponentStorage", TypeName<PositionComponent>(), KeyKind::Entity, Section::Entities, "persistent"}, cs.ballPositionPool);
        f(PoolInfo{"jumpUpFramePool", "ComponentStorage", TypeName<JumpUpFrameComponent>(), KeyKind::Entity, Section::Entities, "persistent"}, cs.jumpUpFramePool);
        f(PoolInfo{"ballTrajectoryPool", "ComponentStorage", TypeName<BallTrajectoryComponent>(), KeyKind::Entity, Section::Entities, "persistent"}, cs.ballTrajectoryPool);
        f(PoolInfo{"characterActionStatePool", "ComponentStorage", TypeName<CharacterActionState>(), KeyKind::Entity, Section::Entities, "persistent"}, cs.characterActionStatePool);
        f(PoolInfo{"ballGameplayStatePool", "ComponentStorage", TypeName<BallGamePlayState>(), KeyKind::Match, Section::Matches, "persistent"}, cs.ballGameplayStatePool);
        f(PoolInfo{"matchStatePool", "ComponentStorage", TypeName<MatchState>(), KeyKind::Match, Section::Matches, "persistent"}, cs.matchStatePool);
        f(PoolInfo{"rallyStatePool", "ComponentStorage", TypeName<RallyState>(), KeyKind::Match, Section::Matches, "persistent"}, cs.rallyStatePool);
        f(PoolInfo{"characterIntentPool", "IntentStorage", TypeName<CharacterIntent>(), KeyKind::Entity, Section::Intents, "tick"}, is.characterIntentPool);
        f(PoolInfo{"ballIntentPool", "IntentStorage", TypeName<BallIntent>(), KeyKind::Entity, Section::Intents, "tick"}, is.ballIntentPool);
        f(PoolInfo{"velocityIntentPool", "IntentStorage", TypeName<VelocityIntent>(), KeyKind::Entity, Section::Intents, "tick"}, is.velocityIntentPool);
        f(PoolInfo{"matchStatPool", "CharacterStatStorage", TypeName<MatchCharacterStat>(), KeyKind::Match, Section::MatchData, "persistent"}, statPool);
    }

    inline constexpr int kPoolCount = 13;

}  // namespace SimSchema
