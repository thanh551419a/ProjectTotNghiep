using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIButtonInputTest : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private int buttonId;
    [SerializeField] private bool allowHold = true;

    public event Action<int, bool> OnInput;
    public event Action<int> OnLongPress;
    public event Action<int, bool> OnHover;
    public const float LongPressSeconds = 0.22f;

    private readonly HashSet<int> activePointers = new HashSet<int>();
    private Coroutine longPressRoutine;

    public int ButtonId => buttonId;

    public void Configure(int id, bool hold)
    {
        buttonId = id;
        allowHold = hold;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (activePointers.Add(eventData.pointerId) && activePointers.Count == 1)
        {
            OnInput?.Invoke(buttonId, true);
            longPressRoutine = StartCoroutine(NotifyLongPress());
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!activePointers.Remove(eventData.pointerId) || activePointers.Count != 0)
            return;

        if (longPressRoutine != null) StopCoroutine(longPressRoutine);
        longPressRoutine = null;

        // Chỉ button cho phép Hold mới cần báo Release
        if (allowHold)
        {
            OnInput?.Invoke(buttonId, false);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        OnHover?.Invoke(buttonId, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        OnHover?.Invoke(buttonId, false);
    }

    private System.Collections.IEnumerator NotifyLongPress()
    {
        yield return new WaitForSecondsRealtime(LongPressSeconds);
        longPressRoutine = null;
        if (activePointers.Count != 0) OnLongPress?.Invoke(buttonId);
    }

    private void OnDisable()
    {
        if (activePointers.Count == 0)
            return;
        activePointers.Clear();
        if (longPressRoutine != null) StopCoroutine(longPressRoutine);
        longPressRoutine = null;

        // Nếu đang Hold mà object bị disable,
        // phải báo release để handler không nghĩ rằng nút vẫn đang giữ.
        if (allowHold)
        {
            OnInput?.Invoke(buttonId, false);
        }
    }
}

