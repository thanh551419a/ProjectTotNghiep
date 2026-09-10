using System;
using UnityEngine;

namespace Presentation.State
{
    [Serializable]
    public class DirectionData
    {
        [SerializeField]
        private Direction direction = Direction.Right;

        [SerializeField]
        private int value = (int)Direction.Right;

        [NonSerialized]
        private Direction previousDirection;

        [NonSerialized]
        private int previousValue;

        [NonSerialized]
        private bool initialized;

        public Direction Direction => direction;
        public int Value => value;

        public void Create(
            Direction initialDirection = Direction.Right)
        {
            direction = initialDirection;
            value = (int)initialDirection;

            SyncPrevious();
        }

        public void Set(Direction newDirection)
        {
            direction = newDirection;
            value = (int)newDirection;

            SyncPrevious();
        }

        public void SetValue(int newValue)
        {
            if (Enum.IsDefined(
                typeof(Direction),
                newValue))
            {
                direction = (Direction)newValue;
                value = newValue;
            }
            else
            {
                direction = Direction.Right;
                value = (int)Direction.Right;
            }

            SyncPrevious();
        }

        public void Synchronize()
        {
            if (!initialized)
            {
                SyncPrevious();
                return;
            }

            bool directionChanged =
                direction != previousDirection;

            bool valueChanged =
                value != previousValue;

            if (directionChanged && !valueChanged)
            {
                value = (int)direction;
            }
            else if (valueChanged)
            {
                SetValue(value);
                return;
            }

            SyncPrevious();
        }

        private void SyncPrevious()
        {
            previousDirection = direction;
            previousValue = value;
            initialized = true;
        }
    }
}