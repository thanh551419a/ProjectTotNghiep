using System.Collections.Generic;

public static class UIInputTable
{
    private static readonly Dictionary<int, bool> inputs = new();

    public static void SetInput(int buttonId, bool isPressed)
    {
        inputs[buttonId] = isPressed;
    }

    public static bool IsPressed(int buttonId)
    {
        return inputs.TryGetValue(buttonId, out bool pressed) && pressed;
    }

    public static IReadOnlyDictionary<int, bool> Inputs => inputs;

    public static void Clear()
    {
        inputs.Clear();
    }
}