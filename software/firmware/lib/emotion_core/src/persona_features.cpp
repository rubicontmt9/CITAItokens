#include "persona_features.h"

namespace persona {

const char* faceName(Face f) {
  switch (f) {
    case Face::Happy: return "happy";
    case Face::Content: return "content";
    case Face::Sleepy: return "sleepy";
    case Face::Cold: return "cold";
    case Face::Hot: return "hot";
    case Face::Surprised: return "surprised";
    case Face::Scared: return "scared";
    case Face::Lonely: return "lonely";
    case Face::Hungry: return "hungry";
    default: return "unknown";
  }
}

const char* temperamentName(Temperament t) {
  switch (t) {
    case Temperament::Cheerful: return "cheerful";
    case Temperament::Shy: return "shy";
    case Temperament::Calm: return "calm";
    default: return "unknown";
  }
}

static bool strEq(const char* a, const char* b) {
  if (!a || !b) return false;
  while (*a && *b) {
    if (*a != *b) return false;
    ++a; ++b;
  }
  return *a == *b;
}

bool temperamentFromName(const char* s, Temperament& out) {
  if (strEq(s, "cheerful")) { out = Temperament::Cheerful; return true; }
  if (strEq(s, "shy"))      { out = Temperament::Shy;      return true; }
  if (strEq(s, "calm"))     { out = Temperament::Calm;     return true; }
  return false;
}

float batteryPercent(uint16_t mv) {
  // LiPo 1セルの簡易直線近似: 3.3V=0% / 4.2V=100%
  float pct = (static_cast<float>(mv) - 3300.0f) / (4200.0f - 3300.0f) * 100.0f;
  return clampf(pct, 0.0f, 100.0f);
}

Features extractFeatures(FeatureState& st, const SensorSample& s, const Thresholds& th) {
  Features f;
  f.temp_c = s.temp_c;
  f.vib_peak_g = s.vib_peak_g;
  f.battery_pct = batteryPercent(s.battery_mv);
  f.is_dark = s.lux < th.dark_lux;

  // 快適域からの逸脱
  if (s.temp_c > th.comfort_high_c) {
    f.temp_dev = s.temp_c - th.comfort_high_c;
  } else if (s.temp_c < th.comfort_low_c) {
    f.temp_dev = s.temp_c - th.comfort_low_c;  // 負値
  }

  if (!st.initialized) {
    st.initialized = true;
    st.prev_temp_c = s.temp_c;
    st.prev_t = s.t;
    st.last_motion_t = s.t;  // 起動直後を「触られた」とみなす(初期表示を穏やかに)
    st.shake_streak = 0;
  }

  // 温度変化率 [°C/min]
  if (s.t > st.prev_t) {
    float dt_min = static_cast<float>(s.t - st.prev_t) / 60.0f;
    if (dt_min > 0.01f) {
      f.temp_rate = (s.temp_c - st.prev_temp_c) / dt_min;
    }
  }

  // 振動の分類
  if (s.vib_peak_g >= th.shock_g) {
    f.shock = true;
    st.shake_streak = (st.shake_streak < 255) ? static_cast<uint8_t>(st.shake_streak + 1) : 255;
    st.last_motion_t = s.t;
  } else if (s.vib_peak_g >= th.pet_g) {
    f.petted = true;
    st.shake_streak = 0;
    st.last_motion_t = s.t;
  } else {
    st.shake_streak = 0;
  }
  // 強い振動が2回以上連続 → 「こわい」(単発の衝撃は「びっくり」)
  if (st.shake_streak >= 2) {
    f.sustained_shake = true;
    f.shock = false;
  }

  // 放置時間
  if (s.t >= st.last_motion_t) {
    f.idle_hours = static_cast<float>(s.t - st.last_motion_t) / 3600.0f;
  }

  st.prev_temp_c = s.temp_c;
  st.prev_t = s.t;
  return f;
}

}  // namespace persona
