// 共有コアの 200x200 の表情を、液晶の 80x80 のカラーの絵にする。
// Arduino に依存しないので、PC の確認用ツール(tools/preview_faces.cpp)からも同じ関数を使う。
#pragma once
#include <stdint.h>

#include "face_renderer.h"
#include "persona_config.h"

namespace lcdface {

constexpr uint16_t rgb565(uint8_t r, uint8_t g, uint8_t b) {
  return uint16_t(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
}

struct FaceColor { uint16_t bg, ink; };

inline FaceColor faceColor(persona::Face f) {
  using persona::Face;
  const uint16_t ink = rgb565(30, 30, 60);
  switch (f) {
    case Face::Happy:     return {rgb565(255, 214, 102), ink};
    case Face::Content:   return {rgb565(170, 230, 170), ink};
    case Face::Sleepy:    return {rgb565(160, 160, 230), ink};
    case Face::Cold:      return {rgb565(150, 220, 255), ink};
    case Face::Hot:       return {rgb565(255, 140, 120), ink};
    case Face::Surprised: return {rgb565(255, 240, 110), ink};
    case Face::Scared:    return {rgb565(200, 160, 230), ink};
    case Face::Lonely:    return {rgb565(180, 190, 200), ink};
    case Face::Hungry:    return {rgb565(255, 190, 120), ink};
    default:              return {rgb565(255, 255, 255), ink};
  }
}

// 表情の部分(上 kFaceRegionH 行)を 80 x (kFaceRegionH * 2 / 5) に縮め、80x80 の上下中央に置く。
// 1 画素 = 元の 2.5x2.5 画素。細い線が消えないよう、元の区画に黒が 1 つでもあれば塗る。
inline void faceTo80(const persona::FrameBuffer& fb, FaceColor c, uint16_t* out) {
  constexpr int kDstH = persona::kFaceRegionH * 2 / 5;
  constexpr int kTop = (80 - kDstH) / 2;
  for (int i = 0; i < 80 * 80; ++i) out[i] = c.bg;
  for (int dy = 0; dy < kDstH; ++dy) {
    const int y0 = dy * 5 / 2, y1 = ((dy + 1) * 5 + 1) / 2;
    for (int dx = 0; dx < 80; ++dx) {
      const int x0 = dx * 5 / 2, x1 = ((dx + 1) * 5 + 1) / 2;
      bool black = false;
      for (int y = y0; y < y1 && !black; ++y) {
        for (int x = x0; x < x1; ++x) {
          if (fb.getPixel(x, y)) {
            black = true;
            break;
          }
        }
      }
      if (black) out[(kTop + dy) * 80 + dx] = c.ink;
    }
  }
}

}  // namespace lcdface
