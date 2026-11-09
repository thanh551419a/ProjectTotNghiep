using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIButtonInputTest : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private int buttonId;
    [SerializeField] private bool allowHold = true;

    public event Action<int, bool> OnInput;

    private readonly HashSet<int> activePointers = new HashSet<int>();

    public int ButtonId => buttonId;

    public void Configure(int id, bool hold)
    {
        buttonId = id;
        allowHold = hold;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (activePointers.Add(eventData.pointerId) && activePointers.Count == 1)
            OnInput?.Invoke(buttonId, true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!activePointers.Remove(eventData.pointerId) || activePointers.Count != 0)
            return;

        // Chỉ button cho phép Hold mới cần báo Release
        if (allowHold)
        {
            OnInput?.Invoke(buttonId, false);
        }
    }

    private void Update()
    {
        // Không cho Hold:
        // Chỉ phát event tại OnPointerDown, không phát liên tục.
        if (!allowHold)
            return;

        // Cho Hold:
        // Phát liên tục trong thời gian giữ nút.
        if (activePointers.Count != 0)
        {
            OnInput?.Invoke(buttonId, true);
        }
    }

    private void OnDisable()
    {
        if (activePointers.Count == 0)
            return;
        activePointers.Clear();

        // Nếu đang Hold mà object bị disable,
        // phải báo release để handler không nghĩ rằng nút vẫn đang giữ.
        if (allowHold)
        {
            OnInput?.Invoke(buttonId, false);
        }
    }
}

