# ProjectTotNghiep — Simulation Runtime

## PHIÊN BẢN TIẾNG VIỆT

> **Lưu ý về thuật ngữ:** Tài liệu ưu tiên các thuật ngữ kỹ thuật đã được sử dụng trong kiến trúc hiện tại. Khi không có cách dịch tiếng Việt đủ chính xác hoặc dễ gây hiểu sai, thuật ngữ tiếng Anh được giữ nguyên.

## 0. Vì sao Project cần Simulation Layer?

Project này không chỉ nhằm tạo ra một game có hình ảnh và gameplay chạy được trong một client. Mục tiêu là xây dựng một **Simulation Runtime** có thể được sử dụng độc lập với lớp hiển thị.

Nếu gameplay được đặt trực tiếp trong Unity/renderer, logic mô phỏng sẽ bị gắn vào một môi trường cụ thể. Khi đó việc dùng cùng logic cho nhiều mục đích sẽ khó hơn: offline game, server-authoritative PvP, PvE, replay, AI training hoặc nhiều Match chạy đồng thời.

Vì vậy project tách **Core Simulation** khỏi **Render Layer** và **UI Layer**.

Core Simulation trả lời câu hỏi:

> **Game đang thực sự xảy ra chuyện gì?**

Render Layer trả lời:

> **Sự việc đó được hiển thị như thế nào?**

UI Layer trả lời:

> **Người dùng tương tác và nhìn thấy thông tin đó như thế nào?**

Một lớp **Data Configuration** được sử dụng để cung cấp dữ liệu cấu hình dùng chung cho các tầng. UI có thể thay đổi lựa chọn/cấu hình được phép; Core đọc cấu hình cần thiết để khởi tạo và mô phỏng dữ liệu gameplay; Render Layer đọc cấu hình presentation để biết hình ảnh, chất lượng hiển thị, animation, audio, visual effect và các thuộc tính trình bày tương ứng.

Mục tiêu của việc tách lớp là để:

```text
Configuration / Contract
          |
          v
Input -> Intent -> Simulation Systems -> Authoritative State
          |                         |
          |                         +-----> Simulation Output
          |                                      |
          v                                      v
       UI / Client                         Render / Server / AI
```

**Simulation là nguồn sự thật của gameplay.** Client, render và UI không được trở thành nơi quyết định authoritative gameplay state.

---

## 1. Mục tiêu của Project

ProjectTotNghiep được phát triển như một **game simulation runtime**, không chỉ là một game phụ thuộc vào renderer.

Mục tiêu dài hạn là dùng cùng một Simulation Core cho:

- game offline;
- server-authoritative PvP;
- PvE;
- headless AI training và evaluation;
- replay và simulation testing;
- nhiều Match được mô phỏng song song.

Nguyên tắc kiến trúc chính:

```text
Input -> Intent -> Simulation Systems -> Authoritative Components/State -> Output
```

Rendering là consumer của Simulation State, không phải owner của gameplay truth.

---

## 2. Quy tắc đọc code: Prototype/Test không đồng nghĩa với kiến trúc cuối

Một phần code hiện tại được viết có chủ đích để **test nhanh, quan sát nhanh và thay đổi nhanh**.

Không được suy luận rằng mọi implementation detail hiện tại là thiết kế runtime cuối cùng.

Ví dụ:

- Axmol types đang được sử dụng cho position/size để có thể quan sát simulation nhanh;
- `GetPositionData()` và temporary `ObjectData` cache là lớp test/view hiện tại;
- `TestTrajectory` và các helper liên quan là tooling cho trajectory testing;
- raw pointer được dùng trong giai đoạn runnable/test hiện tại;
- các giả định single-match như `DEFAULT_MATCH` phục vụ phase hiện tại;
- logging/render/debug helper hiện tại phục vụ validation;
- fixed-size test data phản ánh layout volleyball đang được kiểm thử.

Các phần trên giúp simulation chạy được và dễ kiểm chứng trước khi chuyển sang headless runtime, multi-match, networking và scheduling.

**Không refactor chỉ vì một đoạn code chưa giống production-final architecture.** Trước tiên phải kiểm tra nó có phá simulation invariant hay cản trở phase tiếp theo hay không.

