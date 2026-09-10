using System;
using UnityEngine;

namespace Presentation.State
{
    public class CharacterStateTable : MonoBehaviour
    {
        [SerializeField]
        private int characterCount = 6;

        [SerializeField]
        private CharacterStateData[] states;

        [SerializeField]
        private DirectionData[] directions;

        [SerializeField]
        private GroundStateData[] onTheGround;

        public int Count => characterCount;

        private void Reset()
        {
            characterCount = 6;

            Create();
        }

        private void OnValidate()
        {
            characterCount =
                Mathf.Max(0, characterCount);

            Create();

            Synchronize();
        }

        private void Create()
        {
            CreateArray(
                ref states,
                () => new CharacterStateData()
            );

            CreateArray(
                ref directions,
                () => new DirectionData()
            );

            CreateArray(
                ref onTheGround,
                () => new GroundStateData()
            );
        }

        private void CreateArray<T>(
            ref T[] array,
            Func<T> factory)
        {
            if (array == null ||
                array.Length != characterCount)
            {
                Array.Resize(
                    ref array,
                    characterCount
                );
            }

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null)
                {
                    array[i] = factory();
                }
            }
        }

        private void Synchronize()
        {
            for (int i = 0; i < states.Length; i++)
            {
                states[i].Synchronize();
            }

            for (int i = 0; i < directions.Length; i++)
            {
                directions[i].Synchronize();
            }
        }

        public CharacterStateData GetState(int characterId)
        {
            if (!IsValid(
                characterId,
                states.Length))
            {
                return null;
            }

            return states[characterId];
        }

        public DirectionData GetDirection(int characterId)
        {
            if (!IsValid(
                characterId,
                directions.Length))
            {
                return null;
            }

            return directions[characterId];
        }

        public GroundStateData GetGroundState(int characterId)
        {
            if (!IsValid(
                characterId,
                onTheGround.Length))
            {
                return null;
            }

            return onTheGround[characterId];
        }

        private bool IsValid(
            int characterId,
            int length)
        {
            if (characterId < 0 ||
                characterId >= length)
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