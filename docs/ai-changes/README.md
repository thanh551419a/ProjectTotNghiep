# Thay đổi trong code game cho phần AI

Tài liệu cho các bạn làm **backend** (logic, ECS, hệ thống mô phỏng) và **frontend** (render,
UI) nắm được phần AI đã sửa gì trong game, và **những chỗ nếu sửa thì phải báo cho người làm AI**.

Cập nhật: 23/10/2026. Nhánh: `feat/dong`. Commit gốc trước khi làm AI: `e09bfa2`.
Repo AI đi kèm: `duydong0201/ProjectTotNghiep-AI`.

---

## 1. Tóm tắt trong 5 dòng

1. Game **ghi dữ liệu** mỗi frame vào `Logs/v1/*.csv` để train AI (`DatasetLogger`).
2. Bot sân phải có thể do **model AI** điều khiển (`AIInputSystem` + file model `Source/AI/Generated/*.h`).
3. Có thêm chế độ **bot đấu bot** và **người giành quyền điều khiển bot** (DAgger) để thu dữ liệu.
4. **Luật chơi không đổi** (số chạm, lỗi, quỹ đạo, tính điểm), trừ một lỗi khởi tạo trận đã sửa (mục 4).
5. Log debug cũ `Logs/<thời gian>.csv` đã **tắt**.
6. **Model đang dùng (23/10/2026): v10 vòng 2** - học từ chuyên gia ghi điểm qua vòng tự học. Đấu bot luật
   100 trận: thắng **100/100**, **80,3%** số điểm. Model **tự quyết có nhảy** sau Set (vật lý chọn frame).
7. Có thêm `MatchRunner.exe` (bot tự chơi không cửa sổ) và **chuyên gia** (`ExpertDecide`) chỉ dùng cho vòng
   tự học - không ảnh hưởng `MyGame.exe`.

---

## 2. Ảnh hưởng tới từng nhóm

### Frontend (render, UI)

Gần như không ảnh hưởng. Chỉ có thay đổi để biên dịch được với bản Axmol mới:

| File | Thay đổi |
|---|---|
| `Source/Utils/AxmolCompat.h` (mới) | Định nghĩa lại `Color4F` / `Color4B` trên `ax::Color` / `ax::Color32` |
| `Source/UI/DrawBigRect.cpp`, `Source/Utils/Draw8Rect.h` | `#include "AxmolCompat.h"` |
| `Source/UI/RectSystem.h`, `Source/Utils/TrajectoryDebugDraw.h` | `#include "ui/CocosGUI.h"` |
| `Source/System/AppDelegate.*` | `initGfxContextAttrs` → `applicationWillLaunch`, `RenderViewImpl` → `RenderView`, `stop/startAnimation` → `deactivate/activate`. **Đã bỏ nhánh `FULLSCREEN`** (luôn mở cửa sổ theo `WINDOW_WIDTH x WINDOW_HEIGHT`) |
| `Source/Input/InputListener.cpp` | Callback bàn phím viết bằng lambda thay cho `AX_CALLBACK_2` |

### Backend (logic, ECS)

