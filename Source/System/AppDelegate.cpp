#include "AppDelegate.h"
#include "MainScene.h"
#include "../Config/System/SystemConf.h"

using namespace ax;

static ax::Size designResolutionSize(SystemConfig::VIRTUAL_WIDTH, SystemConfig::VIRTUAL_HEIGHT);

AppDelegate::AppDelegate() {}
AppDelegate::~AppDelegate() {}

void AppDelegate::initGfxContextAttrs()
{
    // Context đồ hoạ

    GfxContextAttrs attrs = {8, 8, 8, 8, 24, 8, 0};
    RenderView::setGfxContextAttrs(attrs);
}

bool AppDelegate::applicationDidFinishLaunching()
{
    // Director init
    auto director   = Director::getInstance();
    auto renderView = director->getRenderView();

    if (!renderView)
    {
#if (AX_TARGET_PLATFORM == AX_PLATFORM_WIN32) || (AX_TARGET_PLATFORM == AX_PLATFORM_MAC) || \
    (AX_TARGET_PLATFORM == AX_PLATFORM_LINUX)

        if (SystemConfig::FULLSCREEN)
        {
            renderView = RenderViewImpl::createWithFullScreen(SystemConfig::WINDOW_TITLE);
        }
        else
        {
            renderView = RenderViewImpl::createWithRect(
                SystemConfig::WINDOW_TITLE, ax::Rect(0, 0, SystemConfig::WINDOW_WIDTH, SystemConfig::WINDOW_HEIGHT));
        }
#else
        renderView = RenderViewImpl::create(SystemConfig::WINDOW_TITLE);
#endif

        director->setRenderView(renderView);
    }

    // FPS cố định cho ECS simulation
    director->setAnimationInterval(SystemConfig::FIXED_DT);

    // resolution + scale mode (IMPORTANT FIX)
    renderView->setDesignResolutionSize(SystemConfig::VIRTUAL_WIDTH+20, SystemConfig::VIRTUAL_HEIGHT,
                                        ResolutionPolicy::SHOW_ALL);

    // Tắt debug FPS (optional cho clean look giống game thật)
    director->setStatsDisplay(true);

    // Khởi tạo scene
    auto scene = utils::createInstance<MainScene>();
    director->runWithScene(scene);

    return true;
}

void AppDelegate::applicationDidEnterBackground()
{
    Director::getInstance()->stopAnimation();

#if USE_AUDIO_ENGINE
    AudioEngine::pauseAll();
#endif
}

void AppDelegate::applicationWillEnterForeground()
{
    Director::getInstance()->startAnimation();

#if USE_AUDIO_ENGINE
    AudioEngine::resumeAll();
#endif
}

void AppDelegate::applicationWillQuit() {}
