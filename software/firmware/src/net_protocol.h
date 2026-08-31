// メッシュ上のJSONメッセージ(report / config)の生成・解析
// スキーマは docs/planning/02_system_architecture.md に準拠
#pragma once
#include <Arduino.h>

#include "emotion_engine.h"
#include "persona_features.h"
#include "sensor_sample.h"

namespace fw {

// ノード → ゲートウェイ: 状態報告
String buildReport(uint32_t node_id, const persona::SensorSample& s,
                   const persona::Features& f, const persona::EmotionState& e,
                   const persona::PersonalityConfig& cfg, uint32_t wake_count);

// ゲートウェイ → ノード: 設定応答
String buildConfig(const persona::PersonalityConfig& cfg, uint32_t epoch);

// 受信メッセージの種別を返す("report" / "config" / "")
String messageType(const String& json);

// config受信(ノード側): revが現在より新しければcfgを更新してtrue。
// epoch_out: 0以外なら時刻同期に使う。
bool applyConfigMessage(const String& json, persona::PersonalityConfig& cfg,
                        uint32_t& epoch_out);

// report受信(ゲートウェイ側)の解析結果
struct ReportData {
  uint32_t node_id = 0;
  uint32_t rev = 0;       // ノードが現在持っている設定rev
  String name;
  String face;
  float valence = 0, arousal = 0;
  float temp_c = 0, hum_pct = 0, lux = 0, vib_peak_g = 0;
  float idle_hours = 0, battery_pct = 0;
  uint16_t battery_mv = 0;
  uint32_t wake_count = 0;
  bool motion_wake = false;
};
bool parseReport(const String& json, ReportData& out);

}  // namespace fw
