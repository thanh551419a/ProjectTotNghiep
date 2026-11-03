#include "SimDiagnostics.h"

#include <cstring>
#include <vector>

#include "SimJsonWriter.h"
#include "../../System/TrajectoryTestConfig.h"

using namespace SimSchema;
using SimJson::Writer;

namespace
{
    struct FlattenVisitor
    {
        SimDiagnostics::FlatMap& out;
        const char* pool;
        uint32_t key;
        bool matchKey;
        std::vector<std::string> path;
        std::vector<int> arrayCounters;

        std::string FieldPath(const char* leaf) const
        {
            std::string s;
            for (const auto& p : path)
            {
                if (!s.empty() && p[0] != '[')
                    s += '.';
                s += p;
            }
            if (leaf)
            {
                if (!s.empty())
                    s += '.';
                s += leaf;
            }
            return s;
        }

        void Add(const char* name, uint8_t kind, double num, const char* enumName, TransitionMode mode)
        {
            SimDiagnostics::FlatEntry e;
            e.pool     = pool;
            e.key      = key;
            e.matchKey = matchKey;
            e.field    = FieldPath(name);
            e.value.kind     = kind;
            e.value.num      = num;
            e.value.enumName = enumName;
            e.value.mode     = mode;
            std::string id   = std::string(pool) + "[" + std::to_string(key) + "]." + e.field;
            out.emplace(std::move(id), std::move(e));
        }

        void Float(const char* n, float v, TransitionMode m) { Add(n, 0, static_cast<double>(v), nullptr, m); }
        void Int(const char* n, int64_t v, TransitionMode m) { Add(n, 1, static_cast<double>(v), nullptr, m); }
        void UInt(const char* n, uint64_t v, TransitionMode m) { Add(n, 2, static_cast<double>(v), nullptr, m); }
        void Bool(const char* n, bool v, TransitionMode m) { Add(n, 3, v ? 1.0 : 0.0, nullptr, m); }
        void Enum(const char* n, int64_t v, const char* e, TransitionMode m) { Add(n, 4, static_cast<double>(v), e, m); }

        void BeginObject(const char* name)
        {
            if (name)
                path.emplace_back(name);
            else
                path.emplace_back("[" + std::to_string(arrayCounters.back()++) + "]");
        }
        void EndObject() { path.pop_back(); }
        void BeginArray(const char* name)
        {
            path.emplace_back(name);
            arrayCounters.push_back(0);
        }
        void EndArray()
        {
            arrayCounters.pop_back();
            path.pop_back();
        }
    };

    bool SameValue(const SimDiagnostics::FlatValue& a, const SimDiagnostics::FlatValue& b)
    {
        return a.kind == b.kind && std::memcmp(&a.num, &b.num, sizeof(double)) == 0;
    }

    int Classify(double v) { return v < 0 ? -1 : (v == 0 ? 0 : 1); }

    const char* EnumValueName(const char* enumName, int64_t value)
    {
        if (!enumName)
            return nullptr;
        for (const auto& e : kEnums)
        {
            if (std::strcmp(e.name, enumName) == 0)
                return (value >= 0 && value < e.count) ? e.names[value] : "OutOfRange";
        }
        return nullptr;
    }

    void WriteFlat(Writer& w, const SimDiagnostics::FlatValue& v)
    {
        switch (v.kind)
        {
        case 0: SimJson::WriteFloat(w, static_cast<float>(v.num)); break;
        case 1: w.Int64(static_cast<int64_t>(v.num)); break;
        case 2: w.Uint64(static_cast<uint64_t>(v.num)); break;
        case 3: w.Bool(v.num != 0.0); break;
        default: w.Int64(static_cast<int64_t>(v.num)); break;
        }
    }

    void WriteIntentObject(Writer& w, const CharacterIntent& ci)
    {
        w.StartObject();
        w.Key("moveX");
        SimJson::WriteFloat(w, ci.moveX);
        w.Key("finalIntent");
        w.String(kFinalIntentNames[static_cast<int>(ci.finalIntent)]);
        w.EndObject();
    }
}  // namespace

bool SimDiagnostics::Configure(uint32_t flags, const char* path)
{
    Close();
    _flags = 0;
    if (flags == 0 || path == nullptr || path[0] == '\0')
        return true;
    _out = std::fopen(path, "wb");
    if (!_out)
        return false;
    _flags = flags;
    return true;
}

void SimDiagnostics::Close()
{
    if (_out)
    {
        std::fflush(_out);
        std::fclose(_out);
        _out = nullptr;
    }
    _flags = 0;
    _snapshotTicks.clear();
}

