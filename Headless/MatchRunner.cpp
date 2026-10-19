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
        else
        {
            std::fprintf(stderr, "Tham so khong hop le: %s\n", a.c_str());
            return false;
        }
    }
    if (o.out.empty() || o.matches <= 0 || o.maxFrames <= 0)
    {
        std::fprintf(stderr,
                     "Cach dung: MatchRunner --out <thu muc> [--matches N] [--max-frames F] [--seed S] [--tag T]\n");
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
    std::srand(o.seed);  // PickAttackIntent dùng std::rand

    std::printf("MatchRunner: %d tran, toi da %lld frame/tran, seed %u, ghi vao %s\n", o.matches, o.maxFrames, o.seed,
                o.out.c_str());
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
