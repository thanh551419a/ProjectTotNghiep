using System;
using UnityEngine;

namespace Presentation.State
{
    [Serializable]
    public class CharacterStateData
    {
        [SerializeField]
        private CharacterState state = CharacterState.None;

        [SerializeField]
        private int value = (int)CharacterState.None;

        [NonSerialized]
        private CharacterState previousState;

        [NonSerialized]
        private int previousValue;

        [NonSerialized]
        private bool initialized;

        public CharacterState State => state;
        public int Value => value;

        public void Create(
            CharacterState initialState = CharacterState.None)
        {
            state = initialState;
            value = (int)initialState;

            SyncPrevious();
        }

        public void Set(CharacterState newState)
        {
            state = newState;
            value = (int)newState;

            SyncPrevious();
        }

        public void SetValue(int newValue)
        {
            if (Enum.IsDefined(
                typeof(CharacterState),
                newValue))
            {
                state = (CharacterState)newValue;
                value = newValue;
            }
            else
            {
                state = CharacterState.None;
                value = (int)CharacterState.None;
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

            bool stateChanged =
                state != previousState;

            bool valueChanged =
                value != previousValue;

            if (stateChanged && !valueChanged)
            {
                value = (int)state;
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
            previousState = state;
            previousValue = value;
            initialized = true;
        }
    }
}