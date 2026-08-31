#include "display_epd.h"

#include <GxEPD2_BW.h>
#include <SPI.h>

#include "pins.h"

namespace fw {

namespace {

GxEPD2_BW<GxEPD2_154_D67, GxEPD2_154_D67::HEIGHT> g_display(
    GxEPD2_154_D67(pins::kEpdCs, pins::kEpdDc, pins::kEpdRst, pins::kEpdBusy));
bool g_inited = false;

void ensureInit() {
  if (g_inited) return;
  SPI.begin(pins::kSpiSck, /*miso=*/-1, pins::kSpiMosi, pins::kEpdCs);
  // initial=false: ディープスリープ明けでもパネル全消去をしない
  g_display.init(0, /*initial=*/false, /*reset_duration=*/2, /*pulldown_rst_mode=*/false);
  g_display.setRotation(0);
  g_inited = true;
}

}  // namespace

void displayShow(const persona::FrameBuffer& fb, bool full_refresh) {
  ensureInit();
  if (full_refresh) {
    g_display.setFullWindow();
  } else {
    g_display.setPartialWindow(0, 0, persona::FrameBuffer::kW,
                               persona::FrameBuffer::kH);
  }
  g_display.firstPage();
  do {
    g_display.fillScreen(GxEPD_WHITE);
    // FrameBufferは 1=黒 / MSB=左 なのでAdafruit_GFXのdrawBitmapと同じ形式
    g_display.drawBitmap(0, 0, fb.bits, persona::FrameBuffer::kW,
                         persona::FrameBuffer::kH, GxEPD_BLACK);
  } while (g_display.nextPage());
  g_display.hibernate();  // パネルをディープスリープへ(画像は保持される)
}

}  // namespace fw
