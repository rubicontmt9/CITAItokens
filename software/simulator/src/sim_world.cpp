#include "sim_world.h"

#include <math.h>
#include <string.h>

namespace sim {

using persona::Face;
using persona::Temperament;

SimWorld::SimWorld() {
  // デモ用の初期3枚(プリセット適用)
  {
    int id = addSticker("FRIDGE");
    applyPreset(*find(id), "fridge");
  }
  {
    int id = addSticker("PLANT");
    applyPreset(*find(id), "plant");
  }
  {
    int id = addSticker("GUITAR");
    applyPreset(*find(id), "guitar");
  }
}

int SimWorld::addSticker(const std::string& name) {
  VirtualSticker st;
  st.id = next_id_++;
  snprintf(st.cfg.name, sizeof(st.cfg.name), "%s", name.c_str());
  st.next_wake_t = sim_t;  // すぐ初回起床
  stickers.push_back(st);
  return st.id;
}

VirtualSticker* SimWorld::find(int id) {
  for (auto& s : stickers)
    if (s.id == id) return &s;
  return nullptr;
}

void SimWorld::applyPreset(VirtualSticker& st, const std::string& preset) {
  if (preset == "fridge") {
    // 冷蔵庫の庫内: 寒い・暗い
    snprintf(st.cfg.name, sizeof(st.cfg.name), "%s", "FRIDGE");
    st.env_temp_c = 5.0f;
    st.env_hum_pct = 60.0f;
    st.lux_auto = false;
    st.lux_manual = 40.0f;
    st.cfg.temperament = Temperament::Cheerful;
  } else if (preset == "plant") {
    // 観葉植物: 快適・明るい・寂しがり(24hで寂しい)
    snprintf(st.cfg.name, sizeof(st.cfg.name), "%s", "PLANT");
    st.env_temp_c = 24.0f;
    st.env_hum_pct = 55.0f;
    st.lux_auto = true;
    st.cfg.temperament = Temperament::Cheerful;
    st.cfg.th.lonely_h = 24.0f;
  } else if (preset == "guitar") {
    // ギターケース: 暗所・こわがり・すぐ寂しくなる
    snprintf(st.cfg.name, sizeof(st.cfg.name), "%s", "GUITAR");
    st.env_temp_c = 22.0f;
    st.env_hum_pct = 45.0f;
    st.lux_auto = false;
    st.lux_manual = 3.0f;
    st.cfg.temperament = Temperament::Shy;
    st.cfg.th.lonely_h = 12.0f;
  }
  st.cfg.rev++;
}

float SimWorld::autoLux(double t) const {
  double hour = fmod(t / 3600.0, 24.0);
  if (hour >= 6.0 && hour <= 18.0) {
    return 100.0f + 400.0f * static_cast<float>(sin(M_PI * (hour - 6.0) / 12.0));
  }
  return 0.5f;  // 夜
}

uint16_t SimWorld::batteryMv(const VirtualSticker& st) const {
  // batteryPercent()(3300-4200mVの直線)の逆関数で電圧を合成
  double frac = st.battery_mah / VirtualSticker::kCapacityMah;
  if (frac < 0) frac = 0;
  if (frac > 1) frac = 1;
  return static_cast<uint16_t>(3300.0 + 900.0 * frac);
}

void SimWorld::wakeCycle(VirtualSticker& st, bool motion_wake) {
  persona::SensorSample s;
  s.temp_c = st.env_temp_c;
  s.hum_pct = st.env_hum_pct;
  s.lux = st.lux_auto ? autoLux(sim_t) : st.lux_manual;
  s.vib_peak_g = st.pending_vib_g;
  s.motion_wake = motion_wake;
  s.battery_mv = batteryMv(st);
  s.t = static_cast<uint32_t>(sim_t);
  st.pending_vib_g = 0.0f;

  persona::Features f = persona::extractFeatures(st.fstate, s, st.cfg.th);
  Face face = persona::EmotionEngine::update(st.estate, f, st.cfg);

  st.last_sample = s;
  st.last_features = f;

  // 電池消費モデル(docs/planning/03の概算値)
  st.battery_mah -= 40.0 * 0.5 / 3600.0;  // 起床+センサー読取 40mA x 0.5s
  bool sync = (st.wake_count % (st.cfg.sync_every_n ? st.cfg.sync_every_n : 1)) == 0;
  if (sync) st.battery_mah -= 100.0 * 3.0 / 3600.0;  // メッシュ同期 100mA x 3s

  // 表情が変わったときだけ画面書き換え(FWと同じ方針)
  if (static_cast<int>(face) != st.last_face) {
    persona::StatusInfo info;
    info.name = st.cfg.name;
    info.temp_c = s.temp_c;
    info.battery_pct = persona::batteryPercent(s.battery_mv);
    persona::renderFace(st.fb, face, info);
    st.frame_seq++;
    st.last_face = static_cast<int>(face);
    st.battery_mah -= 8.0 * 0.3 / 3600.0;  // e-paper部分書き換え
  }
  if (st.battery_mah < 0) st.battery_mah = 0;

  st.wake_count++;

  // 次回起床の予約。ゆらし続け中は割り込み再起床を模擬して短周期
  if (st.sustained_remaining > 0) {
    st.sustained_remaining--;
    st.pending_vib_g = 1.6f;
    st.wake_now = true;
    st.next_wake_t = sim_t + 2.0;
  } else {
    st.next_wake_t = sim_t + st.cfg.report_interval_s;
  }
}

void SimWorld::tick(double real_dt_s) {
  std::lock_guard<std::mutex> lock(mu);
  double target = sim_t + real_dt_s * accel;
  double span = target - sim_t;

  // スリープ暗電流(20uA)は経過時間ぶんまとめて引く
  for (auto& st : stickers) {
    st.battery_mah -= 0.02 * span / 3600.0;
    if (st.battery_mah < 0) st.battery_mah = 0;
  }

  // 起床イベントを時刻順に処理(高加速時に多数の起床をこなす)
  int guard = 0;
  while (guard++ < 5000) {
    // 割り込み起床(wake_now)は現時刻で即処理
    bool did_immediate = false;
    for (auto& st : stickers) {
      if (st.wake_now) {
        st.wake_now = false;
        wakeCycle(st, true);
        did_immediate = true;
      }
    }
    if (did_immediate) continue;

    // 最も早いタイマー起床
    VirtualSticker* next = nullptr;
    for (auto& st : stickers) {
      if (!next || st.next_wake_t < next->next_wake_t) next = &st;
    }
    if (!next || next->next_wake_t > target) break;
    sim_t = next->next_wake_t;
    wakeCycle(*next, false);
  }

  sim_t = target;
}

}  // namespace sim
