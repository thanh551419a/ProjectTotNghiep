#include "UpdateTrajectoryByCollision.h"
#include "TrajectoryUpdate.h"

inline float CalculateBounceB(float a, float c, float x0, float y0, int direction)
{
    float delta = std::sqrt((y0 - c) / a);

    if (direction < 0)
        return -x0 + delta;  // Đỉnh nằm bên trái
    else
        return -x0 - delta;  // Đỉnh nằm bên phải
}

void UpdateTrajectoryByCollision::update(ObjectData* data, float delta, std::ofstream* logFile)
{
    auto trajectory = _componentStorage->GetBallTrajectoryPool().get(GameConfig::BALL);
    auto ballPos    = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);

    // ============================================================
    // 1. Reconstruct previous ball position
    // ============================================================
    PositionComponent oldBallPos;

    oldBallPos.position.x = ballPos->position.x - trajectory->speed;

    if (trajectory->type == TrajectoryType::Linear)
    {
        oldBallPos.position.y = trajectory->a * oldBallPos.position.x + trajectory->b;
    }
    else if (trajectory->type == TrajectoryType::Parabolic)
    {
        oldBallPos.position.y =
            trajectory->a * (oldBallPos.position.x + trajectory->b) * (oldBallPos.position.x + trajectory->b) +
            trajectory->c;
    }

    // ============================================================
    // 2. Incident vector
    // ============================================================
    Vec2 incident(ballPos->position.x - oldBallPos.position.x, ballPos->position.y - oldBallPos.position.y);

    // Normalize
    float len = std::sqrt(incident.x * incident.x + incident.y * incident.y);

    if (len > 0.0001f)
    {
        incident.x /= len;
        incident.y /= len;
    }

    // ============================================================
    // 3. Detect collision FACE
    //    GetCollisionFace() đã tự CheckAABB bên trong.
    // ============================================================
    //AXLOG("=== Collision Face Check ===");

    //AXLOG("NET  pos=(%f, %f), size=(%f, %f)", data[6].pos.x, data[6].pos.y, data[6].size.x, data[6].size.y);

    //AXLOG("BALL pos=(%f, %f), size=(%f, %f)", data[7].pos.x, data[7].pos.y, data[7].size.x, data[7].size.y);

    //CollisionFace face1 = AABBHelper::GetCollisionFace(data[7].pos, data[7].size,  // NET
    //                                                  data[6].pos, data[6].size   // BALL
    //);

    //AXLOG("CollisionFace = %d", static_cast<int>(face1));
    //AXLOG("============================");
    CollisionFace face = AABBHelper::GetCollisionFace(data[7].pos, data[7].size,  // NET
                                                      data[6].pos, data[6].size   // BALL
    );
    
    // ============================================================
    // 4. Chỉ xử lý đúng MỘT collision face
    // ============================================================
    switch (face)
    {
    case CollisionFace::Side:
    {
        // ========================================================
        // Va chạm cạnh bên của lưới
        // Normal theo phương X
        // ========================================================
        Vec2 normal(trajectory->speed > 0 ? -1.0f : 1.0f, 0.0f);

        // Reflection = I - 2(I.N)N
        float dot = incident.x * normal.x + incident.y * normal.y;

        Vec2 reflection;

        reflection.x = incident.x - 2.0f * dot * normal.x;

        reflection.y = incident.y - 2.0f * dot * normal.y;

        if (std::abs(reflection.x) <= 0.0001f)
            break;

        // --------------------------------------------------------
        // Đảo hướng X để bóng bay ngược ra khỏi lưới
        // --------------------------------------------------------
        trajectory->speed = -trajectory->speed;

        // --------------------------------------------------------
        // Giữ logic trajectory hiện tại
        // --------------------------------------------------------
        if (reflection.y > 0)
        {
            // Bóng bật lên -> Parabolic
            trajectory->type = TrajectoryType::Parabolic;

            trajectory->a = -0.005f;

            trajectory->c = ballPos->position.y + 70.0f;

            trajectory->b = CalculateBounceB(trajectory->a, trajectory->c, ballPos->position.x, ballPos->position.y,
                                             trajectory->speed > 0 ? 1 : -1);
        }
        else
        {
            // Bóng tiếp tục đi xuống -> Linear
            trajectory->type = TrajectoryType::Linear;

            trajectory->a = reflection.y / reflection.x;

            trajectory->b = ballPos->position.y - trajectory->a * ballPos->position.x;
        }

        break;
    }

    case CollisionFace::Top:
    {
        AXLOG("Va cham phia tren cua luoi, tinh lai trajectory");
        // ========================================================
        // Va chạm mặt trên của lưới
        //
        // Normal theo phương Y
        // ========================================================
        Vec2 normal(0.0f, 1.0f);

        // Reflection = I - 2(I.N)N
        float dot = incident.x * normal.x + incident.y * normal.y;

        Vec2 reflection;

        reflection.x = incident.x - 2.0f * dot * normal.x;

        reflection.y = incident.y - 2.0f * dot * normal.y;

        // ========================================================
        // Top collision:
        //
        // X không đổi hướng
        // Y được phản xạ
        //
        // Sau đó tạo lại trajectory parabolic để bóng bật lên.
        // ========================================================
        trajectory->speed = trajectory->speed;

        trajectory->type = TrajectoryType::Parabolic;

        trajectory->a = -0.005f;

        trajectory->c = ballPos->position.y + 70.0f;

        trajectory->b = CalculateBounceB(trajectory->a, trajectory->c, ballPos->position.x, ballPos->position.y,
                                         trajectory->speed > 0 ? 1 : -1);

        break;
    }

    case CollisionFace::None:
    default:
    {
        // Không collision -> không thay đổi trajectory
        break;
    }
    }

    // ============================================================
    // 5. Update landing point sau khi trajectory đã được xử lý
    // ============================================================
    if (face != CollisionFace::None)
    {
        UpdateLandingX(_componentStorage);
    }
}
