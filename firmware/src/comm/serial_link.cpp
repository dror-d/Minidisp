#include "serial_link.h"

#include <ArduinoJson.h>
#include <LittleFS.h>
#include <mbedtls/base64.h>

#include "../data/stats_model.h"
#include "../hal/hal.h"
#include "../theme/theme_engine.h"
#include "../ui/ui.h"

namespace serial_link {

namespace {
constexpr size_t kMaxLine = 4096;
char s_line[kMaxLine];
size_t s_lineLen = 0;

// --- file upload state (fs.begin / fs.data / fs.end) -----------------------
constexpr size_t kMaxUploadSize = 64 * 1024;
constexpr const char* kUploadTmp = "/themes/.upload.tmp";
File s_upFile;
char s_upTarget[80] = "";
size_t s_upWritten = 0;
size_t s_upExpected = 0;

void abortUpload() {
    if (s_upFile) s_upFile.close();
    LittleFS.remove(kUploadTmp);
    s_upTarget[0] = 0;
}

void sendAck(const char* cmd) {
    JsonDocument doc;
    doc["ack"] = cmd;
    serializeJson(doc, Serial);
    Serial.println();
}

void sendErr(const char* cmd, const char* msg) {
    JsonDocument doc;
    doc["err"]["cmd"] = cmd;
    doc["err"]["msg"] = msg;
    serializeJson(doc, Serial);
    Serial.println();
}

// Theme upload: validate target, stream base64 chunks into a temp file, then
// atomically swap it into place and hot-reload if the current theme changed.
void handleFsBegin(JsonObjectConst root) {
    const char* path = root["path"];
    size_t size = root["size"] | 0;
    if (!path || strncmp(path, "/themes/", 8) != 0 || strstr(path, "..") ||
        strlen(path) >= sizeof(s_upTarget)) {
        sendErr("fs.begin", "bad path");
        return;
    }
    if (size == 0 || size > kMaxUploadSize) {
        sendErr("fs.begin", "bad size");
        return;
    }
    abortUpload();

    // Ensure the theme folder exists (paths are /themes/<name>/<file>).
    char dir[80];
    strlcpy(dir, path, sizeof(dir));
    char* lastSlash = strrchr(dir, '/');
    if (lastSlash && lastSlash > dir + 8) {
        *lastSlash = 0;
        LittleFS.mkdir(dir);
    }

    s_upFile = LittleFS.open(kUploadTmp, "w");
    if (!s_upFile) {
        sendErr("fs.begin", "open failed");
        return;
    }
    strlcpy(s_upTarget, path, sizeof(s_upTarget));
    s_upWritten = 0;
    s_upExpected = size;
    sendAck("fs.begin");
}

void handleFsData(JsonObjectConst root) {
    const char* b64 = root["b64"];
    if (!s_upTarget[0] || !b64) {
        sendErr("fs.data", "no upload in progress");
        return;
    }
    uint8_t buf[1024];
    size_t decoded = 0;
    if (mbedtls_base64_decode(buf, sizeof(buf), &decoded,
                              (const uint8_t*)b64, strlen(b64)) != 0) {
        abortUpload();
        sendErr("fs.data", "bad base64");
        return;
    }
    if (s_upWritten + decoded > s_upExpected ||
        s_upFile.write(buf, decoded) != decoded) {
        abortUpload();
        sendErr("fs.data", "write failed");
        return;
    }
    s_upWritten += decoded;
    sendAck("fs.data");
}

void handleFsEnd() {
    if (!s_upTarget[0]) {
        sendErr("fs.end", "no upload in progress");
        return;
    }
    s_upFile.close();
    if (s_upWritten != s_upExpected) {
        abortUpload();
        sendErr("fs.end", "size mismatch");
        return;
    }
    LittleFS.remove(s_upTarget);
    if (!LittleFS.rename(kUploadTmp, s_upTarget)) {
        abortUpload();
        sendErr("fs.end", "rename failed");
        return;
    }
    theme_engine::scanThemes();

    // Hot-reload if the file belongs to the currently displayed theme.
    char themeName[24] = "";
    sscanf(s_upTarget, "/themes/%23[^/]", themeName);
    if (!strcmp(themeName, theme_engine::currentTheme())) {
        ui::requestThemeSwitch(themeName);
    }
    s_upTarget[0] = 0;
    sendAck("fs.end");
}

void handleCommand(const char* cmd, JsonObjectConst root) {
    if (!strcmp(cmd, "ping")) {
        sendHello();
    } else if (!strcmp(cmd, "fs.begin")) {
        handleFsBegin(root);
    } else if (!strcmp(cmd, "fs.data")) {
        handleFsData(root);
    } else if (!strcmp(cmd, "fs.end")) {
        handleFsEnd();
    } else if (!strcmp(cmd, "theme")) {
        const char* name = root["name"];
        if (name && theme_engine::hasTheme(name)) {
            ui::requestThemeSwitch(name);
            sendAck(cmd);
        } else {
            sendErr(cmd, "theme not found");
        }
    } else if (!strcmp(cmd, "page")) {
        ui::showPage(root["n"] | 0);
        sendAck(cmd);
    } else if (!strcmp(cmd, "brightness")) {
        int v = root["v"] | 80;
        ui::setBrightness(constrain(v, 0, 100));
        sendAck(cmd);
    } else {
        sendErr(cmd, "unknown command");
    }
}

void handleLine(const char* line) {
    JsonDocument doc;
    DeserializationError err = deserializeJson(doc, line);
    if (err) return; // garbage on the wire — ignore

    JsonObjectConst root = doc.as<JsonObjectConst>();
    JsonObjectConst statsObj = root["stats"];
    if (!statsObj.isNull()) {
        stats::update(statsObj);
        ui::onStatsUpdated();
        return;
    }
    const char* cmd = root["cmd"];
    if (cmd) handleCommand(cmd, root);
}
} // namespace

void begin() {
    Serial.setRxBufferSize(4096); // must precede begin() on ESP32
    Serial.begin(115200);
}

void poll() {
    while (Serial.available()) {
        char c = (char)Serial.read();
        if (c == '\n' || c == '\r') {
            if (s_lineLen > 0) {
                s_line[s_lineLen] = 0;
                handleLine(s_line);
                s_lineLen = 0;
            }
        } else if (s_lineLen < kMaxLine - 1) {
            s_line[s_lineLen++] = c;
        } else {
            s_lineLen = 0; // oversized line — drop it
        }
    }
}

void sendHello() {
    JsonDocument doc;
    JsonObject hello = doc["hello"].to<JsonObject>();
    hello["fw"] = "minidisp";
    hello["ver"] = MINIDISP_VERSION;
    hello["board"] = hal::boardName();
    JsonArray res = hello["res"].to<JsonArray>();
    res.add(hal::width());
    res.add(hal::height());
    JsonArray themes = hello["themes"].to<JsonArray>();
    for (int i = 0; i < theme_engine::themeCount(); i++) {
        themes.add(theme_engine::themeName(i));
    }
    hello["theme"] = theme_engine::currentTheme();
    serializeJson(doc, Serial);
    Serial.println();
}

} // namespace serial_link
