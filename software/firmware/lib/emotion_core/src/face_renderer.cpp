#include "face_renderer.h"

#include <math.h>
#include <stdio.h>
#include <string.h>

namespace persona {

// ---------------------------------------------------------------------------
// FrameBuffer プリミティブ
// ---------------------------------------------------------------------------

void FrameBuffer::clear(bool black) {
  memset(bits, black ? 0xFF : 0x00, sizeof(bits));
}

void FrameBuffer::setPixel(int x, int y, bool black) {
  if (x < 0 || x >= kW || y < 0 || y >= kH) return;
  uint8_t& b = bits[y * kStride + (x >> 3)];
  uint8_t mask = static_cast<uint8_t>(0x80u >> (x & 7));
  if (black) b |= mask; else b &= static_cast<uint8_t>(~mask);
}

bool FrameBuffer::getPixel(int x, int y) const {
  if (x < 0 || x >= kW || y < 0 || y >= kH) return false;
  return (bits[y * kStride + (x >> 3)] >> (7 - (x & 7))) & 1;
}

void FrameBuffer::fillRect(int x, int y, int w, int h, bool black) {
  for (int yy = y; yy < y + h; ++yy)
    for (int xx = x; xx < x + w; ++xx) setPixel(xx, yy, black);
}

void FrameBuffer::drawRect(int x, int y, int w, int h, bool black) {
  fillRect(x, y, w, 1, black);
  fillRect(x, y + h - 1, w, 1, black);
  fillRect(x, y, 1, h, black);
  fillRect(x + w - 1, y, 1, h, black);
}

void FrameBuffer::fillCircle(int cx, int cy, int r, bool black) {
  for (int dy = -r; dy <= r; ++dy)
    for (int dx = -r; dx <= r; ++dx)
      if (dx * dx + dy * dy <= r * r) setPixel(cx + dx, cy + dy, black);
}

void FrameBuffer::drawLine(int x0, int y0, int x1, int y1, int thick, bool black) {
  int dx = x1 - x0, dy = y1 - y0;
  int steps = (int)(sqrtf((float)(dx * dx + dy * dy))) + 1;
  int pr = thick > 1 ? thick / 2 : 0;
  for (int i = 0; i <= steps; ++i) {
    int x = x0 + dx * i / steps;
    int y = y0 + dy * i / steps;
    if (pr > 0) fillCircle(x, y, pr, black);
    else setPixel(x, y, black);
  }
}

void FrameBuffer::drawArc(int cx, int cy, int r, int a0_deg, int a1_deg,
                          int thick, bool black) {
  int pr = thick > 1 ? thick / 2 : 0;
  int steps = (a1_deg - a0_deg) * 2;  // 0.5°刻み相当
  if (steps <= 0) return;
  for (int i = 0; i <= steps; ++i) {
    float a = (a0_deg + (a1_deg - a0_deg) * (float)i / steps) * 3.14159265f / 180.0f;
    int x = cx + (int)lroundf(r * cosf(a));
    int y = cy + (int)lroundf(r * sinf(a));
    if (pr > 0) fillCircle(x, y, pr, black);
    else setPixel(x, y, black);
  }
}

// ---------------------------------------------------------------------------
// 5x7フォント(列単位、LSB=上端)。英数字と最小限の記号のみ。
// ---------------------------------------------------------------------------

namespace {

struct Glyph { char c; uint8_t col[5]; };

const Glyph kFont[] = {
  {' ', {0x00, 0x00, 0x00, 0x00, 0x00}},
  {'!', {0x00, 0x00, 0x5F, 0x00, 0x00}},
  {'%', {0x23, 0x13, 0x08, 0x64, 0x62}},
  {'-', {0x08, 0x08, 0x08, 0x08, 0x08}},
  {'.', {0x00, 0x60, 0x60, 0x00, 0x00}},
  {'/', {0x20, 0x10, 0x08, 0x04, 0x02}},
  {'0', {0x3E, 0x51, 0x49, 0x45, 0x3E}},
  {'1', {0x00, 0x42, 0x7F, 0x40, 0x00}},
  {'2', {0x42, 0x61, 0x51, 0x49, 0x46}},
  {'3', {0x21, 0x41, 0x45, 0x4B, 0x31}},
  {'4', {0x18, 0x14, 0x12, 0x7F, 0x10}},
  {'5', {0x27, 0x45, 0x45, 0x45, 0x39}},
  {'6', {0x3C, 0x4A, 0x49, 0x49, 0x30}},
  {'7', {0x01, 0x71, 0x09, 0x05, 0x03}},
  {'8', {0x36, 0x49, 0x49, 0x49, 0x36}},
  {'9', {0x06, 0x49, 0x49, 0x29, 0x1E}},
  {':', {0x00, 0x36, 0x36, 0x00, 0x00}},
  {'?', {0x02, 0x01, 0x51, 0x09, 0x06}},
  {'A', {0x7E, 0x11, 0x11, 0x11, 0x7E}},
  {'B', {0x7F, 0x49, 0x49, 0x49, 0x36}},
  {'C', {0x3E, 0x41, 0x41, 0x41, 0x22}},
  {'D', {0x7F, 0x41, 0x41, 0x22, 0x1C}},
  {'E', {0x7F, 0x49, 0x49, 0x49, 0x41}},
  {'F', {0x7F, 0x09, 0x09, 0x09, 0x01}},
  {'G', {0x3E, 0x41, 0x49, 0x49, 0x7A}},
  {'H', {0x7F, 0x08, 0x08, 0x08, 0x7F}},
  {'I', {0x00, 0x41, 0x7F, 0x41, 0x00}},
  {'J', {0x20, 0x40, 0x41, 0x3F, 0x01}},
  {'K', {0x7F, 0x08, 0x14, 0x22, 0x41}},
  {'L', {0x7F, 0x40, 0x40, 0x40, 0x40}},
  {'M', {0x7F, 0x02, 0x0C, 0x02, 0x7F}},
  {'N', {0x7F, 0x04, 0x08, 0x10, 0x7F}},
  {'O', {0x3E, 0x41, 0x41, 0x41, 0x3E}},
  {'P', {0x7F, 0x09, 0x09, 0x09, 0x06}},
  {'Q', {0x3E, 0x41, 0x51, 0x21, 0x5E}},
  {'R', {0x7F, 0x09, 0x19, 0x29, 0x46}},
  {'S', {0x46, 0x49, 0x49, 0x49, 0x31}},
  {'T', {0x01, 0x01, 0x7F, 0x01, 0x01}},
  {'U', {0x3F, 0x40, 0x40, 0x40, 0x3F}},
  {'V', {0x1F, 0x20, 0x40, 0x20, 0x1F}},
  {'W', {0x3F, 0x40, 0x38, 0x40, 0x3F}},
  {'X', {0x63, 0x14, 0x08, 0x14, 0x63}},
  {'Y', {0x07, 0x08, 0x70, 0x08, 0x07}},
  {'Z', {0x61, 0x51, 0x49, 0x45, 0x43}},
};

const uint8_t* findGlyph(char c) {
  if (c >= 'a' && c <= 'z') c = static_cast<char>(c - 'a' + 'A');
  for (const auto& g : kFont)
    if (g.c == c) return g.col;
  // 未対応文字(非ASCII含む)は '?' で描画
  for (const auto& g : kFont)
    if (g.c == '?') return g.col;
  return nullptr;
}

}  // namespace

void FrameBuffer::drawText(int x, int y, const char* s, int scale, bool black) {
  int cx = x;
  for (const char* p = s; *p; ++p) {
    // 非ASCIIのマルチバイト先頭以外はスキップ(UTF-8の名前は'?'1文字で表す)
    unsigned char uc = static_cast<unsigned char>(*p);
    if (uc >= 0x80 && (uc & 0xC0) == 0x80) continue;
    const uint8_t* col = findGlyph(static_cast<char>(uc < 0x80 ? uc : '?'));
    if (!col) continue;
    for (int i = 0; i < 5; ++i) {
      for (int j = 0; j < 7; ++j) {
        if ((col[i] >> j) & 1) {
          fillRect(cx + i * scale, y + j * scale, scale, scale, black);
        }
      }
    }
    cx += 6 * scale;  // 5px + 字間1px
  }
}

int FrameBuffer::textWidth(const char* s, int scale) {
  int n = 0;
  for (const char* p = s; *p; ++p) {
    unsigned char uc = static_cast<unsigned char>(*p);
    if (uc >= 0x80 && (uc & 0xC0) == 0x80) continue;
    ++n;
  }
  return n > 0 ? (n * 6 - 1) * scale : 0;
}

// ---------------------------------------------------------------------------
// 表情描画
// ---------------------------------------------------------------------------

namespace {

constexpr int kEyeLX = 64, kEyeRX = 136, kEyeY = 78;
constexpr int kMouthX = 100, kMouthY = 128;

void drawOpenEye(FrameBuffer& fb, int cx, int cy, int r, int pupil_dx = 0,
                 int pupil_dy = 0) {
  fb.fillCircle(cx, cy, r, true);
  // ハイライト(白抜き)
  fb.fillCircle(cx - r / 3 + pupil_dx, cy - r / 3 + pupil_dy, r / 4 + 1, false);
}

void drawScaredEye(FrameBuffer& fb, int cx, int cy) {
  // 白目の縁+小さな瞳(おびえた目)
  fb.fillCircle(cx, cy, 15, true);
  fb.fillCircle(cx, cy, 12, false);
  fb.fillCircle(cx, cy + 3, 4, true);
}

void drawClosedEye(FrameBuffer& fb, int cx, int cy) {
  // 下向きの弧(閉じた目)
  fb.drawArc(cx, cy - 6, 14, 45, 135, 5, true);
}

void drawHappyEye(FrameBuffer& fb, int cx, int cy) {
  // 上向きの弧(^ ^)
  fb.drawArc(cx, cy + 8, 14, 225, 315, 5, true);
}

void drawCrossEye(FrameBuffer& fb, int cx, int cy) {
  fb.drawLine(cx - 9, cy - 9, cx + 9, cy + 9, 5, true);
  fb.drawLine(cx - 9, cy + 9, cx + 9, cy - 9, 5, true);
}

void drawZigzagMouth(FrameBuffer& fb, int cx, int cy, int half_w, int amp) {
  int seg = 4;
  int x0 = cx - half_w;
  int w = half_w * 2;
  for (int i = 0; i < seg; ++i) {
    int xa = x0 + w * i / seg;
    int xb = x0 + w * (i + 1) / seg;
    int ya = cy + ((i % 2 == 0) ? -amp : amp);
    int yb = cy + ((i % 2 == 0) ? amp : -amp);
    fb.drawLine(xa, ya, xb, yb, 4, true);
  }
}

void drawTear(FrameBuffer& fb, int x, int y) {
  fb.fillCircle(x, y + 6, 5, true);
  fb.drawLine(x, y - 4, x - 4, y + 4, 3, true);
  fb.drawLine(x, y - 4, x + 4, y + 4, 3, true);
}

void drawSweatDrop(FrameBuffer& fb, int x, int y) {
  fb.fillCircle(x, y + 5, 4, true);
  fb.drawLine(x, y - 3, x - 3, y + 3, 2, true);
  fb.drawLine(x, y - 3, x + 3, y + 3, 2, true);
}

void drawSnowflake(FrameBuffer& fb, int cx, int cy, int r) {
  fb.drawLine(cx - r, cy, cx + r, cy, 2, true);
  fb.drawLine(cx, cy - r, cx, cy + r, 2, true);
  fb.drawLine(cx - r + 1, cy - r + 1, cx + r - 1, cy + r - 1, 2, true);
  fb.drawLine(cx - r + 1, cy + r - 1, cx + r - 1, cy - r + 1, 2, true);
}

void drawBatteryIcon(FrameBuffer& fb, int x, int y, float pct) {
  // 本体 24x12 + 突起
  fb.drawRect(x, y, 24, 12, true);
  fb.fillRect(x + 24, y + 3, 3, 6, true);
  int fill = (int)(20.0f * clampf(pct, 0.0f, 100.0f) / 100.0f);
  if (fill > 0) fb.fillRect(x + 2, y + 2, fill, 8, true);
}

void drawFaceOnly(FrameBuffer& fb, Face face) {
  switch (face) {
    case Face::Happy:
      drawHappyEye(fb, kEyeLX, kEyeY);
      drawHappyEye(fb, kEyeRX, kEyeY);
      fb.drawArc(kMouthX, kMouthY - 8, 28, 30, 150, 6, true);  // 大きな笑い口
      break;

    case Face::Content:
      drawOpenEye(fb, kEyeLX, kEyeY, 10);
      drawOpenEye(fb, kEyeRX, kEyeY, 10);
      fb.drawArc(kMouthX, kMouthY - 6, 18, 40, 140, 4, true);  // 穏やかな笑み
      break;

    case Face::Sleepy:
      drawClosedEye(fb, kEyeLX, kEyeY);
      drawClosedEye(fb, kEyeRX, kEyeY);
      fb.fillCircle(kMouthX, kMouthY + 4, 6, true);   // 小さく開いた口
      fb.fillCircle(kMouthX, kMouthY + 4, 3, false);
      fb.drawText(150, 24, "Z", 3, true);
      fb.drawText(132, 44, "Z", 2, true);
      break;

    case Face::Cold:
      // 眉(困り)+ 細目 + ふるえる口 + 雪
      fb.drawLine(kEyeLX - 14, kEyeY - 22, kEyeLX + 10, kEyeY - 16, 4, true);
      fb.drawLine(kEyeRX + 14, kEyeY - 22, kEyeRX - 10, kEyeY - 16, 4, true);
      fb.drawLine(kEyeLX - 10, kEyeY, kEyeLX + 10, kEyeY, 5, true);
      fb.drawLine(kEyeRX - 10, kEyeY, kEyeRX + 10, kEyeY, 5, true);
      drawZigzagMouth(fb, kMouthX, kMouthY, 22, 5);
      drawSnowflake(fb, 30, 40, 8);
      drawSnowflake(fb, 172, 56, 6);
      break;

    case Face::Hot:
      // 眉(へにゃ)+ 細目 + 開いた口 + 汗
      fb.drawLine(kEyeLX - 12, kEyeY - 18, kEyeLX + 12, kEyeY - 20, 4, true);
      fb.drawLine(kEyeRX - 12, kEyeY - 20, kEyeRX + 12, kEyeY - 18, 4, true);
      fb.drawLine(kEyeLX - 10, kEyeY, kEyeLX + 10, kEyeY, 5, true);
      fb.drawLine(kEyeRX - 10, kEyeY, kEyeRX + 10, kEyeY, 5, true);
      fb.fillCircle(kMouthX, kMouthY, 13, true);
      fb.fillCircle(kMouthX, kMouthY, 9, false);
      fb.fillRect(kMouthX - 13, kMouthY - 13, 27, 13, false);  // 上半分を消して「はぁ」口
      fb.drawLine(kMouthX - 13, kMouthY, kMouthX + 13, kMouthY, 3, true);
      drawSweatDrop(fb, 168, 44);
      drawSweatDrop(fb, 34, 58);
      break;

    case Face::Surprised:
      drawOpenEye(fb, kEyeLX, kEyeY, 15);
      drawOpenEye(fb, kEyeRX, kEyeY, 15);
      fb.fillCircle(kMouthX, kMouthY + 4, 10, true);
      fb.fillCircle(kMouthX, kMouthY + 4, 6, false);
      // 驚きの効果線
      fb.drawLine(24, 30, 40, 44, 3, true);
      fb.drawLine(176, 30, 160, 44, 3, true);
      fb.drawLine(100, 16, 100, 34, 3, true);
      break;

    case Face::Scared:
      drawScaredEye(fb, kEyeLX, kEyeY);
      drawScaredEye(fb, kEyeRX, kEyeY);
      drawZigzagMouth(fb, kMouthX, kMouthY + 2, 24, 6);
      // ふるえ線
      fb.drawLine(20, 60, 26, 80, 2, true);
      fb.drawLine(180, 60, 174, 80, 2, true);
      break;

    case Face::Lonely:
      // 伏し目 + 涙 + への字口
      drawOpenEye(fb, kEyeLX, kEyeY + 2, 10, 0, 4);
      drawOpenEye(fb, kEyeRX, kEyeY + 2, 10, 0, 4);
      drawTear(fb, kEyeLX - 18, kEyeY + 20);
      fb.drawArc(kMouthX, kMouthY + 16, 20, 220, 320, 4, true);  // 下向きの弧
      break;

    case Face::Hungry:
      drawCrossEye(fb, kEyeLX, kEyeY);
      drawCrossEye(fb, kEyeRX, kEyeY);
      fb.fillCircle(kMouthX, kMouthY + 2, 11, true);
      fb.fillCircle(kMouthX, kMouthY + 2, 7, false);
      // 空の電池アイコン + !
      fb.drawRect(76, 20, 36, 18, true);
      fb.fillRect(112, 25, 5, 8, true);
      fb.drawText(124, 22, "!", 2, true);
      break;

    default:
      drawOpenEye(fb, kEyeLX, kEyeY, 10);
      drawOpenEye(fb, kEyeRX, kEyeY, 10);
      fb.drawLine(kMouthX - 14, kMouthY, kMouthX + 14, kMouthY, 4, true);
      break;
  }
}

}  // namespace

void renderFace(FrameBuffer& fb, Face face, const StatusInfo& st) {
  fb.clear(false);
  drawFaceOnly(fb, face);

  // フッター: 区切り線 / 名前 / 温度 / 電池
  int fy = kFaceRegionH;
  fb.fillRect(0, fy + 2, FrameBuffer::kW, 2, true);
  fb.drawText(6, fy + 10, st.name, 2, true);

  char temp[16];
  snprintf(temp, sizeof(temp), "%.1fC", (double)st.temp_c);
  int tw = FrameBuffer::textWidth(temp, 1);
  fb.drawText(196 - tw, fy + 22, temp, 1, true);
  drawBatteryIcon(fb, 169, fy + 8, st.battery_pct);
}

}  // namespace persona
