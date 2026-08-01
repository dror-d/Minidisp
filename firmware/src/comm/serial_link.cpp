#include "serial_link.h"

#include <ArduinoJson.h>

#include "../data/stats_model.h"
#include "../hal/hal.h"
#include "../theme/theme_engine.h"
#include "../ui/ui.h"

namespace serial_link {

namespace {
constexpr size_t kMaxLine = 4096;
char s_line[kMaxLine];
size_t s_lineLen = 0;

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

void handleCommand(const char* cmd, JsonObjectConst root) {
    if (!strcmp(cmd, "ping")) {
        sendHello();
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
