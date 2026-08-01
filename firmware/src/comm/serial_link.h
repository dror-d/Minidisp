// JSON-lines serial protocol (docs/PROTOCOL.md). Reads commands/stats from the
// PC, replies with hello/ack/err. Transport is Serial (CH340 UART on CYD,
// native USB CDC on C6/S3).
#pragma once

#include <Arduino.h>

namespace serial_link {

void begin();
// Drain incoming bytes, dispatch complete lines. Call every loop().
void poll();
void sendHello();

} // namespace serial_link
