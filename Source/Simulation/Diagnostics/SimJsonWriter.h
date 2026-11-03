#pragma once
// ==============================================================
// SimJsonWriter.h
// RapidJSON helpers shared by the snapshot builder and diagnostics.
// ==============================================================

#include <cmath>
#include <cstdint>
#include <string>

#include "../../ThirdParty/RapidJson/stringbuffer.h"
#include "../../ThirdParty/RapidJson/writer.h"
#include "SimSchema.h"

namespace SimJson
{
    using Buffer = rapidjson::StringBuffer;
    using Writer = rapidjson::Writer<rapidjson::StringBuffer>;

    // Floats are widened to double (exact) so the decimal text round-trips to the same float.
    // Non-finite values are never dropped: encoded as strings.
    inline void WriteFloat(Writer& w, float value)
    {
        if (std::isnan(value))
            w.String("NaN");
        else if (std::isinf(value))
            w.String(value > 0 ? "+Inf" : "-Inf");
        else
            w.Double(static_cast<double>(value));
    }

    // Visitor that writes visited fields as JSON object members.
    struct ValueVisitor
    {
        Writer& w;

        void Key(const char* name)
        {
            if (name)
                w.Key(name);
        }
        void Float(const char* name, float value, SimSchema::TransitionMode)
        {
            Key(name);
            WriteFloat(w, value);
        }
        void Int(const char* name, int64_t value, SimSchema::TransitionMode)
        {
            Key(name);
            w.Int64(value);
        }
        void UInt(const char* name, uint64_t value, SimSchema::TransitionMode)
        {
            Key(name);
            w.Uint64(value);
        }
        void Bool(const char* name, bool value, SimSchema::TransitionMode)
        {
            Key(name);
            w.Bool(value);
        }
        void Enum(const char* name, int64_t value, const char*, SimSchema::TransitionMode)
        {
            Key(name);
            w.Int64(value);
        }
        void BeginObject(const char* name)
        {
            Key(name);
            w.StartObject();
        }
        void EndObject() { w.EndObject(); }
        void BeginArray(const char* name)
        {
            Key(name);
            w.StartArray();
        }
        void EndArray() { w.EndArray(); }
    };

    template <class T>
    void WriteValueObject(Writer& w, const T& value)
    {
        ValueVisitor v{w};
        w.StartObject();
        SimSchema::Visit(v, value);
        w.EndObject();
    }
}  // namespace SimJson
