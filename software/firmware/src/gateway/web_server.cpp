#ifdef ROLE_GATEWAY
#include "web_server.h"

#include <ArduinoJson.h>
#include <AsyncJson.h>
#include <ESPAsyncWebServer.h>
#include <LittleFS.h>
#include <time.h>

namespace fw {

namespace {
AsyncWebServer g_server(80);
AsyncWebSocket g_ws("/ws");
StateRegistry* g_registry = nullptr;
std::function<void(uint32_t)> g_on_config_change;
}  // namespace

void webServerBegin(StateRegistry& registry,
                    std::function<void(uint32_t)> on_config_change) {
  g_registry = &registry;
  g_on_config_change = std::move(on_config_change);

  LittleFS.begin(/*formatOnFail=*/true);

  g_server.addHandler(&g_ws);

  // UI本体
  g_server.serveStatic("/", LittleFS, "/")
      .setDefaultFile("index.html")
      .setCacheControl("no-store");

  // 全ステッカー一覧
  g_server.on("/api/stickers", HTTP_GET, [](AsyncWebServerRequest* req) {
    req->send(200, "application/json",
              g_registry->toJson(static_cast<uint32_t>(time(nullptr))));
  });

  // システム情報
  g_server.on("/api/system", HTTP_GET, [](AsyncWebServerRequest* req) {
    StaticJsonDocument<256> doc;
    doc["uptime_s"] = millis() / 1000;
    doc["epoch"] = static_cast<uint32_t>(time(nullptr));
    doc["free_heap"] = ESP.getFreeHeap();
    doc["sta_ip"] = WiFi.localIP().toString();
    String out;
    serializeJson(doc, out);
    req->send(200, "application/json", out);
  });

  // 設定変更: {"id": <node_id>, "name": ..., "sensitivity": ..., ...}
  auto* cfgHandler = new AsyncCallbackJsonWebHandler(
      "/api/config", [](AsyncWebServerRequest* req, JsonVariant& json) {
        JsonObject o = json.as<JsonObject>();
        uint32_t id = o["id"] | 0;
        if (id == 0) {
          req->send(400, "application/json", "{\"error\":\"id required\"}");
          return;
        }
        NodeEntry* e = g_registry->find(id);
        persona::PersonalityConfig cfg = e ? e->cfg : persona::PersonalityConfig();
        const char* name = o["name"] | (const char*)nullptr;
        if (name) snprintf(cfg.name, sizeof(cfg.name), "%s", name);
        cfg.sensitivity =
            persona::clampf(o["sensitivity"] | cfg.sensitivity, 0.0f, 1.0f);
        const char* temper = o["temperament"] | (const char*)nullptr;
        if (temper) {
          persona::Temperament t;
          if (persona::temperamentFromName(temper, t)) cfg.temperament = t;
        }
        uint32_t interval = o["report_interval_s"] | (uint32_t)cfg.report_interval_s;
        if (interval >= 5 && interval <= 3600) cfg.report_interval_s = interval;
        uint32_t sync_n = o["sync_every_n"] | (uint32_t)cfg.sync_every_n;
        if (sync_n >= 1 && sync_n <= 60) cfg.sync_every_n = sync_n;
        cfg.th.cold_c = o["cold_c"] | cfg.th.cold_c;
        cfg.th.hot_c = o["hot_c"] | cfg.th.hot_c;
        cfg.th.lonely_h = o["lonely_h"] | cfg.th.lonely_h;

        g_registry->updateConfig(id, cfg);
        if (g_on_config_change) g_on_config_change(id);
        req->send(200, "application/json", "{\"ok\":true}");
      });
  g_server.addHandler(cfgHandler);

  g_server.onNotFound([](AsyncWebServerRequest* req) {
    req->send(404, "application/json", "{\"error\":\"not found\"}");
  });

  g_server.begin();
}

void webServerPushReport(const String& report_json) {
  g_ws.cleanupClients();
  if (g_ws.count() > 0) g_ws.textAll(report_json);
}

}  // namespace fw
#endif  // ROLE_GATEWAY
