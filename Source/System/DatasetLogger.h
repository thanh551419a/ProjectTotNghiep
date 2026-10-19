#pragma once

#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../Config/Match/MatchEntityConfig.h"
#include <fstream>
#include <string>

// Phiên bản game, để phía AI lọc dữ liệu theo build. Nhúng lúc build bằng CMake:
//   execute_process(COMMAND git rev-parse --short HEAD OUTPUT_VARIABLE GIT_SHA ...)
//   target_compile_definitions(${APP_NAME} PRIVATE GAME_VERSION="${GIT_SHA}")
#ifndef GAME_VERSION
#    define GAME_VERSION "unknown"
#endif

// Ai điều khiển nhân vật. Giá trị phải khớp enum "Controller" trong
// schema/feature_spec.v1.json của repo AI.
enum class ControllerKind : int
{
    Human   = 0,
    RuleBot = 1,
    MlBot   = 2,
};

// Ghi dataset cho việc huấn luyện AI, theo schema v1 (schema/feature_spec.v1.json).
//
// KHÁC với _logFile cũ của MainScene: log cũ ghi theo SỰ KIỆN (mỗi nhân vật có intent một
// dòng, nhân vật còn lại bị ghi cứng thành "None"), nên 19,5% dòng mang nhãn sai và 45%
// frame vắng mặt - phía AI phải vá bằng rebuild_frames_v0. Lớp này ghi ĐÚNG MỘT DÒNG MỖI
// FRAME, kể cả khi không ai bấm gì, nên không cần vá.
//
// Thời điểm gọi LogFrame rất quan trọng: SAU khi mọi Intent của frame đã sinh ra
// (GenerateIntent::update) và TRƯỚC ApplyIntentToComponent. Nhờ vậy state trong dòng log là
// state *lúc ra quyết định*, còn các cột *_input_* là *quyết định* đó. Log cũ ghi sau khi đã
// áp dụng intent nên feature bị lệch một frame.
class DatasetLogger
{
public:
    DatasetLogger(ComponentStorage* componentStorage, IntentStorage* intentStorage);
    ~DatasetLogger();

    // Mở file mới cho một trận. matchId phải duy nhất trên mọi máy (phía AI dùng nó để chia
    // tập và kiểm tra holdout không trùng trận với dữ liệu train).
    void BeginMatch(const std::string& logDirectory, const std::string& matchId);
    void EndMatch();

    // Ghi một dòng cho frame hiện tại. dtMs = bước thời gian mô phỏng của frame này.
    void LogFrame(uint64_t frame, float dtMs);

    bool IsOpen() const { return _file.is_open(); }

    // Gọi TRƯỚC LogFrame mỗi frame: sân phải frame này có phải do người điều khiển không.
    void SetOpponentHumanControlled(bool human) { _opponentHuman = human; }

private:
    void WriteHeader();

    ComponentStorage* _componentStorage = nullptr;
    IntentStorage* _intentStorage       = nullptr;
    std::ofstream _file;
    std::ofstream _featFile;  // dump 21 feature de doi chieu voi Python
    std::string _matchId;
    bool _opponentHuman = false;
};
