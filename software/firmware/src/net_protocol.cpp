#include "net_protocol.h"

#include <ArduinoJson.h>

namespace fw {

String buildReport(uint32_t node_id, const persona::SensorSample& s,
                   const persona::Features& f, const persona::EmotionState& e,
                   const persona::PersonalityConfig& cfg, uint32_t wake_count) {
  StaticJsonDocument<512> doc;
  doc["type"] = "report";
  doc["id"] = node_id;
  doc["rev"] = cfg.rev;
  doc["name"] = cfg.name;
  doc["wake_count"] = wake_count;
  doc["battery_mv"] = s.battery_mv;
  doc["motion_wake"] = s.motion_wake;

  JsonObject sensors = doc.createNestedObject("sensors");
  sensors["temp_c"] = s.temp_c;
  sensors["hum_pct"] = s.hum_pct;
  sensors["lux"] = s.lux;
  sensors["vib_peak_g"] = s.vib_peak_g;
  sensors["idle_hours"] = f.idle_hours;
  sensors["battery_pct"] = f.battery_pct;

  JsonObject emotion = doc.createNestedObject("emotion");
  emotion["valence"] = e.valence;
  emotion["arousal"] = e.arousal;
  emotion["face"] = persona::faceName(e.face);

  String out;
  serializeJson(doc, out);
  return out;
}

String buildConfig(const persona::PersonalityConfig& cfg, uint32_t epoch) {
  StaticJsonDocument<512> doc;
  doc["type"] = "config";
  doc["rev"] = cfg.rev;
  doc["epoch"] = epoch;
  doc["name"] = cfg.name;
  doc["sensitivity"] = cfg.sensitivity;
  doc["temperament"] = persona::temperamentName(cfg.temperament);
  doc["report_interval_s"] = cfg.report_interval_s;
  doc["sync_every_n"] = cfg.sync_every_n;

  JsonObject th = doc.createNestedObject("thresholds");
  th["cold_c"] = cfg.th.cold_c;
  th["hot_c"] = cfg.th.hot_c;
  th["lonely_h"] = cfg.th.lonely_h;
  th["dark_lux"] = cfg.th.dark_lux;
  th["shock_g"] = cfg.th.shock_g;
  th["pet_g"] = cfg.th.pet_g;

  String out;
  serializeJson(doc, out);
  return out;
}

String messageType(const String& json) {
  StaticJsonDocument<64> filter;
  filter["type"] = true;
  StaticJsonDocument<128> doc;
  if (deserializeJson(doc, json, DeserializationOption::Filter(filter))) return "";
  return doc["type"] | "";
}

bool applyConfigMessage(const String& json, persona::PersonalityConfig& cfg,
                        uint32_t& epoch_out) {
  StaticJsonDocument<768> doc;
  if (deserializeJson(doc, json)) return false;
  if (String(doc["type"] | "") != "config") return false;
  epoch_out = doc["epoch"] | 0;

  uint32_t rev = doc["rev"] | 0;
  if (rev <= cfg.rev) return false;  // 手持ちの設定の方が新しい

  cfg.rev = rev;
  const char* name = doc["name"] | (const char*)nullptr;
  if (name) snprintf(cfg.name, sizeof(cfg.name), "%s", name);
  cfg.sensitivity = persona::clampf(doc["sensitivity"] | cfg.sensitivity, 0.0f, 1.0f);
  const char* temper = doc["temperament"] | (const char*)nullptr;
  if (temper) {
    persona::Temperament t;
    if (persona::temperamentFromName(temper, t)) cfg.temperament = t;
  }
  uint32_t interval = doc["report_interval_s"] | (uint32_t)cfg.report_interval_s;
  if (interval >= 5 && interval <= 3600) cfg.report_interval_s = interval;
  uint32_t sync_n = doc["sync_every_n"] | (uint32_t)cfg.sync_every_n;
  if (sync_n >= 1 && sync_n <= 60) cfg.sync_every_n = sync_n;

  JsonObject th = doc["thresholds"];
  if (!th.isNull()) {
    cfg.th.cold_c = th["cold_c"] | cfg.th.cold_c;
    cfg.th.hot_c = th["hot_c"] | cfg.th.hot_c;
    cfg.th.lonely_h = th["lonely_h"] | cfg.th.lonely_h;
    cfg.th.dark_lux = th["dark_lux"] | cfg.th.dark_lux;
    cfg.th.shock_g = th["shock_g"] | cfg.th.shock_g;
    cfg.th.pet_g = th["pet_g"] | cfg.th.pet_g;
  }
  return true;
}

bool parseReport(const String& json, ReportData& out) {
  StaticJsonDocument<768> doc;
  if (deserializeJson(doc, json)) return false;
  if (String(doc["type"] | "") != "report") return false;
  out.node_id = doc["id"] | 0;
  if (out.node_id == 0) return false;
  out.rev = doc["rev"] | 0;
  out.name = String(doc["name"] | "");
  out.wake_count = doc["wake_count"] | 0;
  out.battery_mv = doc["battery_mv"] | 0;
  out.motion_wake = doc["motion_wake"] | false;
  JsonObject s = doc["sensors"];
  out.temp_c = s["temp_c"] | 0.0f;
  out.hum_pct = s["hum_pct"] | 0.0f;
  out.lux = s["lux"] | 0.0f;
  out.vib_peak_g = s["vib_peak_g"] | 0.0f;
  out.idle_hours = s["idle_hours"] | 0.0f;
  out.battery_pct = s["battery_pct"] | 0.0f;
  JsonObject e = doc["emotion"];
  out.valence = e["valence"] | 0.0f;
  out.arousal = e["arousal"] | 0.0f;
  out.face = String(e["face"] | "content");
  return true;
}

}  // namespace fw
