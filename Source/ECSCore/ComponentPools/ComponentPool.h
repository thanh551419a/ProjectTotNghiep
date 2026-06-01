#pragma once

#include <vector>
#include <cstdint>
#include <limits>
using Entity = uint32_t;

// ======================================================
// COMPONENT POOL
// Sparse Set ECS
//
// O(1):
// - add
// - remove
// - get
// - has
//
// cache friendly
// contiguous iteration
// swap-back removal
// ======================================================

template <typename T>
class ComponentPool
{
public:
    static constexpr uint32_t INVALID_INDEX = std::numeric_limits<uint32_t>::max();

public:
    ComponentPool(size_t maxEntities = 10000) { sparse.resize(maxEntities, INVALID_INDEX); }

    // ======================================================
    // ADD
    // ======================================================

    template <typename... Args>
    T& add(Entity entity, Args&&... args)
    {
        // already exists
        if (has(entity))
        {
            return denseComponents[sparse[entity]];
        }

        const uint32_t denseIndex = static_cast<uint32_t>(denseEntities.size());

        sparse[entity] = denseIndex;

        denseEntities.push_back(entity);

        denseComponents.emplace_back(std::forward<Args>(args)...);

        return denseComponents.back();
    }

    // ======================================================
    // REMOVE
    // swap-back remove
    // ======================================================

    void remove(Entity entity)
    {
        if (!has(entity))
        {
            return;
        }

        const uint32_t removeIndex = sparse[entity];

        const uint32_t lastIndex = static_cast<uint32_t>(denseEntities.size() - 1);

        const Entity lastEntity = denseEntities[lastIndex];

        // move last -> removed slot
        denseEntities[removeIndex] = denseEntities[lastIndex];

        denseComponents[removeIndex] = std::move(denseComponents[lastIndex]);

        // update sparse
        sparse[lastEntity] = removeIndex;

        // pop
        denseEntities.pop_back();
        denseComponents.pop_back();

        // invalidate sparse
        sparse[entity] = INVALID_INDEX;
    }

    // ======================================================
    // HAS
    // ======================================================

    bool has(Entity entity) const { return sparse[entity] != INVALID_INDEX; }

    // ======================================================
    // GET
    // ======================================================

    T* get(Entity entity)
    {
        if (!has(entity))
        {
            return nullptr;
        }

        return &denseComponents[sparse[entity]];
    }

   const T* get(Entity entity) const
    {
        if (!has(entity))
        {
            return nullptr;
        }

        return &denseComponents[sparse[entity]];
    }

    // ======================================================
    // CLEAR
    // ======================================================

    void clear()
    {
        for (Entity entity : denseEntities)
        {
            sparse[entity] = INVALID_INDEX;
        }

        denseEntities.clear();
        denseComponents.clear();
    }

    // ======================================================
    // SIZE
    // ======================================================

    size_t size() const { return denseComponents.size(); }

    // ======================================================
    // RAW ACCESS
    // ======================================================

    std::vector<Entity>& entities() { return denseEntities; }

    std::vector<T>& components() { return denseComponents; }

    const std::vector<Entity>& entities() const { return denseEntities; }

    const std::vector<T>& components() const { return denseComponents; }

private:
    // ======================================================
    // SPARSE
    // entity -> dense index
    // ======================================================

    std::vector<uint32_t> sparse;

    // ======================================================
    // DENSE
    // ======================================================

    std::vector<Entity> denseEntities;

    std::vector<T> denseComponents;
};
