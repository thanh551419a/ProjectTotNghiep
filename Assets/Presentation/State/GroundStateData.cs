using UnityEngine;

namespace Presentation.State
{
    [System.Serializable]
    public class GroundStateData
    {
        [SerializeField]
        private bool value = true;

        public bool Value => value;

        public void Create(bool initialValue = true)
        {
            value = initialValue;
        }

        public void Set(bool newValue)
        {
            value = newValue;
        }

        public bool Get()
        {
            return value;
        }
    }
}