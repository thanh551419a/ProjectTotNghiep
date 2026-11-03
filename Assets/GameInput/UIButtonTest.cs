using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIButtonInputTest : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private int buttonId;
    [SerializeField] private bool allowHold = true;

    public event Action<int, bool> OnInput;

    private bool isHeld;

    public void OnPointerDown(PointerEventData eventData)
    {
        isHeld = true;

        // Nhấn lần đầu
        OnInput?.Invoke(buttonId, true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isHeld)
            return;

        isHeld = false;

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
        if (isHeld)
        {
            OnInput?.Invoke(buttonId, true);
        }
    }

    private void OnDisable()
    {
        if (!isHeld)
            return;

        isHeld = false;

        // Nếu đang Hold mà object bị disable,
        // phải báo release để handler không nghĩ rằng nút vẫn đang giữ.
        if (allowHold)
        {
            OnInput?.Invoke(buttonId, false);
        }
    }
}