---

## 3. Core Architectural Invariant

Luật quan trọng nhất:

```text
Input
  -> Intent
  -> System validation / resolution
  -> Component / State mutation
```

### Input

Input biểu diễn điều actor yêu cầu.

Simulation không phụ thuộc việc input đến từ keyboard, controller, mobile UI, Unity, replay hay AI.

Ở runtime boundary, input được biểu diễn bằng game-defined enum/namespace thay vì phụ thuộc engine-specific key code.

### Intent

Intent biểu diễn điều actor muốn thực hiện.

Ví dụ:

```text
MoveLeft
MoveRight
Jump
Bump
Set
Spike
Serve
```

Player, AI, replay và các producer khác có thể tạo Intent.

### Systems

System tiêu thụ Intent và authoritative state hiện tại.

System quyết định Intent có hợp lệ hay không và state transition nào được tạo ra.

Client không trực tiếp sở hữu authoritative gameplay state.

---

## 4. State Ownership

Gameplay State được lưu bên ngoài System.

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

System chủ yếu là behavior: đọc dữ liệu, xử lý và biến đổi state. System không phải owner lâu dài của gameplay state.

---

## 5. ComponentStorage và World Model

`ComponentStorage` được định hướng là **World-level component storage**, không phải một `ComponentStorage` độc lập cho từng Match.

```text
World
 |
 +-- Chunk 0 -> Match 0
 +-- Chunk 1 -> Match 1
 +-- Chunk 2 -> Match 2
 +-- ...
 +-- Chunk N -> Match N
```

`Chunk` là đơn vị dữ liệu simulation của một Match.

Khi Match bị hủy, Match/Chunk allocation được giải phóng theo cả Match và có thể được recycle cho Match mới.

---

## 6. Chunk Identity và Data Access

Runtime tương lai được thiết kế theo hướng chunk-aware.

Các API hiện tại như:

```cpp
GetData(...)
GetObjectData(...)
```

về sau có thể được mở rộng theo hướng:

```cpp
GetData(chunkId, ...)
GetObjectData(chunkId, ...)
```

Invariant quan trọng:

```text
(chunk, entity) -> đúng simulation data của Match đó
```

`DEFAULT_MATCH` chỉ là giả định của phase runnable/test hiện tại.

---

## 7. Match Lifecycle

Gameplay hiện tại không yêu cầu arbitrary entity destruction trong Match.

Lifecycle hướng tới:

```text
Acquire Chunk
    -> Initialize Match
    -> Simulate
    -> Finish / Destroy Match
    -> Release / Recycle Chunk
```

Vì vậy không nên suy luận rằng project đang hướng tới entity-by-entity destruction trong gameplay thông thường.

Chunk/Match mới là lifecycle boundary quan trọng.

---

## 8. Parallel Simulation Goal

Một mục tiêu quan trọng là chạy nhiều Match mà không cần render object.

```text
simMatch(chunk0)
simMatch(chunk1)
simMatch(chunk2)
...
```

và về sau có thể phân phối cho worker/thread khác nhau.

Invariant cần giữ là các Chunk độc lập không chia sẻ mutable gameplay state ngoài những operation cấp World được thiết kế rõ ràng.

Điều này phục vụ:

- server-side multi-match simulation;
- offline AI training;
- AI-vs-AI simulation;
- large-scale replay/testing.

---

## 9. Determinism

Deterministic simulation là design goal.

Với:

```text
same initial state
+
same input sequence
+
same simulation configuration
```

simulation phải cho ra cùng kết quả.

Randomness có thể được bổ sung sau, nhưng phải là một phần state/input có kiểm soát chứ không phải randomness ngoài tầm kiểm soát.

Determinism phục vụ server authority, replay, debugging, AI training và reproducible test.

---

## 10. Frame-latency Server Model

Online runtime dự kiến dùng pipeline theo frame để các stage không chặn nhau.

```text
Client sends input frame N
        |
        v
Input Queue
        |
        v
Server simulates frame N-2
        |
        v
State / Snapshot Queue
        |
        v
Server sends result for frame N-4
        |
        v
Client
```

