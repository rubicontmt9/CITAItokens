// PersonaSticker 共有コア: 表情の手続き描画
// 200x200 1bppのフレームバッファに表情を描く。ビットマップアセットを持たず
// 図形プリミティブで描画するため、ファームウェアとシミュレーターの表示が
// 完全に一致する。1=黒 / 0=白、各行はMSBが左端。
#pragma once
#include <stdint.h>
#include "persona_config.h"

namespace persona {

struct FrameBuffer {
  static constexpr int kW = kEpdWidth;
  static constexpr int kH = kEpdHeight;
  static constexpr int kStride = kW / 8;  // 25 byte/行
  uint8_t bits[kStride * kH];             // 5000 byte

  void clear(bool black = false);
  void setPixel(int x, int y, bool black = true);
  bool getPixel(int x, int y) const;
  void fillRect(int x, int y, int w, int h, bool black = true);
  void drawRect(int x, int y, int w, int h, bool black = true);
  void fillCircle(int cx, int cy, int r, bool black = true);
  // 太さthickの線分
  void drawLine(int x0, int y0, int x1, int y1, int thick, bool black = true);
  // 円弧: 角度は度。0°=+x方向、90°=+y方向(画面下向き)。
  void drawArc(int cx, int cy, int r, int a0_deg, int a1_deg, int thick,
               bool black = true);
  // 5x7フォント(scale倍)。対応: 英数字(小文字は大文字化)と一部記号。
  void drawText(int x, int y, const char* s, int scale, bool black = true);
  static int textWidth(const char* s, int scale);
};

struct StatusInfo {
  const char* name = "";
  float temp_c = 25.0f;
  float battery_pct = 100.0f;
};

// 表情+フッター(名前・温度・電池)を全面描画する
void renderFace(FrameBuffer& fb, Face face, const StatusInfo& st);

}  // namespace persona
