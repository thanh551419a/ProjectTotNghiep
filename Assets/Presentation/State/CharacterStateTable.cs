using System;
using UnityEngine;

namespace Presentation.State
{
    [Serializable]
    public class CharacterStateEntry
    {
        [SerializeField]
        private CharacterState state = CharacterState.None;

        [SerializeField]
        private int value = 0;

        [NonSerialized]
        private CharacterState previousState;

        [NonSerialized]
        private int previousValue;

        [NonSerialized]
        private bool initialized;

        public CharacterState State => state;
        public int Value => value;

        public void Set(CharacterState newState)
        {
            state = newState;
            value = (int)newState;

            previousState = state;
            previousValue = value;
            initialized = true;
        }

        public void SetValue(int newValue)
        {
            value = newValue;

            if (Enum.IsDefined(
                typeof(CharacterState),
                newValue))
            {
                state = (CharacterState)newValue;
            }
            else
            {
                state = CharacterState.None;
            }

            previousState = state;
            previousValue = value;
            initialized = true;
        }

        public void Synchronize()
        {
            if (!initialized)
            {
                previousState = state;
                previousValue = value;
                initialized = true;
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
                if (Enum.IsDefined(
                    typeof(CharacterState),
                    value))
                {
                    state = (CharacterState)value;
                }
                else
                {
                    state = CharacterState.None;
                }
            }

            previousState = state;
            previousValue = value;
        }
    }

    public class CharacterStateTable : MonoBehaviour
    {
        [SerializeField]
        private int characterCount = 6;

        [SerializeField]
        private CharacterStateEntry[] states;

        [SerializeField]
        private int[] directions;

        [SerializeField]
        private bool[] onTheGround;

        public int Count => characterCount;

        private void Reset()
        {
            characterCount = 6;

            states = new CharacterStateEntry[characterCount];
            directions = new int[characterCount];
            onTheGround = new bool[characterCount];

            for (int i = 0; i < characterCount; i++)
            {
                states[i] = new CharacterStateEntry();
                directions[i] = 1;
                onTheGround[i] = true;
            }
        }

        private void OnValidate()
        {
            characterCount = Mathf.Max(0, characterCount);

            ResizeStates();
            ResizeDirections();
            ResizeGroundStates();

            for (int i = 0; i < directions.Length; i++)
            {
                if (directions[i] == 0)
                {
                    directions[i] = 1;
                }
            }

            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] == null)
                {
                    states[i] = new CharacterStateEntry();
                }

                states[i].Synchronize();
            }
        }

        private void ResizeStates()
        {
            if (states == null)
            {
                states = new CharacterStateEntry[characterCount];
            }
            else if (states.Length != characterCount)
            {
                Array.Resize(
                    ref states,
                    characterCount
                );
            }

            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] == null)
                {
                    states[i] = new CharacterStateEntry();
                }
            }
        }

        private void ResizeDirections()
        {
            if (directions == null ||
                directions.Length != characterCount)
            {
                Array.Resize(
                    ref directions,
                    characterCount
                );
            }
        }

        private void ResizeGroundStates()
        {
            if (onTheGround == null ||
                onTheGround.Length != characterCount)
            {
                Array.Resize(
                    ref onTheGround,
                    characterCount
                );
            }
        }

        public CharacterState Get(int characterId)
        {
            if (!IsValidCharacterId(characterId))
            {
                return CharacterState.None;
            }

            return states[characterId].State;
        }

        public int GetStateValue(int characterId)
        {
            if (!IsValidCharacterId(characterId))
            {
                return 0;
            }

            return states[characterId].Value;
        }

        public void Set(
            int characterId,
            CharacterState state)
        {
            if (!IsValidCharacterId(characterId))
            {
                return;
            }

            // Runtime cũng giữ State + Value đồng bộ.
            SetStateInternal(
                characterId,
                state
            );
        }

        public void SetValue(
            int characterId,
            int value)
        {
            if (!IsValidCharacterId(characterId))
            {
                return;
            }

            CharacterState state;

            if (Enum.IsDefined(
                typeof(CharacterState),
                value))
            {
                state = (CharacterState)value;
            }
            else
            {
                state = CharacterState.None;
            }

            SetStateInternal(
                characterId,
                state
            );
        }

        private void SetStateInternal(
            int characterId,
            CharacterState state)
        {
            // CharacterStateEntry hiện đang expose
            // readonly properties nên runtime cần
            // một method riêng để thay đổi.
            //
            // TODO: implement nếu muốn runtime Set trực tiếp.
        }

        public int GetDirection(int characterId)
        {
            if (characterId < 0 ||
                characterId >= directions.Length)
            {
                Debug.LogError(
                    $"Invalid character id: {characterId}"
                );

                return 1;
            }

            return directions[characterId];
        }

        public void SetDirection(
            int characterId,
            int direction)
        {
            if (characterId < 0 ||
                characterId >= directions.Length)
            {
                Debug.LogError(
                    $"Invalid character id: {characterId}"
                );

                return;
            }

            directions[characterId] =
                direction < 0 ? -1 : 1;
        }

        public bool IsOnTheGround(int characterId)
        {
            if (characterId < 0 ||
                characterId >= onTheGround.Length)
            {
                Debug.LogError(
                    $"Invalid character id: {characterId}"
                );

                return false;
            }

            return onTheGround[characterId];
        }

        public void SetOnTheGround(
            int characterId,
            bool value)
        {
            if (characterId < 0 ||
                characterId >= onTheGround.Length)
            {
                Debug.LogError(
                    $"Invalid character id: {characterId}"
                );

                return;
            }

            onTheGround[characterId] = value;
        }

        private bool IsValidCharacterId(int characterId)
        {
            if (characterId < 0 ||
                characterId >= states.Length)
            {
                Debug.LogError(
                    $"Invalid character id: {characterId}"
                );

                return false;
            }

            return true;
        }
    }
}