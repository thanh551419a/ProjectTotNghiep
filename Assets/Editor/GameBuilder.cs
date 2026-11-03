using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EditorTests
{
    public static class GameBuilder
    {
        public const string GameplayScene = "Assets/Resources/New Scene.unity";
        public const string ReleaseApkPath = @"y:\Learn\ProjectTotNghiep\Release\Android\TheSpikeCross-v1.0-arm64.apk";
        public const string AndroidSoPath = "Assets/Plugins/Android/libs/arm64-v8a/libSpikeCrossCore.so";
        public const string MappingPath = "Assets/StreamingAssets/button_mapping.json";

        /// <summary>Development Windows player (enables the env-var gated self-test / real framebuffer captures).</summary>
        public static void BuildWindowsDevPlayer()
        {
            string outputPath = @"y:\Learn\ProjectTotNghiep\Builds\WindowsDev\TheSpikeCross.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { GameplayScene },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Debug.Log($"[BUILDER] Windows dev player: {report.summary.result} size={report.summary.totalSize} errors={report.summary.totalErrors} -> {outputPath}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>Fails the build early if the Android native library is not a genuine AArch64 ELF or the mapping is missing.</summary>
        private static string AndroidPreflight()
        {
            if (!File.Exists(AndroidSoPath)) return "missing " + AndroidSoPath;
            byte[] h = new byte[20];
            using (var f = File.OpenRead(AndroidSoPath)) f.Read(h, 0, h.Length);
            bool elf = h[0] == 0x7F && h[1] == (byte)'E' && h[2] == (byte)'L' && h[3] == (byte)'F';
            bool is64 = h[4] == 2;
            int machine = h[18] | (h[19] << 8);
            if (!elf || !is64 || machine != 0xB7) return $"{AndroidSoPath} is not an ELF64 AArch64 shared object (elf={elf} class64={is64} e_machine=0x{machine:X})";
            if (!File.Exists(MappingPath)) return "missing " + MappingPath;
            return null;
        }

        private static void VerifyApk(string apk)
        {
            var lines = new System.Collections.Generic.List<string>();
            using (var zip = System.IO.Compression.ZipFile.OpenRead(apk))
            {
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.StartsWith("lib/") || e.FullName.Contains("button_mapping") || e.FullName == "AndroidManifest.xml")
                        lines.Add($"{e.FullName}  {e.Length} bytes");
                }
            }
            bool hasSo = lines.Exists(l => l.StartsWith("lib/arm64-v8a/libSpikeCrossCore.so"));
            bool hasMap = lines.Exists(l => l.StartsWith("assets/button_mapping.json"));
            lines.Insert(0, $"APK {apk} size={new FileInfo(apk).Length} libSpikeCrossCore.so={hasSo} button_mapping.json={hasMap}");
            File.WriteAllLines(Path.ChangeExtension(apk, ".contents.txt"), lines);
            foreach (var l in lines) Debug.Log("[BUILDER][APK] " + l);
            if (!hasSo || !hasMap) throw new Exception("APK verification failed: native library or button mapping not packaged");
        }
        public static void BuildStandalonePlayer()
        {
            Debug.Log("==================================================================");
            Debug.Log("[BUILDER] Building The Spike Cross Standalone Windows Player...");
            Debug.Log("==================================================================");

            string[] scenes = new string[] {
                "Assets/Resources/New Scene.unity"
            };

            string outputDir = @"y:\Learn\ProjectTotNghiep\Builds\TheSpikeCross";
            string outputPath = Path.Combine(outputDir, "TheSpikeCross.exe");

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BUILDER] Standalone build succeeded! Size: {summary.totalSize} bytes. Location: {outputPath}");

                // Copy SpikeCrossCore.dll to build directory next to executable as an extra safeguard
                string pluginSrc = @"y:\Learn\ProjectTotNghiep\Unity\TheSpike\My project\Assets\Plugins\x86_64\SpikeCrossCore.dll";
                string pluginDstExe = Path.Combine(outputDir, "SpikeCrossCore.dll");
                string pluginDstPlugins = Path.Combine(outputDir, "TheSpikeCross_Data", "Plugins", "x86_64", "SpikeCrossCore.dll");

                try
                {
                    if (File.Exists(pluginSrc))
                    {
                        File.Copy(pluginSrc, pluginDstExe, true);
                        Debug.Log($"[BUILDER] Copied native library next to exe: {pluginDstExe}");

                        string pluginsDir = Path.GetDirectoryName(pluginDstPlugins);
                        if (pluginsDir != null && !Directory.Exists(pluginsDir))
                        {
                            Directory.CreateDirectory(pluginsDir);
                        }
                        File.Copy(pluginSrc, pluginDstPlugins, true);
                        Debug.Log($"[BUILDER] Copied native library to Plugins folder: {pluginDstPlugins}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BUILDER] Note on copying DLL: {ex.Message}");
                }

                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[BUILDER] Standalone build failed with result: {summary.result}");
                EditorApplication.Exit(1);
            }
        }

        public static void BuildAndroidApk()
        {
            Debug.Log("==================================================================");
            Debug.Log("[BUILDER] Building The Spike Cross Android APK...");
            Debug.Log("==================================================================");

            // Configure SDK, NDK, JDK paths
            string ndkPath = @"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\NDK";
            string sdkPath = @"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK";
            string jdkPath = @"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK";

            if (Directory.Exists(ndkPath))
            {
                EditorPrefs.SetString("AndroidNdkRoot", ndkPath);
                EditorPrefs.SetString("AndroidNdkRootR27", ndkPath);
                Environment.SetEnvironmentVariable("ANDROID_NDK_ROOT", ndkPath);
                Environment.SetEnvironmentVariable("ANDROID_NDK_HOME", ndkPath);
            }
            if (Directory.Exists(sdkPath))
            {
                EditorPrefs.SetString("AndroidSdkRoot", sdkPath);
                Environment.SetEnvironmentVariable("ANDROID_HOME", sdkPath);
                Environment.SetEnvironmentVariable("ANDROID_SDK_ROOT", sdkPath);
            }
            if (Directory.Exists(jdkPath))
            {
                EditorPrefs.SetString("JdkPath", jdkPath);
                Environment.SetEnvironmentVariable("JAVA_HOME", jdkPath);
            }

            string[] scenes = new string[] { GameplayScene };

            string outputPath = ReleaseApkPath;
            string outputDir = Path.GetDirectoryName(outputPath);

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            string preflight = AndroidPreflight();
            if (preflight != null)
            {
                Debug.LogError("[BUILDER] Android preflight failed: " + preflight);
                EditorApplication.Exit(2);
                return;
            }

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.Generic;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BUILDER] Android APK build succeeded! Size: {summary.totalSize} bytes. Location: {outputPath}");
                try
                {
                    VerifyApk(outputPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[BUILDER] " + ex.Message);
                    EditorApplication.Exit(3);
                    return;
                }
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[BUILDER] Android APK build failed with result: {summary.result}");
                EditorApplication.Exit(1);
            }
        }
    }
}
