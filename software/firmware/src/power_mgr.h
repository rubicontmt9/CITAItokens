// 電源管理: ディープスリープ、起床理由、RTCメモリへの状態退避
#pragma once
#include <stdint.h>

#include "emotion_engine.h"
#include "persona_features.h"

namespace fw {

// ディープスリープをまたいで保持する状態(RTCメモリ上)
struct RtcState {
  uint32_t magic = 0;
  persona::FeatureState fstate;
  persona::EmotionState estate;
  uint32_t wake_count = 0;
  uint32_t last_full_refresh_t = 0;
  int8_t last_face = -1;  // 表示中の表情(-1=まだ何も表示していない)
};

enum class WakeCause { PowerOn, Timer, Motion };

WakeCause wakeCause();

// RTCメモリ上の状態への参照。電源投入直後(magic不一致)は初期化して返す。
RtcState& rtcState();

// タイマー(seconds後)と motion_int_pin のHighで起床するディープスリープへ移行。
// この関数は戻らない。
[[noreturn]] void deepSleep(uint32_t seconds, int motion_int_pin);

}  // namespace fw
