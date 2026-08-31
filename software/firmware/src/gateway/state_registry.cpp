#ifdef ROLE_GATEWAY
#include "state_registry.h"

#include <ArduinoJson.h>
#include <Preferences.h>

namespace fw {

namespace {
constexpr char kNsNodeCfg[] = "nodecfg";
constexpr uint8_t kCfgVersion = 1;

String nodeKey(uint32_t node_id) {
  // Preferencesのキーは15文字まで
  return String(node_id, HEX);
}
}  // namespace

void StateRegistry::loadConfigFromNvs(uint32_t node_id, NodeEntry& e) {
  Preferences prefs;
  if (!prefs.begin(kNsNodeCfg, /*readOnly=*/true)) return;
  String key = nodeKey(node_id);
  String verKey = key + "v";
  if (prefs.getUChar(verKey.c_str(), 0) == kCfgVersion &&
      prefs.getBytesLength(key.c_str()) == sizeof(e.cfg)) {
    prefs.getBytes(key.c_str(), &e.cfg, sizeof(e.cfg));
  }
  prefs.end();
}

void StateRegistry::saveConfigToNvs(uint32_t node_id, const NodeEntry& e) {
  Preferences prefs;
  if (!prefs.begin(kNsNodeCfg, /*readOnly=*/false)) return;
  String key = nodeKey(node_id);
  String verKey = key + "v";
  prefs.putUChar(verKey.c_str(), kCfgVersion);
  prefs.putBytes(key.c_str(), &e.cfg, sizeof(e.cfg));
  prefs.end();
}

NodeEntry& StateRegistry::onReport(const ReportData& rd, uint32_t now_epoch) {
  auto it = nodes_.find(rd.node_id);
  if (it == nodes_.end()) {
    NodeEntry e;
    // 初回は報告に載っていた名前を初期設定として引き継ぐ
    loadConfigFromNvs(rd.node_id, e);
    e.cfg_loaded = true;
    it = nodes_.emplace(rd.node_id, e).first;
  }
  it->second.last_report = rd;
  it->second.last_seen_epoch = now_epoch;
  return it->second;
}

NodeEntry* StateRegistry::find(uint32_t node_id) {
  auto it = nodes_.find(node_id);
  return it == nodes_.end() ? nullptr : &it->second;
}

void StateRegistry::updateConfig(uint32_t node_id,
                                 const persona::PersonalityConfig& cfg) {
  auto it = nodes_.find(node_id);
  if (it == nodes_.end()) {
    NodeEntry e;
    e.cfg_loaded = true;
    it = nodes_.emplace(node_id, e).first;
  }
  uint32_t rev = it->second.cfg.rev;
  it->second.cfg = cfg;
  it->second.cfg.rev = rev + 1;  // 変更のたびにrevを進める → ノードが次回同期で適用
  saveConfigToNvs(node_id, it->second);
}

String StateRegistry::toJson(uint32_t now_epoch) const {
  DynamicJsonDocument doc(8192);
  JsonArray arr = doc.createNestedArray("stickers");
  for (const auto& [id, e] : nodes_) {
    JsonObject o = arr.createNestedObject();
    o["id"] = id;
    o["name"] = e.cfg.name;
    o["face"] = e.last_report.face;
    o["valence"] = e.last_report.valence;
    o["arousal"] = e.last_report.arousal;
    o["temp_c"] = e.last_report.temp_c;
    o["hum_pct"] = e.last_report.hum_pct;
    o["lux"] = e.last_report.lux;
    o["idle_hours"] = e.last_report.idle_hours;
    o["battery_pct"] = e.last_report.battery_pct;
    o["battery_mv"] = e.last_report.battery_mv;
    o["wake_count"] = e.last_report.wake_count;
    o["last_seen_s_ago"] =
        (now_epoch >= e.last_seen_epoch) ? now_epoch - e.last_seen_epoch : 0;
    o["sensitivity"] = e.cfg.sensitivity;
    o["temperament"] = persona::temperamentName(e.cfg.temperament);
    o["report_interval_s"] = e.cfg.report_interval_s;
    o["cfg_rev"] = e.cfg.rev;
    o["node_rev"] = e.last_report.rev;  // ノードに反映済みか比較できる
    JsonObject th = o.createNestedObject("thresholds");
    th["cold_c"] = e.cfg.th.cold_c;
    th["hot_c"] = e.cfg.th.hot_c;
    th["lonely_h"] = e.cfg.th.lonely_h;
  }
  String out;
  serializeJson(doc, out);
  return out;
}

}  // namespace fw
#endif  // ROLE_GATEWAY
