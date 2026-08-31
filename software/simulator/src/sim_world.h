// 仮想世界: ファームウェアと同じ emotion_core を使って複数の仮想ステッカーを
// 動かす。各ステッカーはFW同様の「起床→センシング→感情→表示→スリープ」周期を
// 模擬し、電池消費モデル(docs/planning/03の電力収支)も再現する。
#pragma once
#include <mutex>
#include <string>
#include <vector>

#include "emotion_engine.h"
#include "face_renderer.h"

namespace sim {

struct VirtualSticker {
  int id = 0;
  persona::PersonalityConfig cfg;
  persona::FeatureState fstate;
  persona::EmotionState estate;

  // 環境(ブラウザUIのスライダーで操作)
  float env_temp_c = 24.0f;
  float env_hum_pct = 50.0f;
  bool lux_auto = true;      // trueなら仮想時刻の昼夜サイクルに従う
  float lux_manual = 300.0f;

  // 注入イベント
  float pending_vib_g = 0.0f;   // 次の起床で観測される振動ピーク
  int sustained_remaining = 0;  // 連続振動の残り回数(ゆらし続ける)
  bool wake_now = false;        // 振動割り込み起床(LIS3DH INT相当)

  // 電池モデル(mAh残量)
  double battery_mah = 500.0;
  static constexpr double kCapacityMah = 500.0;

  // 実行状態
  double next_wake_t = 0.0;
  uint32_t wake_count = 0;
  persona::FrameBuffer fb;
  uint32_t frame_seq = 0;   // 表示が書き換わるたびに増える
  int last_face = -1;
  persona::SensorSample last_sample;
  persona::Features last_features;
};

class SimWorld {
 public:
  SimWorld();

  // real_dt_s(実時間秒)ぶん仮想世界を進める。accel倍速。
  void tick(double real_dt_s);

  int addSticker(const std::string& name);
  VirtualSticker* find(int id);
  void applyPreset(VirtualSticker& st, const std::string& preset);

  // 全API呼び出しとtickはこのmutexで直列化する
  std::mutex mu;

  double sim_t = 8.0 * 3600.0;  // 仮想時刻(Day0 08:00 スタート)[秒]
  double accel = 60.0;          // 時間加速率
  std::vector<VirtualSticker> stickers;

 private:
  int next_id_ = 1;
  void wakeCycle(VirtualSticker& st, bool motion_wake);
  float autoLux(double t) const;
  uint16_t batteryMv(const VirtualSticker& st) const;
};

}  // namespace sim
