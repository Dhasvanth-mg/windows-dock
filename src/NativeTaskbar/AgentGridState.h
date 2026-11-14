#pragma once

#include "Connection.h"
#include <cstdint>

struct AgentGridTile
{
    int32_t provider;
    int32_t state;
    wchar_t id[96];
    wchar_t title[100];
    wchar_t step[64];
};

struct AgentGridSelection
{
    LONG revision;
    int32_t provider;
    wchar_t id[96];
    int32_t slot;
};

struct AgentGridState
{
    LONG revision;
    AgentGridTile tiles[4];
    AgentGridSelection selection;
    int64_t codexBlockedUntil;
};

static_assert(sizeof(AgentGridTile) == 528);
static_assert(sizeof(AgentGridSelection) == 204);
static_assert(sizeof(AgentGridState) == 2328);

struct UnmapAgentGrid
{
    void operator()(AgentGridState* value) const noexcept { UnmapViewOfFile(value); }
};
