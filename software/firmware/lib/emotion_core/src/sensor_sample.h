// PersonaSticker 共有コア: 1回の起床で取得するセンサー値スナップショット
#pragma once
#include <stdint.h>

namespace persona {

struct SensorSample {
  float temp_c = 25.0f;
  float hum_pct = 50.0f;
  float lux = 300.0f;
  float vib_peak_g = 0.0f;   // 前回起床以降の最大振動(重力除去後) [g]
  bool motion_wake = false;  // 振動割り込みによる起床か
  uint16_t battery_mv = 4000;
  uint32_t t = 0;            // epoch秒(シミュレーターでは仮想時刻)
};

}  // namespace persona
