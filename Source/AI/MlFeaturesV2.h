#pragma once

#include "MlFeaturesV1.h"

// Tính 27 feature của schema v2 cho model AI = 21 feature của v1 + 6 feature về CHUỖI PHỐI HỢP.
//
// ĐÂY LÀ BẢN CHÉP TAY của `_build_v2()` trong src/spike_ai/features.py (repo AI). Giống hệt
// bên Python, nó gọi lại bản v1 rồi chỉ THÊM 6 feature vào cuối - nhờ vậy 21 feature đã được
// đối chiếu khớp 0/21 với Python trên 3512 frame vẫn nguyên, không phải kiểm lại từ đầu.
//
// VÌ SAO CÓ v2: đo ngày 12/10/2026, chuỗi Set -> Jump -> Spike xuất hiện 0 lần trong game ở
// cả 3 lần chạy. Nguyên nhân không phải tích hợp mà là feature: model là stateless (mỗi frame
// chỉ thấy các số của frame đó) nên không có cách nào biết "mình vừa Set xong". Schema v1 chỉ
// có last_touch_me (AI chạm) và touch_count (mấy lần), KHÔNG có LOẠI cú.
//
// Golden test trong header model CHỈ kiểm model (cùng vector vào -> cùng xác suất ra), KHÔNG
// kiểm cách tính feature. Phải tự đối chiếu số liệu: bật DatasetLogConfig::DUMP_ML_FEATURES
// rồi so với features.build() trên cùng frame (xem docs/integration.md).
namespace MlFeaturesV2
{
    constexpr int kNumFeatures = MlFeaturesV1::kNumFeatures + 6;  // 21 + 6 = 27
    static_assert(kNumFeatures == 27, "schema v2 phai co dung 27 feature");

    // Lấy từ schema/feature_spec.v2.json mục "constants".
    constexpr float MIN_Y = 265.0f;

    // Thứ tự BẮT BUỘC khớp kFeatureNames trong header model đã export: 21 index của v1
    // giữ nguyên giá trị, 6 index mới nối tiếp.
    enum Index : int
    {
        MY_LAST_SET = MlFeaturesV1::kNumFeatures,  // = 21
        MY_LAST_BUMP,
        MY_LAST_SPIKE,
        MY_LAST_SERVE,
        OPP_LAST_SPIKE,
        SELF_AIRBORNE,
    };

    // Điền 27 feature vào `out` theo góc nhìn của `self`. Trả về false nếu storage chưa sẵn
    // sàng - lúc đó KHÔNG được gọi model vì feature sẽ là rác.
    bool Build(ComponentStorage* storage, GameConfig::Entity self, float* out);
}  // namespace MlFeaturesV2
