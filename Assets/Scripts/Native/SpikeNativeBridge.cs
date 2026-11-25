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

    public enum SimAirborneState : short
    {
        Grounded = 0,
        JumpUp   = 1,
        Fall     = 2
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

    public enum SimAnimId : int
    {
        None        = 0,
        Idle        = 10,
        Move        = 20,
        JumpUp      = 30,
        Fall        = 31,
        Spike       = 40,
        Bump        = 50,
        SpikeLight  = 60,
        SpikeMedium = 70,
        SpikeStrong = 80,
        Serve       = 90
    }

    /// <summary>
    /// Authoritative character FinalIntent values exported via NativeSimCharacterIntent.finalIntent.
    /// Matches C++ FinalIntent.h 1-to-1 (PrepareAttackServe = 12, LaunchAttackServe = 13).
    /// </summary>
    public enum SimIntent : int
    {
        None               = 0,
        Jump               = 1,
        MoveLeft           = 2,
        MoveRight          = 3,
        Spike              = 4,
        Bump               = 5,
        Set                = 6,
        Block              = 7,
        Serve              = 8,
        SpikeLight         = 9,
        SpikeMedium        = 10,
        SpikeStrong        = 11,
        PrepareAttackServe = 12,
        LaunchAttackServe  = 13
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
    public struct SimCharacterPresentationData
    {
        public int animId;
        public int cooldownAction;
        public float cooldownRemainMs;
        public float cooldownDurationMs;
        public int reserved;
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

    /// <summary>Mirrors native SimRallyStateData: uint32 lastTouch, uint8 touchCount, uint8 slowMoFramesRemaining, uint16 spikeContactHeight (8 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeSimRallyState
    {
        public uint lastTouch;
        public byte touchCount;
        public byte slowMoFramesRemaining;
        public ushort spikeContactHeight;
    }

    /// <summary>
    /// Mirrors native SimAttackServeConfig (6 contiguous floats = 24 bytes in C++ RAM)
    /// exposed directly via Sim_GetAttackServeConfigPtr().
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeAttackServeConfig
    {
        public float baseA;
        public float maxA;
        public float baseC;
        public float maxC;
        public float totalStep;
        public float tossV0;

        public static NativeAttackServeConfig Default => new NativeAttackServeConfig
        {
            baseA = -20.0f,
            maxA = -18.0f,
            baseC = 465.0f,
            maxC = 800.0f,
            totalStep = 60.0f,
            tossV0 = 30.0f
        };

        public static bool AreEqual(in NativeAttackServeConfig a, in NativeAttackServeConfig b)
        {
            return Math.Abs(a.baseA - b.baseA) <= 0.0000005f &&
                   Math.Abs(a.maxA - b.maxA) <= 0.0000005f &&
                   Math.Abs(a.baseC - b.baseC) <= 0.0005f &&
                   Math.Abs(a.maxC - b.maxC) <= 0.0005f &&
                   Math.Abs(a.totalStep - b.totalStep) <= 0.0005f &&
                   Math.Abs(a.tossV0 - b.tossV0) <= 0.0005f;
        }
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
        public short isGrounded;
        public short airborneState;
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

        [StructLayout(LayoutKind.Explicit, Size = 4)]
        private struct FloatIntBits
        {
            [FieldOffset(0)] public float f;
            [FieldOffset(0)] public int i;
        }

        public SimCharacterPresentationData GetCharacterPresentation()
        {
            return new SimCharacterPresentationData
            {
                animId = ballTrajectory.type,
                cooldownAction = new FloatIntBits { f = ballTrajectory.speed }.i,
                cooldownRemainMs = ballTrajectory.a,
                cooldownDurationMs = ballTrajectory.b,
                reserved = new FloatIntBits { f = ballTrajectory.c }.i
            };
        }
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

        public const int ENTITY_PLAYER = 0;
        public const int ENTITY_PLAYER_LEFT = 0;
        public const int ENTITY_OPPONENT_1 = 3;
        public const int ENTITY_PLAYER_RIGHT = 3;
        public const int ENTITY_NET = 6;
        public const int ENTITY_BALL = 7;

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
#endif

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool Del_Sim_Initialize(string configJsonPath);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Del_Sim_Void();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Del_Sim_Int();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ulong Del_Sim_Ulong();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float Del_Sim_Float();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Del_Sim_SetInput(uint entityId, uint buttonMask, float moveAxisX);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Del_Sim_SubmitButtonInput(uint entityId, int buttonId, int inputState);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Del_Sim_Update(float dt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Del_Sim_GetSnapshot(out SimSnapshot outSnapshot);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate NativeSimData Del_Get_data(NativeSimInput inputSend);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Del_Sim_GetBuffer(byte[] buffer, int capacity, out int requiredBytes);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int Del_Sim_SetDiagnostics(uint flags, string outputPath);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Del_Sim_RequestDiagnosticSnapshot(ulong tick);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Del_Sim_SetLegacyLogEnabled(int enabled);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Del_Sim_SeedRandom(uint seed);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Del_Sim_GetCharacterStats(uint entityId, out float outAttack, out float outDefense, out float outSpeed, out float outJump);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Del_Sim_SetCharacterStats(uint entityId, float attack, float defense, float speed, float jump);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr Del_Sim_GetAttackServeConfigPtr();

        private static readonly object HotLoadLock = new object();
        private static IntPtr hotModuleHandle = IntPtr.Zero;
        private static DateTime loadedSourceWriteUtc = DateTime.MinValue;
        private static long loadedSourceLength = -1;
        private static int hotLoadSequence;
        private static int hotPollCounter;
        private static int getDataHotReloadCheckCounter;
        private static string lastConfigJsonPath;
        private static string activeShadowDllPath;
        private static bool hotReloadedSinceLastCommit;
        private static bool hasInitialNativeAttackServeConfig;
        private static NativeAttackServeConfig initialNativeAttackServeConfig = NativeAttackServeConfig.Default;
        private static bool hasUserAttackServeConfigOverride;
        private static NativeAttackServeConfig userAttackServeConfigOverride = NativeAttackServeConfig.Default;
        private static bool hasLoggedAttackServeRamConfig;
        private static IntPtr lastLoggedAttackServeRamPtr = IntPtr.Zero;
        private static NativeAttackServeConfig lastLoggedAttackServeRamConfig;

        public static event Action<NativeAttackServeConfig, IntPtr> OnAttackServeConfigRamChanged;
        public static bool HasUserAttackServeConfigOverride => hasUserAttackServeConfigOverride;

        private static Del_Sim_Initialize fn_Sim_Initialize;
        private static Del_Sim_Void fn_Sim_Reset;
        private static Del_Sim_Int fn_Sim_CheckAndResetMatch;
        private static Del_Sim_SetInput fn_Sim_SetInput;
        private static Del_Sim_SubmitButtonInput fn_Sim_SubmitButtonInput;
        private static Del_Sim_Update fn_Sim_Update;
        private static Del_Sim_GetSnapshot fn_Sim_GetSnapshot;
        private static Del_Sim_Void fn_Sim_Shutdown;
        private static Del_Get_data fn_Get_data;
        private static Del_Sim_Int fn_Sim_GetApiVersion;
        private static Del_Sim_Int fn_Sim_GetSnapshotSchemaVersion;
        private static Del_Sim_Ulong fn_Sim_GetTick;
        private static Del_Sim_GetBuffer fn_Sim_GetSnapshotJson;
        private static Del_Sim_GetBuffer fn_Sim_GetSchemaJson;
        private static Del_Sim_GetBuffer fn_Sim_GetLastError;
        private static Del_Sim_SetDiagnostics fn_Sim_SetDiagnostics;
        private static Del_Sim_RequestDiagnosticSnapshot fn_Sim_RequestDiagnosticSnapshot;
        private static Del_Sim_SetLegacyLogEnabled fn_Sim_SetLegacyLogEnabled;
        private static Del_Sim_SeedRandom fn_Sim_SeedRandom;
        private static Del_Sim_Int fn_Sim_GetSlowMoFramesRemaining;
        private static Del_Sim_Float fn_Sim_GetLastSpikeContactHeight;
        private static Del_Sim_GetCharacterStats fn_Sim_GetCharacterStats;
        private static Del_Sim_SetCharacterStats fn_Sim_SetCharacterStats;
        private static Del_Sim_Int fn_Sim_ResetCharacterStats;
        private static Del_Sim_GetAttackServeConfigPtr fn_Sim_GetAttackServeConfigPtr;

        public static string ActiveShadowDllPath => activeShadowDllPath;

        public static bool ConsumeHotReloadedFlag()
        {
            lock (HotLoadLock)
            {
                if (!hotReloadedSinceLastCommit) return false;
                hotReloadedSinceLastCommit = false;
                return true;
            }
        }

        static SpikeNativeBridge()
        {
            EnsureNativeLibraryLoaded(checkDiskTimestamp: true);
        }

        private static TDelegate BindExport<TDelegate>(IntPtr module, string exportName, bool required = true) where TDelegate : class
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (module == IntPtr.Zero) return null;
            IntPtr proc = GetProcAddress(module, exportName);
            if (proc == IntPtr.Zero)
            {
                if (required)
                    throw new EntryPointNotFoundException($"Missing export '{exportName}' in SpikeCrossCore.dll");
                return null;
            }
            return Marshal.GetDelegateForFunctionPointer(proc, typeof(TDelegate)) as TDelegate;
#else
            return null;
#endif
        }

        private static string ResolveAssetsRootPath()
        {
            try
            {
                string cwdAssets = System.IO.Path.GetFullPath("Assets");
                if (System.IO.Directory.Exists(cwdAssets))
                    return cwdAssets;
            }
            catch { }

#if UNITY_5_3_OR_NEWER
            try
            {
                return UnityEngine.Application.dataPath;
            }
            catch
            {
                return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            }
#else
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
#endif
        }

        public static void EnsureNativeLibraryLoaded(bool checkDiskTimestamp = false)
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            lock (HotLoadLock)
            {
                if (hotModuleHandle != IntPtr.Zero && !checkDiskTimestamp)
                    return;

                string assetsRoot = ResolveAssetsRootPath();
                string pluginDir = System.IO.Path.Combine(assetsRoot, "Plugins", "x86_64");
                string sourceDllPath = System.IO.Path.Combine(pluginDir, "SpikeCrossCore.dll");
                if (!System.IO.File.Exists(sourceDllPath))
                    return;

                var fileInfo = new System.IO.FileInfo(sourceDllPath);
                DateTime writeUtc = fileInfo.LastWriteTimeUtc;
                long length = fileInfo.Length;

                if (hotModuleHandle != IntPtr.Zero && writeUtc == loadedSourceWriteUtc && length == loadedSourceLength)
                    return;

                SetDllDirectory(pluginDir);

                string loadTargetPath = sourceDllPath;
#if UNITY_EDITOR_WIN
                try
                {
                    string projectRoot = System.IO.Directory.GetParent(assetsRoot)?.FullName ?? assetsRoot;
                    string shadowDir = System.IO.Path.Combine(projectRoot, "Temp", "SpikeNativeHotReload");
                    System.IO.Directory.CreateDirectory(shadowDir);
                    int seq = System.Threading.Interlocked.Increment(ref hotLoadSequence);
                    string shadowPath = System.IO.Path.Combine(shadowDir, $"SpikeCrossCore_hot_{writeUtc.Ticks}_{seq}.dll");
                    System.IO.File.Copy(sourceDllPath, shadowPath, overwrite: true);
                    loadTargetPath = shadowPath;
                }
                catch
                {
                    loadTargetPath = sourceDllPath;
                }
#endif

                IntPtr newHandle = LoadLibrary(loadTargetPath);
                if (newHandle == IntPtr.Zero)
                    return;

                bool wasPreviouslyLoaded = hotModuleHandle != IntPtr.Zero;
                if (hotModuleHandle != IntPtr.Zero)
                {
                    try { fn_Sim_Shutdown?.Invoke(); } catch { }
                    try { FreeLibrary(hotModuleHandle); } catch { }
                }

                hotModuleHandle = newHandle;
                loadedSourceWriteUtc = writeUtc;
                loadedSourceLength = length;
                activeShadowDllPath = loadTargetPath;

                fn_Sim_Initialize = BindExport<Del_Sim_Initialize>(newHandle, nameof(Sim_Initialize));
                fn_Sim_Reset = BindExport<Del_Sim_Void>(newHandle, nameof(Sim_Reset));
                fn_Sim_CheckAndResetMatch = BindExport<Del_Sim_Int>(newHandle, nameof(Sim_CheckAndResetMatch));
                fn_Sim_SetInput = BindExport<Del_Sim_SetInput>(newHandle, nameof(Sim_SetInput));
                fn_Sim_SubmitButtonInput = BindExport<Del_Sim_SubmitButtonInput>(newHandle, nameof(Sim_SubmitButtonInput));
                fn_Sim_Update = BindExport<Del_Sim_Update>(newHandle, nameof(Sim_Update));
                fn_Sim_GetSnapshot = BindExport<Del_Sim_GetSnapshot>(newHandle, nameof(Sim_GetSnapshot));
                fn_Sim_Shutdown = BindExport<Del_Sim_Void>(newHandle, nameof(Sim_Shutdown));
                fn_Get_data = BindExport<Del_Get_data>(newHandle, nameof(Get_data));
                fn_Sim_GetApiVersion = BindExport<Del_Sim_Int>(newHandle, nameof(Sim_GetApiVersion), required: false);
                fn_Sim_GetSnapshotSchemaVersion = BindExport<Del_Sim_Int>(newHandle, nameof(Sim_GetSnapshotSchemaVersion), required: false);
                fn_Sim_GetTick = BindExport<Del_Sim_Ulong>(newHandle, nameof(Sim_GetTick), required: false);
                fn_Sim_GetSnapshotJson = BindExport<Del_Sim_GetBuffer>(newHandle, nameof(Sim_GetSnapshotJson), required: false);
                fn_Sim_GetSchemaJson = BindExport<Del_Sim_GetBuffer>(newHandle, nameof(Sim_GetSchemaJson), required: false);
                fn_Sim_GetLastError = BindExport<Del_Sim_GetBuffer>(newHandle, nameof(Sim_GetLastError), required: false);
                fn_Sim_SetDiagnostics = BindExport<Del_Sim_SetDiagnostics>(newHandle, nameof(Sim_SetDiagnostics), required: false);
                fn_Sim_RequestDiagnosticSnapshot = BindExport<Del_Sim_RequestDiagnosticSnapshot>(newHandle, nameof(Sim_RequestDiagnosticSnapshot), required: false);
                fn_Sim_SetLegacyLogEnabled = BindExport<Del_Sim_SetLegacyLogEnabled>(newHandle, nameof(Sim_SetLegacyLogEnabled), required: false);
                fn_Sim_SeedRandom = BindExport<Del_Sim_SeedRandom>(newHandle, nameof(Sim_SeedRandom), required: false);
                fn_Sim_GetSlowMoFramesRemaining = BindExport<Del_Sim_Int>(newHandle, nameof(Sim_GetSlowMoFramesRemaining), required: false);
                fn_Sim_GetLastSpikeContactHeight = BindExport<Del_Sim_Float>(newHandle, nameof(Sim_GetLastSpikeContactHeight), required: false);
                fn_Sim_GetCharacterStats = BindExport<Del_Sim_GetCharacterStats>(newHandle, nameof(Sim_GetCharacterStats), required: false);
                fn_Sim_SetCharacterStats = BindExport<Del_Sim_SetCharacterStats>(newHandle, nameof(Sim_SetCharacterStats), required: false);
                fn_Sim_ResetCharacterStats = BindExport<Del_Sim_Int>(newHandle, nameof(Sim_ResetCharacterStats), required: false);
                fn_Sim_GetAttackServeConfigPtr = BindExport<Del_Sim_GetAttackServeConfigPtr>(newHandle, nameof(Sim_GetAttackServeConfigPtr), required: false);

                if (fn_Sim_GetAttackServeConfigPtr != null)
                {
                    try
                    {
                        IntPtr cfgPtr = fn_Sim_GetAttackServeConfigPtr();
                        if (cfgPtr != IntPtr.Zero)
                        {
                            initialNativeAttackServeConfig = Marshal.PtrToStructure<NativeAttackServeConfig>(cfgPtr);
                            hasInitialNativeAttackServeConfig = true;
                            if (hasUserAttackServeConfigOverride)
                            {
                                Marshal.StructureToPtr(userAttackServeConfigOverride, cfgPtr, fDeleteOld: false);
                            }
                        }
                    }
                    catch { }
                }

                if (wasPreviouslyLoaded && !string.IsNullOrEmpty(lastConfigJsonPath) && fn_Sim_Initialize != null)
                {
                    hotReloadedSinceLastCommit = true;
                    try { fn_Sim_Initialize(lastConfigJsonPath); } catch { }
                    if (hasUserAttackServeConfigOverride && fn_Sim_GetAttackServeConfigPtr != null)
                    {
                        try
                        {
                            IntPtr cfgPtr = fn_Sim_GetAttackServeConfigPtr();
                            if (cfgPtr != IntPtr.Zero)
                            {
                                Marshal.StructureToPtr(userAttackServeConfigOverride, cfgPtr, fDeleteOld: false);
                            }
                        }
                        catch { }
                    }
                }
            }
#endif
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_Initialize", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool PInvoke_Sim_Initialize(string configJsonPath);

        public static bool Sim_Initialize(string configJsonPath)
        {
            lastConfigJsonPath = configJsonPath;
            EnsureNativeLibraryLoaded(checkDiskTimestamp: true);
            if (fn_Sim_Initialize != null) return fn_Sim_Initialize(configJsonPath);
            return PInvoke_Sim_Initialize(configJsonPath);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_Reset", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PInvoke_Sim_Reset();

        public static void Sim_Reset()
        {
            EnsureNativeLibraryLoaded(checkDiskTimestamp: true);
            if (fn_Sim_Reset != null) { fn_Sim_Reset(); return; }
            PInvoke_Sim_Reset();
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_CheckAndResetMatch", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_CheckAndResetMatch();

        public static int Sim_CheckAndResetMatch()
        {
            bool checkDisk = (++hotPollCounter % 60) == 0;
            EnsureNativeLibraryLoaded(checkDiskTimestamp: checkDisk);
            if (fn_Sim_CheckAndResetMatch != null) return fn_Sim_CheckAndResetMatch();
            return PInvoke_Sim_CheckAndResetMatch();
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_SetInput", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PInvoke_Sim_SetInput(uint entityId, uint buttonMask, float moveAxisX);

        public static void Sim_SetInput(uint entityId, uint buttonMask, float moveAxisX)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_SetInput != null) { fn_Sim_SetInput(entityId, buttonMask, moveAxisX); return; }
            PInvoke_Sim_SetInput(entityId, buttonMask, moveAxisX);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_SubmitButtonInput", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_SubmitButtonInput(uint entityId, int buttonId, int inputState);

        public static int Sim_SubmitButtonInput(uint entityId, int buttonId, int inputState)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_SubmitButtonInput != null) return fn_Sim_SubmitButtonInput(entityId, buttonId, inputState);
            return PInvoke_Sim_SubmitButtonInput(entityId, buttonId, inputState);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_Update", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PInvoke_Sim_Update(float dt);

        public static void Sim_Update(float dt)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_Update != null) { fn_Sim_Update(dt); return; }
            PInvoke_Sim_Update(dt);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_GetSnapshot", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PInvoke_Sim_GetSnapshot(out SimSnapshot outSnapshot);

        public static void Sim_GetSnapshot(out SimSnapshot outSnapshot)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetSnapshot != null) { fn_Sim_GetSnapshot(out outSnapshot); return; }
            PInvoke_Sim_GetSnapshot(out outSnapshot);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_Shutdown", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PInvoke_Sim_Shutdown();

        public static void Sim_Shutdown()
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_Shutdown != null) { fn_Sim_Shutdown(); return; }
            PInvoke_Sim_Shutdown();
        }

        // ==============================================================
        // PRIMARY AUTHORITATIVE API: Get_data(Input)
        // ==============================================================
        [DllImport(DLL_NAME, EntryPoint = "Get_data", CallingConvention = CallingConvention.Cdecl)]
        private static extern NativeSimData PInvoke_Get_data(NativeSimInput inputSend);

        public static NativeSimData Get_data(NativeSimInput inputSend)
        {
            int count = ++getDataHotReloadCheckCounter;
            bool checkDisk = count == 1 || (count % 60) == 0;
            EnsureNativeLibraryLoaded(checkDiskTimestamp: checkDisk);
            if (fn_Get_data != null) return fn_Get_data(inputSend);
            return PInvoke_Get_data(inputSend);
        }

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

        [DllImport(DLL_NAME, EntryPoint = "Sim_GetApiVersion", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_GetApiVersion();

        public static int Sim_GetApiVersion()
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetApiVersion != null) return fn_Sim_GetApiVersion();
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_GetApiVersion));
            return PInvoke_Sim_GetApiVersion();
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_GetSnapshotSchemaVersion", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_GetSnapshotSchemaVersion();

        public static int Sim_GetSnapshotSchemaVersion()
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetSnapshotSchemaVersion != null) return fn_Sim_GetSnapshotSchemaVersion();
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_GetSnapshotSchemaVersion));
            return PInvoke_Sim_GetSnapshotSchemaVersion();
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_GetTick", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong PInvoke_Sim_GetTick();

        public static ulong Sim_GetTick()
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetTick != null) return fn_Sim_GetTick();
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_GetTick));
            return PInvoke_Sim_GetTick();
        }

        /// <summary>buffer may be null with capacity 0 to query the size (includes the NUL).</summary>
        [DllImport(DLL_NAME, EntryPoint = "Sim_GetSnapshotJson", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_GetSnapshotJson(byte[] buffer, int capacity, out int requiredBytes);

        public static int Sim_GetSnapshotJson(byte[] buffer, int capacity, out int requiredBytes)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetSnapshotJson != null) return fn_Sim_GetSnapshotJson(buffer, capacity, out requiredBytes);
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_GetSnapshotJson));
            return PInvoke_Sim_GetSnapshotJson(buffer, capacity, out requiredBytes);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_GetSchemaJson", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_GetSchemaJson(byte[] buffer, int capacity, out int requiredBytes);

        public static int Sim_GetSchemaJson(byte[] buffer, int capacity, out int requiredBytes)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetSchemaJson != null) return fn_Sim_GetSchemaJson(buffer, capacity, out requiredBytes);
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_GetSchemaJson));
            return PInvoke_Sim_GetSchemaJson(buffer, capacity, out requiredBytes);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_GetLastError", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_GetLastError(byte[] buffer, int capacity, out int requiredBytes);

        public static int Sim_GetLastError(byte[] buffer, int capacity, out int requiredBytes)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetLastError != null) return fn_Sim_GetLastError(buffer, capacity, out requiredBytes);
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_GetLastError));
            return PInvoke_Sim_GetLastError(buffer, capacity, out requiredBytes);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_SetDiagnostics", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int PInvoke_Sim_SetDiagnostics(uint flags, string outputPath);

        public static int Sim_SetDiagnostics(uint flags, string outputPath)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_SetDiagnostics != null) return fn_Sim_SetDiagnostics(flags, outputPath);
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_SetDiagnostics));
            return PInvoke_Sim_SetDiagnostics(flags, outputPath);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_RequestDiagnosticSnapshot", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_RequestDiagnosticSnapshot(ulong tick);

        public static int Sim_RequestDiagnosticSnapshot(ulong tick)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_RequestDiagnosticSnapshot != null) return fn_Sim_RequestDiagnosticSnapshot(tick);
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_RequestDiagnosticSnapshot));
            return PInvoke_Sim_RequestDiagnosticSnapshot(tick);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_SetLegacyLogEnabled", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PInvoke_Sim_SetLegacyLogEnabled(int enabled);

        public static void Sim_SetLegacyLogEnabled(int enabled)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_SetLegacyLogEnabled != null) { fn_Sim_SetLegacyLogEnabled(enabled); return; }
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_SetLegacyLogEnabled));
            PInvoke_Sim_SetLegacyLogEnabled(enabled);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_SeedRandom", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PInvoke_Sim_SeedRandom(uint seed);

        public static void Sim_SeedRandom(uint seed)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_SeedRandom != null) { fn_Sim_SeedRandom(seed); return; }
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_SeedRandom));
            PInvoke_Sim_SeedRandom(seed);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_GetSlowMoFramesRemaining", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_GetSlowMoFramesRemaining();

        public static int Sim_GetSlowMoFramesRemaining()
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetSlowMoFramesRemaining != null) return fn_Sim_GetSlowMoFramesRemaining();
            if (hotModuleHandle != IntPtr.Zero) return 0;
            return PInvoke_Sim_GetSlowMoFramesRemaining();
        }

        public static int GetSlowMoFramesRemaining()
        {
            try
            {
                return Sim_GetSlowMoFramesRemaining();
            }
            catch (EntryPointNotFoundException)
            {
                return 0;
            }
            catch (DllNotFoundException)
            {
                return 0;
            }
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_GetLastSpikeContactHeight", CallingConvention = CallingConvention.Cdecl)]
        private static extern float PInvoke_Sim_GetLastSpikeContactHeight();

        public static float Sim_GetLastSpikeContactHeight()
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetLastSpikeContactHeight != null) return fn_Sim_GetLastSpikeContactHeight();
            if (hotModuleHandle != IntPtr.Zero) return 0f;
            return PInvoke_Sim_GetLastSpikeContactHeight();
        }

        public static float GetLastSpikeContactHeight()
        {
            try
            {
                return Sim_GetLastSpikeContactHeight();
            }
            catch (EntryPointNotFoundException)
            {
                return 0f;
            }
            catch (DllNotFoundException)
            {
                return 0f;
            }
        }

        public static float GetLastSpikeContactHeight(in NativeSimData data)
        {
            try
            {
                float height = Sim_GetLastSpikeContactHeight();
                return height > 0f ? height : data.rallyState.spikeContactHeight;
            }
            catch (EntryPointNotFoundException)
            {
                return data.rallyState.spikeContactHeight;
            }
            catch (DllNotFoundException)
            {
                return data.rallyState.spikeContactHeight;
            }
        }

        // ==============================================================
        // IN-RAM CHARACTER STATS API (Live Runtime Tuning)
        // ==============================================================
        [DllImport(DLL_NAME, EntryPoint = "Sim_GetCharacterStats", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_GetCharacterStats(uint entityId, out float outAttack, out float outDefense, out float outSpeed, out float outJump);

        public static int Sim_GetCharacterStats(uint entityId, out float outAttack, out float outDefense, out float outSpeed, out float outJump)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetCharacterStats != null) return fn_Sim_GetCharacterStats(entityId, out outAttack, out outDefense, out outSpeed, out outJump);
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_GetCharacterStats));
            return PInvoke_Sim_GetCharacterStats(entityId, out outAttack, out outDefense, out outSpeed, out outJump);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_SetCharacterStats", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_SetCharacterStats(uint entityId, float attack, float defense, float speed, float jump);

        public static int Sim_SetCharacterStats(uint entityId, float attack, float defense, float speed, float jump)
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_SetCharacterStats != null) return fn_Sim_SetCharacterStats(entityId, attack, defense, speed, jump);
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_SetCharacterStats));
            return PInvoke_Sim_SetCharacterStats(entityId, attack, defense, speed, jump);
        }

        [DllImport(DLL_NAME, EntryPoint = "Sim_ResetCharacterStats", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PInvoke_Sim_ResetCharacterStats();

        public static int Sim_ResetCharacterStats()
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_ResetCharacterStats != null) return fn_Sim_ResetCharacterStats();
            if (hotModuleHandle != IntPtr.Zero) throw new EntryPointNotFoundException(nameof(Sim_ResetCharacterStats));
            return PInvoke_Sim_ResetCharacterStats();
        }

        private struct RamCharacterStatsEntry
        {
            public float attack;
            public float defense;
            public float speed;
            public float jump;
        }

        private static readonly System.Collections.Generic.Dictionary<uint, RamCharacterStatsEntry> RamStatsMirror =
            new System.Collections.Generic.Dictionary<uint, RamCharacterStatsEntry>();

        private static RamCharacterStatsEntry GetDefaultCharacterStats(uint entityId)
        {
            // Matches native Player (ID 0: atk=300, def=80, spd=300, jmp=300) / Opponent (ID 3: atk=20, def=85, spd=85, jmp=90) baseline stats
            return entityId == 3u
                ? new RamCharacterStatsEntry { attack = 20f, defense = 85f, speed = 85f, jump = 90f }
                : new RamCharacterStatsEntry { attack = 300f, defense = 80f, speed = 300f, jump = 300f };
        }

        public static bool TryGetCharacterStats(uint entityId, out float attack, out float defense, out float speed, out float jump)
        {
            try
            {
                int rc = Sim_GetCharacterStats(entityId, out float nativeAtk, out float nativeDef, out float nativeSpd, out float nativeJmp);
                if (rc >= 0 && (nativeAtk > 0f || nativeDef > 0f || nativeSpd > 0f || nativeJmp > 0f))
                {
                    attack = nativeAtk;
                    defense = nativeDef;
                    speed = nativeSpd;
                    jump = nativeJmp;
                    RamStatsMirror[entityId] = new RamCharacterStatsEntry
                    {
                        attack = attack,
                        defense = defense,
                        speed = speed,
                        jump = jump
                    };
                    return true;
                }
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            if (!RamStatsMirror.TryGetValue(entityId, out RamCharacterStatsEntry cached))
            {
                cached = GetDefaultCharacterStats(entityId);
                RamStatsMirror[entityId] = cached;
            }
            attack = cached.attack;
            defense = cached.defense;
            speed = cached.speed;
            jump = cached.jump;
            return true;
        }

        public static bool TrySetCharacterStats(uint entityId, float attack, float defense, float speed, float jump)
        {
            RamStatsMirror[entityId] = new RamCharacterStatsEntry
            {
                attack = attack,
                defense = defense,
                speed = speed,
                jump = jump
            };
            try
            {
                int rc = Sim_SetCharacterStats(entityId, attack, defense, speed, jump);
                return rc >= 0;
            }
            catch (EntryPointNotFoundException)
            {
                return true;
            }
            catch (DllNotFoundException)
            {
                return true;
            }
        }

        public static bool TryResetCharacterStats()
        {
            RamStatsMirror.Clear();
            try
            {
                int rc = Sim_ResetCharacterStats();
                return rc >= 0;
            }
            catch (EntryPointNotFoundException)
            {
                return true;
            }
            catch (DllNotFoundException)
            {
                return true;
            }
        }

        // ==============================================================
        // DIRECT IN-RAM ATTACK SERVE CONFIG POINTER API (Zero-Copy RAM)
        // ==============================================================
        [DllImport(DLL_NAME, EntryPoint = "Sim_GetAttackServeConfigPtr", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr PInvoke_Sim_GetAttackServeConfigPtr();

        public static IntPtr Sim_GetAttackServeConfigPtr()
        {
            EnsureNativeLibraryLoaded();
            if (fn_Sim_GetAttackServeConfigPtr != null) return fn_Sim_GetAttackServeConfigPtr();
            if (hotModuleHandle != IntPtr.Zero) return IntPtr.Zero;
            return PInvoke_Sim_GetAttackServeConfigPtr();
        }

        public static IntPtr GetAttackServeConfigRamPtr()
        {
            try
            {
                return Sim_GetAttackServeConfigPtr();
            }
            catch (EntryPointNotFoundException)
            {
                return IntPtr.Zero;
            }
            catch (DllNotFoundException)
            {
                return IntPtr.Zero;
            }
        }

        public static bool TryReadAttackServeConfigFromRam(out NativeAttackServeConfig config)
        {
            IntPtr ptr = GetAttackServeConfigRamPtr();
            if (ptr != IntPtr.Zero)
            {
                config = Marshal.PtrToStructure<NativeAttackServeConfig>(ptr);
                if (!hasInitialNativeAttackServeConfig)
                {
                    initialNativeAttackServeConfig = config;
                    hasInitialNativeAttackServeConfig = true;
                }
                return true;
            }

            config = hasUserAttackServeConfigOverride
                ? userAttackServeConfigOverride
                : (hasInitialNativeAttackServeConfig ? initialNativeAttackServeConfig : NativeAttackServeConfig.Default);
            return false;
        }

        public static bool TryWriteAttackServeConfigToRam(in NativeAttackServeConfig config)
        {
#if UNITY_5_3_OR_NEWER
            NativeAttackServeConfig defaultCfg = NativeAttackServeConfig.Default;
            if (hasUserAttackServeConfigOverride &&
                UnityEngine.Time.frameCount <= 2 &&
                !NativeAttackServeConfig.AreEqual(in userAttackServeConfigOverride, in defaultCfg) &&
                NativeAttackServeConfig.AreEqual(in config, in defaultCfg))
            {
                IntPtr existingPtr = GetAttackServeConfigRamPtr();
                if (existingPtr != IntPtr.Zero)
                {
                    Marshal.StructureToPtr(userAttackServeConfigOverride, existingPtr, fDeleteOld: false);
                    OnAttackServeConfigRamChanged?.Invoke(userAttackServeConfigOverride, existingPtr);
                    return true;
                }
            }
#endif
            userAttackServeConfigOverride = config;
            hasUserAttackServeConfigOverride = true;

            IntPtr ptr = GetAttackServeConfigRamPtr();
            if (ptr != IntPtr.Zero)
            {
                Marshal.StructureToPtr(config, ptr, fDeleteOld: false);
                bool changed = !hasLoggedAttackServeRamConfig ||
                               ptr != lastLoggedAttackServeRamPtr ||
                               !NativeAttackServeConfig.AreEqual(in config, in lastLoggedAttackServeRamConfig);
                if (changed)
                {
                    hasLoggedAttackServeRamConfig = true;
                    lastLoggedAttackServeRamPtr = ptr;
                    lastLoggedAttackServeRamConfig = config;
#if UNITY_5_3_OR_NEWER
                    // UnityEngine.Debug.Log($"[CPP_RAM_CONFIG_SYNC] ptr=0x{ptr.ToInt64():X} baseA={config.baseA:F6} maxA={config.maxA:F6} baseC={config.baseC:F2} maxC={config.maxC:F2} totalStep={config.totalStep:F1} stepA={(config.maxA - config.baseA) / UnityEngine.Mathf.Max(1f, config.totalStep):F6} stepC={(config.maxC - config.baseC) / UnityEngine.Mathf.Max(1f, config.totalStep):F2} tossV0={config.tossV0:F2}");
#endif
                    OnAttackServeConfigRamChanged?.Invoke(config, ptr);
                }
                return true;
            }
            return false;
        }

        public static bool TryResetAttackServeConfigInRam(out NativeAttackServeConfig restored)
        {
            hasUserAttackServeConfigOverride = false;
            restored = hasInitialNativeAttackServeConfig ? initialNativeAttackServeConfig : NativeAttackServeConfig.Default;

            IntPtr ptr = GetAttackServeConfigRamPtr();
            if (ptr != IntPtr.Zero)
            {
                Marshal.StructureToPtr(restored, ptr, fDeleteOld: false);
                hasLoggedAttackServeRamConfig = true;
                lastLoggedAttackServeRamPtr = ptr;
                lastLoggedAttackServeRamConfig = restored;
#if UNITY_5_3_OR_NEWER
                // UnityEngine.Debug.Log($"[CPP_RAM_CONFIG_SYNC] ptr=0x{ptr.ToInt64():X} baseA={restored.baseA:F6} maxA={restored.maxA:F6} baseC={restored.baseC:F2} maxC={restored.maxC:F2} totalStep={restored.totalStep:F1} stepA={(restored.maxA - restored.baseA) / UnityEngine.Mathf.Max(1f, restored.totalStep):F6} stepC={(restored.maxC - restored.baseC) / UnityEngine.Mathf.Max(1f, restored.totalStep):F2} tossV0={restored.tossV0:F2}");
#endif
                OnAttackServeConfigRamChanged?.Invoke(restored, ptr);
                return true;
            }
            return false;
        }
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
