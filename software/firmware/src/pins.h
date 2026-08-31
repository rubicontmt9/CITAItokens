// XIAO ESP32S3 ピン割当
// docs/planning/03_hardware_design.md の表と一致させること(変更時は両方更新)
#pragma once

namespace pins {

// 電子ペーパー(SPI)
constexpr int kEpdCs = 1;     // D0
constexpr int kEpdDc = 2;     // D1
constexpr int kEpdRst = 43;   // D6 (UART0 TX。USB CDCでシリアルを使うため空く)
constexpr int kEpdBusy = 44;  // D7 (UART0 RX)
constexpr int kSpiSck = 7;    // D8
constexpr int kSpiMosi = 9;   // D10 (MISOは不使用)

// I2C(AHT20 / LIS3DH / BH1750)
constexpr int kI2cSda = 5;    // D4
constexpr int kI2cScl = 6;    // D5

// LIS3DH INT1 → ディープスリープ起床(ESP32-S3のRTC GPIO 0-21の範囲内であること)
constexpr int kLisInt = 8;    // D9

// 電池電圧監視(分圧 100kΩ:100kΩ → 実電圧の1/2を入力)
constexpr int kVbatAdc = 4;   // D3 (ADC1_CH3)

constexpr int kSpare = 3;     // D2 予備

}  // namespace pins
