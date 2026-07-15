#include "MainScene.h"
#include "../UI/DrawBigRect.h"
#include <iostream>
using namespace std;
using namespace ax;
void show(Point k) {
    cout << "show: " << k.x << ", " << k.y << endl;
}

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
    scheduleUpdate();

    return true;
}

void MainScene::update(float delta)
{
    //AXLOG("MAIN SCENE UPDATE");
    float FIXED_TIME = delta * 1000;
    _intentStorage->clear();
    _framePrearationSystem->update();
    // xóa intent cũ trước khi nhận intent mới
    //_input->update(delta);// update input 
    InputListener::InputFrame input = _input->GetFrameInput();// nhận input
    _generateIntent->update(FIXED_TIME, input);// Xử lý input và ghi data vào trong component storage
    ApplyIntentToComponent(_intentStorage, _storage);       // Áp dụng intent vào component storage (thay đổi data)
    _gameRuleEvaluationSystem->update();
    _rectSystem->update(FIXED_TIME);                               // Ve rect dựa trên data trong component storage

}
