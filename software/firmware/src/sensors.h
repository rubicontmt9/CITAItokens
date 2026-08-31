// センサー(AHT20 / LIS3DH / BH1750)の読取とLIS3DH動作割り込みの設定
#pragma once
#include "sensor_sample.h"

namespace fw {

// I2C開始と各センサーの初期化。失敗したセンサーはデフォルト値で読める。
void sensorsInit();

// 1回ぶんのセンサースナップショットを取得する。
// motion_wake: 振動割り込みで起床した場合true(振動を長めに観測する)
persona::SensorSample readSample(bool motion_wake);

// LIS3DHのINT1を「動作検知でHigh」に設定する(ディープスリープ起床用)。
// threshold_g: 検知閾値 [g](なでなでも拾えるよう小さめの値を渡す)
void configureMotionInterrupt(float threshold_g);

}  // namespace fw
