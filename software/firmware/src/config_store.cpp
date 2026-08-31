#include "config_store.h"

#include <Preferences.h>

namespace fw {

namespace {
constexpr char kNsPersona[] = "persona";
constexpr char kNsNet[] = "net";
// 構造体レイアウト変更時にインクリメントする(古いblobを読まないため)
constexpr uint8_t kCfgVersion = 1;
}  // namespace

void loadConfig(persona::PersonalityConfig& cfg) {
  Preferences prefs;
  if (!prefs.begin(kNsPersona, /*readOnly=*/true)) return;
  if (prefs.getUChar("ver", 0) == kCfgVersion &&
      prefs.getBytesLength("cfg") == sizeof(cfg)) {
    prefs.getBytes("cfg", &cfg, sizeof(cfg));
  }
  prefs.end();
}

void saveConfig(const persona::PersonalityConfig& cfg) {
  Preferences prefs;
  if (!prefs.begin(kNsPersona, /*readOnly=*/false)) return;
  prefs.putUChar("ver", kCfgVersion);
  prefs.putBytes("cfg", &cfg, sizeof(cfg));
  prefs.end();
}

bool loadWifiCreds(String& ssid, String& pass) {
  Preferences prefs;
  if (!prefs.begin(kNsNet, /*readOnly=*/true)) return false;
  ssid = prefs.getString("ssid", "");
  pass = prefs.getString("pass", "");
  prefs.end();
  return ssid.length() > 0;
}

void saveWifiCreds(const String& ssid, const String& pass) {
  Preferences prefs;
  if (!prefs.begin(kNsNet, /*readOnly=*/false)) return;
  prefs.putString("ssid", ssid);
  prefs.putString("pass", pass);
  prefs.end();
}

void clearWifiCreds() {
  Preferences prefs;
  if (!prefs.begin(kNsNet, /*readOnly=*/false)) return;
  prefs.clear();
  prefs.end();
}

}  // namespace fw
