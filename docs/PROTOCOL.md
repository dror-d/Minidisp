# Minidisp Serial Protocol v1

Transport: USB serial, **115200 baud, 8N1**. Messages are single-line JSON objects terminated by `\n` (JSON Lines). Either side ignores lines it cannot parse.

## Device → PC

### hello
Sent once on boot, and as reply to `ping`. Used by the companion for port auto-detection.

```json
{"hello":{"fw":"minidisp","ver":"0.1.0","board":"cyd","res":[320,240],"themes":["carbon","gauges","terminal"],"theme":"carbon"}}
```

### ack / err
Reply to any `cmd`:

```json
{"ack":"theme"}
{"err":{"cmd":"theme","msg":"theme not found"}}
```

## PC → Device

### ping
```json
{"cmd":"ping"}
```

### stats (pushed at 2 Hz)
All fields optional — the firmware renders what it receives, widgets bound to missing paths show `--`.

```json
{"stats":{
  "host":"MYPC","uptime":123456,
  "cpu":{"load":42.1,"temp":56.0,"freq":3.8,"cores":[35.1,46.2,55.3,44.8],"name":"Ryzen 7 5800X"},
  "mem":{"pct":61.2,"used":9.8,"total":16.0},
  "gpu":{"load":17.0,"temp":48.0,"name":"RTX 3070"},
  "net":[{"if":"Ethernet","ip":"192.168.1.10","up":1.2,"down":34.5}],
  "disk":[{"n":"C:","pct":75.0,"free":250.1}]
}}
```

Units: temps °C, mem/disk free GB, net rates Mbit/s, uptime seconds, loads/pct 0–100.

### Commands

```json
{"cmd":"theme","name":"gauges"}      // switch theme (persisted to NVS)
{"cmd":"page","n":1}                 // jump to page n (0-based)
{"cmd":"brightness","v":80}          // backlight 0-100 (persisted)
```

### Theme upload (fs.*) — protocol v1.1

Streams a file into `/themes/` on the device's LittleFS (used by the companion
theme editor's "Push to device"). Every command is acked; the sender must wait
for each `ack` before the next chunk (flow control for the 4KB RX buffer).

```json
{"cmd":"fs.begin","path":"/themes/mytheme/theme.json","size":2048}
{"cmd":"fs.data","b64":"<base64 of up to 768 raw bytes>"}   // repeat
{"cmd":"fs.end"}
```

Rules: `path` must start with `/themes/` (no `..`), max file size 64KB. The
file is written to a temp file and atomically renamed on `fs.end`; the theme
list is rescanned and the current theme hot-reloads if it was the one updated.
Errors (`bad path`, `bad size`, `bad base64`, `write failed`, `size mismatch`,
`rename failed`) abort the upload.

## Bind paths (used by themes, see THEMES.md)

`host`, `uptime`, `cpu.load`, `cpu.temp`, `cpu.freq`, `cpu.name`, `cpu.core0`..`cpu.core31`,
`mem.pct`, `mem.used`, `mem.total`, `gpu.load`, `gpu.temp`, `gpu.name`,
`net.if`, `net.ip`, `net.up`, `net.down` (first interface; `net1.ip` etc. for others),
`disk.n`, `disk.pct`, `disk.free` (first disk; `disk1.*` for others).

## Timeouts

- Firmware: no valid `stats` for **5 s** → "waiting for PC" screen; resumes on next stats line.
- Companion: no `hello` reply to `ping` within **2 s** during port probing → try next port. After connect, if writes fail the companion re-enters auto-detect.

## Port auto-detection (companion)

1. Enumerate serial ports; prefer known VID:PIDs — CH340 `1A86:7523` (CYD), Espressif CDC `303A:1001` (ESP32-C6), CP210x `10C4:EA60`.
2. Open at 115200, send `{"cmd":"ping"}`, wait 2 s for `{"hello":...}`.
3. Fall back to probing remaining COM ports. Re-probe every 5 s while disconnected.
