# ProjectTotNghiep — Simulation Runtime

## 1. Project purpose

ProjectTotNghiep is being developed as a **game simulation runtime**, not merely as a render-oriented game project.

The long-term goal is to provide one simulation core that can be used by:

- offline games;
- server-authoritative PvP;
- PvE games;
- headless AI training and evaluation;
- replay / simulation testing;
- multiple matches simulated in parallel.

The key architectural principle is:

```text
Input -> Intent -> Simulation Systems -> Authoritative Components/State -> Output
```

Rendering is a consumer of simulation state. It is not the owner of gameplay truth.

---

## 2. IMPORTANT: current code is a development/prototype phase

Some code in the repository is intentionally written for **fast testing and fast iteration**.

Do NOT interpret every current implementation detail as the final framework design.

Examples include:

- Axmol types currently used for positions/sizes;
- `GetPositionData()` and its temporary `ObjectData` cache;
- `TestTrajectory` and related test helpers;
- raw pointers during the current runnable/test phase;
- single-match assumptions such as `DEFAULT_MATCH`;
- current render/debug/logging helpers;
- fixed-size test data based on the current volleyball match layout.

These exist to make the simulation runnable, observable, and easy to validate before the headless/runtime/network phases are implemented.

**Do not refactor these merely because they are not production-final abstractions.**

The correct question is whether the code violates a simulation invariant or prevents the next architectural phase.

---

## 3. Core architectural invariant

The most important rule in this project is:

```text
Input
  -> Intent
  -> System validation / resolution
  -> Component / State mutation
```

### Input

Input represents what an actor requests.

The simulation should not depend on whether that input came from:

- keyboard;
- controller;
- mobile UI;
- Unity;
- replay;
- AI.

At the runtime boundary, input is represented by a game-defined enum/namespace rather than engine-specific key codes.

### Intent

Intent represents what an actor wants to do.

Examples:

```text
MoveLeft
MoveRight
Jump
Bump
Set
Spike
Serve
```

Player AI, replay systems, and other producers may create Intent.

### Systems

Systems consume Intent and current authoritative state.

A system decides whether an Intent is valid and what state transition it produces.

For example:

```text
Client requests Spike
        |
        v
CharacterIntent(Spike)
        |
        v
Simulation checks current state
        |
        +---- invalid -> no Spike state transition
        |
        +---- valid   -> update trajectory / state
```

A client never directly owns authoritative gameplay state.

---

## 4. State ownership

Gameplay state is stored outside systems.

Conceptually:

```text
World Storage
    |
    +-- Components
    +-- Gameplay State
    +-- Match State
    +-- Rally State
    +-- Trajectory State
    +-- Intent Storage (per simulation step)
```

Systems are intended to be behavior that **consumes and transforms state**, not long-lived owners of gameplay state.

Do not introduce persistent gameplay state into a system unless there is a deliberate runtime/scheduler reason.

---

## 5. ComponentStorage and World model

`ComponentStorage` is intended to represent **world-level component storage**, not one isolated `ComponentStorage` object per match.

The world will contain multiple simulation chunks.

Conceptually:

```text
World
 |
 +-- Chunk 0 -> Match 0
 +-- Chunk 1 -> Match 1
 +-- Chunk 2 -> Match 2
 +-- ...
 +-- Chunk N -> Match N
```

A chunk is the unit of match-local simulation data.

When a match is destroyed, the match/chunk allocation is released as a whole. The runtime is expected to recycle the chunk data so that a later match can acquire it again.

This is intentional and should not be confused with a design where arbitrary entities are dynamically created/destroyed independently during normal gameplay.

---

## 6. Chunk identity and data access

The future runtime is chunk-aware.

Conceptually, APIs that currently look like:

```cpp
GetData(...)
GetObjectData(...)
```

will evolve toward the equivalent of:

```cpp
GetData(chunkId, ...)
GetObjectData(chunkId, ...)
```

The important invariant is:

```text
(chunk, entity) -> the correct simulation data for that match
```

Therefore, when adding multi-match support, do not accidentally reintroduce hidden global/default-match access.

`DEFAULT_MATCH` exists for the current runnable/test configuration.

---

## 7. No independent entity lifecycle in the current gameplay model

The current simulation model does not require arbitrary entity destruction during gameplay.

The lifecycle is closer to:

```text
Acquire Chunk
    -> Initialize Match
    -> Simulate
    -> Finish / Destroy Match
    -> Release / Recycle Chunk
```

Do not infer an entity-by-entity deletion architecture from the existence of an ECS-style entity ID.

The chunk/match is the important lifecycle boundary.

---

## 8. Parallel simulation goal

One of the major goals is to run many matches without render objects.

Conceptually:

```text
simMatch(chunk0)
simMatch(chunk1)
simMatch(chunk2)
...
```

and eventually:

