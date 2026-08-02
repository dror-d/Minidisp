#include "stats_model.h"

namespace stats {

namespace {
Snapshot s_snap;
uint32_t s_lastUpdateMs = 0;
bool s_ever = false;

void copyStr(char* dst, size_t n, JsonVariantConst v) {
    const char* s = v.as<const char*>();
    if (s) {
        strlcpy(dst, s, n);
    }
}

// Splits an indexed prefix: "net1" -> ("net", 1), "net" -> ("net", 0).
int trailingIndex(const char* word) {
    size_t len = strlen(word);
    if (len && isdigit((unsigned char)word[len - 1])) {
        return word[len - 1] - '0';
    }
    return 0;
}
} // namespace

void update(JsonObjectConst o) {
    if (o.isNull()) return;

    copyStr(s_snap.host, sizeof(s_snap.host), o["host"]);
    if (!o["uptime"].isNull()) s_snap.uptime = o["uptime"].as<uint32_t>();

    JsonObjectConst cpu = o["cpu"];
    if (!cpu.isNull()) {
        if (!cpu["load"].isNull()) s_snap.cpuLoad = cpu["load"].as<float>();
        if (!cpu["temp"].isNull()) s_snap.cpuTemp = cpu["temp"].as<float>();
        if (!cpu["freq"].isNull()) s_snap.cpuFreq = cpu["freq"].as<float>();
        copyStr(s_snap.cpuName, sizeof(s_snap.cpuName), cpu["name"]);
        JsonArrayConst cores = cpu["cores"];
        if (!cores.isNull()) {
            s_snap.nCores = 0;
            for (JsonVariantConst c : cores) {
                if (s_snap.nCores >= kMaxCores) break;
                s_snap.cores[s_snap.nCores++] = c.as<float>();
            }
        }
    }

    JsonObjectConst mem = o["mem"];
    if (!mem.isNull()) {
        if (!mem["pct"].isNull()) s_snap.memPct = mem["pct"].as<float>();
        if (!mem["used"].isNull()) s_snap.memUsed = mem["used"].as<float>();
        if (!mem["total"].isNull()) s_snap.memTotal = mem["total"].as<float>();
    }

    JsonObjectConst gpu = o["gpu"];
    if (!gpu.isNull()) {
        if (!gpu["load"].isNull()) s_snap.gpuLoad = gpu["load"].as<float>();
        if (!gpu["temp"].isNull()) s_snap.gpuTemp = gpu["temp"].as<float>();
        copyStr(s_snap.gpuName, sizeof(s_snap.gpuName), gpu["name"]);
    }

    JsonArrayConst net = o["net"];
    if (!net.isNull()) {
        int i = 0;
        for (JsonObjectConst n : net) {
            if (i >= kMaxNets) break;
            NetIf& d = s_snap.net[i];
            copyStr(d.name, sizeof(d.name), n["if"]);
            copyStr(d.ip, sizeof(d.ip), n["ip"]);
            d.up = n["up"] | 0.0f;
            d.down = n["down"] | 0.0f;
            d.valid = true;
            i++;
        }
        for (; i < kMaxNets; i++) s_snap.net[i].valid = false;
    }

    JsonArrayConst disk = o["disk"];
    if (!disk.isNull()) {
        int i = 0;
        for (JsonObjectConst d : disk) {
            if (i >= kMaxDisks) break;
            DiskInfo& x = s_snap.disk[i];
            copyStr(x.name, sizeof(x.name), d["n"]);
            x.pct = d["pct"] | 0.0f;
            x.freeGb = d["free"] | 0.0f;
            x.valid = true;
            i++;
        }
        for (; i < kMaxDisks; i++) s_snap.disk[i].valid = false;
    }

    JsonObjectConst custom = o["custom"];
    if (!custom.isNull()) {
        int i = 0;
        for (JsonPairConst kv : custom) {
            if (i >= kMaxCustom) break;
            CustomVal& c = s_snap.custom[i];
            strlcpy(c.key, kv.key().c_str(), sizeof(c.key));
            JsonVariantConst v = kv.value();
            if (v.is<float>()) {
                c.num = v.as<float>();
                c.isNum = true;
                snprintf(c.text, sizeof(c.text), "%g", (double)c.num);
            } else {
                c.isNum = false;
                const char* s = v.as<const char*>();
                strlcpy(c.text, s ? s : "", sizeof(c.text));
            }
            c.valid = true;
            i++;
        }
        for (; i < kMaxCustom; i++) s_snap.custom[i].valid = false;
    }

    s_lastUpdateMs = millis();
    s_ever = true;
}

const CustomVal* findCustom(const char* path) {
    for (int i = 0; i < kMaxCustom; i++) {
        if (s_snap.custom[i].valid && !strcmp(s_snap.custom[i].key, path)) {
            return &s_snap.custom[i];
        }
    }
    return nullptr;
}

bool isFresh(uint32_t timeoutMs) {
    return s_ever && (millis() - s_lastUpdateMs) < timeoutMs;
}

bool everReceived() { return s_ever; }

bool getNumber(const char* path, float& out) {
    char group[16];
    const char* dot = strchr(path, '.');
    const char* field = "";
    if (dot) {
        size_t glen = dot - path;
        if (glen >= sizeof(group)) return false;
        memcpy(group, path, glen);
        group[glen] = 0;
        field = dot + 1;
    } else {
        strlcpy(group, path, sizeof(group));
    }

    if (!strcmp(group, "uptime")) { out = (float)s_snap.uptime; return true; }

    if (!strcmp(group, "cpu")) {
        if (!strcmp(field, "load")) { out = s_snap.cpuLoad; return s_snap.cpuLoad >= 0; }
        if (!strcmp(field, "temp")) { out = s_snap.cpuTemp; return s_snap.cpuTemp >= 0; }
        if (!strcmp(field, "freq")) { out = s_snap.cpuFreq; return s_snap.cpuFreq >= 0; }
        if (!strncmp(field, "core", 4)) {
            int idx = atoi(field + 4);
            if (idx >= 0 && idx < s_snap.nCores) { out = s_snap.cores[idx]; return true; }
            return false;
        }
        return false;
    }
    if (!strcmp(group, "mem")) {
        if (!strcmp(field, "pct")) { out = s_snap.memPct; return s_snap.memPct >= 0; }
        if (!strcmp(field, "used")) { out = s_snap.memUsed; return s_snap.memUsed >= 0; }
        if (!strcmp(field, "total")) { out = s_snap.memTotal; return s_snap.memTotal >= 0; }
        return false;
    }
    if (!strcmp(group, "gpu")) {
        if (!strcmp(field, "load")) { out = s_snap.gpuLoad; return s_snap.gpuLoad >= 0; }
        if (!strcmp(field, "temp")) { out = s_snap.gpuTemp; return s_snap.gpuTemp >= 0; }
        return false;
    }
    if (!strncmp(group, "net", 3)) {
        int idx = trailingIndex(group);
        if (idx >= kMaxNets || !s_snap.net[idx].valid) return false;
        if (!strcmp(field, "up")) { out = s_snap.net[idx].up; return true; }
        if (!strcmp(field, "down")) { out = s_snap.net[idx].down; return true; }
        return false;
    }
    if (!strncmp(group, "disk", 4)) {
        int idx = trailingIndex(group);
        if (idx >= kMaxDisks || !s_snap.disk[idx].valid) return false;
        if (!strcmp(field, "pct")) { out = s_snap.disk[idx].pct; return true; }
        if (!strcmp(field, "free")) { out = s_snap.disk[idx].freeGb; return true; }
        return false;
    }
    if (const CustomVal* c = findCustom(path)) {
        if (c->isNum) { out = c->num; return true; }
    }
    return false;
}

bool getText(const char* path, char* buf, size_t bufLen) {
    char group[16];
    const char* dot = strchr(path, '.');
    const char* field = "";
    if (dot) {
        size_t glen = dot - path;
        if (glen >= sizeof(group)) return false;
        memcpy(group, path, glen);
        group[glen] = 0;
        field = dot + 1;
    } else {
        strlcpy(group, path, sizeof(group));
    }

    if (!strcmp(group, "host")) {
        if (!s_snap.host[0]) return false;
        strlcpy(buf, s_snap.host, bufLen);
        return true;
    }
    if (!strcmp(group, "cpu") && !strcmp(field, "name")) {
        if (!s_snap.cpuName[0]) return false;
        strlcpy(buf, s_snap.cpuName, bufLen);
        return true;
    }
    if (!strcmp(group, "gpu") && !strcmp(field, "name")) {
        if (!s_snap.gpuName[0]) return false;
        strlcpy(buf, s_snap.gpuName, bufLen);
        return true;
    }
    if (!strncmp(group, "net", 3)) {
        int idx = trailingIndex(group);
        if (idx >= kMaxNets || !s_snap.net[idx].valid) return false;
        if (!strcmp(field, "ip")) { strlcpy(buf, s_snap.net[idx].ip, bufLen); return true; }
        if (!strcmp(field, "if")) { strlcpy(buf, s_snap.net[idx].name, bufLen); return true; }
    }
    if (!strncmp(group, "disk", 4)) {
        int idx = trailingIndex(group);
        if (idx >= kMaxDisks || !s_snap.disk[idx].valid) return false;
        if (!strcmp(field, "n")) { strlcpy(buf, s_snap.disk[idx].name, bufLen); return true; }
    }
    if (!strcmp(group, "uptime")) {
        uint32_t s = s_snap.uptime;
        snprintf(buf, bufLen, "%lud %02lu:%02lu", (unsigned long)(s / 86400),
                 (unsigned long)((s / 3600) % 24), (unsigned long)((s / 60) % 60));
        return s_snap.uptime > 0;
    }

    if (const CustomVal* c = findCustom(path)) {
        strlcpy(buf, c->text, bufLen);
        return true;
    }

    // Fall back to numeric lookup, rendered with one decimal.
    float v;
    if (getNumber(path, v)) {
        snprintf(buf, bufLen, "%.1f", v);
        return true;
    }
    return false;
}

} // namespace stats
