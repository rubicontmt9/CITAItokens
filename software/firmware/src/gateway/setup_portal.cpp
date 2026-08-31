#ifdef ROLE_GATEWAY
#include "setup_portal.h"

#include <DNSServer.h>
#include <ESPAsyncWebServer.h>
#include <WiFi.h>

#include "../config_store.h"

namespace fw {

namespace {

DNSServer g_dns;
AsyncWebServer g_portal(80);

const char kSetupPage[] PROGMEM = R"HTML(<!DOCTYPE html>
<html lang="ja"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>PersonaSticker セットアップ</title>
<style>
body{font-family:sans-serif;background:#f2f0ea;margin:0;padding:24px;color:#2b2a26}
.card{max-width:360px;margin:40px auto;background:#fff;border-radius:12px;padding:24px;
      box-shadow:0 2px 8px rgba(0,0,0,.08)}
h1{font-size:18px;margin:0 0 8px}p{font-size:13px;color:#8a877e}
label{display:block;font-size:13px;margin:12px 0 4px}
input{width:100%;box-sizing:border-box;padding:10px;border:1px solid #dedbd2;
      border-radius:8px;font-size:15px}
button{width:100%;margin-top:20px;padding:12px;border:0;border-radius:8px;
       background:#3a6ea5;color:#fff;font-size:15px}
</style></head><body>
<div class="card">
<h1>🏷️ PersonaSticker</h1>
<p>ご家庭のWi-Fiルーターの情報を入力してください。保存後、ゲートウェイが再起動して接続します。</p>
<form method="POST" action="/setup">
<label>Wi-Fi名 (SSID)</label><input name="ssid" required>
<label>パスワード</label><input name="pass" type="password">
<button type="submit">保存して再起動</button>
</form>
</div></body></html>)HTML";

}  // namespace

void setupPortalBegin() {
  WiFi.mode(WIFI_AP);
  WiFi.softAP("PersonaSticker-Setup");
  g_dns.start(53, "*", WiFi.softAPIP());  // すべてのドメインをポータルへ

  g_portal.on("/setup", HTTP_POST, [](AsyncWebServerRequest* req) {
    String ssid, pass;
    if (req->hasParam("ssid", /*post=*/true)) {
      ssid = req->getParam("ssid", true)->value();
    }
    if (req->hasParam("pass", /*post=*/true)) {
      pass = req->getParam("pass", true)->value();
    }
    if (ssid.length() == 0) {
      req->send(400, "text/plain", "SSID required");
      return;
    }
    saveWifiCreds(ssid, pass);
    req->send(200, "text/html; charset=utf-8",
              "<meta charset=utf-8>保存しました。再起動します…");
    delay(500);
    ESP.restart();
  });
  // どのURLでもセットアップページを返す(キャプティブポータル)
  g_portal.onNotFound([](AsyncWebServerRequest* req) {
    req->send_P(200, "text/html; charset=utf-8", kSetupPage);
  });
  g_portal.begin();
}

void setupPortalLoop() { g_dns.processNextRequest(); }

}  // namespace fw
#endif  // ROLE_GATEWAY
