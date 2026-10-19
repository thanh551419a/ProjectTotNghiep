#include "GenerateIntent.h"
#include <fstream>
#include <algorithm>
GenerateIntent::GenerateIntent(IntentStorage* intentStorage , ComponentStorage* componentStorage)
{
    _intentStorage = intentStorage; 
    _componentStorage = componentStorage;
    playerInputSystem  = new PlayerInputSystem(_intentStorage , componentStorage);
    player2InputSystem = new Player2InputSystem(_intentStorage, _componentStorage);
    //ballIntentSystem   = new BallIntentSystem(_intentStorage);
    aiInputSystem      = new AIInputSystem(_intentStorage, _componentStorage, GameConfig::OPPONENT_1);
    aiLeftInputSystem  = new AIInputSystem(_intentStorage, _componentStorage, GameConfig::PLAYER);
    velocityIntentSystem = new VelocityIntentSystem(_intentStorage);
    jumpStartSystem      = new JumpStartSystem(_intentStorage, _componentStorage);
    updateTrajectory = new UpdateTrajectory(_intentStorage, _componentStorage);
    updateCharacterActionStateSystem = new UpdateCharacterActionStateSystem(_componentStorage);
    AXLOG("[ProcessInput] storage=%p", (void*)_intentStorage);
}
void GenerateIntent::UpdateTakeover(const InputListener::InputFrame& input)
{
    // Chạm phím Player 2 là giành quyền ngay.
    if (Player2InputSystem::AnyKeyHeld(input))
    {
        _takeoverActive     = true;
        _takeoverFramesLeft = MatchRuleConfig::TAKEOVER_GRACE_FRAMES;
        return;
    }
    if (!_takeoverActive)
        return;

    // Không bấm phím nào: CHƯA trả quyền nếu lượt bóng của người còn đang diễn ra.
    //
    // Bản trước trả quyền sau đúng 20 frame không bấm phím. Đo trên buổi DAgger đầu tiên
    // (15/10/2026): 45 lần người giành quyền kết thúc bằng Set, vì sau cú Set bóng bay
    // lên cao, người đứng chờ bóng rơi (trung vị 69 frame) mà không bấm gì -> bot lấy lại
    // quyền giữa chừng. 40/45 lần, lần giành quyền ngay sau đó bắt đầu bằng Jump -> Spike:
    // chuỗi Set -> Jump -> Spike CÓ diễn ra nhưng bị cắt làm đôi, và các frame người di
    // chuyển vào vị trí chờ bóng bị ghi nhầm là của bot.
    //
    // Lượt bóng của người còn đang diễn ra khi:
    //   - bóng còn sống VÀ còn ở sân phải (đang Set / chờ bóng rơi / nhảy lên đập), hoặc
    //   - đang chờ sân phải giao bóng (để kịp Jump rồi Serve). Tính CẢ lúc đếm ngược:
    //     sau mỗi điểm stateFrame đếm 50 -> 1 rồi mới về Reset (0), đo được cửa sổ chỉ
    //     ~50 frame. Chỉ giữ quyền ở đúng Reset thì quyền bị trả về giữa lúc đếm ngược và
    //     người không kịp nhảy giao (buổi 1: giữ được 1/23 lượt giao của sân phải).
    auto* ballPos   = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);
    auto* ballState = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    auto* match     = _componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

    const bool myPossession = ballPos && ballState && ballState->stateFrame == BallStateFrame::Alive &&
                              ballPos->position.x > BigRect::NET_X;
    const bool myServe = ballState && match && ballState->stateFrame != BallStateFrame::Alive &&
                         match->servingTeam == Team::RIGHT;

    if (myPossession || myServe)
    {
        _takeoverFramesLeft = MatchRuleConfig::TAKEOVER_GRACE_FRAMES;  // còn lượt -> nạp lại đệm
        return;
    }

    // Bóng đã sang sân đối phương hoặc pha bóng đã kết thúc: đếm lùi rồi trả quyền cho bot.
    if (--_takeoverFramesLeft <= 0)
        _takeoverActive = false;
}

void GenerateIntent::update(float delta, InputListener::InputFrame input, std::ofstream* logFile)
{
    // DEBUG: kiểm tra input có xuống đúng không
    // Sân trái: người chơi, hoặc bot thứ hai khi đang tự sinh dữ liệu (bot-vs-bot)
    if (MatchRuleConfig::ENABLE_BOT_VS_BOT)
        aiLeftInputSystem->update();
    else
        playerInputSystem->update(delta, input);// sinh Intent muốn di chuyển đi đâu , cần nhận input và ghi vào intent Storage , vậy thì cần phải có con trỏ đến object IntentStorage đưa xuống
    // Sân phải: bot, hoặc người chơi thứ hai, hoặc (buổi DAgger) bot nhưng người giành
    // quyền được bất cứ lúc nào bằng cách chạm phím Player 2.
    if (MatchRuleConfig::ENABLE_BOT)
    {
        if (MatchRuleConfig::DAGGER_SESSION)
            UpdateTakeover(input);

        if (_takeoverActive)
        {
            player2InputSystem->update(delta, input);
            _opponentHuman = true;
        }
        else
        {
            aiInputSystem->update();  // sinh Intent cua AI
            _opponentHuman = false;
        }
    }
    else
    {
        player2InputSystem->update(delta, input);
        _opponentHuman = true;
    }
    updateCharacterActionStateSystem->update(delta);
    //characterStatusSystem->update(delta);
    jumpStartSystem->update(delta);
    auto data = GetPositionData(_componentStorage);// lấy data
    updateTrajectory->update(data, delta, logFile);
    velocityIntentSystem->update(delta, data);     // sinh Intent của velocity dựa trên Intent của player và AI
    //ballIntentSystem->update(delta, input , data); // sinh Intent của bóng chỉ dựa trên trajectory và delta time
}





