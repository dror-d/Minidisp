// Loads /themes/<name>/theme.json from LittleFS and builds the LVGL screen.
#pragma once

#include <lvgl.h>

namespace theme_engine {

// Enumerate theme folders under /themes. Returns number found.
int scanThemes();

int themeCount();
const char* themeName(int i);
bool hasTheme(const char* name);
const char* currentTheme();
int currentThemeIndex();

// Parse + build the theme on a fresh screen, load it, delete the old one.
// Returns the new screen (nullptr on failure — previous screen stays active).
lv_obj_t* loadTheme(const char* name);

int pageCount();
lv_obj_t* pageObj(int i);

// Push current stats into all bound widgets.
void applyStats();

} // namespace theme_engine
