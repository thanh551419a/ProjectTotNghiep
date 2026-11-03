using UnityEngine;
using UnityEditor;

public static class TestRunner
{
    public static void RunTest()
    {
        Debug.Log("=== TEST RUNNER SUCCESSFUL ===");
        EditorApplication.Exit(0);
    }
}
