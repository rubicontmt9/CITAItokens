// PersonaSticker 共有コア: 設定・定数・列挙
// このライブラリ(emotion_core)は純粋C++17。Arduino APIに依存してはならない。
// ESP32ファームウェア / PCシミュレーター / ネイティブテストの3者が共有する。
#pragma once
#include <stdint.h>

namespace persona {

constexpr int kEpdWidth = 200;
constexpr int kEpdHeight = 200;
// 表情領域の高さ。ここより下はフッター(名前・温度・電池)で、
// 部分書き換えは表情領域のみを対象にする。
constexpr int kFaceRegionH = 168;

// 電池残量がこの%を下回ると「おなかすいた」表示が他の表情に優先する
constexpr float kHungryBatteryPct = 15.0f;

enum class Face : uint8_t {
  Happy = 0,   // うれしい
  Content,     // おだやか
  Sleepy,      // ねむい
  Cold,        // さむい
  Hot,         // あつい
  Surprised,   // びっくり
  Scared,      // こわい
  Lonely,      // さみしい
  Hungry,      // おなかすいた(低電池)
  Count
};

const char* faceName(Face f);

enum class Temperament : uint8_t {
  Cheerful = 0,  // ほがらか: valenceに+バイアス
  Shy,           // こわがり: arousal反応が大きい
  Calm,          // おっとり: 感情変化が緩やか(強い平滑化)
  Count
};

const char* temperamentName(Temperament t);
bool temperamentFromName(const char* s, Temperament& out);

struct Thresholds {
  float cold_c = 10.0f;         // これ未満で「さむい」
  float hot_c = 30.0f;          // これ超過で「あつい」
  float comfort_low_c = 18.0f;  // 快適域下限
  float comfort_high_c = 26.0f; // 快適域上限
  float lonely_h = 48.0f;       // 放置がこの時間を超えると「さみしい」
  float dark_lux = 10.0f;       // これ未満で暗所とみなす
  float shock_g = 1.2f;         // 「びっくり」する振動ピーク [g]
  float pet_g = 0.15f;          // 「なでられた」とみなす微振動の下限 [g]
};

struct PersonalityConfig {
  char name[24] = "STICKER";        // 表示名(フッター描画はASCIIのみ対応)
  float sensitivity = 0.5f;         // 0..1 反応の強さ
  Temperament temperament = Temperament::Cheerful;
  Thresholds th;
  uint16_t report_interval_s = 120; // 起床(=報告)周期
  uint8_t sync_every_n = 1;         // 起床N回につき1回メッシュ同期
  uint32_t rev = 0;                 // 設定リビジョン(ゲートウェイと同期)
};

inline float clampf(float v, float lo, float hi) {
  return v < lo ? lo : (v > hi ? hi : v);
}

}  // namespace persona
