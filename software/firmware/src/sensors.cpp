#include "sensors.h"

#include <Adafruit_AHTX0.h>
#include <Adafruit_LIS3DH.h>
#include <BH1750.h>
#include <Wire.h>
#include <math.h>
#include <time.h>

#include "pins.h"

namespace fw {

namespace {

constexpr uint8_t kLisAddr = 0x18;

Adafruit_AHTX0 g_aht;
Adafruit_LIS3DH g_lis;
BH1750 g_bh1750(0x23);
bool g_aht_ok = false;
bool g_lis_ok = false;
bool g_bh_ok = false;

// Adafruitライブラリが公開していないINT関連レジスタへの直接アクセス
void lisWriteReg(uint8_t reg, uint8_t val) {
  Wire.beginTransmission(kLisAddr);
  Wire.write(reg);
  Wire.write(val);
  Wire.endTransmission();
}

uint8_t lisReadReg(uint8_t reg) {
  Wire.beginTransmission(kLisAddr);
  Wire.write(reg);
  Wire.endTransmission(false);
  Wire.requestFrom(kLisAddr, (uint8_t)1);
  return Wire.available() ? Wire.read() : 0;
}

// 数サンプル読んで |a|-1g のピークを返す [g]
float vibrationPeak(int samples, int interval_ms) {
  if (!g_lis_ok) return 0.0f;
  float peak = 0.0f;
  for (int i = 0; i < samples; ++i) {
    g_lis.read();
    float ax = g_lis.x_g, ay = g_lis.y_g, az = g_lis.z_g;
    float mag = sqrtf(ax * ax + ay * ay + az * az);
    float dev = fabsf(mag - 1.0f);  // 重力ぶんを除去
    if (dev > peak) peak = dev;
    delay(interval_ms);
  }
  return peak;
}

uint16_t readBatteryMv() {
  // 100k:100k 分圧 → 実電圧の1/2がADCに入る
  uint32_t mv = analogReadMilliVolts(pins::kVbatAdc);
  return static_cast<uint16_t>(mv * 2);
}

}  // namespace

void sensorsInit() {
  Wire.begin(pins::kI2cSda, pins::kI2cScl);
  g_aht_ok = g_aht.begin(&Wire);
  g_lis_ok = g_lis.begin(kLisAddr, &Wire);
  if (g_lis_ok) {
    g_lis.setRange(LIS3DH_RANGE_4_G);
    g_lis.setDataRate(LIS3DH_DATARATE_100_HZ);
    // ラッチされた割り込みをクリア(INT1_SRC読み出し)
    lisReadReg(0x31);
  }
  g_bh_ok = g_bh1750.begin(BH1750::ONE_TIME_HIGH_RES_MODE, 0x23, &Wire);
  analogSetPinAttenuation(pins::kVbatAdc, ADC_11db);
}

persona::SensorSample readSample(bool motion_wake) {
  persona::SensorSample s;
  s.motion_wake = motion_wake;
  s.t = static_cast<uint32_t>(time(nullptr));
  s.battery_mv = readBatteryMv();

  if (g_aht_ok) {
    sensors_event_t hum, temp;
    if (g_aht.getEvent(&hum, &temp)) {
      s.temp_c = temp.temperature;
      s.hum_pct = hum.relative_humidity;
    }
  }
  if (g_bh_ok) {
    float lux = g_bh1750.readLightLevel();
    if (lux >= 0) s.lux = lux;
  }
  // 振動割り込み起床時は長めに観測してピークを捉える
  s.vib_peak_g = motion_wake ? vibrationPeak(16, 10) : vibrationPeak(5, 5);
  return s;
}

void configureMotionInterrupt(float threshold_g) {
  if (!g_lis_ok) return;
  // ±4gレンジ: INT1_THS 1LSB = 32mg
  uint8_t ths = static_cast<uint8_t>(persona::clampf(threshold_g / 0.032f, 1.0f, 127.0f));
  lisWriteReg(0x22, 0x40);  // CTRL_REG3: I1_IA1(動作検知をINT1へ)
  lisWriteReg(0x24, 0x08);  // CTRL_REG5: LIR_INT1(読み出しまでラッチ)
  lisWriteReg(0x32, ths);   // INT1_THS
  lisWriteReg(0x33, 0x00);  // INT1_DURATION: 即時
  lisWriteReg(0x30, 0x2A);  // INT1_CFG: X/Y/Z high イベントのOR
  lisReadReg(0x31);         // ラッチクリアしてからスリープへ
}

}  // namespace fw