```text
Worker 0 -> chunk 0, 4, 8, ...
Worker 1 -> chunk 1, 5, 9, ...
Worker 2 -> chunk 2, 6, 10, ...
...
```

The expected property is that independent chunks do not share mutable gameplay state.

This enables:

- server-side multi-match simulation;
- offline AI training;
- AI-vs-AI simulation;
- large-scale replay/testing.

The simulation workload is expected to be the computationally heavy part. Network and rendering should remain thin adapters around it.

---

## 9. Determinism

Deterministic simulation is a design goal.

Given:

```text
same initial state
+
same input sequence
+
same simulation configuration
```

the simulation should produce the same result.

Randomness may be introduced later, but it must be an explicit simulation input/state rather than accidental uncontrolled randomness.

Determinism is important for:

- server authority;
- replay;
- debugging;
- AI training;
- reproducible tests.

---

## 10. Frame-latency server model

The intended online architecture is frame-pipelined rather than blocking the network thread on simulation work.

Conceptually:

```text
Client sends input frame N
        |
        v
Input queue
        |
        v
Server simulates frame N-2
        |
        v
State / snapshot queue
        |
        v
Server sends result for frame N-4
        |
        v
Client
```

The exact offsets are part of the runtime/network design and may evolve.

The important principle is:

```text
Network input/output and simulation computation are separate stages.
```

The server receives client input, converts it into Intent internally, and remains authoritative over the resulting state.

The client does **not** send authoritative gameplay state or trusted Intent as the source of truth.

---

## 11. Server-authoritative model

For online play:

```text
Client
   |
   | Input packet
   v
Server
   |
   | Input -> Intent
   v
Simulation
   |
   | authoritative state
   v
Output / snapshot packet
   |
   v
Client render
```

The Unity client should not need to know the internal ECS structure.

The client contract should expose only what it needs:

### Input contract

A set of game-defined input commands/enums.

### Output contract

Only the components/state required by the client to render or present the current simulation result.

Everything else remains a server/simulation concern.

---

## 12. Unity is a consumer/adapter, not the simulation core

The intended relationship is:

```text
Simulation Core
      |
      +---- Unity client adapter
      +---- Headless server
      +---- AI training adapter
      +---- Test/replay adapter
```

Unity does not define:

- authoritative positions;
- collision outcomes;
- trajectory results;
- gameplay legality;
- rally state;
- server truth.

Unity consumes simulation output and sends input signals back through the contract.

The current Axmol integration exists so simulation behavior can be observed and tested quickly.

The eventual headless runtime may remove the render dependency entirely and use a lighter math/data layer.

---

## 13. Physics / trajectory design philosophy

The trajectory implementation is deliberately designed around functions that are easy to reason about, test, and evaluate quickly.

A useful conceptual model is:

```text
x = f(t)
y = g(x)

therefore:
position(t) = ( f(t), g(f(t)) )
```

For example, the current parabolic representation can be viewed as:

```text
y = a(x + b)^2 + c
```

with horizontal motion determined separately.

This is not intended to compete with a general-purpose rigid-body physics engine.

The design priorities are:

- deterministic behavior;
- cheap computation;
- easy prediction;
- easy unit testing;
- easy trajectory inspection;
- stable gameplay behavior;
- ability to add controlled modifiers later.

Possible future modifiers include gravity, wind, spin, drag, knockback, etc.

New forces/modifiers should be added without destroying the stable trajectory contract unless the gameplay model requires a deliberate change.

---

## 14. Why the project does not use arbitrary physics for everything

A general-purpose physics simulation can be more expensive and harder to reason about than required for this project.

This runtime is optimized for gameplay simulation and large numbers of independent simulations, not maximum physical realism.

The intended trade-off is:

```text
Predictability + speed + testability + controllability
        over
Maximum physical realism
```

This choice is deliberate.

---

## 15. AI integration

The AI is another Intent producer.

```text
AI model / policy
      |
      v
CharacterIntent
      |
      v
Normal simulation validation
      |
      v
Authoritative state
```

The simulation does not care whether the Intent came from:

- rule-based AI;
- KNN;
- decision tree;
- neural network;
- another learned model;
- player input;
- replay data.

The current AI implementations include baseline/test behavior intended to keep rallies running and generate useful simulation/log data.

Do not interpret the current baseline AI as the final learning architecture.

---

## 16. AI training mode

The long-term AI workflow is intended to run headlessly.

Conceptually:

```text
AI policy
   -> Intent
   -> Simulation
   -> observation / event data
   -> training / evaluation
```

Many independent chunks/matches can be simulated in parallel.

Unity rendering is not required for the training loop.

A learned AI should ultimately control the character through the same Intent boundary used by other input sources.

---

## 17. Logging and dataset generation

Current CSV/logging code is primarily a **development and observation tool**.

It is not the final universal data format.

Logging may be placed at specific simulation points where events need to be validated or captured. This is intentional.