Mô hình `N -> N-2 -> N-4` là execution model mục tiêu của server runtime. Các offset cụ thể có thể được điều chỉnh khi contract và implementation được hoàn thiện.

Nguyên tắc quan trọng là Network Input/Output và Simulation Computation là các stage riêng biệt.

---

## 11. Server-authoritative Model

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
   | Authoritative State
   v
Output / Snapshot packet
   |
   v
Client render
```

Client chỉ gửi input. Server tự tạo Intent và quyết định kết quả simulation.

Client không gửi authoritative gameplay state và không được là nguồn sự thật của Intent/action result.

---

## 12. Unity Render Layer

Unity là **consumer/adapter**, không phải Simulation Core.

Unity chịu trách nhiệm:

- tích hợp Core Simulation;
- Input Bridge;
- Rendering;
- Animation;
- Character/Ball representation;
- Camera;
- Environment;
- Visual Effect/Presentation;
- Runtime Integration;
- đóng gói và Build game.

Unity không quyết định:

- Gameplay Rule;
- Character State;
- Ball State;
- authoritative Position;
- trajectory result;
- rally/match state;
- ECS storage.

Unity chỉ nhận các dữ liệu cần thiết để hiển thị.

---

## 13. UI Layer

UI chịu trách nhiệm về:

- Interface;
- Match Selection;
- Configuration Selection;
- Event information;
- Progress;
- Information;
- Result;
- các thao tác giao diện.

UI không trực tiếp thao tác ECS hoặc Gameplay State.

Flow chính:

```text
User
  -> UI
  -> Select Match / Configuration
  -> Play
  -> Unity Render Layer
  -> Core Simulation
```

---

## 14. Data Configuration Layer

Đây là lớp dữ liệu cấu hình dùng chung, tách khỏi gameplay state runtime.

Mục tiêu là để các thành phần khác nhau đọc cùng một nguồn cấu hình mà không tự định nghĩa lại dữ liệu.

Ví dụ cấu hình có thể phục vụ:

```text
Data Configuration
        |
        +---- UI
        |      -> đọc/hiển thị lựa chọn
        |      -> cập nhật các configuration được phép
        |
        +---- Core Simulation
        |      -> đọc dữ liệu cần để khởi tạo gameplay
        |      -> load character/gameplay configuration
        |      -> áp dụng các thông số simulation
        |
        +---- Unity Render Layer
               -> đọc presentation configuration
               -> hình ảnh / chất lượng render
               -> animation
               -> audio
               -> visual effect
               -> presentation parameters