void SimDiagnostics::Flush()
{
    if (_out)
        std::fflush(_out);
}

void SimDiagnostics::WriteLine(const std::string& line)
{
    if (!_out)
        return;
    std::fwrite(line.data(), 1, line.size(), _out);
    std::fputc('\n', _out);
}

void SimDiagnostics::Lifecycle(const char* type, uint64_t tick, const char* detail)
{
    if (!Has(kLifecycle))
        return;
    SimJson::Buffer b;
    Writer w(b);
    w.StartObject();
    w.Key("type");
    w.String(type);
    w.Key("tick");
    w.Uint64(tick);
    if (detail)
    {
        w.Key("detail");
        w.String(detail);
    }
    w.EndObject();
    WriteLine(b.GetString());
}

void SimDiagnostics::Input(uint64_t tick, uint32_t entity, uint32_t holding, uint32_t pressed, float axis,
                           uint32_t prevMask)
{
    if (!Has(kInput))
        return;
    SimJson::Buffer b;
    Writer w(b);
    w.StartObject();
    w.Key("type");
    w.String("input");
    w.Key("forTick");
    w.Uint64(tick);
    w.Key("entity");
    w.Uint(entity);
    w.Key("holdingButtons");
    w.Uint(holding);
    w.Key("pressedButtons");
    w.Uint(pressed);
    w.Key("moveAxisX");
    SimJson::WriteFloat(w, axis);
    w.Key("prevButtonMask");
    w.Uint(prevMask);
    w.Key("accepted");
    w.Bool(entity == 0);
    w.EndObject();
    WriteLine(b.GetString());
}

void SimDiagnostics::Snapshot(uint64_t tick, const char* reason, const std::string& json)
{
    if (!Active())
        return;
    SimJson::Buffer b;
    Writer w(b);
    w.StartObject();
    w.Key("type");
    w.String("snapshot");
    w.Key("tick");
    w.Uint64(tick);
    w.Key("reason");
    w.String(reason);
    w.Key("data");
    w.RawValue(json.c_str(), json.size(), rapidjson::kObjectType);
    w.EndObject();
    WriteLine(b.GetString());
}

void SimDiagnostics::Intents(uint64_t tick, const SimStateView& view)
{
    if (!Has(kIntentDump))
        return;
    SimJson::Buffer b;
    Writer w(b);
    w.StartObject();
    w.Key("type");
    w.String("intents");
    w.Key("producedByTick");
    w.Uint64(tick);
    ForEachPool(*view.components, *view.intents, *view.statPool, [&](const PoolInfo& info, const auto& pool) {
        if (info.section != Section::Intents)
            return;
        w.Key(info.name);
        w.StartArray();
        const auto& keys = pool.entities();
        const auto& vals = pool.components();
        for (size_t i = 0; i < keys.size(); ++i)
        {
            w.StartObject();
            w.Key("entity");
            w.Uint(keys[i]);
            w.Key("value");
            SimJson::WriteValueObject(w, vals[i]);
            w.EndObject();
        }
        w.EndArray();
    });
    w.EndObject();
    WriteLine(b.GetString());
}

void SimDiagnostics::Marker(const char* type, const std::string& body)
{
    if (!_out)
        return;
    std::string line = std::string("{\"type\":\"") + type + "\"";
    if (!body.empty())
        line += "," + body;
    line += "}";
    WriteLine(line);
}

SimDiagnostics::FlatMap SimDiagnostics::Flatten(const SimStateView& view)
{
    FlatMap out;
    ForEachPool(*view.components, *view.intents, *view.statPool, [&](const PoolInfo& info, const auto& pool) {
        const auto& keys = pool.entities();
        const auto& vals = pool.components();
        for (size_t i = 0; i < keys.size(); ++i)
        {
            FlattenVisitor v{out, info.name, keys[i], info.key == KeyKind::Match, {}, {}};
            Visit(v, vals[i]);
        }
    });
    const TestTrajectory& tt = TestTrajectory::getInstance();
    FlattenVisitor g{out, "globals.testTrajectory", 0, false, {}, {}};
    g.Int("frame", tt.frame, TransitionMode::None);
    g.Float("AttackPower", tt.AttackPower, TransitionMode::None);
    return out;
}

