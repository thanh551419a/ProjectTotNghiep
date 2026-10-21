#pragma once

#include "DatasetLogConfig.h"
#include "MatchRuleConfig.h"

#include <cstdlib>

// CHẾ ĐỘ CHẠY, đọc lúc chạy thay vì hằng số lúc biên dịch.
//
// Vì sao: trước đây đổi giữa "người vs bot" và "bot vs bot" phải sửa MatchRuleConfig rồi build lại,
// và cái exe đang chơi bị đổi theo. Giờ:
//   - MyGame.exe dùng GIÁ TRỊ MẶC ĐỊNH dưới đây = đúng các hằng số trong MatchRuleConfig /
//     DatasetLogConfig, nên chơi y như cũ (mặc định: người vs bot ML).
//   - MatchRunner.exe (Headless/MatchRunner.cpp, chạy không cửa sổ) ghi đè các trường này
//     theo tham số dòng lệnh TRƯỚC khi tạo bất kỳ hệ thống nào.
//
// Không có luật chơi nào ở đây - chỉ là ai điều khiển nhân vật nào và ghi log ra sao.
struct RunModeData
{
    bool botVsBot      = MatchRuleConfig::ENABLE_BOT_VS_BOT;  // sân trái do bot luật điều khiển
    bool dagger        = MatchRuleConfig::DAGGER_SESSION;     // người giành quyền sân phải bằng phím Player 2
    int mlAgentEntity  = MatchRuleConfig::ML_AGENT_ENTITY;    // nhân vật dùng model: -1 tắt, 0 trái, 3 phải
    unsigned int seed  = 20261010u;                           // seed rút thăm của bot ML (AIInputSystem)
    const char* playerId   = DatasetLogConfig::PLAYER_ID;     // ID ghi vào log, cột p_player_id
    const char* opponentId = DatasetLogConfig::OPPONENT_ID;   // cột o_player_id

    // Chuyên gia ghi điểm (AIInputSystem::ExpertDecide). Xác suất mỗi lượt cầm bóng của bot ML
    // được giao cho chuyên gia. 0 = tắt (MyGame luôn 0); MatchRunner đặt qua --expert-prob.
    float expertProb = 0.0f;
    // Đa dạng cách ghi điểm của chuyên gia: tỉ lệ cú Set xong KHÔNG nhảy (đập từ mặt đất), và có rút thăm
    // giữa các cú đập chắc ăn hay không. MatchRunner: --expert-ground-after-set P, --expert-varied 0|1.
    float expertGroundAfterSet = 0.25f;
    bool expertVaried          = true;
    // Frame hiện tại sân bot ML có đang do chuyên gia điều khiển không - AIInputSystem ghi,
    // DatasetLogger đọc để ghi o_controller = Expert (3).
    bool expertNow = false;

    // Luật nhảy của bot ML (AIInputSystem, ADR-012 repo AI). Mặc định = cách bắt chước người chơi:
    // sau cú Set của mình nhảy với xác suất 31,4%, model không tự đề xuất Jump.
    // MatchRunner đổi được qua --jump-after-set P và --model-may-jump 0|1 để đo đối chứng.
    float jumpAfterSetProb = 0.314f;
    bool modelMayJump      = false;
    // Luật chặn cú đập của bot ML: true = kiểm tra qua lưới + cách vạch cuối một bước bay (đã sửa 21/10),
    // false = cách cũ. MatchRunner: --safe-spike-mask 0|1 để đo đối chứng.
    bool safeSpikeMask     = true;
    // Cú đập do LUẬT GỐC chọn (PickAttackIntent) cho bot ML cũng dùng phép kiểm tra đã sửa. Bot luật không đổi.
    // MatchRunner: --safe-rule-attack 0|1.
    bool safeRuleAttack    = true;

    // Thư mục con trong Logs/ để ghi dataset. Xem bot-vs-bot có cửa sổ thì ghi riêng, không trộn
    // vào Logs/v1 (dữ liệu người chơi).
    const char* logSubdir = DatasetLogConfig::SUBDIR;
};

namespace RunMode
{
// Đọc biến môi trường MỘT LẦN lúc khởi động, để mở MyGame ở chế độ khác mà không sửa code / build lại
// (run_botvsbot.bat). Không đặt biến nào thì giữ nguyên mặc định = game như cũ (người vs bot ML).
//   SPIKE_BOT_VS_BOT=1        sân trái là bot luật, xem hai bot đánh nhau; log vào Logs/auto/xem_truc_tiep
//   SPIKE_JUMP_AFTER_SET=1.0  xác suất bot ML nhảy sau cú Set của mình (mặc định 0,314)
inline RunModeData FromEnvironment()
{
    RunModeData data;
#pragma warning(suppress : 4996)
    const char* botVsBot = std::getenv("SPIKE_BOT_VS_BOT");
    if (botVsBot && botVsBot[0] == '1')
    {
        data.botVsBot   = true;
        data.playerId   = "BOTRULE";
        data.opponentId = "BOT";
        data.logSubdir  = "auto/xem_truc_tiep";
    }
#pragma warning(suppress : 4996)
    const char* jump = std::getenv("SPIKE_JUMP_AFTER_SET");
    if (jump && jump[0] != '\0')
        data.jumpAfterSetProb = static_cast<float>(std::atof(jump));
    return data;
}

// Một thể hiện duy nhất cho cả chương trình (inline + static cục bộ, C++17).
inline RunModeData& Get()
{
    static RunModeData data = FromEnvironment();
    return data;
}
}  // namespace RunMode
