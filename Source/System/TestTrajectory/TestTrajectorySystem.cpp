#include "TestTrajectorySystem.h"
#include "../TrajectoryTestConfig.h"
constexpr float EPSILON = 1e-6f;
constexpr Vec2 DefaultBallPositionTest = Vec2(700.0f, 450.0f);
TestTrajectorySystem::TestTrajectorySystem(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    _intentStorage    = intentStorage;
    _componentStorage = componentStorage;
}
inline bool IsPressed(const InputListener::InputFrame& input, EventKeyboard::KeyCode key)
{
    return std::find(input.pressedKeys.begin(), input.pressedKeys.end(), key) != input.pressedKeys.end();
}
void TestTrajectorySystem::update(float delta, InputListener::InputFrame input) {
    auto ballTrajectory = _componentStorage->GetBallTrajectoryPool().get(GameConfig::BALL);
    auto& testTrajectory = TestTrajectory::getInstance();
    if (IsPressed(input , EventKeyboard::KeyCode::KEY_J)) {// start test trajectory 1
        auto* ballPos = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);
        auto temp     = testTrajectory.tempTrajectory;

        const float x0 = DefaultBallPositionTest.x;
        const float y0 = DefaultBallPositionTest.y;

        // =========================
        // Linear
        // =========================
        if (temp.type == TrajectoryType::Linear)
        {
            ballTrajectory->type  = TrajectoryType::Linear;
            ballTrajectory->speed = temp.speed;

            ballTrajectory->a = temp.a;

            // y = ax + b
            // b = y0 - ax0
            ballTrajectory->b = y0 - ballTrajectory->a * x0;

            ballTrajectory->c = 0.0f;
        }
        else if (temp.type == TrajectoryType::Parabolic)
        {
            ballTrajectory->type  = TrajectoryType::Parabolic;
            ballTrajectory->speed = temp.speed;
            ballTrajectory->a     = temp.a;

            const float x0 = ballPos->position.x;
            const float y0 = ballPos->position.y;

            // =====================================================
            // b gần bằng 0
            // => coi như b chưa nhập
            // => dùng c để tính b
            // =====================================================

            if (std::abs(temp.b) <= EPSILON)
            {
                ballTrajectory->c = temp.c;

                const float value = (y0 - ballTrajectory->c) / ballTrajectory->a;

                if (value < 0.0f)
                {
                    AXLOG(
                        "Cannot calculate B: "
                        "a=%f c=%f x0=%f y0=%f",
                        ballTrajectory->a, ballTrajectory->c, x0, y0);

                    return;
                }

                const float root = std::sqrt(value);

                // Chọn nghiệm B nhỏ hơn
                ballTrajectory->b = -x0 - root;

                AXLOG("Calculated B = %f | a = %f | c = %f | x0 = %f | y0 = %f | value = %f | root = %f",
                      ballTrajectory->b, ballTrajectory->a, ballTrajectory->c, x0, y0, value, root);
            }

            // =====================================================
            // b có giá trị
            // => giữ b người dùng nhập
            // => tính c để quỹ đạo đi qua điểm hiện tại
            // =====================================================

            else
            {
                ballTrajectory->b = temp.b;

                ballTrajectory->c = y0 - ballTrajectory->a * std::pow(x0 + ballTrajectory->b, 2);

                AXLOG("Keep B = %f, Calculated C = %f", ballTrajectory->b, ballTrajectory->c);
            }
        }
        testTrajectory.tempTrajectory = *ballTrajectory;
    }
    if (IsPressed(input, EventKeyboard::KeyCode::KEY_K))
    {
       /* testTrajectory.resetTempTrajectory();
        //ballTrajectory = testTrajectory.tempTrajectory;*/
        auto ballPos    = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);
        ballPos->position.x = DefaultBallPositionTest.x;
        ballPos->position.y = DefaultBallPositionTest.y;

        ballTrajectory->type          = TrajectoryType::Parabolic;
        ballTrajectory->speed         = 0.0f;
        ballTrajectory->a             = 0.0f;
        ballTrajectory->b             = 0.0f;
        ballTrajectory->c             = 0.0f;
        auto ballGameplayState = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
        ballGameplayState->stateFrame = Alive;
    }
}
