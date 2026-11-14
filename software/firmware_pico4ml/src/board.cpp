#include "board.h"

#include <SPI.h>
#include <Wire.h>
#include <string.h>

#include "hardware/dma.h"
#include "hardware/gpio.h"
#include "hardware/pio.h"
#include "hardware/pio_instructions.h"
#include "hardware/pwm.h"

namespace board {

// ================================================================ ピン
// カメラと IMU は同じ I2C(i2c0、GPIO4/5)にいる
constexpr uint PIN_I2C_SDA = 4;
constexpr uint PIN_I2C_SCL = 5;
constexpr uint PIN_CAM_RESETB = 2;
constexpr uint PIN_CAM_XCLK = 3;
constexpr uint PIN_CAM_D0 = 6;      // 1-bit データ。PIO の in の基準
constexpr uint PIN_CAM_PCLK = 14;   // = 基準 + 8
constexpr uint PIN_CAM_HSYNC = 15;  // = 基準 + 9
constexpr uint PIN_CAM_VSYNC = 16;
constexpr uint8_t CAM_ADDR = 0x24;

constexpr uint PIN_LCD_RST = 7;
constexpr uint PIN_LCD_DC = 9;
constexpr uint PIN_LCD_SCK = 10;
constexpr uint PIN_LCD_MOSI = 11;
constexpr uint PIN_LCD_CS = 13;
constexpr int LCD_XSTART = 24;

constexpr uint PIN_BLE_TX = 0;  // RP2040 → モジュール
constexpr uint PIN_BLE_RX = 1;

static bool g_wire_started = false;
static void wireBegin() {
  if (g_wire_started) return;
  Wire.setSDA(PIN_I2C_SDA);
  Wire.setSCL(PIN_I2C_SCL);
  Wire.setClock(400000);
  Wire.begin();
  g_wire_started = true;
}

// ================================================================ 液晶
static void lcdCmd(uint8_t c, const uint8_t* d = nullptr, size_t n = 0) {
  digitalWrite(PIN_LCD_CS, LOW);
  digitalWrite(PIN_LCD_DC, LOW);
  SPI1.transfer(c);
  if (n) {
    digitalWrite(PIN_LCD_DC, HIGH);
    for (size_t i = 0; i < n; ++i) SPI1.transfer(d[i]);
  }
  digitalWrite(PIN_LCD_CS, HIGH);
}

void lcdWindow(int x0, int y0, int x1, int y1) {
  const uint8_t ca[4] = {0, uint8_t(x0 + LCD_XSTART), 0, uint8_t(x1 + LCD_XSTART)};
  const uint8_t ra[4] = {0, uint8_t(y0), 0, uint8_t(y1)};
  lcdCmd(0x2A, ca, 4);
  lcdCmd(0x2B, ra, 4);
}

// RGB565 を big endian の 2 バイトで送る
void lcdPixels(const uint16_t* px, size_t n) {
  digitalWrite(PIN_LCD_CS, LOW);
  digitalWrite(PIN_LCD_DC, LOW);
  SPI1.transfer(uint8_t(0x2C));
  digitalWrite(PIN_LCD_DC, HIGH);
  for (size_t i = 0; i < n; ++i) {
    SPI1.transfer(uint8_t(px[i] >> 8));
    SPI1.transfer(uint8_t(px[i] & 0xFF));
  }
  digitalWrite(PIN_LCD_CS, HIGH);
}

void lcdFill(int x0, int y0, int w, int h, uint16_t col) {
  lcdWindow(x0, y0, x0 + w - 1, y0 + h - 1);
  digitalWrite(PIN_LCD_CS, LOW);
  digitalWrite(PIN_LCD_DC, LOW);
  SPI1.transfer(uint8_t(0x2C));
  digitalWrite(PIN_LCD_DC, HIGH);
  for (int i = 0; i < w * h; ++i) {
    SPI1.transfer(uint8_t(col >> 8));
    SPI1.transfer(uint8_t(col & 0xFF));
  }
  digitalWrite(PIN_LCD_CS, HIGH);
}

void lcdInit() {
  pinMode(PIN_LCD_CS, OUTPUT);
  pinMode(PIN_LCD_DC, OUTPUT);
  pinMode(PIN_LCD_RST, OUTPUT);
  digitalWrite(PIN_LCD_CS, HIGH);
  SPI1.setSCK(PIN_LCD_SCK);
  SPI1.setTX(PIN_LCD_MOSI);
  SPI1.begin();
  SPI1.beginTransaction(SPISettings(20000000, MSBFIRST, SPI_MODE0));
  digitalWrite(PIN_LCD_RST, LOW);
  delay(5);
  digitalWrite(PIN_LCD_RST, HIGH);
  delay(120);
  lcdCmd(0x01);  // SWRESET
  delay(150);
  lcdCmd(0x11);  // SLPOUT
  delay(150);
  static const uint8_t frm[] = {0x01, 0x2C, 0x2D};
  lcdCmd(0xB1, frm, 3);
  lcdCmd(0xB2, frm, 3);
  static const uint8_t frm3[] = {0x01, 0x2C, 0x2D, 0x01, 0x2C, 0x2D};
  lcdCmd(0xB3, frm3, 6);
  static const uint8_t inv[] = {0x07};
  lcdCmd(0xB4, inv, 1);
  static const uint8_t p1[] = {0xA2, 0x02, 0x84}, p2[] = {0xC5}, p3[] = {0x0A, 0x00}, p4[] = {0x8A, 0x2A},
                       p5[] = {0x8A, 0xEE}, vm[] = {0x0E};
  lcdCmd(0xC0, p1, 3);
  lcdCmd(0xC1, p2, 1);
  lcdCmd(0xC2, p3, 2);
  lcdCmd(0xC3, p4, 2);
  lcdCmd(0xC4, p5, 2);
  lcdCmd(0xC5, vm, 1);
  lcdCmd(0x20);  // 色の反転なし(IR ボールの実験で確かめた設定)
  static const uint8_t madctl[] = {0x08};  // BGR
  lcdCmd(0x36, madctl, 1);
  static const uint8_t colmod[] = {0x05};  // 16 bit
  lcdCmd(0x3A, colmod, 1);
  static const uint8_t gp[] = {0x02, 0x1c, 0x07, 0x12, 0x37, 0x32, 0x29, 0x2d,
                               0x29, 0x25, 0x2B, 0x39, 0x00, 0x01, 0x03, 0x10};
  static const uint8_t gn[] = {0x03, 0x1d, 0x07, 0x06, 0x2E, 0x2C, 0x29, 0x2D,
                               0x2E, 0x2E, 0x37, 0x3F, 0x00, 0x00, 0x02, 0x10};
  lcdCmd(0xE0, gp, 16);
  lcdCmd(0xE1, gn, 16);
  lcdCmd(0x13);  // NORON
  delay(10);
  lcdCmd(0x29);  // DISPON
  delay(100);
  lcdFill(0, 0, kLcdW, kLcdH, 0x0000);
}

// 5x7 の文字(ASCII 0x20〜0x5A、列ごと、下位ビットが上)
static const uint8_t kFont[][5] = {
    {0x00, 0x00, 0x00, 0x00, 0x00}, {0x00, 0x00, 0x5F, 0x00, 0x00}, {0x00, 0x07, 0x00, 0x07, 0x00},
    {0x14, 0x7F, 0x14, 0x7F, 0x14}, {0x24, 0x2A, 0x7F, 0x2A, 0x12}, {0x23, 0x13, 0x08, 0x64, 0x62},
    {0x36, 0x49, 0x56, 0x20, 0x50}, {0x00, 0x08, 0x07, 0x03, 0x00}, {0x00, 0x1C, 0x22, 0x41, 0x00},
    {0x00, 0x41, 0x22, 0x1C, 0x00}, {0x2A, 0x1C, 0x7F, 0x1C, 0x2A}, {0x08, 0x08, 0x3E, 0x08, 0x08},
    {0x00, 0x80, 0x70, 0x30, 0x00}, {0x08, 0x08, 0x08, 0x08, 0x08}, {0x00, 0x00, 0x60, 0x60, 0x00},
    {0x20, 0x10, 0x08, 0x04, 0x02}, {0x3E, 0x51, 0x49, 0x45, 0x3E}, {0x00, 0x42, 0x7F, 0x40, 0x00},
    {0x72, 0x49, 0x49, 0x49, 0x46}, {0x21, 0x41, 0x49, 0x4D, 0x33}, {0x18, 0x14, 0x12, 0x7F, 0x10},
    {0x27, 0x45, 0x45, 0x45, 0x39}, {0x3C, 0x4A, 0x49, 0x49, 0x31}, {0x41, 0x21, 0x11, 0x09, 0x07},
    {0x36, 0x49, 0x49, 0x49, 0x36}, {0x46, 0x49, 0x49, 0x29, 0x1E}, {0x00, 0x00, 0x14, 0x00, 0x00},
    {0x00, 0x40, 0x34, 0x00, 0x00}, {0x00, 0x08, 0x14, 0x22, 0x41}, {0x14, 0x14, 0x14, 0x14, 0x14},
    {0x00, 0x41, 0x22, 0x14, 0x08}, {0x02, 0x01, 0x59, 0x09, 0x06}, {0x3E, 0x41, 0x5D, 0x59, 0x4E},
    {0x7C, 0x12, 0x11, 0x12, 0x7C}, {0x7F, 0x49, 0x49, 0x49, 0x36}, {0x3E, 0x41, 0x41, 0x41, 0x22},
    {0x7F, 0x41, 0x41, 0x41, 0x3E}, {0x7F, 0x49, 0x49, 0x49, 0x41}, {0x7F, 0x09, 0x09, 0x09, 0x01},
    {0x3E, 0x41, 0x41, 0x51, 0x73}, {0x7F, 0x08, 0x08, 0x08, 0x7F}, {0x00, 0x41, 0x7F, 0x41, 0x00},
    {0x20, 0x40, 0x41, 0x3F, 0x01}, {0x7F, 0x08, 0x14, 0x22, 0x41}, {0x7F, 0x40, 0x40, 0x40, 0x40},
    {0x7F, 0x02, 0x1C, 0x02, 0x7F}, {0x7F, 0x04, 0x08, 0x10, 0x7F}, {0x3E, 0x41, 0x41, 0x41, 0x3E},
    {0x7F, 0x09, 0x09, 0x09, 0x06}, {0x3E, 0x41, 0x51, 0x21, 0x5E}, {0x7F, 0x09, 0x19, 0x29, 0x46},
    {0x26, 0x49, 0x49, 0x49, 0x32}, {0x03, 0x01, 0x7F, 0x01, 0x03}, {0x3F, 0x40, 0x40, 0x40, 0x3F},
    {0x1F, 0x20, 0x40, 0x20, 0x1F}, {0x3F, 0x40, 0x38, 0x40, 0x3F}, {0x63, 0x14, 0x08, 0x14, 0x63},
    {0x03, 0x04, 0x78, 0x04, 0x03}, {0x61, 0x59, 0x49, 0x4D, 0x43},
};

void lcdText(int y, const char* s, uint16_t fg, uint16_t bg) {
  static uint16_t buf[kLcdW * 8];
  for (int i = 0; i < kLcdW * 8; ++i) buf[i] = bg;
  for (int ci = 0; s[ci] && ci < 13; ++ci) {
    char ch = s[ci];
    if (ch >= 'a' && ch <= 'z') ch = char(ch - 'a' + 'A');
    if (ch < 0x20 || ch > 0x5A) ch = '?';
    const uint8_t* g = kFont[ch - 0x20];
    for (int col = 0; col < 5; ++col) {
      for (int r = 0; r < 7; ++r) {
        if (g[col] & (1 << r)) buf[r * kLcdW + ci * 6 + col + 1] = fg;
      }
    }
  }
  lcdWindow(0, y, kLcdW - 1, y + 7);
  lcdPixels(buf, kLcdW * 8);
}

// ================================================================ カメラ HM01B0
static bool camWrite(uint16_t reg, uint8_t val) {
  Wire.beginTransmission(CAM_ADDR);
  Wire.write(uint8_t(reg >> 8));
  Wire.write(uint8_t(reg & 0xFF));
  Wire.write(val);
  return Wire.endTransmission() == 0;
}

static int camRead(uint16_t reg) {
  Wire.beginTransmission(CAM_ADDR);
  Wire.write(uint8_t(reg >> 8));
  Wire.write(uint8_t(reg & 0xFF));
  if (Wire.endTransmission(false) != 0) return -1;
  if (Wire.requestFrom(CAM_ADDR, uint8_t(1)) != 1) return -1;
  return Wire.read();
}

// hm01b0_init.h(pico-tflmicro)の hm01b0_324x244
struct Reg { uint16_t reg; uint8_t val; };
static const Reg kCamInit[] = {
    {0x0103, 0x00}, {0x0100, 0x00}, {0x1003, 0x08}, {0x1007, 0x08}, {0x3044, 0x0A}, {0x3045, 0x00},
    {0x3047, 0x0A}, {0x3050, 0xC0}, {0x3051, 0x42}, {0x3052, 0x50}, {0x3053, 0x00}, {0x3054, 0x03},
    {0x3055, 0xF7}, {0x3056, 0xF8}, {0x3057, 0x29}, {0x3058, 0x1F}, {0x3059, 0x1E}, {0x3064, 0x00},
    {0x3065, 0x04}, {0x1000, 0x43}, {0x1001, 0x40}, {0x1002, 0x32}, {0x0350, 0x7F}, {0x1006, 0x01},
    {0x1008, 0x00}, {0x1009, 0xA0}, {0x100A, 0x60}, {0x100B, 0x90}, {0x100C, 0x40}, {0x3022, 0x01},
    {0x1012, 0x01}, {0x2000, 0x07}, {0x2003, 0x00}, {0x2004, 0x1C}, {0x2007, 0x00}, {0x2008, 0x58},
    {0x200B, 0x00}, {0x200C, 0x7A}, {0x200F, 0x00}, {0x2010, 0xB8}, {0x2013, 0x00}, {0x2014, 0x58},
    {0x2017, 0x00}, {0x2018, 0x9B}, {0x2100, 0x01}, {0x2101, 0x5F}, {0x2102, 0x0A}, {0x2103, 0x03},
    {0x2104, 0x05}, {0x2105, 0x02}, {0x2106, 0x14}, {0x2107, 0x02}, {0x2108, 0x03}, {0x2109, 0x03},
    {0x210A, 0x00}, {0x210B, 0x80}, {0x210C, 0x40}, {0x210D, 0x20}, {0x210E, 0x03}, {0x210F, 0x00},
    {0x2110, 0x85}, {0x2111, 0x00}, {0x2112, 0xA0}, {0x2150, 0x03}, {0x0340, 0x01}, {0x0341, 0x7A},
    {0x0342, 0x01}, {0x0343, 0x77}, {0x3010, 0x00}, {0x0383, 0x01}, {0x0387, 0x01}, {0x0390, 0x00},
    {0x3011, 0x70}, {0x3059, 0x22}, {0x3060, 0x30}, {0x0101, 0x03}, {0x0104, 0x01}, {0x0100, 0x01},
};

constexpr int IMG_W = 324, IMG_H = 324;
static uint8_t g_img[IMG_W * IMG_H];
static int g_cam_id = -1;
static PIO g_pio = pio0;
static uint g_sm = 0, g_pio_off = 0;
static int g_dma = -1;

// 露出: 既定は自動露出。手動にすると行数・ゲインを固定する
static bool g_manual = false;
static uint16_t g_exp_lines = 368;
static uint8_t g_exp_gain = 0;

// 撮影中のカメラの I2C は化けることがあるので、書いたら読み返して確かめる
static bool camWriteVerify(uint16_t reg, uint8_t val) {
  for (int i = 0; i < 5; ++i) {
    camWrite(reg, val);
    if (camRead(reg) == val) return true;
    delay(2);
  }
  return false;
}

static void camApplyExposure() {
  if (!g_manual) {
    camWriteVerify(0x2100, 0x01);  // 自動露出 入
    camWriteVerify(0x0104, 0x01);
    return;
  }
  camWriteVerify(0x2100, 0x00);  // 自動露出 切
  camWriteVerify(0x0202, uint8_t(g_exp_lines >> 8));
  camWriteVerify(0x0203, uint8_t(g_exp_lines & 0xFF));
  camWriteVerify(0x0205, g_exp_gain);
  camWriteVerify(0x020E, 0x01);  // デジタルゲイン 1x
  camWriteVerify(0x020F, 0x00);
  camWriteVerify(0x0104, 0x01);  // 反映
}

void camSetExposure(uint16_t integ_lines, uint8_t again_code) {
  g_manual = true;
  g_exp_lines = integ_lines;
  g_exp_gain = again_code;
  camApplyExposure();
}

void camSetAuto() {
  g_manual = false;
  camApplyExposure();
}

// 撮影開始(1)/ 待機(0)。実機(2026-11-14)で、起動によっては設定表の最後の 0x0100 = 1 が効かず
// VSYNC/HSYNC/PCLK が止まったままになったので、読み返して確かめる
static bool camSetStreaming(bool on) { return camWriteVerify(0x0100, on ? 0x01 : 0x00); }

// 実機(2026-11-14)で、起動によっては設定表の一部が効かず、初期値のまま(約 60 fps、明るさ 0)で
// 撮影が始まることがあった。リセットから設定表を入れ直し、何か所か読み返して効いたか確かめる。
static bool camConfigured() {
  return camRead(0x3059) == 0x22 && camRead(0x0340) == 0x01 && camRead(0x0341) == 0x7A &&
         camRead(0x1012) == 0x01;
}

static bool camLoad() {
  for (int attempt = 0; attempt < 3; ++attempt) {
    gpio_put(PIN_CAM_RESETB, 0);
    delay(20);
    gpio_put(PIN_CAM_RESETB, 1);
    delay(100);
    for (const Reg& r : kCamInit) {
      for (int k = 0; k < 3 && !camWrite(r.reg, r.val); ++k) delay(2);
      if (r.reg == 0x0103) delay(10);  // ソフトリセットの後は少し待つ
    }
    // 設定表の最後で撮影が始まっているので、すぐ待機にしてから確かめる
    camSetStreaming(false);
    camApplyExposure();
    if (camConfigured()) return true;
  }
  return false;
}

bool camInit() {
  gpio_init(PIN_CAM_RESETB);
  gpio_set_dir(PIN_CAM_RESETB, GPIO_OUT);
  // XCLK = sysclk / 8(200 MHz → 25 MHz)
  gpio_set_function(PIN_CAM_XCLK, GPIO_FUNC_PWM);
  const uint slice = pwm_gpio_to_slice_num(PIN_CAM_XCLK);
  pwm_set_wrap(slice, 7);
  pwm_set_gpio_level(PIN_CAM_XCLK, 4);
  pwm_set_enabled(slice, true);

  gpio_put(PIN_CAM_RESETB, 0);
  delay(100);
  gpio_put(PIN_CAM_RESETB, 1);
  delay(100);

  wireBegin();
  const int hi = camRead(0x0000), lo = camRead(0x0001);
  g_cam_id = (hi < 0 || lo < 0) ? -1 : (hi << 8) | lo;
  if (!camLoad()) Serial.println("# cam: 設定表が効かない(読み返し NG)");

  for (uint p : {PIN_CAM_D0, PIN_CAM_PCLK, PIN_CAM_HSYNC, PIN_CAM_VSYNC}) {
    gpio_init(p);
    gpio_set_dir(p, GPIO_IN);
  }

  // PIO: hsync を待つ → pclk の立ち上がりで D0 を 1 bit 取る(image.pio と同じ 4 命令)
  static uint16_t instr[4];
  instr[0] = pio_encode_wait_pin(true, 9);
  instr[1] = pio_encode_wait_pin(true, 8);
  instr[2] = pio_encode_in(pio_pins, 1);
  instr[3] = pio_encode_wait_pin(false, 8);
  static const pio_program_t prog = {instr, 4, -1, 0};
  g_pio_off = pio_add_program(g_pio, &prog);
  g_sm = pio_claim_unused_sm(g_pio, true);
  pio_sm_set_consecutive_pindirs(g_pio, g_sm, PIN_CAM_D0, 1, false);
  pio_sm_config c = pio_get_default_sm_config();
  sm_config_set_wrap(&c, g_pio_off, g_pio_off + 3);
  sm_config_set_in_pins(&c, PIN_CAM_D0);
  sm_config_set_in_shift(&c, false, true, 8);
  sm_config_set_fifo_join(&c, PIO_FIFO_JOIN_RX);
  pio_sm_init(g_pio, g_sm, g_pio_off, &c);
  g_dma = dma_claim_unused_channel(true);
  return g_cam_id == 0x01B0;
}

int camId() { return g_cam_id; }

void camDiag() {
  camSetStreaming(true);
  delay(60);
  uint32_t nv = 0, nh = 0, np = 0;
  bool v = gpio_get(PIN_CAM_VSYNC), h = gpio_get(PIN_CAM_HSYNC), pc = gpio_get(PIN_CAM_PCLK);
  const uint32_t t0 = millis();
  while (millis() - t0 < 100) {
    const bool v2 = gpio_get(PIN_CAM_VSYNC), h2 = gpio_get(PIN_CAM_HSYNC), p2 = gpio_get(PIN_CAM_PCLK);
    nv += v2 != v; nh += h2 != h; np += p2 != pc;
    v = v2; h = h2; pc = p2;
  }
  camSetStreaming(false);
  Serial.printf("# cam diag 100ms: vsync=%lu hsync=%lu pclk=%lu (pclk は取りこぼしあり) id=0x%02X%02X mode(0x0100)=0x%02X configured=%d\n",
                (unsigned long)nv, (unsigned long)nh, (unsigned long)np, camRead(0x0000), camRead(0x0001), camRead(0x0100), int(camConfigured()));
}

static bool camCapture(uint32_t timeout_ms) {
  pio_sm_set_enabled(g_pio, g_sm, false);
  pio_sm_clear_fifos(g_pio, g_sm);
  pio_sm_restart(g_pio, g_sm);
  pio_sm_exec(g_pio, g_sm, pio_encode_jmp(g_pio_off));
  dma_channel_config c = dma_channel_get_default_config(g_dma);
  channel_config_set_read_increment(&c, false);
  channel_config_set_write_increment(&c, true);
  channel_config_set_dreq(&c, pio_get_dreq(g_pio, g_sm, false));
  channel_config_set_transfer_data_size(&c, DMA_SIZE_8);
  dma_channel_configure(g_dma, &c, g_img, &g_pio->rxf[g_sm], sizeof(g_img), false);

  const uint32_t t0 = millis();
  while (gpio_get(PIN_CAM_VSYNC)) {
    if (millis() - t0 > timeout_ms) return false;
  }
  while (!gpio_get(PIN_CAM_VSYNC)) {
    if (millis() - t0 > timeout_ms) return false;
  }
  dma_channel_start(g_dma);
  pio_sm_set_enabled(g_pio, g_sm, true);
  while (dma_channel_is_busy(g_dma)) {
    if (millis() - t0 > timeout_ms) {
      dma_channel_abort(g_dma);
      pio_sm_set_enabled(g_pio, g_sm, false);
      return false;
    }
  }
  pio_sm_set_enabled(g_pio, g_sm, false);
  return true;
}

// 診断: 1 フレーム撮って、半分に間引いた 162x162 の PGM(P5)をシリアルにそのまま流す
void camDumpPgm() {
  camSetStreaming(true);
  bool ok = true;
  for (int i = 0; i < 6 && ok; ++i) ok = camCapture(1000);  // 自動露出をなじませる
  camSetStreaming(false);
  if (!ok) {
    Serial.println("# pgm: no frame");
    return;
  }
  Serial.printf("P5\n162 162\n255\n");
  for (int y = 0; y < IMG_H; y += 2) {
    for (int x = 0; x < IMG_W; x += 2) Serial.write(g_img[y * IMG_W + x]);
  }
  Serial.printf("\n");
}

static int imgMean() {
  // 4 画素おきに間引いて平均(端の 2 割は周辺減光があるので外す)
  uint32_t sum = 0, n = 0;
  for (int y = IMG_H / 5; y < IMG_H * 4 / 5; y += 4) {
    const uint8_t* row = &g_img[y * IMG_W];
    for (int x = IMG_W / 5; x < IMG_W * 4 / 5; x += 4) {
      sum += row[x];
      ++n;
    }
  }
  return n ? int(sum / n) : -1;
}

// 自動露出がなじむまでのフレーム数(20 fps で 1 枚 約 50 ms)。待機の間もカメラは露出の値を覚えているので、
// 2 回目以降はほぼなじんだところから始まる。読み返しは最後のフレームの後なので、わずかにずれうる [VERIFY]
constexpr int kSettleFrames = 6;

bool camMeasure(CamLight& out) {
  if (g_cam_id != 0x01B0) return false;
  bool ok = camSetStreaming(true);
  for (int i = 0; i < kSettleFrames && ok; ++i) ok = camCapture(300);
  camSetStreaming(false);  // ここから先は I2C が化けない
  // 撮れなかった・設定が外れていた時は、リセットから入れ直して次の回に任せる
  if (!ok || !camConfigured()) {
    camLoad();
    return false;
  }
  const int ih = camRead(0x0202), il = camRead(0x0203), ag = camRead(0x0205);
  const int dh = camRead(0x020E), dl = camRead(0x020F);
  if (ih < 0 || il < 0 || ag < 0 || dh < 0 || dl < 0) return false;
  out.mean = imgMean();
  out.integ = uint16_t((ih << 8) | il);
  out.again = float(1 << ((ag >> 4) & 0x07));        // 0x0205 bit6:4 = 1x/2x/4x/8x
  out.dgain = float(((dh & 0x03) << 6) | (dl >> 2)) / 64.0f;  // 0x020E[1:0].0x020F[7:2](整数 2 bit・小数 6 bit)
  if (out.dgain <= 0.0f) out.dgain = 1.0f;
  const float integ = out.integ ? float(out.integ) : 1.0f;
  out.light = float(out.mean) * (368.0f / integ) / (out.again * out.dgain);
  return true;
}

// ================================================================ IMU
static ImuType g_imu = ImuType::None;
static uint8_t g_imu_addr = 0;

static bool regWrite(uint8_t addr, uint8_t reg, uint8_t val) {
  Wire.beginTransmission(addr);
  Wire.write(reg);
  Wire.write(val);
  return Wire.endTransmission() == 0;
}

static bool regRead(uint8_t addr, uint8_t reg, uint8_t* buf, size_t n) {
  Wire.beginTransmission(addr);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(addr, uint8_t(n)) != n) return false;
  for (size_t i = 0; i < n; ++i) buf[i] = uint8_t(Wire.read());
  return true;
}

