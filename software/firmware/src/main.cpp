// PersonaSticker ファームウェア エントリポイント
//   ROLE_NODE:    電池駆動ノード。起床→センシング→感情→表示→(同期)→ディープスリープ
//   ROLE_GATEWAY: 常時給電。src/gateway/gateway_main.cpp に実体がある
#include <Arduino.h>

#ifdef ROLE_GATEWAY

namespace fw {
void gatewaySetup();
void gatewayLoop();
}  // namespace fw

void setup() { fw::gatewaySetup(); }
void loop() { fw::gatewayLoop(); }

#else  // ROLE_NODE ---------------------------------------------------------

#include <WiFi.h>
#include <sys/time.h>
#include <time.h>

#include "config_store.h"
#include "display_epd.h"
#include "emotion_engine.h"
#include "face_renderer.h"
#include "mesh_net.h"
#include "net_protocol.h"
#include "pins.h"
#include "power_mgr.h"
#include "sensors.h"

namespace {

// 全面リフレッシュの間隔(残像・焼付き防止)
constexpr uint32_t kFullRefreshIntervalS = 3600;

// MACアドレス下位から安定したノードIDを作る(painlessMeshのnodeIdと同一)
uint32_t nodeId() {
  uint8_t mac[6];
  WiFi.macAddress(mac);
  return (static_cast<uint32_t>(mac[2]) << 24) |
         (static_cast<uint32_t>(mac[3]) << 16) |
         (static_cast<uint32_t>(mac[4]) << 8) | mac[5];
}

persona::FrameBuffer g_fb;  // 5KBあるのでスタックに置かない

}  // namespace

void setup() {
  // 1起床サイクルのすべてをsetup()内で行い、最後にディープスリープする
  Serial.begin(115200);  // USB CDC。電池運用で電力が気になる場合は無効化する
  fw::WakeCause cause = fw::wakeCause();
  fw::RtcState& rtc = fw::rtcState();

  persona::PersonalityConfig cfg;
  fw::loadConfig(cfg);

  // --- センシング ---
  fw::sensorsInit();
  persona::SensorSample s = fw::readSample(cause == fw::WakeCause::Motion);

  // --- 感情の更新 ---
  persona::Features f = persona::extractFeatures(rtc.fstate, s, cfg.th);
  persona::Face face = persona::EmotionEngine::update(rtc.estate, f, cfg);
  Serial.printf("[node] wake=%d face=%s v=%.2f a=%.2f temp=%.1f vib=%.2f\n",
                static_cast<int>(cause), persona::faceName(face),
                rtc.estate.valence, rtc.estate.arousal, s.temp_c, s.vib_peak_g);

  // --- 表示(表情が変わったときだけ書き換え)---
  bool need_draw = (static_cast<int8_t>(face) != rtc.last_face) ||
                   cause == fw::WakeCause::PowerOn;
  if (need_draw) {
    bool full = (rtc.last_face < 0) ||
                (s.t - rtc.last_full_refresh_t > kFullRefreshIntervalS);
    persona::StatusInfo info;
    info.name = cfg.name;
    info.temp_c = s.temp_c;
    info.battery_pct = f.battery_pct;
    persona::renderFace(g_fb, face, info);
    fw::displayShow(g_fb, full);
    rtc.last_face = static_cast<int8_t>(face);
    if (full) rtc.last_full_refresh_t = s.t;
  }

  // --- メッシュ同期(N回に1回 / 振動起床時は必ず)---
  bool sync_due = (cause == fw::WakeCause::Motion) ||
                  (rtc.wake_count % (cfg.sync_every_n ? cfg.sync_every_n : 1)) == 0;
  if (sync_due) {
    String report = fw::buildReport(nodeId(), s, f, rtc.estate, cfg, rtc.wake_count);
    String config_json;
    if (fw::nodeSyncCycle(report, config_json, /*timeout_ms=*/8000)) {
      uint32_t epoch = 0;
      if (fw::applyConfigMessage(config_json, cfg, epoch)) {
        fw::saveConfig(cfg);
        Serial.printf("[node] config applied rev=%u\n", cfg.rev);
      }
      if (epoch > 1600000000U) {
        // ゲートウェイの時刻に同期(RTCはディープスリープ中も進む)
        struct timeval tv = {static_cast<time_t>(epoch), 0};
        settimeofday(&tv, nullptr);
      }
    }
  }

  rtc.wake_count++;

  // --- 次の起床を設定してディープスリープ ---
  fw::configureMotionInterrupt(cfg.th.pet_g);
  fw::deepSleep(cfg.report_interval_s, pins::kLisInt);
}

void loop() {}  // 到達しない(setup末尾でディープスリープする)

#endif  // ROLE_GATEWAY / ROLE_NODE
