#include "MainScene.h"
#include "../UI/DrawBigRect.h"
#include <iostream>
#include <fstream>
#include <filesystem>

using namespace std;
using namespace ax;

void MainScene::OpenLogFile(const std::string& fileName)
{
    if (_logFile.is_open())
        _logFile.close();

    _logFile.open(fileName.c_str(), std::ios::out);

    if (!_logFile.is_open())
    {
        AXLOG("Cannot open file: %s", fileName.c_str());

    }
}

void MainScene::CloseLogFile()
{
    if (_logFile.is_open())
    {
        _logFile.close();
    }
}

std::string MainScene::CreateLogFileName()
{
    auto now      = std::chrono::system_clock::now();
    std::time_t t = std::chrono::system_clock::to_time_t(now);

    std::tm tm{};

#ifdef _WIN32
    localtime_s(&tm, &t);
#else
    localtime_r(&t, &tm);
#endif

    std::ostringstream oss;
    oss << std::setfill('0') << std::setw(4) << (tm.tm_year + 1900) << "-" << std::setw(2) << (tm.tm_mon + 1)
        << "-" << std::setw(2) << tm.tm_mday << "_" << std::setw(2) << tm.tm_hour << "-" << std::setw(2) << tm.tm_min
        << "-" << std::setw(2) << tm.tm_sec << ".csv";

    return oss.str();
}

//inline void WritePlayer1Data(ComponentStorage* storage)
//{
//    if (!_logFile.is_open())
//        return;
//
//    auto* player = storage->GetCharacterPositionPool().get(GameConfig::PLAYER);
//
//    _logFile << "Player1 " << player->position.x << ' ' << player->position.y << '\n';
//}
//
//inline void WritePlayer2Data(ComponentStorage* storage)
//{
//    if (!_logFile.is_open())
//        return;
//
//    auto* player = storage->GetCharacterPositionPool().get(GameConfig::OPPONENT_1);
//
//    _logFile << "Player2 " << player->position.x << ' ' << player->position.y << '\n';
//}

bool MainScene::init()
{
    // Super init
    
    if (!Scene::init())
    {
        return false;
    }
    _input = InputListener::Create(this);// có init được nhưng không chạy 
    // =========================
    // Init code
    // =========================

    auto visibleSize = Director::getInstance()->getVisibleSize();

    auto label = Label::createWithSystemFont("ECS Simulation Running", "Arial", 32);

    label->setPosition(Vec2(visibleSize.width * 0.5f, visibleSize.height * 0.5f));
    DrawBigRect(this, SystemConfig::offsetX, SystemConfig::offsetY);
    addChild(label);
    // Required for update()
    // Create _logFileName
    _logFileName = "../Logs/" + CreateLogFileName();
    OpenLogFile(_logFileName);
     // 1. tạo storage
    _storage = new ComponentStorage();
    _storage->InitComponent();
    
    _intentStorage = new IntentStorage();
    _generateIntent = new GenerateIntent(_intentStorage, _storage);

    _updateCharacterActionStateSystem = new UpdateCharacterActionStateSystem(_storage);
    _characterStatStorage = new CharacterStatStorage();

    _loadStatsData        = new LoadStatsData(_characterStatStorage);
    _loadStatsData->LoadData();

    // 2. truyền ref xuống render system
    auto& CharacterIntentPool = _intentStorage->GetCharacterIntentPool();
    AXLOG("[MainScene] CharacterIntentPool addr = %p", &CharacterIntentPool);
    _rectSystem = new RectSystem(this,_storage);// // đối tượng giữ ref của 8 rect cần nhận input trong này và xử lý
    _gameRuleEvaluationSystem = new GameRuleEvaluationSystem(_storage, _intentStorage);
    _framePrearationSystem = new FramePreparationSystem(_storage, _intentStorage);
    AXLOG("Current Path : %s", std::filesystem::current_path().string().c_str());

    AXLOG("Log File : %s", _logFileName.c_str());

    AXLOG("Absolute : %s", std::filesystem::absolute(_logFileName).string().c_str());
    scheduleUpdate();

    return true;
}
void MainScene::update(float delta)
{
    //AXLOG("MAIN SCENE UPDATE");
    auto matchState = _storage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    //AXLOG("[outCheck]Left Score %d and RightScore %d ", matchState->leftScore, matchState->rightScore);
    //AXLOG("Update MainScene");
    if (matchState->leftScore >= SCORE_TO_WIN || matchState->rightScore >= SCORE_TO_WIN)
    {
        AXLOG("Reset Lien tuc");
        _logFileName = CreateLogFileName();
        _storage->Reset();
        _storage->InitComponent();
        _intentStorage->clear();
        _characterStatStorage->reset();

        CloseLogFile();

        _logFileName = CreateLogFileName();

        OpenLogFile(_logFileName);
        AXLOG("[InCheck]Left Score %d and RightScore %d ", matchState->leftScore, matchState->rightScore);
        return;  // Kết thúc frame này
    }
    //AXLOG("Ket thuc Reset");
    //AXLOG("Frame hien tai la : %d", frame);
    float FIXED_TIME = delta * 1000;
    _intentStorage->clear();
    _framePrearationSystem->update();
    // xóa intent cũ trước khi nhận intent mới
    //_input->update(delta);// update input 
    InputListener::InputFrame input = _input->GetFrameInput();// nhận input
    _generateIntent->update(FIXED_TIME, input, &_logFile);// Xử lý input và ghi data vào trong component storage
    ApplyIntentToComponent(_intentStorage, _storage, &_logFile);       // Áp dụng intent vào component storage (thay đổi data)
    _gameRuleEvaluationSystem->update();
    _rectSystem->update(FIXED_TIME);                               // Ve rect dựa trên data trong component storage

}
