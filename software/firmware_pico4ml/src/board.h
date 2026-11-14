// Pico4ML-BLE の部品をつなぐ層(カメラ・液晶・IMU・BLE の UART)
//
// ピン・初期化列は ArduCAM/pico-tflmicro(include/arducam_hm01b0、include/LCD_st7735、include/ICM42622、
// examples/magic_wand_ble)から。カメラと液晶のコードは、同じ基板で動作を確かめた実験ファーム
// (RoboCup 用の IR ボールの実験、2026-10-27)から写した。
#pragma once
#include <Arduino.h>
#include <stdint.h>

namespace board {

// ---------------------------------------------------------------- 液晶 ST7735 80x160(縦長)
constexpr int kLcdW = 80;
constexpr int kLcdH = 160;

void lcdInit();
void lcdWindow(int x0, int y0, int x1, int y1);
void lcdPixels(const uint16_t* px, size_t n);
void lcdFill(int x0, int y0, int w, int h, uint16_t col);
// 1 行(13 文字 × 6 px、高さ 8 px)を背景ごと描く。y は画面の上からの px
void lcdText(int y, const char* s, uint16_t fg, uint16_t bg);

constexpr uint16_t rgb565(uint8_t r, uint8_t g, uint8_t b) {
  return uint16_t(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
}

// ---------------------------------------------------------------- カメラ HM01B0(明るさだけ使う)
// カメラは撮る時だけ起こし、ふだんは待機させる(撮影中は I2C が化けて IMU が読めないため、2026-11-14 実機)。
// 明るさは自動露出に任せ、カメラが選んだ露出の行数・ゲインで画素の平均を割り戻す。
//   light = mean × (368 / 露出の行数) / (アナログゲイン × デジタルゲイン)
// = 「露出 368 行・ゲイン 1x で撮った時の平均」に相当(電気をつけた部屋で机に向けて 13、明るい方へ向けて 232)。
struct CamLight {
  int mean = -1;         // 最後のフレームの平均(0..255)
  uint16_t integ = 0;    // 露出の行数(読み返し)
  float again = 1.0f;    // アナログゲイン(倍)
  float dgain = 1.0f;    // デジタルゲイン(倍)
  float light = -1.0f;   // 上の式の値
};
bool camInit();
int camId();
// 起こす → 数フレーム撮って自動露出をなじませる → 待機に戻す → レジスタを読む。失敗なら false
bool camMeasure(CamLight& out);
// 手動の露出(自動露出を切る)/ 自動露出に戻す
void camSetExposure(uint16_t integ_lines, uint8_t again_code);
void camSetAuto();

// ---------------------------------------------------------------- IMU(ICM-42622 か ICM-20948 を自動で見分ける)
enum class ImuType : uint8_t { None, Icm42622, Icm20948 };
bool imuInit();
// 診断: I2C の全アドレスを叩いて応答した番地と、IMU の WHO_AM_I の候補をシリアルに出す
void i2cScan();
// 診断: 100 ms の間に VSYNC / HSYNC / PCLK が何回変わったかと、カメラの主なレジスタを出す
void camDiag();
// 診断: 1 フレームを 162x162 の PGM でシリアルに流す
void camDumpPgm();
ImuType imuType();
const char* imuName();
// 加速度 [g]
bool imuReadAccel(float& ax, float& ay, float& az);
// チップの温度 [°C](室温より高く出るので、呼ぶ側でずれを足す)
bool imuReadTempC(float& t);

// ---------------------------------------------------------------- BLE モジュール(UART0、GPIO0/1)
// 接続中だけ、状態の 1 行を BLE にも流す。モジュールの仕様は未確認 [VERIFY 実機]
void bleInit();
void blePoll();
bool bleConnected();
void bleSendLine(const char* s);

}  // namespace board
