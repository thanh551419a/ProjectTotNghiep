Y:.

|   README.md

|   Source\_tree.md

|

+---Config

|   +---ECS

|   |       ChunkConfig.h

|   |

|   +---Match

|   |       BigRect.h

|   |       MatchEntityConfig.h

|   |       MatchObjectConfig.h

|   |       MatchRuleConfig.h

|   |       TrajectoryConfig.h

|   |       TrajectoryData.h

|   |

|   \\---System

|           SystemConf.h

|

+---Constants

|       BallGameplayConstants.h

|

+---Data

|       CharacterStats.json

|

+---ECSCore

|   +---ComponentPools

|   |       ComponentPool.h

|   |

|   +---Components

|   |       BallTrajectoryComponent.h

|   |       JumpUpFrameComponent.h

|   |       PositionComponent.h

|   |       SizeComponent.h

|   |

|   +---ComponentStorage

|   |       ComponentStorage.cpp

|   |       ComponentStorage.h

|   |

|   +---IntentComponents

|   |       BallIntent.h

|   |       CharacterIntent.h

|   |       VelocityIntent.h

|   |

|   +---IntentStorage

|   |       IntentStorage.h

|   |

|   \\---States

|           ActionState.h

|           BallGamePlayState.h

|           MatchState.h

|           RallyState.h

|

+---Gameplay

|   |   FinalIntent.h

|   |

|   \\---MatchData

|           CharacterStats.h

|           CharacterStatStorage.h

|

+---Input

|       InputListener.cpp

|       InputListener.h

|

+---SimMath

|       AxmolInterop.h

|       Size.h

|       Vec2.h

|

+---System

|   |   AIInputSystem.cpp

|   |   AIInputSystem.h

|   |   AppDelegate.cpp

|   |   AppDelegate.h

|   |   CharacterStatusSystem.cpp

|   |   FramePreparationSystem.cpp

|   |   FramePreparationSystem.h

|   |   GameRuleEvaluationSystem.cpp

|   |   GameRuleEvaluationSystem.h

|   |   JumpStartSystem.cpp

|   |   JumpStartSystem.h

|   |   LoadStatsData.cpp

|   |   LoadStatsData.h

|   |   MainScene.cpp

|   |   MainScene.h

|   |   Player2InputSystem.cpp

|   |   Player2InputSystem.h

|   |   PlayerInputSystem.cpp

|   |   PlayerInputSystem.h

|   |   TrajectoryTestConfig.h

|   |   TrajectoryTestData.h

|   |   UpdateCharacterActionStateSystem.cpp

|   |   UpdateCharacterActionStateSystem.h

|   |   UpdateTrajectory.cpp

|   |   UpdateTrajectory.h

|   |   VelocityIntentSystem.cpp

|   |   VelocityIntentSystem.h

|   |

|   +---TestTrajectory

|   |       TestTrajectorySystem.cpp

|   |       TestTrajectorySystem.h

|   |

|   \\---Trajectory

|           RallyStateUtils.cpp

|           RallyStateUtils.h

|           TrajectoryMath.cpp

|           TrajectoryMath.h

|           TrajectoryUpdate.cpp

|           TrajectoryUpdate.h

|           UpdateTrajectoryByCollision.cpp

|           UpdateTrajectoryByCollision.h

|           UpdateTrajectoryFromIntent.cpp

|           UpdateTrajectoryFromIntent.h

|

+---ThirdParty

|   \\---RapidJson

|       |   allocators.h

|       |   cursorstreamwrapper.h

|       |   document.h

|       |   encodedstream.h

|       |   encodings.h

|       |   filereadstream.h

|       |   filewritestream.h

|       |   fwd.h

|       |   istreamwrapper.h

|       |   memorybuffer.h

|       |   memorystream.h

|       |   ostreamwrapper.h

|       |   pointer.h

|       |   prettywriter.h

|       |   rapidjson.h

|       |   reader.h

|       |   schema.h

|       |   stream.h

|       |   stringbuffer.h

|       |   uri.h

|       |   writer.h

|       |

|       +---error

|       |       en.h

|       |       error.h

|       |

|       +---internal

|       |       biginteger.h

|       |       clzll.h

|       |       diyfp.h

|       |       dtoa.h

|       |       ieee754.h

|       |       itoa.h

|       |       meta.h

|       |       pow10.h

|       |       regex.h

|       |       stack.h

|       |       strfunc.h

|       |       strtod.h

|       |       swap.h

|       |

|       \\---msinttypes

|               inttypes.h

|               stdint.h

|

+---UI

|       DrawBigRect.cpp

|       DrawBigRect.h

|       RectSystem.cpp

|       RectSystem.h

|

\\---Utils

&#x20;       AABB.h

&#x20;       ApplyIntentToComponent.h

&#x20;       Draw8Rect.h

&#x20;       GenerateIntent.cpp

&#x20;       GenerateIntent.h

&#x20;       GetData.h

&#x20;       ObjectData.h

&#x20;       ResetNode.cpp

&#x20;       ResetNode.h

&#x20;       TrajectoryDebugDraw.cpp

&#x20;       TrajectoryDebugDraw.h

