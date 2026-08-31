// ノード専用(ゲートウェイは gateway/gateway_main.cpp が独自にメッシュを持つ)
#ifdef ROLE_NODE
#include "mesh_net.h"

#include <painlessMesh.h>

#include "net_protocol.h"

namespace fw {

namespace {
painlessMesh g_mesh;
Scheduler g_scheduler;
String g_config_json;
bool g_config_received = false;
bool g_report_sent = false;
String g_report_json;
}  // namespace

bool nodeSyncCycle(const String& report_json, String& config_json_out,
                   uint32_t timeout_ms) {
  g_config_json = "";
  g_config_received = false;
  g_report_sent = false;
  g_report_json = report_json;

  g_mesh.init(kMeshPrefix, kMeshPassword, &g_scheduler, kMeshPort);
  g_mesh.setContainsRoot(true);

  g_mesh.onReceive([](uint32_t from, String& msg) {
    (void)from;
    if (messageType(msg) == "config") {
      g_config_json = msg;
      g_config_received = true;
    }
  });
  // 接続できたらreportを送る(ブロードキャストでルートまで届く)
  g_mesh.onNewConnection([](uint32_t nodeId) {
    (void)nodeId;
    if (!g_report_sent) {
      g_report_sent = g_mesh.sendBroadcast(g_report_json);
    }
  });

  uint32_t start = millis();
  while (millis() - start < timeout_ms) {
    g_mesh.update();
    // 接続済みなのに未送信のケース(onNewConnectionを取りこぼした場合)を拾う
    if (!g_report_sent && g_mesh.getNodeList().size() > 0) {
      g_report_sent = g_mesh.sendBroadcast(g_report_json);
    }
    if (g_config_received) break;
    delay(5);
  }

  g_mesh.stop();
  if (g_config_received) {
    config_json_out = g_config_json;
    return true;
  }
  return false;
}

}  // namespace fw
#endif  // ROLE_NODE
