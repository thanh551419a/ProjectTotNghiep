using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Native
{
    // ==============================================================
    // BUTTON BITMASKS (Matches C++ SimButton)
    // ==============================================================
    public static class SimButton
    {
        public const uint None        = 0;
        public const uint MoveLeft    = 1 << 0; // 1
        public const uint MoveRight   = 1 << 1; // 2
        public const uint JumpSpike   = 1 << 2; // 4
        public const uint Bump        = 1 << 3; // 8
        public const uint Set         = 1 << 4; // 16
        public const uint Serve       = 1 << 5; // 32
        public const uint SpikeLight  = 1 << 6; // 64
        public const uint SpikeMedium = 1 << 7; // 128
        public const uint SpikeStrong = 1 << 8; // 256
        public const uint Jump        = 1 << 9; // 512 (mask, not configured input ID)
    }

    // ==============================================================
    // ABSTRACT BUTTON IDS & STATES (Contract Section 5)
    // ==============================================================
    public static class SimButtonId
    {
        public const int MoveLeft    = 0;
        public const int MoveRight   = 1;
        public const int JumpSpike   = 2;
        public const int Bump        = 3;
        public const int Set         = 4;
        public const int Serve       = 5;
        public const int SpikeLight  = 6;
        public const int SpikeMedium = 7;
        public const int SpikeStrong = 8;
        public const int Jump        = 9; // direct Sim_SubmitButtonInput ID; Get_data uses configured ID 5
        public const int TotalCount  = 10;
    }

    public static class SimButtonState
    {
        public const int Released = 0;
        public const int Pressed  = 1;
        public const int Held     = 2;
    }

    public enum SimActionState : int
    {
        None        = 0,
        Jump        = 1,
        Spike       = 2,
        Slide       = 3,
        Serve       = 4,
        Bump        = 5,
        Set         = 6,
        SpikeLight  = 7,
        SpikeMedium = 8,
        SpikeStrong = 9
    }

    // ==============================================================
    // BLITTABLE SNAPSHOT STRUCTURES (Pack = 4)
    // ==============================================================
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SimEntitySnapshot
    {
        public float posX;              // World horizontal position (pixels)
        public float posY;              // World vertical position (pixels)
        public float sizeX;             // Hitbox width
        public float sizeY;             // Hitbox height
        public int actionState;         // SimActionState
        public float actionRemainTime;  // Cooldown remaining
        public float moveX;             // Horizontal velocity / intent
        public int isGrounded;          // 1 if on floor, else 0
        public int facingDirection;     // -1: Left, 1: Right
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SimBallSnapshot
    {
        public float posX;              // World horizontal position
        public float posY;              // World vertical position
        public float speed;             // Horizontal trajectory speed
        public int trajectoryType;      // 0: Linear, 1: Parabolic
        public float landingX;          // Predicted floor landing coordinate
        public int stateFrame;          // -1: Alive, 0: Reset, >0: Resetting countdown
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SimMatchSnapshot
    {
        public int leftScore;
        public int rightScore;
        public int servingTeam;         // 0: Left, 1: Right
        public int servingEntity;       // Entity index currently serving
        public int finished;            // 1 if match finished
        public int matchWinner;         // Team enum: 0 LEFT, 1 RIGHT, 2 NONE (snapshot schema enum "Team")
        public int lastTouchEntity;     // Last entity index to touch the ball
        public int touchCount;          // Consecutive touches on current side
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SimSnapshot
    {
        public ulong frameNumber;

        public SimEntitySnapshot entity0; // Player 1 (User)
        public SimEntitySnapshot entity1; // Teammate 1
        public SimEntitySnapshot entity2; // Teammate 2
        public SimEntitySnapshot entity3; // Opponent 1 (AI)
        public SimEntitySnapshot entity4; // Opponent 2 (AI)
        public SimEntitySnapshot entity5; // Opponent 3 (AI)

        public SimEntitySnapshot net;     // Entity 6: Net
        public SimBallSnapshot ball;      // Entity 7: Ball
        public SimMatchSnapshot match;    // Match score & rules

        public SimEntitySnapshot GetEntity(int index)
        {
            switch (index)
            {
                case 0: return entity0;
                case 1: return entity1;
                case 2: return entity2;
                case 3: return entity3;
                case 4: return entity4;
                case 5: return entity5;
                default: return default;
            }
        }

        public void CopyEntitiesTo(SimEntitySnapshot[] target)
        {
            if (target == null || target.Length < 6) return;
            target[0] = entity0;
            target[1] = entity1;
            target[2] = entity2;
            target[3] = entity3;
            target[4] = entity4;
            target[5] = entity5;
        }
    }

    // ==============================================================
    // BLITTABLE AUTHORITATIVE GET_DATA STRUCTURES (Pack = 4)
    // Matches SimulationApi.h ABI
    // ==============================================================
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimInput
    {
        public uint buttonCount;
        public uint btn0;
        public uint btn1;
        public uint btn2;
        public uint btn3;
        public uint btn4;
        public uint btn5;
        public uint btn6;
        public uint btn7;

        public static NativeSimInput Empty => new NativeSimInput();

        public static NativeSimInput FromButtons(params uint[] buttons)
        {
            var input = new NativeSimInput();
            if (buttons == null || buttons.Length == 0) return input;
            input.buttonCount = (uint)Math.Min(buttons.Length, 8);
            if (input.buttonCount > 0) input.btn0 = buttons[0];
            if (input.buttonCount > 1) input.btn1 = buttons[1];
            if (input.buttonCount > 2) input.btn2 = buttons[2];
            if (input.buttonCount > 3) input.btn3 = buttons[3];
            if (input.buttonCount > 4) input.btn4 = buttons[4];
            if (input.buttonCount > 5) input.btn5 = buttons[5];
            if (input.buttonCount > 6) input.btn6 = buttons[6];
            if (input.buttonCount > 7) input.btn7 = buttons[7];
            return input;
        }

        public uint GetButton(int index)
        {
            switch (index)
            {
                case 0: return btn0;
                case 1: return btn1;
                case 2: return btn2;
                case 3: return btn3;
                case 4: return btn4;
                case 5: return btn5;
                case 6: return btn6;
                case 7: return btn7;
                default: return 0;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimVec2
    {
        public float x;
        public float y;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimComponentFlags
    {
        public int hasPosition;
        public int hasSize;
        public int hasJumpUpFrame;
        public int hasCharacterActionState;
        public int hasCharacterIntent;
        public int hasVelocityIntent;
        public int hasBallTrajectory;
        public int hasBallGameplayState;

        public bool HasPosition => hasPosition != 0;
        public bool HasSize => hasSize != 0;
        public bool HasJumpUpFrame => hasJumpUpFrame != 0;
        public bool HasCharacterActionState => hasCharacterActionState != 0;
        public bool HasCharacterIntent => hasCharacterIntent != 0;
        public bool HasVelocityIntent => hasVelocityIntent != 0;
        public bool HasBallTrajectory => hasBallTrajectory != 0;
        public bool HasBallGameplayState => hasBallGameplayState != 0;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimPosition
    {
        public NativeSimVec2 position;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimSize
    {
        public NativeSimVec2 size;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimJumpUpFrame
    {
        public int remainingFrames;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimCharacterActionState
    {
        public int status;
        public float remainTime;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimCharacterIntent
    {
        public float moveX;
        public int finalIntent;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimVelocityIntent
    {
        public float vx;
        public float vy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimBallTrajectory
    {
        public int type;
        public float speed;
        public float a;
        public float b;
        public float c;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimBallGameplayState
    {
        public float landingX;
        public int stateFrame;
        public int collisionState;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimRoleName32
    {
        public ulong part0;
        public ulong part1;
        public ulong part2;
        public ulong part3;

        public string AsString()
        {
            byte[] bytes = new byte[32];
            BitConverter.GetBytes(part0).CopyTo(bytes, 0);
            BitConverter.GetBytes(part1).CopyTo(bytes, 8);
            BitConverter.GetBytes(part2).CopyTo(bytes, 16);
            BitConverter.GetBytes(part3).CopyTo(bytes, 24);
            int len = 0;
            while (len < 32 && bytes[len] != 0) len++;
            return len > 0 ? Encoding.ASCII.GetString(bytes, 0, len) : string.Empty;
        }
    }

    /// <summary>
    /// Mirrors native SimMatchStateData (SimulationApi.h): 7 x 4 bytes = 28 bytes.
    /// NOTE: leftServingEntity/rightServingEntity MUST be present; omitting them shifts
    /// finished/matchWinner and shrinks NativeSimData by 8 bytes (native struct return overflow).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimMatchState
    {
        public int leftScore;
        public int rightScore;
        public int servingTeam;
        public uint leftServingEntity;
        public uint rightServingEntity;
        public int finished;
        public int matchWinner;
    }

    /// <summary>Mirrors native SimRallyStateData: uint32 lastTouch, uint8 touchCount, uint8[3] padding (8 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimRallyState
    {
        public uint lastTouch;
        public byte touchCount;
        public byte pad0;
        public byte pad1;
        public byte pad2;
    }

    /// <summary>
    /// Expected native layout of the Get_data C ABI, measured from the native header with
    /// SimulationDllTest/abi_probe (MSVC x64; identical rules under #pragma pack(4) on arm64).
    /// </summary>
    public static class NativeAbiLayout
    {
        public const int SizeOfData = 4772;
        public const int SizeOfInput = 36;
        public const int SizeOfEntity = 156;
        public const int OffsetOfMatchState = 640;
        public const int OffsetOfRallyState = 668;
        public const int OffsetOfFormattedJson = 676;

        /// <summary>Returns null when the managed marshaling layout matches the native ABI, else a description.</summary>
        public static string Validate()
        {
            var problems = new System.Collections.Generic.List<string>();
            void Check(string what, int actual, int expected)
            {
                if (actual != expected) problems.Add($"{what}={actual} (native {expected})");
            }
            Check("sizeof(NativeSimData)", Marshal.SizeOf<NativeSimData>(), SizeOfData);
            Check("sizeof(NativeSimInput)", Marshal.SizeOf<NativeSimInput>(), SizeOfInput);
            Check("sizeof(NativeSimEntity)", Marshal.SizeOf<NativeSimEntity>(), SizeOfEntity);
            Check("offsetof(matchState)", (int)Marshal.OffsetOf<NativeSimData>(nameof(NativeSimData.matchState)), OffsetOfMatchState);
            Check("offsetof(rallyState)", (int)Marshal.OffsetOf<NativeSimData>(nameof(NativeSimData.rallyState)), OffsetOfRallyState);
            Check("offsetof(formattedJson)", (int)Marshal.OffsetOf<NativeSimData>(nameof(NativeSimData.formattedJson)), OffsetOfFormattedJson);
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 4096)]
    public struct NativeSimFormattedJson4096
    {
        public byte firstByte;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimEntity
    {
        public uint entityId;
        public NativeSimRoleName32 roleName;
        public int team;
        public int isGrounded;
        public int facingDirection;

        public NativeSimComponentFlags flags;
        public NativeSimPosition position;
        public NativeSimSize size;
        public NativeSimJumpUpFrame jumpFrame;
        public NativeSimCharacterActionState actionState;
        public NativeSimCharacterIntent characterIntent;
        public NativeSimVelocityIntent velocityIntent;
        public NativeSimBallTrajectory ballTrajectory;
        public NativeSimBallGameplayState ballGameplayState;

        public string RoleName => roleName.AsString();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimData
    {
        public int status;
        public ulong tick;
        public int entityCount;

        public NativeSimEntity entity0; // PLAYER (0)
        public NativeSimEntity entity1; // OPPONENT_1 (3)
        public NativeSimEntity entity2; // BALL (7)
        public NativeSimEntity entity3; // NET (6)

        public NativeSimMatchState matchState;
        public NativeSimRallyState rallyState;
        public NativeSimFormattedJson4096 formattedJson;

        public NativeSimEntity GetEntity(int index)
        {
            switch (index)
            {
                case 0: return entity0;
                case 1: return entity1;
                case 2: return entity2;
                case 3: return entity3;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public bool TryGetEntityById(uint id, out NativeSimEntity entity)
        {
            if (entity0.entityId == id) { entity = entity0; return true; }
            if (entity1.entityId == id) { entity = entity1; return true; }
            if (entity2.entityId == id) { entity = entity2; return true; }
            if (entity3.entityId == id) { entity = entity3; return true; }
            entity = default;
            return false;
        }
    }

    // ==============================================================
    // NATIVE C P/INVOKE DECLARATIONS
    // ==============================================================
    public static class SpikeNativeBridge
    {
        private const string DLL_NAME = "SpikeCrossCore";

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
#endif

        static SpikeNativeBridge()
        {
#if UNITY_EDITOR_WIN
            try
            {
                string pluginDir = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Plugins", "x86_64");
                if (System.IO.Directory.Exists(pluginDir))
                {
                    SetDllDirectory(pluginDir);
                    string dllPath = System.IO.Path.Combine(pluginDir, "SpikeCrossCore.dll");
                    if (System.IO.File.Exists(dllPath))
                    {
                        IntPtr handle = LoadLibrary(dllPath);
                        if (handle != IntPtr.Zero)
                        {
                            UnityEngine.Debug.Log($"[SpikeNativeBridge] Successfully preloaded native DLL: {dllPath}");
                        }
                        else
                        {
                            int err = Marshal.GetLastWin32Error();
                            UnityEngine.Debug.LogWarning($"[SpikeNativeBridge] LoadLibrary failed for {dllPath} with error code: {err}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SpikeNativeBridge] Exception while preloading native library: {ex.Message}");
            }
#endif
        }

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Sim_Initialize(string configJsonPath);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Sim_Reset();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Sim_CheckAndResetMatch();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Sim_SetInput(uint entityId, uint buttonMask, float moveAxisX);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Sim_SubmitButtonInput(uint entityId, int buttonId, int inputState);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Sim_Update(float dt);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Sim_GetSnapshot(out SimSnapshot outSnapshot);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Sim_Shutdown();

        // ==============================================================
        // PRIMARY AUTHORITATIVE API: Get_data(Input)
        // ==============================================================
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern NativeSimData Get_data(NativeSimInput inputSend);

        // ==============================================================
        // API v2: COMPLETE AUTHORITATIVE SNAPSHOT CONTRACT
        // (see SpikeNativeApi.h / THE_SPIKE_NATIVE_API.md)
        // ==============================================================
        public const int SIM_OK = 0;
        public const int SIM_ERR_NOT_INITIALIZED = -1;
        public const int SIM_ERR_BUFFER_TOO_SMALL = -2;
        public const int SIM_ERR_INVALID_ARGUMENT = -3;
        public const int SIM_ERR_IO = -4;

        public const uint SIM_DIAG_SYSTEM_TRACE = 1u << 0;
        public const uint SIM_DIAG_FIELD_CHANGES = 1u << 1;
        public const uint SIM_DIAG_TRANSITIONS = 1u << 2;
        public const uint SIM_DIAG_INPUT = 1u << 3;
        public const uint SIM_DIAG_SNAPSHOT_INITIAL = 1u << 4;
        public const uint SIM_DIAG_SNAPSHOT_EVERY_TICK = 1u << 5;
        public const uint SIM_DIAG_INTENTS = 1u << 6;
        public const uint SIM_DIAG_LIFECYCLE = 1u << 7;
        public const uint SIM_DIAG_SYSTEM_TRACE_CHANGED_ONLY = 1u << 8;

        public const int EXPECTED_API_VERSION = 2;
        public const int EXPECTED_SNAPSHOT_SCHEMA_VERSION = 1;

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Sim_GetApiVersion();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Sim_GetSnapshotSchemaVersion();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong Sim_GetTick();

        /// <summary>buffer may be null with capacity 0 to query the size (includes the NUL).</summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Sim_GetSnapshotJson(byte[] buffer, int capacity, out int requiredBytes);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Sim_GetSchemaJson(byte[] buffer, int capacity, out int requiredBytes);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Sim_GetLastError(byte[] buffer, int capacity, out int requiredBytes);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int Sim_SetDiagnostics(uint flags, string outputPath);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Sim_RequestDiagnosticSnapshot(ulong tick);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Sim_SetLegacyLogEnabled(int enabled);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Sim_SeedRandom(uint seed);
    }

    /// <summary>
    /// Reads the variable-size UTF-8 payloads of API v2 (snapshot, schema, last error)
    /// following the caller-owned buffer contract: the payload is never truncated; when
    /// the buffer is too small the native side reports the required size and the call is
    /// repeated with a larger buffer. Single-threaded (same thread as Sim_Update).
    /// </summary>
    public static class SpikeNativeJson
    {
        public delegate int PayloadGetter(byte[] buffer, int capacity, out int requiredBytes);

        private static byte[] snapshotBuffer = new byte[16 * 1024];
        private static byte[] scratchBuffer = new byte[4 * 1024];

        public static int LastRequiredBytes { get; private set; }

        public static int GetSnapshotJson(out string json) =>
            Read(SpikeNativeBridge.Sim_GetSnapshotJson, ref snapshotBuffer, out json);

        public static int GetSchemaJson(out string json) =>
            Read(SpikeNativeBridge.Sim_GetSchemaJson, ref scratchBuffer, out json);

        public static string GetLastError()
        {
            byte[] buf = new byte[512];
            return Read(SpikeNativeBridge.Sim_GetLastError, ref buf, out string s) == SpikeNativeBridge.SIM_OK ? s : string.Empty;
        }

        public static int Read(PayloadGetter getter, ref byte[] buffer, out string payload)
        {
            payload = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int rc = getter(buffer, buffer.Length, out int required);
                LastRequiredBytes = required;
                if (rc == SpikeNativeBridge.SIM_OK)
                {
                    if (required < 1 || required > buffer.Length)
                        return SpikeNativeBridge.SIM_ERR_INVALID_ARGUMENT;
                    payload = Encoding.UTF8.GetString(buffer, 0, required - 1); // exclude NUL
                    return rc;
                }
                if (rc != SpikeNativeBridge.SIM_ERR_BUFFER_TOO_SMALL || required <= buffer.Length)
                    return rc;
                buffer = new byte[Math.Max(required, buffer.Length * 2)];
            }
            return SpikeNativeBridge.SIM_ERR_BUFFER_TOO_SMALL;
        }

        /// <summary>True when the loaded native library exports API v2 with the expected versions.</summary>
        public static bool ProbeApiV2(out string detail)
        {
            try
            {
                int api = SpikeNativeBridge.Sim_GetApiVersion();
                int schema = SpikeNativeBridge.Sim_GetSnapshotSchemaVersion();
                if (api != SpikeNativeBridge.EXPECTED_API_VERSION || schema != SpikeNativeBridge.EXPECTED_SNAPSHOT_SCHEMA_VERSION)
                {
                    detail = $"native apiVersion={api} schemaVersion={schema}, expected {SpikeNativeBridge.EXPECTED_API_VERSION}/{SpikeNativeBridge.EXPECTED_SNAPSHOT_SCHEMA_VERSION}";
                    return false;
                }
                detail = $"apiVersion={api} schemaVersion={schema}";
                return true;
            }
            catch (EntryPointNotFoundException ex)
            {
                detail = "native library predates API v2 (missing export): " + ex.Message;
                return false;
            }
        }
    }
}
