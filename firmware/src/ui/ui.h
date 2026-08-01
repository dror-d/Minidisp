// Screen/page management: boot screen, page & theme cycling via touch,
// "waiting for PC" banner, persisted settings (theme, brightness).
#pragma once

namespace ui {

// Build the boot screen and the status banner. Call after LVGL display init.
void init();

// Load a theme now and wire up touch events. Falls back to the first available
// theme if `name` is missing.
void applyTheme(const char* name);

// Queue a theme switch (safe to call from LVGL event callbacks / serial).
void requestThemeSwitch(const char* name);

// Handle queued switches + staleness banner. Call every loop() iteration.
void update();

void showPage(int n);
void nextPage();
void nextTheme();

// Called whenever a stats message was applied.
void onStatsUpdated();

void setBrightness(int pct); // persists
const char* savedThemeName(); // persisted theme ("" if none)

} // namespace ui
