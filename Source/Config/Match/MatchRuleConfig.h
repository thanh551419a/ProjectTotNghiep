#pragma once
namespace MatchRuleConfig
{
    constexpr int MAX_TOUCH_PER_TEAM      = 3;
    constexpr int MAX_TOUCH_PER_CHARACTER = 1;

    constexpr int SCORE_TO_WIN = 15;
    constexpr bool ENABLE_BOT          = true;

    // ===== DAgger (người giành quyền điều khiển) =====
    // MỘT CÔNG TẮC DUY NHẤT cho buổi thu dữ liệu DAgger. Bật thì:
    //   - sân TRÁI do bot LUẬT tự đánh (ENABLE_BOT_VS_BOT tự bật theo)
    //   - sân PHẢI do bot ML đánh, NHƯNG người chơi chạm vào bất kỳ phím Player 2 nào
    //     (mũi tên, phím /, Shift phải, phím ; và ' và Enter) thì giành quyền điều khiển
    //     sân phải ngay lập tức
    //   - logger ghi o_controller = 0 (Human) đúng ở những frame người đang điều khiển
    //   - PLAYER_ID / OPPONENT_ID trong log tự đổi theo (xem DatasetLogConfig.h)
    //
    // Vì sao cần: model học từ tình huống NGƯỜI gặp, nhưng bot rơi vào tình huống KHÁC
    // (vị trí của bot do luật điều khiển). Những frame người giành quyền là hành động của
    // người ở ĐÚNG tình huống bot tự đưa mình vào - đó là nhãn DAgger.
    //
    // CHƠI BÌNH THƯỜNG (người vs bot ML) thì đặt lại false.
    constexpr bool DAGGER_SESSION = false;

    // Người giữ quyền SUỐT lượt bóng của mình: chừng nào bóng còn sống ở sân phải, hoặc
    // đang chờ sân phải giao bóng, thì không trả quyền dù không bấm phím (đứng chờ bóng
    // rơi sau cú Set là một quyết định, phải ghi là của người). Lượt xong rồi - bóng đã
    // sang sân trái hoặc pha bóng kết thúc - mới đếm thêm ngần này frame (~1/3 giây ở
    // 60 fps) rồi trả cho bot. Xem GenerateIntent::UpdateTakeover.
    constexpr int TAKEOVER_GRACE_FRAMES = 20;

    // Bot điều khiển luôn nhân vật bên TRÁI (thay cho người chơi) -> hai bot tự đánh với nhau.
    // Dùng để tự sinh dữ liệu huấn luyện mà không cần người ngồi chơi. Cần ENABLE_BOT = true.
    // Khi bật, sân trái trong log là hành vi của BOT. Ghi chung thư mục với dữ liệu người
    // chơi vẫn an toàn: logger ghi p_controller / o_controller từng frame, và phía AI chỉ
    // train trên frame có controller = Human (train.py::build_rows, only_human).
    // Tự bật khi DAGGER_SESSION. Muốn bot-vs-bot thuần (không người) thì đặt true ở đây.
    constexpr bool ENABLE_BOT_VS_BOT   = DAGGER_SESSION;
    // Nhân vật nào dùng model AI để quyết định hành động. Dùng chỉ số entity thay vì
    // GameConfig::Entity để không phải include thêm header vào file config này.
    //   -1 = tắt model, cả hai bên chơi theo luật
    //    0 = PLAYER (sân trái)
    //    3 = OPPONENT_1 (sân phải)
    // Để đo tỉ lệ thắng ML-bot vs rule-bot: đặt 3 rồi bật ENABLE_BOT_VS_BOT -> sân phải dùng
    // model, sân trái dùng luật, hai bên đánh nhau tự động.
    constexpr int ML_AGENT_ENTITY = 3;

    constexpr bool ENABLE_DOUBLE_TOUCH = true;
    constexpr bool ENABLE_NET_FAULT    = true;
}  // namespace MatchRuleConfig
