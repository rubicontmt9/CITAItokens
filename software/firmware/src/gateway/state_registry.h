// ゲートウェイ: 全ノードの最新状態と設定の保持(ROLE_GATEWAYのみ)
#pragma once
#include <Arduino.h>

#include <map>

#include "../net_protocol.h"
#include "persona_config.h"

namespace fw {

struct NodeEntry {
  ReportData last_report;
  uint32_t last_seen_epoch = 0;
  persona::PersonalityConfig cfg;   // ゲートウェイが正とする設定
  bool cfg_loaded = false;
};

class StateRegistry {
 public:
  // report受信時に呼ぶ。エントリを作成/更新して返す。
  NodeEntry& onReport(const ReportData& rd, uint32_t now_epoch);

  NodeEntry* find(uint32_t node_id);

  // 設定を更新してNVSへ保存(revをインクリメント)
  void updateConfig(uint32_t node_id, const persona::PersonalityConfig& cfg);

  // /api/stickers 応答用のJSON
  String toJson(uint32_t now_epoch) const;

 private:
  std::map<uint32_t, NodeEntry> nodes_;
  void loadConfigFromNvs(uint32_t node_id, NodeEntry& e);
  void saveConfigToNvs(uint32_t node_id, const NodeEntry& e);
};

}  // namespace fw
