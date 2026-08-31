#include "emotion_engine.h"

namespace persona {

void EmotionEngine::computeTarget(const Features& f, const PersonalityConfig& cfg,
                                  float& target_v, float& target_a) {
  float v = 0.0f;
  float a = 0.0f;

  // --- 温度 ---
  if (f.temp_dev == 0.0f) {
    v += 0.35f;  // 快適
  } else if (f.temp_dev > 0.0f) {
    float m = clampf(f.temp_dev / 6.0f, 0.0f, 1.0f);
    v -= 0.9f * m;
    a += 0.5f * m;  // 暑さはいらだち(覚醒+)
  } else {
    float m = clampf(-f.temp_dev / 6.0f, 0.0f, 1.0f);
    v -= 0.9f * m;
    a -= 0.3f * m;  // 寒さは縮こまる(覚醒-)
  }
  // 急な温度変化はどちら向きでも覚醒を上げる
  if (f.temp_rate > 0.5f || f.temp_rate < -0.5f) {
    a += 0.3f;
  }

  // --- 振動 ---
  if (f.sustained_shake) {
    a += 0.8f;
    v -= 0.7f;
  } else if (f.shock) {
    a += 1.0f;
    v -= 0.3f;
  } else if (f.petted) {
    v += 0.5f;
    a += 0.2f;
  }

  // --- 放置 ---
  if (f.idle_hours > cfg.th.lonely_h) {
    v -= 0.6f;
    a -= 0.4f;
  }

  // --- 明るさ ---
  if (f.is_dark) {
    a -= 0.45f;
  }

  // --- 性格による変調 ---
  // sensitivity: 0..1 → 反応の強さ 0.5..1.5倍
  float gain = 0.5f + cfg.sensitivity;
  v *= gain;
  a *= gain;
  switch (cfg.temperament) {
    case Temperament::Cheerful: v += 0.15f; break;
    case Temperament::Shy:      a *= 1.4f;  break;
    case Temperament::Calm:     break;  // 平滑化係数側で表現
    default: break;
  }

  target_v = clampf(v, -1.0f, 1.0f);
  target_a = clampf(a, -1.0f, 1.0f);
}

Face EmotionEngine::selectFace(const EmotionState& st, const Features& f,
                               const PersonalityConfig& cfg) {
  const Thresholds& th = cfg.th;

  // 優先度順のルール。閾値系はヒステリシス(現在の表情の維持条件)を持つ。
  if (f.battery_pct < kHungryBatteryPct) return Face::Hungry;
  if (f.sustained_shake) return Face::Scared;
  if (f.shock) return Face::Surprised;

  // 温度(ヒステリシス幅 0.5°C)
  const float hyst_c = 0.5f;
  if (st.face == Face::Hot ? (f.temp_c > th.hot_c - hyst_c) : (f.temp_c > th.hot_c)) {
    return Face::Hot;
  }
  if (st.face == Face::Cold ? (f.temp_c < th.cold_c + hyst_c) : (f.temp_c < th.cold_c)) {
    return Face::Cold;
  }

  // 放置(振動があれば idle_hours がリセットされるので自然に解除される)
  if (f.idle_hours > th.lonely_h) return Face::Lonely;

  // ねむい: 暗所、または覚醒度が十分低い(ヒステリシス 0.05)
  const float hyst_va = 0.05f;
  float sleepy_a = (st.face == Face::Sleepy) ? -0.40f : -0.45f;
  if (f.is_dark || st.arousal < sleepy_a) return Face::Sleepy;

  // うれしい: valenceが十分高い(ヒステリシス 0.05)
  float happy_v = (st.face == Face::Happy) ? (0.35f - hyst_va) : 0.35f;
  if (st.valence > happy_v) return Face::Happy;

  return Face::Content;
}

Face EmotionEngine::update(EmotionState& st, const Features& f,
                           const PersonalityConfig& cfg) {
  float target_v = 0.0f, target_a = 0.0f;
  computeTarget(f, cfg, target_v, target_a);

  if (!st.initialized) {
    st.initialized = true;
    st.valence = target_v;
    st.arousal = target_a;
  } else {
    // 指数移動平均で平滑化(気質で係数が変わる)。衝撃時は即応する。
    float alpha;
    switch (cfg.temperament) {
      case Temperament::Calm: alpha = 0.25f; break;
      case Temperament::Shy:  alpha = 0.60f; break;
      default:                alpha = 0.50f; break;
    }
    if (f.shock || f.sustained_shake) alpha = 0.85f;
    st.valence += alpha * (target_v - st.valence);
    st.arousal += alpha * (target_a - st.arousal);
  }
  st.valence = clampf(st.valence, -1.0f, 1.0f);
  st.arousal = clampf(st.arousal, -1.0f, 1.0f);

  st.face = selectFace(st, f, cfg);
  return st.face;
}

}  // namespace persona
