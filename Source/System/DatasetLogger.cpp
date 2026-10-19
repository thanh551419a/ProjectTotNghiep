#include "DatasetLogger.h"
#include "../Config/Match/RunMode.h"
#include "../Config/Match/DatasetLogConfig.h"
#include "../Config/Match/MatchRuleConfig.h"
#include "../AI/MlFeaturesV2.h"
#include <filesystem>
#include <iomanip>

namespace
{
// Thứ tự cột BẮT BUỘC khớp `raw_columns` trong schema/feature_spec.v1.json. Phía AI
// (spike_ai.schema.validate_columns) sẽ báo lỗi nếu thiếu cột, nhưng KHÔNG phát hiện được
// nếu sai thứ tự - lúc đó dữ liệu vẫn đọc được mà sai âm thầm.
constexpr const char* kHeader =
    "schema_version,match_id,frame,dt_ms,game_version,"
    "p_x,p_y,p_action_state,p_action_remain_ms,p_controller,p_player_id,"
    "o_x,o_y,o_action_state,o_action_remain_ms,o_controller,o_player_id,"
    "ball_x,ball_y,ball_traj_type,ball_speed,ball_a,ball_b,ball_c,ball_landing_x,"
    "ball_state_frame,ball_collision_state,"
    "rally_last_touch,rally_touch_count,score_left,score_right,serving_team,"
    "p_input_move,p_input_intent,o_input_move,o_input_intent,"
    // SCHEMA V2 - cột thêm ở CUỐI, không chèn vào giữa, để công cụ nào đọc v1 theo vị trí
    // vẫn đọc đúng 36 cột đầu.
    //
    // rally_last_touch_kind = LOẠI cú chạm gần nhất, theo enum ActionState
    // (0 None, 1 Jump, 2 Spike, 3 Slide, 4 Serve, 5 Bump, 6 Set, 7 SpikeLight,
    //  8 SpikeMedium, 9 SpikeStrong). CHÚ Ý: thứ tự này KHÁC enum FinalIntent dùng ở
    // cột p_input_intent/o_input_intent - đừng dùng lẫn bảng ánh xạ.
    "rally_last_touch_kind";

// Quy tắc 3 của docs/data_contract.md: nhân vật không có Intent trong frame thì ghi
// move = 0 và intent = 0 (None), KHÔNG bỏ trống dòng.
struct LoggedIntent
{
    float moveX    = 0.0f;
    int finalIntent = 0;
};

LoggedIntent IntentOf(IntentStorage* intentStorage, GameConfig::Entity entity)
{
    auto* intent = intentStorage->GetCharacterIntentPool().get(entity);
    if (!intent)
        return {};
    return {intent->moveX, static_cast<int>(intent->finalIntent)};
}
}  // namespace

DatasetLogger::DatasetLogger(ComponentStorage* componentStorage, IntentStorage* intentStorage)
{
    _componentStorage = componentStorage;
    _intentStorage    = intentStorage;
}

DatasetLogger::~DatasetLogger()
{
    EndMatch();
}

void DatasetLogger::BeginMatch(const std::string& logDirectory, const std::string& matchId)
{
    EndMatch();

    std::error_code ec;
    std::filesystem::create_directories(logDirectory, ec);

    _matchId = matchId;
    _file.open((std::filesystem::path(logDirectory) / (matchId + ".csv")).string(), std::ios::out);
    if (_file.is_open())
    {
        // 9 chữ số có nghĩa = đủ để ghi rồi đọc lại một float32 KHÔNG mất bit nào.
        //
        // Mặc định của ostream là 6 chữ số, và nó đủ để làm sai lệch đo lường: ball_x
        // khoảng 1909.98 mất tới 0.005, sai số đó truyền thẳng vào mọi feature tính bằng
        // phép trừ (ball_dx, landing_dx). Đo được 14/10/2026: C++ tính từ trạng thái game
        // đầy đủ độ chính xác, Python tính lại từ số đã bị CSV làm tròn, nên hai bên lệch
        // 0.005 ở 3737/5354 frame - trông như lỗi logic mà thật ra chỉ là làm tròn khi ghi.
        _file << std::setprecision(9);
        WriteHeader();
    }

    if (DatasetLogConfig::DUMP_ML_FEATURES)
    {
        _featFile.open((std::filesystem::path(logDirectory) / (matchId + "_feat.csv")).string(), std::ios::out);
        if (_featFile.is_open())
        {
            _featFile << std::setprecision(9);
            _featFile << "frame,agent";
            for (int i = 0; i < MlFeaturesV2::kNumFeatures; ++i)
                _featFile << ",f" << i;
            _featFile << '\n';
        }
    }
}