// 実機(2026-11-14)で、カメラが撮影している間だけ IMU の読みの後ろのバイトが 0x00 / 0xFF / でたらめに
// 化けた(カメラの信号のノイズが I2C に乗っていると見ている)。同じ番地を続けて読み、一致した時だけ使う。
// センサーの更新(100 Hz)と重なって 1 回目と 2 回目が違うこともあるので、最大 4 回まで読んで隣り合う 2 回の一致を探す。
static bool regReadStable(uint8_t addr, uint8_t reg, uint8_t* buf, size_t n) {
  uint8_t prev[8], cur[8];
  if (n > sizeof(prev) || !regRead(addr, reg, prev, n)) return false;
  for (int k = 0; k < 3; ++k) {
    if (!regRead(addr, reg, cur, n)) return false;
    if (memcmp(prev, cur, n) == 0) {
      memcpy(buf, cur, n);
      return true;
    }
    memcpy(prev, cur, n);
  }
  return false;
}

// ICM-42622: WHO_AM_I(0x75)= 0x46。バンク 0 の TEMP_DATA1(0x1D)から温度・加速度が続く
// ICM-20948: WHO_AM_I(0x00、バンク 0)= 0xEA
bool imuInit() {
  wireBegin();
  for (uint8_t addr : {uint8_t(0x68), uint8_t(0x69)}) {
    uint8_t who = 0;
    regWrite(addr, 0x76, 0x00);  // ICM-42622 の REG_BANK_SEL = 0(ICM-20948 では無害な番地)
    // 0x46 = ICM-42622 のはずが、実機(2026-11-14)は 0x41 = ICM-42602 だった。
    // ICM-426xx の仲間(42600/02/05/86/22/88)はバンク 0 のレジスタの並びが同じなので、どれでも受ける
    const bool icm426 = regRead(addr, 0x75, &who, 1) &&
                        (who == 0x40 || who == 0x41 || who == 0x42 || who == 0x44 || who == 0x46 || who == 0x47);
    if (icm426) {
      g_imu = ImuType::Icm42622;
      g_imu_addr = addr;
      regWrite(addr, 0x4E, 0x0F);  // PWR_MGMT0: 加速度・ジャイロを低ノイズで ON
      delay(1);
      regWrite(addr, 0x50, 0x48);  // ACCEL_CONFIG0: ±4 g(010)・100 Hz(1000)
      delay(50);
      return true;
    }
    who = 0;
    regWrite(addr, 0x7F, 0x00);  // ICM-20948 の REG_BANK_SEL = 0
    if (regRead(addr, 0x00, &who, 1) && who == 0xEA) {
      g_imu = ImuType::Icm20948;
      g_imu_addr = addr;
      regWrite(addr, 0x06, 0x01);  // PWR_MGMT_1: スリープ解除・クロック自動
      delay(50);
      return true;  // 加速度は既定の ±2 g
    }
  }
  return false;
}

