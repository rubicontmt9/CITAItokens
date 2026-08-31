// ゲートウェイ本体(ROLE_GATEWAYのみ)
// メッシュルート + ルーターブリッジ + Web UI + NTP/mDNS を担う。常時給電前提。
#ifdef ROLE_GATEWAY
#include <Arduino.h>
#include <ESPmDNS.h>
#include <painlessMesh.h>

#include "../config_store.h"
#include "../mesh_net.h"
#include "../net_protocol.h"
#include "setup_portal.h"
#include "state_registry.h"
#include "web_server.h"

namespace fw {

namespace {

painlessMesh g_mesh;
Scheduler g_scheduler;
StateRegistry g_registry;
bool g_portal_mode = false;
bool g_online_services_started = false;

// メッシュとルーターは同一チャンネルである必要があるため、
// 起動時にルーターのチャンネルをスキャンして合わせる
int findRouterChannel(const String& ssid) {
  WiFi.mode(WIFI_STA);
  int n = WiFi.scanNetworks();
  for (int i = 0; i < n; ++i) {
    if (WiFi.SSID(i) == ssid) {
      int ch = WiFi.channel(i);
      WiFi.scanDelete();
      return ch;
    }
  }
  WiFi.scanDelete();
  return 1;
}

void onMeshReceive(uint32_t from, String& msg) {
  if (messageType(msg) != "report") return;
  ReportData rd;
  if (!parseReport(msg, rd)) return;

  uint32_t now = static_cast<uint32_t>(time(nullptr));
  NodeEntry& e = g_registry.onReport(rd, now);
  // まだWeb UIで名前が付けられていないノードは、報告に載っていた名前を採用
  if (e.cfg.rev == 0 && rd.name.length() > 0) {
    snprintf(e.cfg.name, sizeof(e.cfg.name), "%s", rd.name.c_str());
  }
  // 報告への応答として設定を返す(ノードはrevが新しいときだけ適用する)
  g_mesh.sendSingle(from, buildConfig(e.cfg, now));
  webServerPushReport(msg);
  Serial.printf("[gw] report from %u face=%s\n", rd.node_id, rd.face.c_str());
}

}  // namespace

void gatewaySetup() {
  Serial.begin(115200);
  delay(100);

  String ssid, pass;
  if (!loadWifiCreds(ssid, pass)) {
    Serial.println("[gw] no wifi creds -> setup portal");
    g_portal_mode = true;
    setupPortalBegin();
    return;
  }

  int channel = findRouterChannel(ssid);
  Serial.printf("[gw] router '%s' on channel %d\n", ssid.c_str(), channel);

  g_mesh.init(kMeshPrefix, kMeshPassword, &g_scheduler, kMeshPort, WIFI_AP_STA,
              static_cast<uint8_t>(channel));
  g_mesh.setRoot(true);
  g_mesh.setContainsRoot(true);
  g_mesh.stationManual(ssid, pass);
  g_mesh.setHostname("persona");
  g_mesh.onReceive(&onMeshReceive);

  webServerBegin(g_registry, [](uint32_t node_id) {
    // 設定はゲートウェイが保持し、該当ノードの次回報告時の応答で配布される
    Serial.printf("[gw] config updated for %u (applied on next report)\n", node_id);
  });
}

void gatewayLoop() {
  if (g_portal_mode) {
    setupPortalLoop();
    return;
  }
  g_mesh.update();

  // ルーターからIPを取得できたらNTPとmDNSを開始する(1回だけ)
  if (!g_online_services_started &&
      g_mesh.getStationIP() != IPAddress(0, 0, 0, 0)) {
    configTime(0, 0, "pool.ntp.org", "time.google.com");  // epochはUTCで扱う
    if (MDNS.begin("persona")) {
      MDNS.addService("http", "tcp", 80);
      Serial.println("[gw] http://persona.local ready");
    }
    g_online_services_started = true;
  }
}

}  // namespace fw
#endif  // ROLE_GATEWAY