void DatasetLogger::EndMatch()
{
    if (_file.is_open())
    {
        // Phải flush: ofstream đệm dữ liệu, tắt game bằng Task Manager là mất sạch log.
        _file.flush();
        _file.close();
    }
    if (_featFile.is_open())
    {
        _featFile.flush();
        _featFile.close();
    }
}

void DatasetLogger::WriteHeader()
{
    _file << kHeader << '\n';
}

void DatasetLogger::LogFrame(uint64_t frame, float dtMs)
{
    if (!_file.is_open())
        return;

    auto& posPool    = _componentStorage->GetCharacterPositionPool();
    auto& statePool  = _componentStorage->GetCharacterActionStatePool();
    auto* playerPos  = posPool.get(GameConfig::PLAYER);
    auto* oppPos     = posPool.get(GameConfig::OPPONENT_1);
    auto* playerStat = statePool.get(GameConfig::PLAYER);
    auto* oppStat    = statePool.get(GameConfig::OPPONENT_1);
    auto* ballPos    = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);
    auto* ballTraj   = _componentStorage->GetBallTrajectoryPool().get(GameConfig::BALL);
    auto* ballState  = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    auto* rally      = _componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
    auto* match      = _componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

    if (!playerPos || !oppPos || !playerStat || !oppStat || !ballPos || !ballTraj || !ballState || !rally || !match)
        return;  // chưa khởi tạo xong, bỏ frame này thay vì ghi dòng thiếu dữ liệu

    // Ai điều khiển từng sân Ở FRAME NÀY. Phía AI lọc theo đúng hai cột này (chỉ train trên
    // frame người điều khiển), nên ghi sai ở đây là nhãn sai âm thầm.
    //
    // Bản trước ghi cứng o_controller = MlBot, kể cả khi sân phải là người (ENABLE_BOT =
    // false) hoặc bot luật (ML_AGENT_ENTITY != 3). Giờ tính đúng cho mọi chế độ.
    const RunModeData& mode = RunMode::Get();
    const ControllerKind leftKind = !mode.botVsBot           ? ControllerKind::Human
                                    : mode.mlAgentEntity == 0 ? ControllerKind::MlBot
                                                                            : ControllerKind::RuleBot;
    const ControllerKind rightKind = _opponentHuman                          ? ControllerKind::Human
                                     : mode.mlAgentEntity == 3 ? ControllerKind::MlBot
                                                                             : ControllerKind::RuleBot;
    const int pController = static_cast<int>(leftKind);
    const int oController = static_cast<int>(rightKind);

    const LoggedIntent pIntent = IntentOf(_intentStorage, GameConfig::PLAYER);
    const LoggedIntent oIntent = IntentOf(_intentStorage, GameConfig::OPPONENT_1);

    _file << 2 << ',' << _matchId << ',' << frame << ',' << dtMs << ',' << GAME_VERSION << ','
          << playerPos->position.x << ',' << playerPos->position.y << ','
          << static_cast<int>(playerStat->status) << ',' << playerStat->remainTime << ',' << pController << ','
          << mode.playerId << ','
          << oppPos->position.x << ',' << oppPos->position.y << ','
          << static_cast<int>(oppStat->status) << ',' << oppStat->remainTime << ',' << oController << ','
          << mode.opponentId << ','
          << ballPos->position.x << ',' << ballPos->position.y << ','
          << static_cast<int>(ballTraj->type) << ',' << ballTraj->speed << ',' << ballTraj->a << ','
          << ballTraj->b << ',' << ballTraj->c << ',' << ballState->landingX << ','
          << ballState->stateFrame << ',' << static_cast<int>(ballState->collisionState) << ','
          << rally->lastTouch << ',' << static_cast<int>(rally->touchCount) << ','
          << match->leftScore << ',' << match->rightScore << ',' << static_cast<int>(match->servingTeam) << ','
          << pIntent.moveX << ',' << pIntent.finalIntent << ','
          << oIntent.moveX << ',' << oIntent.finalIntent << ','
          << static_cast<int>(rally->lastTouchKind) << '\n';

    if (!_featFile.is_open())
        return;

    // Ghi feature cho CẢ HAI nhân vật: phía AI đối chiếu được cả agent=player lẫn
    // agent=opponent, tức kiểm luôn phép lật sân qua lưới.
    const GameConfig::Entity agents[2] = {GameConfig::PLAYER, GameConfig::OPPONENT_1};
    for (GameConfig::Entity agent : agents)
    {
        float f[MlFeaturesV2::kNumFeatures];
        if (!MlFeaturesV2::Build(_componentStorage, agent, f))
            continue;

        _featFile << frame << ',' << agent;
        for (int i = 0; i < MlFeaturesV2::kNumFeatures; ++i)
            _featFile << ',' << f[i];
        _featFile << '\n';
    }
}