```

UI không trực tiếp sửa Component.

UI chỉ thay đổi hoặc lựa chọn **configuration data theo contract**.

Core đọc configuration cần thiết để khởi tạo và mô phỏng gameplay.

Render Layer đọc configuration presentation để biết cách biểu diễn simulation output.

Configuration data là **data contract/configuration source**, không phải authoritative gameplay state.

---

# 15. Work Breakdown — Phân chia công việc

Project được chia thành các phần có ranh giới trách nhiệm rõ ràng:

```text
GAME
├── CORE SIMULATION
├── UNITY RENDER LAYER
├── UI LAYER
└── DATA CONFIGURATION
```

Data Configuration là lớp dữ liệu dùng chung và là contract/configuration source cho các tầng; không phải một nơi thay thế Core Simulation.

## 15.1 Core Simulation

Core chịu trách nhiệm trả lời:

> **Game đang xảy ra chuyện gì?**

### Công việc chính của Core

- ECS Foundation;
- Component Storage;
- Intent Architecture;
- Game State;
- Character State;
- Ball State;
- Gameplay Rule;
- Movement;
- Action;
- Trajectory;
- Detection;
- Rally;
- Match State;
- Simulation Tick;
- Simulation Data;
- Logging phục vụ debug/analysis/dataset khi cần.

### Core không chịu trách nhiệm

- Rendering;
- Animation presentation;
- UI;
- Camera;
- Visual Effect;
- Audio presentation;
- Unity GameObject representation.

### Core hiện đã có nền tảng

- ECS Foundation;
- Component Storage;
- Intent;
- Character Simulation;
- Ball Simulation;
- Trajectory;
- Detection;
- Gameplay Action;
- Rally / Match State;
- Logging;
- Single Match.

Các hạng mục này được mô tả trong tài liệu phân rã công việc là đã có implementation hoặc đã có nền tảng. fileciteturn28file0L620-L632

### Core cần tiếp tục

Theo tài liệu phân rã công việc:

- Match Abstraction;
- Multi-Match;
- MatchIndex;
- Data Layout hoàn chỉnh cho Multi-Match;
- Frame Buffer;
- Input Queue;
- Output Queue;
- Server Simulation;
- Network Integration;
- Performance Benchmark.

Các phần này thuộc các milestone phát triển tiếp theo, không được hiểu là đã hoàn thành chỉ vì đã có trong kiến trúc. fileciteturn28file0L633-L644

### Hướng phát triển dài hạn của Core

- AI Runtime;
- AI Training;
- Large-scale Simulation;
- Server Scale.

---

## 15.2 Unity Render Layer

Unity chịu trách nhiệm trả lời:

> **Game đang được hiển thị như thế nào?**

### Công việc chính của Unity

- Core Integration;
- Input Integration / Input Bridge;
- Simulation Data Integration;
- Character Rendering;
- Ball Rendering;
- Environment;
- Animation;
- Camera;
- Presentation;
- Runtime Handling;
- Build / Release.

Unity nhận Simulation Output và biến nó thành các đối tượng trình bày như GameObject, Animator, Transform, Visual Effect và Camera/Presentation. fileciteturn28file0L441-L468 fileciteturn28file0L478-L483

### Unity không làm

- Gameplay Rule;
- quyết định Ball State;
- quyết định Character State;
- tự mô phỏng lại Core;
- trực tiếp thao tác ECS Storage.

### Phạm vi tối thiểu

```text
Core Integration
 -> Input
 -> Receive Simulation Data
 -> Character / Ball Rendering
 -> Animation
 -> Camera
 -> Playable Game
 -> Release Build
```

Các phần nâng cao như Advanced VFX, Audio, Advanced Animation, Advanced Camera, Optimization, Editor Tools và Debug Visualization có thể được phân rã thêm sau. fileciteturn28file0L486-L499

---

## 15.3 UI Layer

UI chịu trách nhiệm trả lời:

> **Người dùng tương tác và nhìn thấy thông tin như thế nào?**

### Công việc chính của UI

- Interface;
- Match Selection;
- Configuration Selection;
- Event display;
- Progress display;
- Information display;
- Result;
- Play flow;
- các thao tác giao diện.

### Phạm vi tối thiểu

```text
UI
 -> Select Match
 -> Select Configuration
 -> Play
 -> Unity Gameplay
 -> Event / Progress / Information
 -> Result
```

UI không trực tiếp khởi tạo hoặc thao tác ECS. fileciteturn28file0L500-L526 fileciteturn28file0L665-L676

---

## 15.4 Data Configuration Layer

Data Configuration chịu trách nhiệm cung cấp dữ liệu cấu hình dùng chung cho các tầng.

### UI sử dụng Data Configuration để

- đọc các lựa chọn được phép;
- hiển thị configuration;
- cập nhật các giá trị cấu hình theo quyền của UI;
- gửi configuration/control information theo contract.

### Core sử dụng Data Configuration để

- load dữ liệu nhân vật;
- load gameplay-related configuration;
- xác định các thông số cần thiết cho initialization và simulation;
- giữ configuration tách khỏi runtime state.

### Unity Render Layer sử dụng Data Configuration để

- biết resource/presentation configuration;
- biết hình ảnh và chất lượng hiển thị;
- biết animation configuration;
- biết audio configuration;
- biết visual effect configuration;
- biết các presentation parameter khác.

Một thay đổi ở configuration không đồng nghĩa với thay đổi trực tiếp authoritative Component State.

---

## 16. API / Data Contract

Các tầng không truy cập implementation nội bộ của nhau.

Contract được thiết kế theo nguyên tắc:

```text
CORE
  |
  | API / Data Contract
  +-------------------+
  |                   |
  v                   v
