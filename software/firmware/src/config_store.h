// NVS(Preferences)への設定永続化
#pragma once
#include <Arduino.h>

#include "persona_config.h"

namespace fw {

// ノードの性格・設定
void loadConfig(persona::PersonalityConfig& cfg);
void saveConfig(const persona::PersonalityConfig& cfg);

// ゲートウェイのルーター接続情報。未設定ならfalse。
bool loadWifiCreds(String& ssid, String& pass);
void saveWifiCreds(const String& ssid, const String& pass);
void clearWifiCreds();

}  // namespace fw
