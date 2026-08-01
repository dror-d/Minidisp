// Latest PC stats snapshot + lookup by theme bind path (see docs/PROTOCOL.md).
#pragma once

#include <Arduino.h>
#include <ArduinoJson.h>

namespace stats {

constexpr int kMaxCores = 32;
constexpr int kMaxNets = 4;
constexpr int kMaxDisks = 4;

struct NetIf {
    char name[24];
    char ip[40];
    float up = 0, down = 0;
    bool valid = false;
};

struct DiskInfo {
    char name[16];
    float pct = 0, freeGb = 0;
    bool valid = false;
};

struct Snapshot {
    char host[32] = "";
    uint32_t uptime = 0;
    char cpuName[48] = "";
    float cpuLoad = -1, cpuTemp = -1, cpuFreq = -1;
    float cores[kMaxCores];
    int nCores = 0;
    float memPct = -1, memUsed = -1, memTotal = -1;
    char gpuName[48] = "";
    float gpuLoad = -1, gpuTemp = -1;
    NetIf net[kMaxNets];
    DiskInfo disk[kMaxDisks];
};

// Merge a parsed {"stats":{...}} object into the snapshot.
void update(JsonObjectConst obj);

// True if data arrived within the last timeoutMs.
bool isFresh(uint32_t timeoutMs);
bool everReceived();

// Resolve a bind path ("cpu.load", "net.ip", "disk1.pct", "cpu.core3", ...).
// Numeric paths fill `out` and return true. Negative sentinel = not yet known.
bool getNumber(const char* path, float& out);
// String paths ("host", "net.ip", "cpu.name", ...); also stringifies numbers.
bool getText(const char* path, char* buf, size_t bufLen);

} // namespace stats
