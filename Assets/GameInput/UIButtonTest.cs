using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIButtonInputTest : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private int buttonId;
    [SerializeField] private bool allowHold = true;

    public static bool IsLayoutEditMode { get; private set; }
    public static event Action<bool> LayoutEditModeChanged;

    public event Action<int, bool> OnInput;
    public event Action<int> OnHold;
    public event Action<int> OnLongPress;
    public event Action<int, bool> OnHover;
    public event Action<bool> PressedStateChanged;
    public const float LongPressSeconds = 0.22f;

    private static readonly List<UIButtonInputTest> RegisteredInstances = new List<UIButtonInputTest>(16);

    private readonly HashSet<int> activePointers = new HashSet<int>();
    private Coroutine longPressRoutine;
    private bool isPressed;

    public int ButtonId => buttonId;
    public bool AllowHold => allowHold;
    public bool IsPressed => isPressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        IsLayoutEditMode = false;
        LayoutEditModeChanged = null;
        RegisteredInstances.Clear();
    }

    public static void SetLayoutEditMode(bool enabled)
    {
        if (IsLayoutEditMode == enabled)
            return;

        IsLayoutEditMode = enabled;

        if (enabled)
        {
            for (int i = RegisteredInstances.Count - 1; i >= 0; i--)
            {
                if (i >= RegisteredInstances.Count)
                    continue;

                var instance = RegisteredInstances[i];
                if (instance == null)
                {
                    RegisteredInstances.RemoveAt(i);
                    continue;
                }

                instance.ReleasePressedState(notifyGameplayInput: true);
            }
        }

        LayoutEditModeChanged?.Invoke(enabled);
    }

    private void OnEnable()
    {
        if (!RegisteredInstances.Contains(this))
            RegisteredInstances.Add(this);
    }

    private void Update()
    {
        if (IsLayoutEditMode || !allowHold || !isPressed || activePointers.Count == 0)
            return;

        OnHold?.Invoke(buttonId);
    }

    private void OnDestroy()
    {
        RegisteredInstances.Remove(this);
    }

    public void Configure(int id, bool hold)
    {
        buttonId = id;
        allowHold = hold;
    }

    public void CancelPress()
    {
        ReleasePressedState(notifyGameplayInput: !IsLayoutEditMode);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsLayoutEditMode)
            return;

        if (!RegisteredInstances.Contains(this))
            RegisteredInstances.Add(this);

        if (activePointers.Add(eventData.pointerId) && activePointers.Count == 1)
        {
            if (!isPressed)
            {
                isPressed = true;
                PressedStateChanged?.Invoke(true);
            }

            OnInput?.Invoke(buttonId, true);
            if (isActiveAndEnabled)
                longPressRoutine = StartCoroutine(NotifyLongPress());
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (IsLayoutEditMode)
        {
            ReleasePressedState(notifyGameplayInput: false);
            return;
        }

        if (!activePointers.Remove(eventData.pointerId) || activePointers.Count != 0)
            return;

        if (longPressRoutine != null) StopCoroutine(longPressRoutine);
        longPressRoutine = null;

        if (isPressed)
        {
            isPressed = false;
            PressedStateChanged?.Invoke(false);
        }

        // Chỉ button cho phép Hold mới cần báo Release
        if (allowHold)
        {
            OnInput?.Invoke(buttonId, false);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsLayoutEditMode)
            return;

        OnHover?.Invoke(buttonId, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (IsLayoutEditMode)
            return;

        OnHover?.Invoke(buttonId, false);
    }

    private System.Collections.IEnumerator NotifyLongPress()
    {
        yield return new WaitForSecondsRealtime(LongPressSeconds);
        longPressRoutine = null;
        if (!IsLayoutEditMode && activePointers.Count != 0) OnLongPress?.Invoke(buttonId);
    }

    private void OnDisable()
    {
        // Nếu đang Hold mà object bị disable,
        // phải báo release để handler không nghĩ rằng nút vẫn đang giữ.
        ReleasePressedState(notifyGameplayInput: !IsLayoutEditMode);
    }

    private void ReleasePressedState(bool notifyGameplayInput)
    {
        bool hadActivePointers = activePointers.Count > 0;
        if (!hadActivePointers && !isPressed)
            return;

        activePointers.Clear();
        if (longPressRoutine != null) StopCoroutine(longPressRoutine);
        longPressRoutine = null;

        if (isPressed)
        {
            isPressed = false;
            PressedStateChanged?.Invoke(false);
        }

        // Chỉ button cho phép Hold mới cần báo Release
        if (notifyGameplayInput && allowHold && hadActivePointers)
        {
            OnInput?.Invoke(buttonId, false);
        }
    }
}

