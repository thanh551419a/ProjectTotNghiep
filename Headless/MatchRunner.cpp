// MatchRunner: chạy trận bot luật (sân trái) vs bot ML (sân phải) KHÔNG CỬA SỔ, nhanh nhất có thể,
// và ghi log dataset y như game (schema v1, DatasetLogger). Dùng cho vòng tự học bên repo AI
// (ProjectTotNghiep-AI: python -m spike_ai.selfplay).
//
// Cách dùng:
//   MatchRunner.exe --out <thư mục log> [--matches 10] [--max-frames 54000] [--seed 1] [--tag auto]
//
// KHÔNG đổi luật chơi. Vòng lặp mỗi frame chép đúng thứ tự trong MainScene::update(), chỉ bỏ:
//   - phần vẽ (RectSystem) và nhận phím (InputListener, TestTrajectorySystem - chỉ phản ứng phím M);
//   - log debug cũ (_logFile, đã tắt ở game chính bằng ENABLE_LEGACY_EVENT_LOG = false).
// SỬA MainScene::update() THÌ SỬA THEO Ở ĐÂY - hai vòng lặp phải giống nhau, nếu không dữ liệu tự
// sinh sẽ khác với game thật.
//
// Game chính (MyGame.exe) không bị ảnh hưởng: nó dùng chế độ mặc định trong RunMode (người vs bot)
// và model ở Source/AI/Generated/. MatchRunner được build với model ở Source/AI/GeneratedAuto/.

// TrajectoryTestConfig.h phải đứng đầu: vài header của game dùng TestTrajectory mà không tự include
// (ở game chính MainScene.h include sẵn nó trước).
#include "System/TrajectoryTestConfig.h"
#include "Config/Match/MatchRuleConfig.h"
#include "Config/Match/RunMode.h"
#include "ECSCore/ComponentStorage/ComponentStorage.h"
#include "ECSCore/IntentStorage/IntentStorage.h"
#include "System/DatasetLogger.h"
#include "System/FramePreparationSystem.h"
#include "System/GameRuleEvaluationSystem.h"
#include "Utils/ApplyIntentToComponent.h"
#include "Utils/GenerateIntent.h"

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <string>

namespace
{
// Bước thời gian của game ở 60 fps. MainScene nhận delta từ Director (~1/60 s) rồi đổi sang ms.
constexpr float FRAME_DELTA_SEC = 1.0f / 60.0f;

struct Options
{
    std::string out;
    int matches            = 10;
    long long maxFrames    = 54000;  // ~15 phút; chặn pha bóng vô tận giữa hai bot
    unsigned int seed      = 1;
    std::string tag        = "auto";
    float expertProb       = 0.0f;  // xac suat giao luot cam bong cua bot ML cho chuyen gia
    float jumpAfterSet     = -1.0f; // <0: giu mac dinh cua game (0,314)
    int modelMayJump       = -1;    // <0: giu mac dinh cua game (khong)
    float expertGround     = -1.0f; // <0: giu mac dinh (0,25)
    int expertVaried       = -1;    // <0: giu mac dinh (co)
    int safeSpikeMask      = -1;    // <0: giu mac dinh (co)
    int safeRuleAttack     = -1;    // <0: giu mac dinh (co)
    float expertQuick      = -1.0f; // <0: giu mac dinh (0)
    bool hasQuickMin       = false;
    float quickMinMargin   = 0.0f;
};

bool Parse(int argc, char** argv, Options& o)
{
    for (int i = 1; i < argc; ++i)
    {
        const std::string a = argv[i];
        const bool hasValue = i + 1 < argc;
        if (a == "--out" && hasValue)
            o.out = argv[++i];
        else if (a == "--matches" && hasValue)
            o.matches = std::atoi(argv[++i]);
        else if (a == "--max-frames" && hasValue)
            o.maxFrames = std::atoll(argv[++i]);
        else if (a == "--seed" && hasValue)
            o.seed = static_cast<unsigned int>(std::strtoul(argv[++i], nullptr, 10));
        else if (a == "--tag" && hasValue)
            o.tag = argv[++i];
        else if (a == "--expert-prob" && hasValue)
            o.expertProb = static_cast<float>(std::atof(argv[++i]));
        else if (a == "--jump-after-set" && hasValue)
            o.jumpAfterSet = static_cast<float>(std::atof(argv[++i]));
        else if (a == "--model-may-jump" && hasValue)
            o.modelMayJump = std::atoi(argv[++i]);
        else if (a == "--expert-ground-after-set" && hasValue)
            o.expertGround = static_cast<float>(std::atof(argv[++i]));
        else if (a == "--expert-varied" && hasValue)
            o.expertVaried = std::atoi(argv[++i]);
        else if (a == "--safe-spike-mask" && hasValue)
            o.safeSpikeMask = std::atoi(argv[++i]);
        else if (a == "--safe-rule-attack" && hasValue)
            o.safeRuleAttack = std::atoi(argv[++i]);
        else if (a == "--expert-quick-spike" && hasValue)
            o.expertQuick = static_cast<float>(std::atof(argv[++i]));
        else if (a == "--expert-quick-min-margin" && hasValue)
        {
            o.hasQuickMin    = true;
            o.quickMinMargin = static_cast<float>(std::atof(argv[++i]));
        }
        else
        {
            std::fprintf(stderr, "Tham so khong hop le: %s\n", a.c_str());
            return false;
        }
    }
    if (o.out.empty() || o.matches <= 0 || o.maxFrames <= 0)
    {
        std::fprintf(stderr,
                     "Cach dung: MatchRunner --out <thu muc> [--matches N] [--max-frames F] [--seed S] [--tag T] [--expert-prob P]\n"
                     "  [--jump-after-set P] [--model-may-jump 0|1]\n");
        return false;
    }
    return true;
}

struct MatchResult
{
    int left        = 0;
    int right       = 0;
    long long frames = 0;
    bool finished   = false;  // có bên đạt SCORE_TO_WIN (không phải dừng vì max-frames)
};

// Một trận: dựng hệ thống như MainScene::init(), chạy vòng lặp như MainScene::update().
MatchResult PlayMatch(const Options& o, const std::string& matchId)
{
    ComponentStorage storage;
    storage.InitComponent();
    IntentStorage intents;
    GenerateIntent generateIntent(&intents, &storage);
    GameRuleEvaluationSystem ruleSystem(&storage, &intents);
    FramePreparationSystem framePreparation(&storage, &intents);
    DatasetLogger logger(&storage, &intents);
    logger.BeginMatch(o.out, matchId);

    std::ofstream legacyLog;  // không mở: các lệnh ghi log debug cũ bị bỏ qua, như game chính
    const InputListener::InputFrame noInput{};
    const float fixedTimeMs = FRAME_DELTA_SEC * 1000.0f;

    MatchResult r;
    auto* match = storage.GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    for (; r.frames < o.maxFrames; ++r.frames)
    {
        if (match->leftScore >= MatchRuleConfig::SCORE_TO_WIN || match->rightScore >= MatchRuleConfig::SCORE_TO_WIN)
        {
            r.finished = true;
            break;
        }
        // ---- giống MainScene::update() từ đây ----
        intents.clear();
        framePreparation.update();
        generateIntent.update(fixedTimeMs, noInput, &legacyLog);
        if (DatasetLogConfig::ENABLE)
        {
            logger.SetOpponentHumanControlled(generateIntent.OpponentHumanControlled());
            logger.LogFrame(testTrajectory.frame, fixedTimeMs);
        }
        ApplyIntentToComponent(&intents, &storage, &legacyLog);
        ruleSystem.update();
        // ---- hết phần giống; bỏ _rectSystem->update() (vẽ) ----
    }
    r.left  = match->leftScore;
    r.right = match->rightScore;
    logger.EndMatch();
    return r;
}
}  // namespace

