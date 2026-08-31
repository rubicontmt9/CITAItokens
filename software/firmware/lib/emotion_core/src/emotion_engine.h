// PersonaSticker 共有コア: 感情エンジン
// 特徴量 → Valence-Arousal 2次元感情 → 表情(Face)へのマッピング。
// 性格パラメータ(sensitivity / temperament)で反応が変調される。
#pragma once
#include "persona_features.h"

namespace persona {

// 起床をまたいで保持する感情状態(ファームウェアではRTCメモリに置く)
struct EmotionState {
  bool initialized = false;
  float valence = 0.0f;  // 快 -1..+1
  float arousal = 0.0f;  // 覚醒度 -1..+1
  Face face = Face::Content;
};

class EmotionEngine {
 public:
  // 1回の起床サイクルごとに呼ぶ。stを更新し、表示すべき表情を返す。
  static Face update(EmotionState& st, const Features& f, const PersonalityConfig& cfg);

 private:
  static void computeTarget(const Features& f, const PersonalityConfig& cfg,
                            float& target_v, float& target_a);
  static Face selectFace(const EmotionState& st, const Features& f,
                         const PersonalityConfig& cfg);
};

}  // namespace persona
