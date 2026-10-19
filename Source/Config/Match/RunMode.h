#pragma once

#include "DatasetLogConfig.h"
#include "MatchRuleConfig.h"

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
};

namespace RunMode
{
// Một thể hiện duy nhất cho cả chương trình (inline + static cục bộ, C++17).
inline RunModeData& Get()
{
    static RunModeData data;
    return data;
}
}  // namespace RunMode
