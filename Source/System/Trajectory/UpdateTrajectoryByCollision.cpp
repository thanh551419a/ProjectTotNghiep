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
    PositionComponent oldBallPos;
    oldBallPos.position.x = ballPos->position.x - trajectory->speed;
    if (trajectory->type == TrajectoryType::Linear)
    {
        oldBallPos.position.x = ballPos->position.x - trajectory->speed;
        oldBallPos.position.y = trajectory->a * oldBallPos.position.x + trajectory->b;
    }
    else if (trajectory->type == TrajectoryType::Parabolic)
    {
        oldBallPos.position.x = ballPos->position.x - trajectory->speed;
        oldBallPos.position.y =
            trajectory->a * (oldBallPos.position.x + trajectory->b) * (oldBallPos.position.x + trajectory->b) +
            trajectory->c;
    }
    // Vector tới
    Vec2 incident(ballPos->position.x - oldBallPos.position.x, ballPos->position.y - oldBallPos.position.y);

    // Chuẩn hóa
    float len = std::sqrt(incident.x * incident.x + incident.y * incident.y);
    if (len > 0.0001f)
    {
        incident.x /= len;
        incident.y /= len;
    }
    if (AABBHelper::CheckAABB(data[6].pos, data[6].size, data[7].pos, data[7].size))
    {
        // Normal của mặt đứng
        Vec2 normal(1.0f, 0.0f);

        // Reflection = I - 2(I.N)N
        float dot = incident.x * normal.x + incident.y * normal.y;

        Vec2 reflection;
        reflection.x = incident.x - 2.0f * dot * normal.x;
        reflection.y = incident.y - 2.0f * dot * normal.y;

        // Cập nhật trajectory
      

        // Giữ nguyên tốc độ theo trục X
       

        // y = ax + b
        if (std::abs(reflection.x) > 0.0001f )
        {
            CollisionFace face = AABBHelper::GetCollisionFace(data[6].pos, data[6].size, data[7].pos, data[7].size);

            if (face == CollisionFace::Side)
            {
                if (reflection.y > 0)
                    trajectory->type = TrajectoryType::Parabolic;
                else
                    trajectory->type = TrajectoryType::Linear;
                trajectory->speed = -1 * trajectory->speed;
                if (reflection.y < 0)
                {
                    
                    trajectory->a = reflection.y / reflection.x;
                    trajectory->b = ballPos->position.y - trajectory->a * ballPos->position.x;
                }
                else
                {
                    trajectory->a = -0.005f;

                    trajectory->c = ballPos->position.y + 70.0f;

                    trajectory->b = CalculateBounceB(trajectory->a, trajectory->c, ballPos->position.x,
                                                     ballPos->position.y, trajectory->speed > 0 ? 1 : -1);
                }
            }
            else if (face == CollisionFace::Top)
            {
                trajectory->speed = trajectory->speed;
                trajectory->a     = -0.005f;
                trajectory->c     = ballPos->position.y + 70.0f;

                trajectory->b = CalculateBounceB(trajectory->a, trajectory->c, ballPos->position.x, ballPos->position.y,
                                                 trajectory->speed > 0 ? 1 : -1);
            }
        }


        UpdateLandingX(_componentStorage);
       
    }
}