UNITY                 UI
```

Core không expose trực tiếp cho consumer:

- `ComponentPool` implementation;
- `ComponentStorage` implementation;
- internal System;
- internal ECS structure.

Unity và UI chỉ nhận dữ liệu cần thiết cho chức năng của mình. Tài liệu architecture gốc cũng xác định rõ contract không được biến thành quyền truy cập trực tiếp vào ECS nội bộ. fileciteturn28file0L549-L567

---

## 17. Toàn bộ Game Flow

Ở mức interface:

```text
USER
  |
  v
UI
  |
  +--> Select Match
  |
  +--> Configuration
  |
  +--> Play
  |
  v
UNITY RENDER LAYER
  |
  +--> Start / Connect Core
  |
  v
CORE SIMULATION
  |
  +--> Input / Intent
  +--> Components
  +--> Gameplay Rule
  +--> Simulation State
  |
  v
Simulation Output
  |
  v
UNITY RENDER LAYER
  |
  +--> Rendering / Animation
  |
  v
USER
```

UI có thể tiếp tục hiển thị Event, Progress, Score, Status và Result nhưng không trở thành một phần của Simulation. fileciteturn28file0L568-L611

---

## 18. Single Match / Multi-Match / Server / AI Scope

### Hiện tại

Core tập trung vào việc làm Simulation một Match chạy ổn định và có thể quan sát được.

### Phát triển tiếp theo

```text
Single Match
    -> Match Abstraction
    -> Multi-Match
    -> MatchIndex / Data Layout
    -> Frame Buffer
    -> Input Queue / Output Queue
    -> Server Simulation
    -> Network Integration
    -> Benchmark
```

### Sau đó

```text
Server / Multi-client
        -> Large-scale Simulation
        -> AI Runtime
        -> AI Training / Inference
```

Theo tài liệu phân rã công việc, Multi-Match, Match Scheduler, Match × Frame Queue, Server Input/Output Queue, N-2/N-4, Multi-client Server, Network Layer, Large-scale Benchmark và AI Runtime/Training hoàn chỉnh chưa được ghi là đã hoàn thành trong implementation hiện tại. fileciteturn28file0L336-L354

---

## 19. Release Scope

### Release 1

Mục tiêu là tạo một **playable game** dựa trên Core hiện có, Unity Render Layer và UI Layer.

```text
RELEASE 1
   |
   +--- CORE      -> Simulation / Game Rule / Single Match
   +--- UNITY     -> Integration / Rendering / Animation / Camera
   +--- UI        -> Interface / Selection / Configuration / Information
   +--- DATA      -> Shared configuration required by the above
```

Release 1 không bắt buộc phải có:

- Multi-Match Server;
- N-2/N-4 Server Model;
- Multi-client Server;
- Large-scale Simulation;
- Full AI Training;
- Reinforcement Learning.

Điều này đúng với scope được nêu trong tài liệu phân rã công việc. fileciteturn28file0L677-L703

---

## 20. Definition of Done

### Core

Một milestone Core đạt khi:

- Simulation chạy ổn định;
- Gameplay Rule hoạt động;
- Input/Intent hoạt động;
- Character/Ball hoạt động;
- Output có thể được Unity sử dụng;
- Simulation không phụ thuộc Rendering.

### Unity

Một milestone Unity đạt khi:

- tích hợp được Core;
- gửi được Input;
- nhận được Simulation Output;
- render được Character/Ball;
- có Animation;
- có gameplay presentation;
- build và chạy được game.

### UI

Một milestone UI đạt khi:

- chọn được Match/configuration;
- nhấn Play được;
- chuyển sang Unity Gameplay được;
- xem được Event/Progress/Information;
- xem được Result.

Các tiêu chí này giữ nguyên tinh thần Definition of Done trong tài liệu công việc. fileciteturn28file0L712-L738

---

## 21. Scope Rules cho Developer và AI Coding Agent

Project phân biệt rõ ba trạng thái:

```text
ĐÃ LÀM
PHÁT TRIỂN TIẾP
TƯƠNG LAI
```

Không được:

- coi thiết kế là implementation đã hoàn thành;
- coi prototype helper là final runtime design;
- refactor một thành phần chỉ vì nó chưa phải abstraction cuối;
- thêm coupling giữa Unity/UI và authoritative Simulation State;
- tự ý tạo một contract mới nếu contract hiện tại chưa yêu cầu.

Trước khi sửa code, hãy kiểm tra:

1. File thuộc Core Simulation, Adapter, UI, Data Configuration hay Test/Prototype?
2. Change có giữ `Input -> Intent -> Simulation -> State` không?
3. Change có tạo mutable gameplay state global/shared không?
4. Change có khiến Render/UI/Network trở thành nguồn authoritative truth không?
5. Change có phá Determinism hoặc Chunk Isolation không?
6. Change có thực sự cần cho phase hiện tại hay chỉ là premature refactor?

---

## 22. Kiến trúc dài hạn

```text
                    CORE SIMULATION
                         |
            +------------+------------+
            |            |            |
            v            v            v
         Offline       Server         AI
         1 Match       Multi-Match    Training
                        |
                        v
                    Frame Queue
                        |
                        v
                  Server Simulation
