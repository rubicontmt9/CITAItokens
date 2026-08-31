// ゲートウェイ: Web UI(静的ページ + REST API + WebSocket)(ROLE_GATEWAYのみ)
#pragma once
#include <Arduino.h>

#include <functional>

#include "state_registry.h"

namespace fw {

// ポート80で起動する。UI本体はLittleFSの /index.html。
// on_config_change: Web UIで設定が変更されたときに呼ばれる(node_id)
void webServerBegin(StateRegistry& registry,
                    std::function<void(uint32_t)> on_config_change);

// 新しいreportを接続中の全ブラウザにプッシュする
void webServerPushReport(const String& report_json);

}  // namespace fw
