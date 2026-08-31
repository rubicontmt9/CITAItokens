// emotion_core のネイティブユニットテスト(pio test -e native で実行)
#include <string.h>
#include <unity.h>

#include "emotion_engine.h"
#include "face_renderer.h"

using namespace persona;

namespace {

// テスト用のヘルパー: 1サイクル実行して表情を返す
struct Sim {
  FeatureState fs;
  EmotionState es;
  PersonalityConfig cfg;

  Face step(const SensorSample& s) {
    Features f = extractFeatures(fs, s, cfg.th);
    return EmotionEngine::update(es, f, cfg);
  }
};

SensorSample comfortable(uint32_t t) {
  SensorSample s;
  s.temp_c = 22.0f;
  s.hum_pct = 50.0f;
  s.lux = 300.0f;
  s.vib_peak_g = 0.0f;
  s.battery_mv = 4000;
  s.t = t;
  return s;
}

}  // namespace

void setUp() {}
void tearDown() {}

// ---------------------------------------------------------------------------
// features
// ---------------------------------------------------------------------------

void test_battery_percent_bounds() {
  TEST_ASSERT_FLOAT_WITHIN(0.1f, 0.0f, batteryPercent(3300));
  TEST_ASSERT_FLOAT_WITHIN(0.1f, 100.0f, batteryPercent(4200));
  TEST_ASSERT_FLOAT_WITHIN(0.1f, 0.0f, batteryPercent(3000));   // 下限クランプ
  TEST_ASSERT_FLOAT_WITHIN(0.1f, 100.0f, batteryPercent(4300)); // 上限クランプ
  float mid = batteryPercent(3750);
  TEST_ASSERT_TRUE(mid > 45.0f && mid < 55.0f);
}

void test_temp_deviation_and_rate() {
  FeatureState st;
  Thresholds th;
  SensorSample s = comfortable(0);
  Features f = extractFeatures(st, s, th);
  TEST_ASSERT_FLOAT_WITHIN(0.001f, 0.0f, f.temp_dev);  // 快適域

  s.temp_c = 32.0f;  // 6°C超過
  s.t = 60;          // 1分後
  f = extractFeatures(st, s, th);
  TEST_ASSERT_FLOAT_WITHIN(0.01f, 6.0f, f.temp_dev);
  TEST_ASSERT_FLOAT_WITHIN(0.5f, 10.0f, f.temp_rate);  // 10°C/min

  s.temp_c = 12.0f;
  s.t = 120;
  f = extractFeatures(st, s, th);
  TEST_ASSERT_FLOAT_WITHIN(0.01f, -6.0f, f.temp_dev);
}

void test_vibration_classification() {
  FeatureState st;
  Thresholds th;
  SensorSample s = comfortable(0);

  s.vib_peak_g = 0.3f;  // なでなで
  Features f = extractFeatures(st, s, th);
  TEST_ASSERT_TRUE(f.petted);
  TEST_ASSERT_FALSE(f.shock);

  s.vib_peak_g = 1.5f;  // 単発の衝撃
  s.t = 120;
  f = extractFeatures(st, s, th);
  TEST_ASSERT_TRUE(f.shock);
  TEST_ASSERT_FALSE(f.sustained_shake);

  s.t = 240;  // 連続する強い振動 → sustained
  f = extractFeatures(st, s, th);
  TEST_ASSERT_TRUE(f.sustained_shake);
  TEST_ASSERT_FALSE(f.shock);
}

void test_idle_hours() {
  FeatureState st;
  Thresholds th;
  Features f = extractFeatures(st, comfortable(0), th);
  TEST_ASSERT_FLOAT_WITHIN(0.01f, 0.0f, f.idle_hours);

  f = extractFeatures(st, comfortable(49 * 3600), th);  // 49時間放置
  TEST_ASSERT_FLOAT_WITHIN(0.1f, 49.0f, f.idle_hours);

  SensorSample s = comfortable(50 * 3600);
  s.vib_peak_g = 0.3f;  // 触られたらリセット
  f = extractFeatures(st, s, th);
  f = extractFeatures(st, comfortable(50 * 3600 + 60), th);
  TEST_ASSERT_TRUE(f.idle_hours < 1.0f);
}

// ---------------------------------------------------------------------------
// emotion engine: 表情遷移
// ---------------------------------------------------------------------------

void test_comfortable_cheerful_is_happy() {
  Sim sim;  // デフォルト: cheerful, sensitivity 0.5
  Face face = sim.step(comfortable(0));
  TEST_ASSERT_EQUAL(Face::Happy, face);
  TEST_ASSERT_TRUE(sim.es.valence > 0.0f);
}

void test_comfortable_calm_is_content() {
  Sim sim;
  sim.cfg.temperament = Temperament::Calm;
  Face face = sim.step(comfortable(0));
  TEST_ASSERT_EQUAL(Face::Content, face);
}

void test_cold_face_and_hysteresis() {
  Sim sim;
  SensorSample s = comfortable(0);
  s.temp_c = 5.0f;
  TEST_ASSERT_EQUAL(Face::Cold, sim.step(s));

  // ヒステリシス: 閾値(10°C)を僅かに超えてもColdを維持
  s.temp_c = 10.3f;
  s.t = 120;
  TEST_ASSERT_EQUAL(Face::Cold, sim.step(s));

  // 十分暖まれば解除
  s.temp_c = 15.0f;
  s.t = 240;
  TEST_ASSERT_NOT_EQUAL(Face::Cold, sim.step(s));
}