void i2cScan() {
  wireBegin();
  Serial.print("# i2c:");
  for (uint8_t a = 0x08; a < 0x78; ++a) {
    Wire.beginTransmission(a);
    if (Wire.endTransmission() == 0) Serial.printf(" 0x%02X", a);
  }
  Serial.println();
  for (uint8_t a : {uint8_t(0x68), uint8_t(0x69)}) {
    uint8_t w75 = 0, w00 = 0;
    const bool r75 = regRead(a, 0x75, &w75, 1), r00 = regRead(a, 0x00, &w00, 1);
    Serial.printf("# 0x%02X: reg75=%s0x%02X reg00=%s0x%02X\n", a, r75 ? "" : "NG ", w75, r00 ? "" : "NG ", w00);
  }
  if (g_imu_addr) {
    uint8_t pm = 0, ac = 0;
    regRead(g_imu_addr, 0x4E, &pm, 1);
    regRead(g_imu_addr, 0x50, &ac, 1);
    Serial.printf("# imu PWR_MGMT0=0x%02X ACCEL_CONFIG0=0x%02X\n", pm, ac);
    for (int k = 0; k < 3; ++k) {
      uint8_t b[8] = {0};
      const bool ok = regRead(g_imu_addr, 0x1D, b, 8);
      Serial.printf("# imu raw 0x1D..0x24 %s %02X %02X | %02X %02X %02X %02X %02X %02X\n", ok ? "ok" : "NG", b[0], b[1],
                    b[2], b[3], b[4], b[5], b[6], b[7]);
    }
  }
}