| File | Thay đổi | Có đổi luật chơi? |
|---|---|---|
| `ECSCore/States/BallGamePlayState.h` | `stateFrame` khởi tạo là `Reset` thay cho `Alive` | Sửa lỗi: trước đây không giao được quả đầu trận |
| `ECSCore/States/RallyState.h` | Thêm `lastTouchKind` (loại cú chạm cuối) | Không - chỉ ghi nhận, không luật nào đọc |
| `System/Trajectory/RallyStateUtils.*` | `UpdateRallyTouch(..., ActionState kind)` thêm tham số `kind` | Không |
| `System/Trajectory/UpdateTrajectoryFromIntent.cpp` | Truyền loại cú vào `UpdateRallyTouch`; **chỉ ở chế độ bot đấu bot**: ngẫu nhiên hoá lực quả giao (`RandomizeServe`) | Không ở chế độ thường |
| `System/AIInputSystem.*` | Viết lại gần hết: chạy được cho **cả hai phía sân**, gọi model AI, các luật chặn (mục 5) | Bot ML được tối đa 3 chạm (`MAX_TOUCH_PER_TEAM`) như người; bot luật giữ 1 chạm |
| `Utils/GenerateIntent.*` | Tạo 2 `AIInputSystem` (trái + phải); chế độ bot đấu bot; người giành quyền sân phải (`UpdateTakeover`) | Không |
| `System/Player2InputSystem.*` | Thêm `AnyKeyHeld()` | Không |
| `System/MainScene.*` | Gọi `DatasetLogger` mỗi frame; tắt log debug cũ | Không |
| `System/DatasetLogger.*` (mới) | Ghi `Logs/v1/<thời gian>_<ID>.csv`, một dòng mỗi frame | Không |
| `Config/Match/MatchRuleConfig.h` | Các công tắc ở mục 3 | Không |
| `Config/Match/DatasetLogConfig.h` (mới) | Cấu hình ghi log | Không |
| `AI/MlFeaturesV1.*`, `AI/MlFeaturesV2.*` (mới) | Tính 21 (v1) / 27 (v2) feature từ `ComponentStorage` cho model | Không |
| `AI/Generated/spike_ai_action_random_forest.h` (mới, ~20 MB) | File model, **sinh tự động** từ repo AI | Không |
| `Config/Match/RunMode.h` (mới) | Chế độ chạy đọc lúc chạy (tham số MatchRunner / biến môi trường), thay hằng số | Không - mặc định = game như cũ |
| `Headless/MatchRunner.cpp` (mới) | Bot luật vs bot ML không cửa sổ cho vòng tự học (mục 3b) | Không |
| `System/AIInputSystem.*` - phần chuyên gia | `ExpertDecide`, `BestSpike`, `SimulateAttack`: chuyên gia ghi điểm, chỉ chạy khi `--expert-prob > 0` | Không - game chính không bật |

---

## 3. Công tắc cấu hình

`Source/Config/Match/MatchRuleConfig.h`:

| Hằng số | Giá trị hiện tại | Ý nghĩa |
|---|---|---|
| `DAGGER_SESSION` | `false` | `true` = buổi thu dữ liệu DAgger: sân trái bot luật, sân phải bot ML, người chạm phím Player 2 là giành quyền sân phải |
| `TAKEOVER_GRACE_FRAMES` | `20` | Số frame chờ trước khi trả quyền cho bot (sau khi lượt bóng của người kết thúc) |
| `ENABLE_BOT_VS_BOT` | `= DAGGER_SESSION` | `true` = hai bot tự đánh (sân trái là bot luật) |
| `ML_AGENT_ENTITY` | `3` | Nhân vật dùng model: `-1` tắt, `0` sân trái, `3` sân phải |

`Source/Config/Match/DatasetLogConfig.h`:

| Hằng số | Giá trị | Ý nghĩa |
|---|---|---|
| `ENABLE` | `true` | Ghi dataset vào `Logs/v1` |
| `PLAYER_ID` / `OPPONENT_ID` | `P01` / `BOT` (DAgger: `BOTRULE` / `P01`) | ID ghi vào log và tên file |
| `SUBDIR` | `v1` | Thư mục con trong `Logs/` |
| `DUMP_ML_FEATURES` | `false` | Ghi thêm file `_feat.csv` để đối chiếu feature với Python |
| `ENABLE_LEGACY_EVENT_LOG` | `false` | Log debug cũ `Logs/<thời gian>.csv` (7 cột, định dạng v0) |

**Chơi bình thường (người vs bot ML):** để nguyên như bảng trên, chạy `run.bat`.

**Biến môi trường** (đọc lúc khởi động `MyGame.exe`, không cần build lại):

| Biến | Ý nghĩa |
|---|---|
| `SPIKE_BOT_VS_BOT=1` | Xem bot luật (trái) đấu bot ML (phải); log vào `Logs/auto/xem_truc_tiep`, không trộn `Logs/v1`. Hoặc chạy `run_botvsbot.bat` |
| `SPIKE_MODEL_DECIDES_JUMP=0` | Tắt "model tự quyết nhảy" (mặc định **bật**), quay về rút thăm |
| `SPIKE_JUMP_AFTER_SET=0.314` | Xác suất nhảy sau Set khi đã tắt model tự quyết nhảy |

---

## 3b. MatchRunner - bot tự chơi không cửa sổ (vòng tự học)

`MatchRunner.exe` là chương trình **riêng**, không thay `MyGame.exe`:

```text
build\RelWithDebInfo\MatchRunner.exe --out <thư mục log> --matches 20 --max-frames 54000 --seed 1
```

- Chạy bot luật (sân trái) đấu bot ML (sân phải), không vẽ, nhanh khoảng 1.000 lần thời gian thật
  (20.000 frame trong 0,3 giây). Ghi log y như game (`DatasetLogger`), `o_player_id = MLAUTO`,
  `game_version = <commit>-runner`.
- Dùng model ở `Source/AI/GeneratedAuto/` (đã `.gitignore`, do repo AI ghi đè trong vòng tự học);
  chưa có thì dùng model của game chính.
- Vòng lặp mỗi frame trong `Headless/MatchRunner.cpp` **chép đúng** `MainScene::update()`. Sửa
  `MainScene::update()` thì phải sửa theo, nếu không dữ liệu tự sinh sẽ khác game thật.

Tham số chính của MatchRunner (đầy đủ: chạy không tham số để xem cách dùng):

| Tham số | Ý nghĩa |
|---|---|
| `--expert-prob P` | Tỉ lệ lượt cầm bóng của bot ML giao cho **chuyên gia** (DAgger tự động); log ghi `o_controller = 3` |
| `--expert-quick-spike P`, `--expert-ground-after-set P`, `--expert-air-strong 0/1`, `--expert-far-dist D` | Các kiểu đánh của chuyên gia (đập ngay, đập từ đất, ưu tiên SpikeStrong, quy ước theo khoảng cách lưới) |
| `--model-decides-jump 0/1` | Model tự quyết nhảy (mặc định 1) |
| `--jump-after-set P`, `--jump-far-*` | Luật rút thăm nhảy (khi tắt model tự quyết nhảy) |
| `--safe-spike-mask 0/1`, `--safe-rule-attack 0/1` | Luật chặn cú đập an toàn (mặc định 1) |

**Chế độ chạy giờ là `RunMode`** (`Source/Config/Match/RunMode.h`), đọc lúc chạy thay vì hằng số.
Giá trị mặc định lấy đúng từ `MatchRuleConfig` / `DatasetLogConfig`, nên `MyGame.exe` vẫn là
người vs bot ML, không cần sửa cờ và build lại khi muốn cho bot tự chơi.

## 4. Luồng dữ liệu giữa game và AI

```text
Game (C++)                                      Repo AI (Python)
-----------                                      ----------------
DatasetLogger --> Logs/v1/*.csv  ------------->  train.py (học từ dữ liệu người chơi)
                                                       |
AIInputSystem <-- Source/AI/Generated/*.h  <----  export_cpp.py (sinh file .h)
     ^
     +-- MlFeaturesV1 tính 21 feature, PHẢI giống features.py bên AI
```

Khi khởi động, game chạy `RunGoldenTest()` trong file model: nếu kết quả C++ lệch Python thì
log `[MLAction] GOLDEN TEST FAIL` và bot chỉ chơi bằng luật.

---

## 5. Bot ML ra quyết định thế nào (`AIInputSystem`)

Mỗi frame, nếu nhân vật không đang trong cooldown:

1. **Luật gốc** quyết định di chuyển và cú đánh như bot cũ.
2. **Model** tính xác suất các hành động. Nếu `P(ra tay) = 1 - P(None) > 0,3` thì rút thăm một hành
   động theo xác suất (có làm phẳng, temperature 3).
3. **Luật chặn** loại các đề xuất sai: không xoá hành động của luật, cú đập phải **qua lưới và rơi trong
   sân cách vạch cuối ít nhất một bước bay** (`safeSpikeMask`, áp cả cho cú đập luật gốc chọn cho bot ML -
   `safeRuleAttack`), chỉ chạm bóng khi bóng trong vùng detect, chỉ Jump sau cú Set/Bump của mình, chỉ
   Serve đúng lượt.
4. **Nhảy đập sau cú Set**: **model quyết có nhảy** (chỉ cần một frame P(Jump) > 0,3 sau cú Set); **thời
   điểm** nhảy do mô phỏng vật lý. Model chọn cú đập lúc ở trên không. (Trước 23/10: rút thăm 31,4%.)

Đo bot luật vs bot ML (cùng model v3, seed 400000): rút thăm 31,4% → 72,6% điểm; model tự quyết nhảy →
93,8%. Model v10 vòng 2 (đang dùng) đổi một phần sức mạnh lấy lối chơi đa dạng (đập ngay ~62 lần/trận): 80,3%.

