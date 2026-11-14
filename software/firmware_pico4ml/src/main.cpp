// PersonaSticker 簡素版(Arducam Pico4ML-BLE)
//
// ESP32 版との違い:
//   ・電池・ディープスリープ・Wi-Fi メッシュは無し。USB 給電で常時動き、1 秒ごとに感情を更新する
//   ・温度 = IMU のチップ温度 + ずれの補正、明るさ = カメラの画素の平均、振動 = IMU の加速度
//   ・表示 = 0.96 型カラー液晶(80x160)。表情は共有コアの 200x200 の絵を縮めて上半分に、数値を下半分に
//   ・感情エンジン(lib/emotion_core)は ESP32 版・シミュレーター・テストと同じものを使う
#include <Arduino.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "board.h"
#include "face_lcd.h"
#include "emotion_engine.h"
#include "face_renderer.h"
#include "persona_features.h"

using namespace persona;

namespace {

// ---------------------------------------------------------------- 設定(シリアルのコマンドで変える)
struct Settings {
  float temp_offset_c = -5.0f;  // チップ温度 → 室温のずれ [VERIFY 実機で温度計と比べる]
  // 明るさ(board.h の light)がこれ未満で「暗い」。実機(2026-11-14、電気をつけた部屋): 机に置いて 134、
  // 置き方で暗い側を向くと 3.5〜12、指でふさぐと 0.5〜0.7 → 間を取って 1.5
  float dark_level = 1.5f;
  // 露出は既定で自動。固定の露出だと置き場所しだいで真っ暗(13)にも真っ白(255)にもなった(2026-11-14 実機)
  bool manual = false;
  uint16_t exposure = 368;      // 手動の時の露出の行数(最大 0x170)
  uint8_t gain = 0;             // 手動の時のアナログゲイン 0..3 = 1x / 2x / 4x / 8x(0x0205 に gain << 4)
  bool demo = true;             // デモモード: 放置 60 秒で「さみしい」(通常は 48 時間)
};
Settings S;
PersonalityConfig g_cfg;

FeatureState g_fstate;
EmotionState g_estate;
FrameBuffer g_fb;

constexpr uint32_t kTickMs = 1000;        // 感情を更新する間隔
constexpr uint32_t kImuPeriodMs = 10;     // 加速度を読む間隔(IMU は 100 Hz)
constexpr uint32_t kCamPeriodMs = 3000;   // 明るさを取る間隔(撮る約 0.3 秒は加速度が読めないので間を空ける)
constexpr uint32_t kHoldMs = 3000;        // 「びっくり」「こわい」を最低これだけ見せる
constexpr float kTempTauS = 20.0f;        // 温度の平滑化の時定数
constexpr float kTempRateWindowS = 60.0f; // 温度の変化率を見る幅

bool g_cam_ok = false;
bool g_imu_ok = false;

float g_vib_peak = 0.0f;  // 前の tick からの |a| - 1 g の最大
float g_temp_raw = NAN, g_temp_smooth = NAN;
float g_temp_hist[64];    // 1 秒ごとの平滑温度(変化率用のリングバッファ)
int g_temp_hist_n = 0, g_temp_hist_i = 0;
float g_light = -1.0f;    // 明るさ(board.h の CamLight::light)
board::CamLight g_cam;

Face g_shown = Face::Count;
Face g_hold_face = Face::Count;
uint32_t g_hold_until = 0;

// 表情を液晶の上 80x80 に描く(縮め方と色は face_lcd.h、PC の確認用ツールと共通)
void drawFace(Face face) {
  static uint16_t buf[80 * 80];
  StatusInfo info;
  info.name = g_cfg.name;
  info.temp_c = isnan(g_temp_smooth) ? 25.0f : g_temp_smooth;
  info.battery_pct = 100.0f;
  renderFace(g_fb, face, info);
  lcdface::faceTo80(g_fb, lcdface::faceColor(face), buf);
  board::lcdWindow(0, 0, 79, 79);
  board::lcdPixels(buf, 80 * 80);
}

void drawStatus(Face face) {
  const uint16_t fg = 0xFFFF, bg = 0x0000, dim = board::rgb565(140, 140, 140);
  char s[20];
  board::lcdText(84, g_cfg.name, lcdface::faceColor(face).bg, bg);
  board::lcdText(96, faceName(face), fg, bg);
  if (isnan(g_temp_smooth)) snprintf(s, sizeof(s), "TEMP --");
  else snprintf(s, sizeof(s), "TEMP %.1fC", g_temp_smooth);
  board::lcdText(108, s, fg, bg);
  if (g_light < 0) snprintf(s, sizeof(s), "LIGHT --");
  else snprintf(s, sizeof(s), "LIGHT %.0f%s", g_light, g_light < S.dark_level ? " D" : "");
  board::lcdText(120, s, fg, bg);
  snprintf(s, sizeof(s), "V%+.1f A%+.1f", g_estate.valence, g_estate.arousal);
  board::lcdText(132, s, dim, bg);
  snprintf(s, sizeof(s), "%s%s%s", temperamentName(g_cfg.temperament), S.demo ? " DEMO" : "",
           board::bleConnected() ? " BT" : "");
  board::lcdText(144, s, dim, bg);
}

// ---------------------------------------------------------------- センサー
void pollImu() {
  float ax, ay, az;
  if (!g_imu_ok || !board::imuReadAccel(ax, ay, az)) return;
  const float dev = fabsf(sqrtf(ax * ax + ay * ay + az * az) - 1.0f);  // 重力ぶんを除く
  if (dev > g_vib_peak) g_vib_peak = dev;
}

void updateTemp() {
  float t;
  if (!g_imu_ok || !board::imuReadTempC(t)) return;
  g_temp_raw = t;
  const float room = t + S.temp_offset_c;
  if (isnan(g_temp_smooth)) {
    g_temp_smooth = room;
  } else {
    const float alpha = (kTickMs / 1000.0f) / kTempTauS;
    g_temp_smooth += alpha * (room - g_temp_smooth);
  }
  g_temp_hist[g_temp_hist_i] = g_temp_smooth;
  g_temp_hist_i = (g_temp_hist_i + 1) % 64;
  if (g_temp_hist_n < 64) ++g_temp_hist_n;
}

// 温度の変化率 [°C/min]。共有コアは「前回の起床との差」で出すが、1 秒ごとだとチップ温度の揺れで
// 常に「急変」になるので、平滑した温度の 60 秒前との差で置き換える。
float tempRatePerMin() {
  const int back = int(kTempRateWindowS * 1000.0f / kTickMs);
  if (g_temp_hist_n <= back) return 0.0f;
  const int cur = (g_temp_hist_i + 63) % 64;
  const int old = (cur - back + 64 * 4) % 64;
  return (g_temp_hist[cur] - g_temp_hist[old]) / (kTempRateWindowS / 60.0f);
}

// ---------------------------------------------------------------- 1 秒ごとの更新
void tick() {
  updateTemp();
  g_cfg.th.lonely_h = S.demo ? (60.0f / 3600.0f) : 48.0f;
  g_cfg.th.dark_lux = S.dark_level;

  SensorSample s;
  s.t = millis() / 1000;
  s.temp_c = isnan(g_temp_smooth) ? 22.0f : g_temp_smooth;
  s.lux = g_light < 0 ? 300.0f : g_light;  // lux ではなくカメラの明るさ(閾値も同じ尺度)
  s.vib_peak_g = g_vib_peak;
  s.battery_mv = 4200;  // USB 給電
  g_vib_peak = 0.0f;

  Features f = extractFeatures(g_fstate, s, g_cfg.th);
  f.temp_rate = tempRatePerMin();
  Face face = EmotionEngine::update(g_estate, f, g_cfg);

  const uint32_t now = millis();
  if (face == Face::Surprised || face == Face::Scared) {
    g_hold_face = face;
    g_hold_until = now + kHoldMs;
  } else if (now < g_hold_until) {
    face = g_hold_face;
  }

  if (face != g_shown) {
    drawFace(face);
    g_shown = face;
  }
  drawStatus(face);

  char line[320];
  snprintf(line, sizeof(line),
           "{\"t\":%lu,\"face\":\"%s\",\"v\":%.2f,\"a\":%.2f,\"temp\":%.2f,\"temp_chip\":%.2f,"
           "\"temp_rate\":%.2f,\"light\":%.1f,\"cam_mean\":%d,\"cam_int\":%u,\"cam_gain\":%.2f,"
           "\"dark\":%d,\"vib\":%.3f,\"idle_s\":%lu,\"ble\":%d}",
           (unsigned long)s.t, faceName(face), g_estate.valence, g_estate.arousal, s.temp_c,
           isnan(g_temp_raw) ? 0.0f : g_temp_raw, f.temp_rate, g_light, g_cam.mean, unsigned(g_cam.integ), g_cam.again * g_cam.dgain, f.is_dark ? 1 : 0, s.vib_peak_g,
           (unsigned long)(f.idle_hours * 3600.0f), board::bleConnected() ? 1 : 0);
  Serial.println(line);
  board::bleSendLine(line);
}

// ---------------------------------------------------------------- シリアルのコマンド
void printHelp() {
  Serial.printf("# PersonaSticker pico4ml  cam=%s(0x%04X) imu=%s\n", g_cam_ok ? "ok" : "NG",
                board::camId() < 0 ? 0 : board::camId(), board::imuName());
  Serial.printf("# name=%s temperament=%s sensitivity=%.2f offset=%.1f dark=%.1f exposure=%s(%u,%u) demo=%d\n",
                g_cfg.name, temperamentName(g_cfg.temperament), g_cfg.sensitivity, S.temp_offset_c,
                S.dark_level, S.manual ? "manual" : "auto", S.exposure, S.gain, S.demo);
  Serial.println("# n<NAME> 名前 / p0 p1 p2 性格(cheerful/shy/calm) / s<0-100> 感度 / o<C> 温度のずれ");
  Serial.println("# l<値> 暗いとみなす明るさ / e<行> a<0-3> 手動の露出・ゲイン / x 自動露出 / d デモモード切替 / h この表示");
  Serial.println("# 診断: i I2C の一覧 / c カメラの信号 / g 画像(PGM)");
}

void handleSerial() {
  static char line[32];
  static size_t len = 0;
  while (Serial.available()) {
    const char c = char(Serial.read());
    if (c != '\n' && c != '\r') {
      if (len < sizeof(line) - 1) line[len++] = c;
      continue;
    }
    if (len == 0) continue;
    line[len] = 0;
    len = 0;
    const char* arg = line + 1;
    switch (line[0]) {
      case 'n':
        strncpy(g_cfg.name, arg, sizeof(g_cfg.name) - 1);
        g_cfg.name[sizeof(g_cfg.name) - 1] = 0;
        break;
      case 'p': {
        const int v = atoi(arg);
        if (v >= 0 && v < int(Temperament::Count)) g_cfg.temperament = Temperament(v);
        break;
      }
      case 's': g_cfg.sensitivity = clampf(atoi(arg) / 100.0f, 0.0f, 1.0f); break;
      case 'o': S.temp_offset_c = float(atof(arg)); g_temp_smooth = NAN; g_temp_hist_n = 0; break;
      case 'l': S.dark_level = clampf(float(atof(arg)), 0.0f, 10000.0f); break;
      case 'e':
        S.exposure = uint16_t(constrain(atoi(arg), 1, 0x0170));
        S.manual = true;
        board::camSetExposure(S.exposure, uint8_t(S.gain << 4));
        break;
      case 'a':
        S.gain = uint8_t(constrain(atoi(arg), 0, 3));
        S.manual = true;
        board::camSetExposure(S.exposure, uint8_t(S.gain << 4));
        break;
      case 'x': S.manual = false; board::camSetAuto(); break;
      case 'd': S.demo = !S.demo; break;
      case 'i': board::i2cScan(); break;
      case 'c': board::camDiag(); break;
      case 'g': board::camDumpPgm(); break;
      default: break;
    }
    g_shown = Face::Count;  // 次の tick で描き直す
    printHelp();
  }
}

}  // namespace

