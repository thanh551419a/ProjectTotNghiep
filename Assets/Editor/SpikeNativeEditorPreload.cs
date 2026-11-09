using System.Runtime.CompilerServices;
using Native;
using UnityEditor;

/// <summary>
/// Loads the existing native bridge before Mono prepares gameplay P/Invoke wrappers.
/// SpikeNativeBridge's static constructor retains the current plugin path and loader policy.
/// </summary>
[InitializeOnLoad]
internal static class SpikeNativeEditorPreload
{
    static SpikeNativeEditorPreload()
    {
        RuntimeHelpers.RunClassConstructor(typeof(SpikeNativeBridge).TypeHandle);
    }
}