The project may later have multiple output mechanisms for different purposes, such as:

```text
simulation events
    -> debug log

selected events
    -> CSV dataset

state history
    -> replay/binary format

network-facing state
    -> snapshot/packet format
```

These outputs serve different contracts and should not be forced into one universal logging layer.

---

## 18. Why test helpers may look unusual

Files/classes containing names such as:

```text
TestTrajectory
TrajectoryTestConfig
TrajectoryTestData
GetPositionData
```

may contain shortcuts, fixed-size buffers, singleton test state, or render-friendly structures.

These are intended to make validation faster during development.

**Do not report these as final production architecture problems without first checking whether the code belongs to the test/prototype layer.**

The fact that a helper is not ideal for a future multi-threaded runtime does not mean the current test implementation is incorrect.

---

## 19. System scheduling

The current runtime uses explicit system ordering.

For example, conceptually:

```text
Input generation
    -> action/state preparation
    -> jump preparation
    -> trajectory calculation
    -> velocity intent
    -> intent application
    -> gameplay rules
    -> render/output
```

The order matters because some phases may consume results produced by earlier phases.

A future parallel scheduler must preserve these dependencies explicitly.

Do not parallelize systems merely because they are separate classes. Parallelism must respect read/write dependencies on shared chunk data.

---

## 20. What counts as a real bug

When reviewing this repository, distinguish between:

### Real architectural bugs

Examples:

- a system or adapter directly bypasses Intent and mutates authoritative state without a deliberate reason;
- simulation depends on rendering to compute gameplay results;
- mutable gameplay state becomes unintentionally global across chunks;
- one chunk can modify another chunk's state without an explicit world-level operation;
- deterministic results depend on uncontrolled external state;
- a network client can directly authoritatively define simulation state.

### Technical debt / future work

Examples:

- replacing Axmol math types for the headless build;
- replacing raw pointers with a production allocator/pool;
- adding the final network contract;
- implementing the final snapshot serialization;
- replacing prototype logging;
- introducing the parallel scheduler;
- moving fixed test assumptions into final chunk configuration.

### Intentional prototype/test code

Examples:

- fixed-size `ObjectData` test views;
- `DEFAULT_MATCH` in the one-match test stage;
- `TestTrajectory` singleton;
- direct render hooks used only to observe simulation behavior.

Do not mix these categories during review.

---

## 21. Current development order

The project is being developed in phases.

### Phase 1 — simulation correctness

- Intent flow;
- trajectory correctness;
- state transitions;
- gameplay rules;
- collision/trajectory behavior;
- event/log validation.

### Phase 2 — simulation isolation

- chunk-aware data access;
- chunk acquisition/recycling;
- deterministic multi-match execution;
- parallel simulation.

### Phase 3 — runtime/network

- Unity input/output contract;
- packet definitions;
- frame queues;
- server-authoritative execution;
- snapshot/output contract.

### Phase 4 — headless / AI

- headless simulation build;
- high-throughput multi-match simulation;
- AI observation interfaces;
- training/evaluation runners.

The code should not be judged as if all four phases have already been implemented.

---

## 22. Review mindset for humans and AI coding agents

Before changing code, answer these questions:

1. Is this file part of the simulation core, an adapter, or test/prototype tooling?
2. Does the change preserve `Input -> Intent -> Simulation -> State`?
3. Does the change introduce hidden mutable state shared by chunks?
4. Does the change make render/UI/network code authoritative over simulation state?
5. Does the change break deterministic/reproducible simulation?
6. Does the change unnecessarily force a final architecture before the current test phase is complete?

Prefer the smallest change that improves correctness without prematurely forcing later-phase abstractions.

---

## 23. Final architectural summary

The intended end-state is approximately:

```text
                          CLIENT / AI / REPLAY
                                  |
                              Input Contract
                                  |
                                  v
                               Intent
                                  |
                                  v
                    +---------------------------+
                    |     SIMULATION WORLD       |
                    |                           |
                    |  Chunk 0  Chunk 1 ... N  |
                    |     |       |             |
                    |     v       v             |
                    |  Components / State      |
                    |           ^              |
                    |           |              |
                    |        Systems            |
                    +-----------+---------------+
                                |
                         authoritative result
                                |
                 +--------------+---------------+
                 |              |               |
               Unity         Server          AI/Replay
              render        snapshot        observation
```

The core idea is to keep the simulation independent from its consumers while allowing the same simulation code to serve multiple products and workflows.

---

## 24. Short version for code reviewers

If you only remember five things about this repository, remember these:

```text
1. This is a simulation runtime in development, not just a renderer-driven game.
2. Input becomes Intent; systems validate Intent and update authoritative state.
3. Components/state belong to the world/chunk data, not to systems.
4. Unity/Axmol is currently used to observe/test the simulation, not to define gameplay truth.
5. Some current test helpers are intentionally temporary and should not be mistaken for final runtime architecture.
```