ImuType imuType() { return g_imu; }

const char* imuName() {
  switch (g_imu) {
    case ImuType::Icm42622: return "ICM426XX";
    case ImuType::Icm20948: return "ICM20948";
    default: return "NONE";
  }
}

static int16_t be16(const uint8_t* p) { return int16_t((uint16_t(p[0]) << 8) | p[1]); }

bool imuReadAccel(float& ax, float& ay, float& az) {
  uint8_t b[6];
  if (g_imu == ImuType::Icm42622) {
    if (!regReadStable(g_imu_addr, 0x1F, b, 6)) return false;
    for (int i = 0; i < 3; ++i) {
      if (be16(b + 2 * i) == -32768) return false;  // データ無効
    }
    constexpr float kLsbPerG = 8192.0f;  // ±4 g
    ax = be16(b) / kLsbPerG;
    ay = be16(b + 2) / kLsbPerG;
    az = be16(b + 4) / kLsbPerG;
    return true;
  }
  if (g_imu == ImuType::Icm20948) {
    if (!regReadStable(g_imu_addr, 0x2D, b, 6)) return false;
    constexpr float kLsbPerG = 16384.0f;  // ±2 g
    ax = be16(b) / kLsbPerG;
    ay = be16(b + 2) / kLsbPerG;
    az = be16(b + 4) / kLsbPerG;
    return true;
  }
  return false;
}

