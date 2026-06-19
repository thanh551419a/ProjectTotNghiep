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
    _storage->InitDemo();

    _intentStorage = new IntentStorage();
    _generateIntent = new GenerateIntent(_intentStorage, _storage);
    // 2. truyền ref xuống render system

    _rectSystem = new RectSystem(this,_storage);// // đối tượng giữ ref của 8 rect cần nhận input trong này và xử lý

    scheduleUpdate();

    return true;
}

void MainScene::update(float delta)
{
    //AXLOG("MAIN SCENE UPDATE");

    _intentStorage->clear();                                  // xóa intent cũ trước khi nhận intent mới
    //_input->update(delta);// update input 
    InputListener::InputFrame input = _input->GetFrameInput();// nhận input
    _generateIntent->update(delta, input);// Xử lý input và ghi data vào trong component storage
    ApplyIntentToComponent(_intentStorage, _storage);       // Áp dụng intent vào component storage (thay đổi data)
    _rectSystem->update(delta);                               // Ve rect dựa trên data trong component storage
}