Di chuyển vẫn do luật, model chỉ quyết định hành động.

---

## 6. ⚠️ Nếu sửa những chỗ này, PHẢI báo người làm AI

`AIInputSystem` và `MlFeaturesV1` **chép lại công thức** của game để dự đoán. Sửa bên game mà
không sửa theo thì bot dự đoán sai một cách âm thầm, không có lỗi biên dịch nào báo.

| Nếu sửa | Thì phải sửa theo |
|---|---|
| Thông số quỹ đạo cú đánh trong `UpdateTrajectoryFromIntent.cpp` | `AIInputSystem::AttackOptions()` |
| Vùng detect trong `DetectPlayerBall()` (`TrajectoryMath.cpp`), `WIDTH/HEIGHT_PERCENT_CHANGE`, ngưỡng 50 / `DISTANCE_DETECTION_*` | `AIInputSystem::DetectPercentAt`, `BallInDetectZone` |
| Cơ chế nhảy: `JUMP_FRAME`, `GRAVITY`, `MIN_Y`, `ApplyIntentToComponent.h`, `JumpStartSystem` | `AIInputSystem::FramesUntilAirContact` |
| Kích thước nhân vật / bóng, `NET_X`, vạch biên sân | `MlFeaturesV1`, `SpikeLandsInCourt`, `BestSpike`, schema bên repo AI |
| `MainScene::update()` | `Headless/MatchRunner.cpp` (chép đúng vòng lặp mỗi frame) |
| Enum `FinalIntent`, `ActionState`, thứ tự entity (`GameConfig`) | `schema/feature_spec.v1.json` bên repo AI |
| Cột hoặc thứ tự ghi trong `DatasetLogger`, chỗ gọi `LogFrame` trong `MainScene::update` | Toàn bộ dữ liệu đã thu - **không đổi** nếu chưa bàn với người làm AI |

---

## 7. Danh sách commit và cách quay lại

| Commit | Nội dung |
|---|---|
| `22234fe` | Tương thích bản Axmol mới |
| `3e91271` | Bóng bắt đầu ở trạng thái Reset |
| `2e57741` | Công tắc DAgger, bot đấu bot, `ML_AGENT_ENTITY` |
| `25d1ce1` | Ghi loại cú chạm cuối; ngẫu nhiên quả giao khi bot đấu bot |
| `f3ae63b` | Bot ML bản trước hướng A+C (model `played_v1_noweight`) |
| `184ab9a` | Bot đấu bot + người giành quyền bằng phím Player 2 |
| `00e3923` | `DatasetLogger` ghi `Logs/v1` |
| `c02b6f6` | Bot ML hướng A+C (ngưỡng 0,3, nhảy theo vật lý, model `played_v1_aligned`) |
| `dcff1de` | Tắt log debug cũ |
| `61ffe62` | Cập nhật `Logs/` theo dữ liệu hiện tại |
| `6e318b8` | Model `dagger_v1_w_aligned`: train thêm dữ liệu DAgger (người giành quyền sân phải), trọng số ×20 |
| `107a4ce`, `32bdd1e`, `9e90828` | `RunMode`, `MatchRunner`, ngẫu nhiên theo seed |
| `566eca5`, `00ec4b1` | Chuyên gia ghi điểm (vòng tự học) |
| `461a455`, `98f8655` | Luật chặn cú đập an toàn cho bot ML |
| `d11ea0e` | Model v3 (`selfplay_expert_v3`) vào game chính |
| `f90f43b`, `1f36538`, `feeb5f9`, `9c85077` | Tuỳ chọn thử nghiệm (đập nhanh, luật nhảy, model tự quyết nhảy) - mặc định không đổi game |
| `2cc342c` | **Model v10 vòng 2** vào game chính, mặc định model tự quyết nhảy |

Quay lại một phần: `git revert <commit>`. Ví dụ quay về model v3 + rút thăm 31,4%: `git revert 2cc342c`.

Chi tiết lý do từng quyết định nằm ở repo AI: `docs/decisions/adr-*.md` (đặc biệt ADR-011 về
thứ tự ghi log và ADR-012 về cách bot nhảy) và `reports/experiments.md`.