bool imuReadTempC(float& t) {
  uint8_t b[2];
  if (g_imu == ImuType::Icm42622) {
    if (!regReadStable(g_imu_addr, 0x1D, b, 2)) return false;
    t = be16(b) / 132.48f + 25.0f;  // データシートの式
    return true;
  }
  if (g_imu == ImuType::Icm20948) {
    if (!regReadStable(g_imu_addr, 0x39, b, 2)) return false;
    t = (be16(b) - 21.0f) / 333.87f + 21.0f;
    return true;
  }
  return false;
}

// ================================================================ BLE(UART0)
static bool g_ble_conn = false;

void bleInit() {
  Serial1.setTX(PIN_BLE_TX);
  Serial1.setRX(PIN_BLE_RX);
  Serial1.begin(115200);
}

// モジュールは接続・切断を "IND=BLECONNECTED" / "IND=BLEDISCONNECTED" で知らせる(magic_wand_ble の例)
void blePoll() {
  static char line[40];
  static size_t len = 0;
  while (Serial1.available()) {
    const char c = char(Serial1.read());
    if (c == '\r' || c == '\n') {
      line[len] = 0;
      if (len) {
        if (strstr(line, "BLEDISCONNECTED")) g_ble_conn = false;
        else if (strstr(line, "BLECONNECTED")) g_ble_conn = true;
      }
      len = 0;
    } else if (len < sizeof(line) - 1) {
      line[len++] = c;
    }
  }
}

bool bleConnected() { return g_ble_conn; }

void bleSendLine(const char* s) {
  if (!g_ble_conn) return;
  Serial1.print(s);
  Serial1.print("\r\n");
}

}  // namespace board
