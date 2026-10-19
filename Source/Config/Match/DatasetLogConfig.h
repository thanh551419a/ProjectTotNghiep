#pragma once

#include "MatchRuleConfig.h"

// Cấu hình ghi dataset cho việc huấn luyện AI (Source/System/DatasetLogger.cpp).
namespace DatasetLogConfig
{
    // Bật/tắt việc ghi dataset. Tắt thì game chạy y như cũ, không tạo file nào.
    constexpr bool ENABLE = true;

    // ID ẩn danh của người đang chơi. ĐỔI TRƯỚC MỖI BUỔI THU DỮ LIỆU với người mới
    // (P01, P02, ...). Phía AI dùng nó cho chế độ đánh giá "train trên người A, test trên
    // người B" (dev_mode: lopo), nên nếu mọi buổi đều để P01 thì mất khả năng đó.
    // Không dùng tên thật - dữ liệu sẽ nằm trong báo cáo.
    //
    // Ở buổi DAgger sân trái là BOT LUẬT nên ghi "BOTRULE", còn người chơi chuyển sang
    // sân phải. Cột p_player_id / o_player_id chỉ là nhãn cho cả file; ai điều khiển ở
    // TỪNG frame thì xem cột p_controller / o_controller (phía AI lọc theo cột đó).
    constexpr const char* PLAYER_ID = MatchRuleConfig::DAGGER_SESSION ? "BOTRULE" : "P01";

    // Nhân vật sân phải: bình thường là bot; ở buổi DAgger là người chơi (khi giành quyền).
    constexpr const char* OPPONENT_ID = MatchRuleConfig::DAGGER_SESSION ? "P01" : "BOT";

    // Ghi thêm một file <match_id>_feat.csv chứa 21 feature do C++ tính, để đối chiếu với
    // features.build() của Python trên cùng frame. Golden test chỉ kiểm MODEL, không kiểm
    // cách tính FEATURE - lệch feature thì model vẫn chạy mà đoán sai âm thầm.
    // Chỉ bật khi cần kiểm chứng, vì nó làm log nặng gần gấp đôi.
    // Đã kiểm chứng 09/10/2026: khớp 21/21 feature (schema v1) trên 3512 frame.
    // Đã kiểm chứng 14/10/2026: khớp 27/27 feature (schema v2) trên 8.979 frame.
    // Cả hai lần đều đối chiếu cho CẢ HAI phía sân, nên kiểm luôn phép lật qua lưới.
    // Bật lại khi nào sửa MlFeaturesV2 hoặc đổi schema.
    constexpr bool DUMP_ML_FEATURES = false;

    // Thư mục con trong Logs/. Để riêng với log debug cũ (ghi thẳng vào Logs/).
    //
    // GIỮ "v1" theo yêu cầu: toàn bộ dữ liệu người chơi nằm trong MỘT thư mục.
    // Thư mục này giờ chứa LẪN hai định dạng - log ghi trước 12/10/2026 là schema v1
    // (36 cột), từ đó trở đi là v2 (37 cột). Phía AI xử lý được: load_dirs() tự chuyển
    // mỗi file sang schema đích và in ra số file đã chuyển (xem data.py::adapt_version).
    constexpr const char* SUBDIR = "v1";
}  // namespace DatasetLogConfig