```

Core Simulation được xây trước; sau đó mở rộng sang Multi-Match, Frame Queue, Server và AI mà không thay đổi bản chất của Simulation nếu các API/Data Contract được giữ ổn định. fileciteturn28file0L704-L711

---

# ENGLISH VERSION

## Additional English Architecture Context

### Why the Simulation Layer exists

The project is not designed only as a renderer-driven game. It is being developed around a **Simulation Runtime** that can later be reused by an offline client, a server-authoritative PvP server, PvE, replay/testing, and headless AI workloads.

The Core Simulation answers **what is happening in the game**. Unity answers **how that result is presented**. UI answers **how users interact with and view interface information**. A separate **Data Configuration** layer provides shared configuration data to the relevant layers.

The intended boundary is:

```text
Input -> Intent -> Simulation Systems -> Authoritative Components/State -> Output
```

Rendering must consume simulation results rather than become the source of gameplay truth.

### Data Configuration

Data Configuration is a shared configuration/data-contract layer. UI may select or modify permitted configuration data; Core reads the configuration required to initialize and simulate gameplay data; Unity reads presentation configuration such as image/quality settings, animation, audio, visual effects, and other presentation parameters.

Configuration data is not authoritative runtime gameplay state.

### Project Work Breakdown

The project is divided into:

```text
GAME
|
+-- CORE SIMULATION
+-- UNITY RENDER LAYER
+-- UI LAYER
+-- DATA CONFIGURATION
```

**Core Simulation** is responsible for ECS, storage, Intent, character/ball simulation, trajectory, detection, actions, gameplay rules, rally/match state, simulation tick, and simulation data. It is not responsible for rendering, animation presentation, UI, camera, VFX, or Unity GameObjects.

**Unity Render Layer** is responsible for Core integration, Input Bridge, rendering, animation, character/ball presentation, environment, camera, presentation, runtime handling, and build/release. It must not decide gameplay rules or authoritative simulation state.

**UI Layer** is responsible for interface, match/configuration selection, event/progress/information display, results, and user operations. It must not directly manipulate ECS or gameplay state.

**Data Configuration** provides shared configuration used by UI, Core, and Unity Render Layer. The layer is a data/configuration source and contract, not a replacement for the authoritative simulation state.

### Current Core status and next work

Currently established in the Core include ECS Foundation, Component Storage, Intent, Character Simulation, Ball Simulation, Trajectory, Detection, Gameplay Action, Rally/Match State, Logging, and Single Match support.

Next Core work includes Match Abstraction, Multi-Match, MatchIndex, complete multi-match data layout, Frame Buffer, Input Queue, Output Queue, Server Simulation, Network Integration, and Performance Benchmark. Future directions include AI Runtime, AI Training, Large-scale Simulation, and Server Scale.

### Release scope

Release 1 focuses on a playable game using the existing Core, Unity Render Layer, UI Layer, and required Data Configuration. Multi-Match Server, N-2/N-4 server execution, Multi-client Server, large-scale simulation, and full AI training are not required for Release 1.

### Review rule

Do not confuse **implemented**, **next development**, and **future architecture**. Also do not treat fast test/prototype helpers as final runtime abstractions without first checking their intended scope.

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