void test_hot_face() {
  Sim sim;
  SensorSample s = comfortable(0);
  s.temp_c = 34.0f;
  TEST_ASSERT_EQUAL(Face::Hot, sim.step(s));
}

void test_shock_then_sustained() {
  Sim sim;
  TEST_ASSERT_EQUAL(Face::Happy, sim.step(comfortable(0)));

  SensorSample s = comfortable(120);
  s.vib_peak_g = 2.0f;
  s.motion_wake = true;
  TEST_ASSERT_EQUAL(Face::Surprised, sim.step(s));  // 単発 → びっくり

  s.t = 240;
  TEST_ASSERT_EQUAL(Face::Scared, sim.step(s));  // 連続 → こわい

  // 収まれば通常へ戻る
  SensorSample calm_s = comfortable(360);
  Face f = sim.step(calm_s);
  TEST_ASSERT_TRUE(f != Face::Surprised && f != Face::Scared);
}

void test_lonely_after_long_idle() {
  Sim sim;
  sim.step(comfortable(0));
  Face face = sim.step(comfortable(49 * 3600));  // lonely_h=48hを超過
  TEST_ASSERT_EQUAL(Face::Lonely, face);

  // 触られたら解消
  SensorSample s = comfortable(49 * 3600 + 60);
  s.vib_peak_g = 0.3f;
  face = sim.step(s);
  TEST_ASSERT_NOT_EQUAL(Face::Lonely, face);
}

void test_dark_is_sleepy() {
  Sim sim;
  SensorSample s = comfortable(0);
  s.lux = 1.0f;
  TEST_ASSERT_EQUAL(Face::Sleepy, sim.step(s));
}

void test_hungry_overrides_everything() {
  Sim sim;
  SensorSample s = comfortable(0);
  s.battery_mv = 3350;   // 約5.6%
  s.vib_peak_g = 2.0f;   // 衝撃があっても
  TEST_ASSERT_EQUAL(Face::Hungry, sim.step(s));
}

void test_sensitivity_changes_reaction() {
  // 鈍感な個体は同じ寒さでもvalenceの落ち込みが小さい
  Sim dull, keen;
  dull.cfg.sensitivity = 0.0f;
  keen.cfg.sensitivity = 1.0f;
  dull.cfg.temperament = keen.cfg.temperament = Temperament::Calm;

  SensorSample s = comfortable(0);
  s.temp_c = 12.0f;
  dull.step(s);
  keen.step(s);
  TEST_ASSERT_TRUE(keen.es.valence < dull.es.valence);
}

// ---------------------------------------------------------------------------
// face renderer
// ---------------------------------------------------------------------------

static int countBlack(const FrameBuffer& fb) {
  int n = 0;
  for (int y = 0; y < FrameBuffer::kH; ++y)
    for (int x = 0; x < FrameBuffer::kW; ++x)
      if (fb.getPixel(x, y)) ++n;
  return n;
}

void test_render_all_faces_nonempty_and_distinct() {
  static FrameBuffer fb1, fb2;
  StatusInfo st;
  st.name = "TEST";
  st.temp_c = 22.5f;
  st.battery_pct = 80.0f;

  int prev = -1;
  for (int i = 0; i < static_cast<int>(Face::Count); ++i) {
    renderFace(fb1, static_cast<Face>(i), st);
    int n = countBlack(fb1);
    TEST_ASSERT_TRUE_MESSAGE(n > 300, "face should draw something");
    TEST_ASSERT_TRUE_MESSAGE(n < 200 * 200 / 2, "face should not be mostly black");
    TEST_ASSERT_TRUE_MESSAGE(n != prev, "faces should differ");
    prev = n;
  }

  // フッター区切り線が描かれている
  renderFace(fb2, Face::Content, st);
  TEST_ASSERT_TRUE(fb2.getPixel(100, kFaceRegionH + 2));
}

void test_text_rendering() {
  static FrameBuffer fb;
  fb.clear(false);
  fb.drawText(10, 10, "ABC 123", 2, true);
  TEST_ASSERT_TRUE(countBlack(fb) > 50);
  TEST_ASSERT_EQUAL((7 * 6 - 1) * 2, FrameBuffer::textWidth("ABC 123", 2));
}

int main(int, char**) {
  UNITY_BEGIN();
  RUN_TEST(test_battery_percent_bounds);
  RUN_TEST(test_temp_deviation_and_rate);
  RUN_TEST(test_vibration_classification);
  RUN_TEST(test_idle_hours);
  RUN_TEST(test_comfortable_cheerful_is_happy);
  RUN_TEST(test_comfortable_calm_is_content);
  RUN_TEST(test_cold_face_and_hysteresis);
  RUN_TEST(test_hot_face);
  RUN_TEST(test_shock_then_sustained);
  RUN_TEST(test_lonely_after_long_idle);
  RUN_TEST(test_dark_is_sleepy);
  RUN_TEST(test_hungry_overrides_everything);
  RUN_TEST(test_sensitivity_changes_reaction);
  RUN_TEST(test_render_all_faces_nonempty_and_distinct);
  RUN_TEST(test_text_rendering);
  return UNITY_END();
}
