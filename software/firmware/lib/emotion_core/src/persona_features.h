// PersonaSticker 共有コア: センサー値からの特徴量抽出
#pragma once
#include "persona_config.h"
#include "sensor_sample.h"

namespace persona {

// 起床をまたいで保持する状態。ファームウェアではRTCメモリに置く
// (ディープスリープ中も保持され、電源断でリセットされる)。
struct FeatureState {
  bool initialized = false;
  float prev_temp_c = 0.0f;
  uint32_t prev_t = 0;
  uint32_t last_motion_t = 0;  // 最後に振動を検知した時刻
  uint8_t shake_streak = 0;    // 強い振動を連続observeした回数
};

struct Features {
  float temp_c = 25.0f;
  float temp_dev = 0.0f;   // 快適域からの逸脱 [°C](+:暑い側 / -:寒い側 / 0:快適)
  float temp_rate = 0.0f;  // 温度変化率 [°C/min]
  float vib_peak_g = 0.0f;
  bool shock = false;            // 今回、強い衝撃があった
  bool sustained_shake = false;  // 強い振動が連続している(こわい)
  bool petted = false;           // なでられた程度の微振動
  float idle_hours = 0.0f;       // 最後の振動からの経過時間 [h]
  bool is_dark = false;
  float battery_pct = 100.0f;
};

// 電池電圧[mV] → 残量% の簡易変換(LiPo 1セル)
float batteryPercent(uint16_t mv);

// 1回の起床ごとに呼ぶ。stを更新し特徴量を返す。
Features extractFeatures(FeatureState& st, const SensorSample& s, const Thresholds& th);

}  // namespace persona