void setup() {
  Serial.begin(115200);
  board::lcdInit();
  board::lcdText(84, "PERSONA", 0xFFFF, 0x0000);
  strncpy(g_cfg.name, "STICKER", sizeof(g_cfg.name) - 1);

  g_cam_ok = board::camInit();
  g_imu_ok = board::imuInit();
  board::bleInit();

  char s[20];
  snprintf(s, sizeof(s), "CAM %s", g_cam_ok ? "OK" : "NG");
  board::lcdText(96, s, g_cam_ok ? 0x07E0 : 0xF800, 0x0000);
  snprintf(s, sizeof(s), "IMU %s", board::imuName());
  board::lcdText(108, s, g_imu_ok ? 0x07E0 : 0xF800, 0x0000);
  delay(1500);
  printHelp();
  board::i2cScan();
}

void loop() {
  static uint32_t t_imu = 0, t_tick = 0, t_cam = 0;
  const uint32_t now = millis();
  handleSerial();
  board::blePoll();
  if (now - t_imu >= kImuPeriodMs) {
    t_imu = now;
    pollImu();
  }
  if (g_cam_ok && now - t_cam >= kCamPeriodMs) {
    t_cam = now;
    board::CamLight c;
    if (board::camMeasure(c)) {
      g_cam = c;
      g_light = c.light;
    }
    else Serial.println("# cam: no frame (vsync/pclk timeout)");
  }
  if (now - t_tick >= kTickMs) {
    t_tick = now;
    tick();
  }
}