int main(int argc, char** argv)
{
    Options o;
    if (!Parse(argc, argv, o))
        return 2;

    std::error_code ec;
    std::filesystem::create_directories(o.out, ec);

    // Chế độ: bot luật (trái) vs bot ML (phải), không DAgger. Phải đặt TRƯỚC khi tạo hệ thống nào.
    RunModeData& mode  = RunMode::Get();
    mode.botVsBot      = true;
    mode.dagger        = false;
    mode.mlAgentEntity = 3;
    mode.seed          = o.seed;
    mode.playerId      = "BOTRULE";
    mode.opponentId    = "MLAUTO";
    mode.expertProb    = o.expertProb;
    if (o.jumpAfterSet >= 0.0f)
        mode.jumpAfterSetProb = o.jumpAfterSet;
    if (o.modelMayJump >= 0)
        mode.modelMayJump = o.modelMayJump != 0;
    if (o.expertGround >= 0.0f)
        mode.expertGroundAfterSet = o.expertGround;
    if (o.expertVaried >= 0)
        mode.expertVaried = o.expertVaried != 0;
    if (o.safeSpikeMask >= 0)
        mode.safeSpikeMask = o.safeSpikeMask != 0;
    if (o.safeRuleAttack >= 0)
        mode.safeRuleAttack = o.safeRuleAttack != 0;
    if (o.expertQuick >= 0.0f)
        mode.expertQuickSpike = o.expertQuick;
    if (o.hasQuickMin)
        mode.expertQuickMinMargin = o.quickMinMargin;
    std::srand(o.seed);  // PickAttackIntent dùng std::rand

    std::printf("MatchRunner: %d tran, toi da %lld frame/tran, seed %u, chuyen gia %.2f, ghi vao %s\n", o.matches,
                o.maxFrames, o.seed, o.expertProb, o.out.c_str());
    int wonLeft = 0, wonRight = 0;
    for (int m = 0; m < o.matches; ++m)
    {
        char id[128];
        std::snprintf(id, sizeof(id), "%s_s%u_m%03d_%s", o.tag.c_str(), o.seed, m, mode.playerId);
        const auto t0 = std::chrono::steady_clock::now();
        const MatchResult r = PlayMatch(o, id);
        const double sec = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        wonLeft += r.left;
        wonRight += r.right;
        std::printf("  %s: bot luat %d - %d bot ML, %lld frame%s, %.1f s\n", id, r.left, r.right, r.frames,
                    r.finished ? "" : " (dung vi max-frames)", sec);
        std::fflush(stdout);
    }
    std::printf("TONG: bot luat %d - %d bot ML\n", wonLeft, wonRight);
    return 0;
}
