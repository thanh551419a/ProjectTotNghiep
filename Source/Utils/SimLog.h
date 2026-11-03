#pragma once

#include <algorithm>
#include <cstdio>

// ==============================================================
// SimLog.h
// Platform-independent logging and basic utility helpers.
// If Axmol is present and enabled, routes to AXLOG.
// Otherwise, routes to standard formatted output.
// ==============================================================

namespace SimLogControl
{
    // Runtime switch for legacy text logging in non-Axmol builds (default: enabled, unchanged behaviour).
    // Does not affect simulation state.
    inline bool& Enabled()
    {
        static bool enabled = true;
        return enabled;
    }
}  // namespace SimLogControl

#if defined(AXMOL_ENABLED)
#   include <axmol.h>
#   define SIM_LOG(...) AXLOG(__VA_ARGS__)
#elif defined(__ANDROID__)
#   include <android/log.h>
#   define SIM_LOG(format, ...) do { if (SimLogControl::Enabled()) __android_log_print(ANDROID_LOG_INFO, "SpikeCrossCore", "[SIM] " format, ##__VA_ARGS__); } while (0)
#else
#   define SIM_LOG(format, ...) do { if (SimLogControl::Enabled()) std::printf("[SIM] " format "\n", ##__VA_ARGS__); } while (0)
#endif

#ifndef AXLOG
#   define AXLOG(format, ...) SIM_LOG(format, ##__VA_ARGS__)
#endif

#ifndef clampf
inline float clampf(float min_val, float val, float max_val)
{
    return std::max(min_val, std::min(val, max_val));
}
#endif
