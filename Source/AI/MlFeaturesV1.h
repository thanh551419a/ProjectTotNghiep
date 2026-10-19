#pragma once

#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Config/Match/MatchEntityConfig.h"
#include "../Config/Match/BigRect.h"

// Tính 21 feature của schema v1 cho model AI.
//
// ĐÂY LÀ BẢN CHÉP TAY của `_build_v1()` trong src/spike_ai/features.py (repo AI). Hai bản
// phải cho ra SỐ GIỐNG NHAU và ĐÚNG THỨ TỰ, nếu lệch thì model vẫn chạy mà đoán sai âm thầm,
// không có lỗi nào báo ra.
//
// Golden test trong header model CHỈ kiểm tra model (cùng vector vào -> cùng xác suất ra),
// KHÔNG kiểm tra cách tính feature. Nên chỗ này phải tự kiểm bằng cách đối chiếu số liệu:
// ghi feature từ C++ ra log rồi so với features.build() trên cùng frame (xem docs/integration.md).
//
// Hằng số lấy từ schema/feature_spec.v1.json mục "constants".
namespace MlFeaturesV1
{
    constexpr int kNumFeatures = 21;

    constexpr float NET_X                 = 1370.0f;
    constexpr float LANDING_X_VALID_BELOW = 9000.0f;

    // Thứ tự BẮT BUỘC khớp kFeatureNames trong header model đã export.
    enum Index : int
    {
        SELF_X = 0,
        SELF_Y,
        OPP_X,
        SELF_DIST_TO_NET,
        BALL_DX,
        BALL_Y,
        BALL_VX,
        BALL_A,
        BALL_C,
        BALL_VERTEX_DX,
        LANDING_VALID,
        LANDING_DX,
        LANDING_ON_MY_SIDE,
        BALL_ALIVE,
        TOUCH_COUNT,
        LAST_TOUCH_ME,
        LAST_TOUCH_OPP,
        CAN_ACT,
        ACTION_REMAIN_MS,
        MY_SERVE,
        SCORE_DIFF,
    };

    // Điền 21 feature vào `out` theo góc nhìn của `self`. Trả về false nếu storage chưa sẵn
    // sàng (đang khởi tạo) - lúc đó KHÔNG được gọi model vì feature sẽ là rác.
    bool Build(ComponentStorage* storage, GameConfig::Entity self, float* out);
}  // namespace MlFeaturesV1