void SimDiagnostics::EmitDiff(uint64_t tick, int order, const char* name, const SimStateView& view,
                              const FlatMap& before, const FlatMap& after)
{
    struct Change
    {
        const FlatEntry* entry;
        const FlatValue* oldV;
        const FlatValue* newV;
        const char* op;
    };
    std::vector<Change> changes;

    auto ib = before.begin();
    auto ia = after.begin();
    while (ib != before.end() || ia != after.end())
    {
        if (ia == after.end() || (ib != before.end() && ib->first < ia->first))
        {
            changes.push_back({&ib->second, &ib->second.value, nullptr, "remove"});
            ++ib;
        }
        else if (ib == before.end() || ia->first < ib->first)
        {
            changes.push_back({&ia->second, nullptr, &ia->second.value, "add"});
            ++ia;
        }
        else
        {
            if (!SameValue(ib->second.value, ia->second.value))
                changes.push_back({&ia->second, &ib->second.value, &ia->second.value, "set"});
            ++ib;
            ++ia;
        }
    }

    if (Has(kSystemTrace) && !(Has(kSystemTraceChangedOnly) && changes.empty()))
    {
        SimJson::Buffer b;
        Writer w(b);
        w.StartObject();
        w.Key("type");
        w.String("system");
        w.Key("tick");
        w.Uint64(tick);
        w.Key("order");
        w.Int(order);
        w.Key("system");
        w.String(name);
        w.Key("changes");
        w.Uint64(changes.size());
        w.EndObject();
        WriteLine(b.GetString());
    }

    if (!Has(kFieldChanges | kTransitions))
        return;

    const auto& intentPool = view.intents->characterIntentPool;

    for (const auto& c : changes)
    {
        const FlatValue& ref = c.newV ? *c.newV : *c.oldV;
        bool isTransition    = false;
        switch (ref.mode)
        {
        case TransitionMode::Always: isTransition = true; break;
        case TransitionMode::Boundary:
            if (!c.oldV || !c.newV)
                isTransition = true;
            else
                isTransition = Classify(c.oldV->num) != Classify(c.newV->num) || c.newV->num > c.oldV->num;
            break;
        default: break;
        }

        const bool wantChange     = Has(kFieldChanges);
        const bool wantTransition = Has(kTransitions) && isTransition;
        if (!wantChange && !wantTransition)
            continue;

        SimJson::Buffer b;
        Writer w(b);
        w.StartObject();
        w.Key("type");
        w.String(wantTransition ? "transition" : "change");
        w.Key("tick");
        w.Uint64(tick);
        w.Key("system");
        w.String(name);
        w.Key("op");
        w.String(c.op);
        w.Key("pool");
        w.String(c.entry->pool.c_str());
        w.Key(c.entry->matchKey ? "match" : "entity");
        w.Uint(c.entry->key);
        w.Key("field");
        w.String(c.entry->field.c_str());
        w.Key("old");
        if (c.oldV)
            WriteFlat(w, *c.oldV);
        else
            w.Null();
        w.Key("new");
        if (c.newV)
            WriteFlat(w, *c.newV);
        else
            w.Null();
        if (ref.kind == 4)
        {
            w.Key("oldName");
            if (c.oldV)
                w.String(EnumValueName(ref.enumName, static_cast<int64_t>(c.oldV->num)));
            else
                w.Null();
            w.Key("newName");
            if (c.newV)
                w.String(EnumValueName(ref.enumName, static_cast<int64_t>(c.newV->num)));
            else
                w.Null();
        }

        // Cause: intents/input present when the system ran.
        w.Key("cause");
        w.StartObject();
        if (!c.entry->matchKey && c.entry->key < static_cast<uint32_t>(ChunkConfig::CHARACTER_PER_MATCH) &&
            c.entry->pool.rfind("globals", 0) != 0)
        {
            w.Key("intent");
            if (const CharacterIntent* ci = intentPool.get(c.entry->key))
                WriteIntentObject(w, *ci);
            else
                w.Null();
            if (c.entry->key == 0)
            {
                w.Key("input");
                w.StartObject();
                w.Key("holding");
                w.Uint(view.input.holdingButtons);
                w.Key("pressed");
                w.Uint(view.input.pressedButtons);
                w.Key("axis");
                SimJson::WriteFloat(w, view.input.moveAxisX);
                w.EndObject();
            }
        }
        else
        {
            w.Key("intents");
            w.StartArray();
            const auto& keys = intentPool.entities();
            const auto& vals = intentPool.components();
            for (size_t i = 0; i < keys.size(); ++i)
            {
                w.StartObject();
                w.Key("entity");
                w.Uint(keys[i]);
                w.Key("intent");
                WriteIntentObject(w, vals[i]);
                w.EndObject();
            }
            w.EndArray();
        }
        w.EndObject();
        w.EndObject();
        WriteLine(b.GetString());
    }
}
