#include "power_mgr.h"

#include <Arduino.h>
#include <esp_sleep.h>

namespace fw {

namespace {
constexpr uint32_t kMagic = 0x50455253;  // "PERS"
RTC_DATA_ATTR RtcState g_rtc;
}  // namespace

WakeCause wakeCause() {
  switch (esp_sleep_get_wakeup_cause()) {
    case ESP_SLEEP_WAKEUP_TIMER: return WakeCause::Timer;
    case ESP_SLEEP_WAKEUP_EXT0:  return WakeCause::Motion;
    default:                     return WakeCause::PowerOn;
  }
}

RtcState& rtcState() {
  if (g_rtc.magic != kMagic) {
    g_rtc = RtcState();
    g_rtc.magic = kMagic;
  }
  return g_rtc;
}

void deepSleep(uint32_t seconds, int motion_int_pin) {
  esp_sleep_enable_timer_wakeup(static_cast<uint64_t>(seconds) * 1000000ULL);
  // LIS3DH INT1(アクティブHigh)で即時起床
  esp_sleep_enable_ext0_wakeup(static_cast<gpio_num_t>(motion_int_pin), 1);
  esp_deep_sleep_start();
  for (;;) {}  // ここには到達しない
}

}  // namespace fw
