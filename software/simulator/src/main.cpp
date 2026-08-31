// PersonaSticker PCシミュレーター
// ファームウェアと同一の emotion_core を使い、仮想ステッカーをブラウザから
// 操作・観察する。使い方: ./persona_sim [--port 8080] [--web web]
#include <math.h>
#include <stdio.h>
#include <string.h>

#include <chrono>
#include <fstream>
#include <sstream>
#include <string>
#include <thread>

#include "http_server.h"
#include "json_util.h"
#include "sim_world.h"

using namespace sim;

namespace {

SimWorld g_world;

// "/api/stickers/12/frame" → id=12, rest="frame"
bool parseStickerPath(const std::string& path, int& id, std::string& rest) {
  const std::string prefix = "/api/stickers/";
  if (path.rfind(prefix, 0) != 0) return false;
  size_t i = prefix.size();
  size_t slash = path.find('/', i);
  std::string idstr = path.substr(i, slash == std::string::npos ? std::string::npos : slash - i);
  if (idstr.empty()) return false;
  id = atoi(idstr.c_str());
  rest = (slash == std::string::npos) ? "" : path.substr(slash + 1);
  return true;
}

std::string stickerJson(const VirtualSticker& st, double sim_t) {
  const auto& s = st.last_sample;
  const auto& f = st.last_features;
  std::ostringstream o;
  o << "{"
    << "\"id\":" << st.id
    << ",\"name\":\"" << jsonEscape(st.cfg.name) << "\""
    << ",\"face\":\"" << persona::faceName(st.estate.face) << "\""
    << ",\"valence\":" << st.estate.valence
    << ",\"arousal\":" << st.estate.arousal
    << ",\"temp_c\":" << s.temp_c
    << ",\"hum_pct\":" << s.hum_pct
    << ",\"lux\":" << s.lux
    << ",\"vib_peak_g\":" << s.vib_peak_g
    << ",\"idle_hours\":" << f.idle_hours
    << ",\"battery_pct\":" << persona::batteryPercent(s.battery_mv)
    << ",\"battery_mv\":" << s.battery_mv
    << ",\"frame_seq\":" << st.frame_seq
    << ",\"wake_count\":" << st.wake_count
    << ",\"next_wake_in\":" << (st.next_wake_t > sim_t ? st.next_wake_t - sim_t : 0)
    << ",\"sensitivity\":" << st.cfg.sensitivity
    << ",\"temperament\":\"" << persona::temperamentName(st.cfg.temperament) << "\""
    << ",\"report_interval_s\":" << st.cfg.report_interval_s
    << ",\"env\":{"
    << "\"temp_c\":" << st.env_temp_c
    << ",\"hum_pct\":" << st.env_hum_pct
    << ",\"lux_auto\":" << (st.lux_auto ? "true" : "false")
    << ",\"lux_manual\":" << st.lux_manual
    << "}"
    << ",\"thresholds\":{"
    << "\"cold_c\":" << st.cfg.th.cold_c
    << ",\"hot_c\":" << st.cfg.th.hot_c
    << ",\"lonely_h\":" << st.cfg.th.lonely_h
    << "}"
    << "}";
  return o.str();
}

HttpResponse ok(const std::string& body = "{\"ok\":true}") {
  HttpResponse r;
  r.body = body;
  return r;
}

HttpResponse err(int status, const char* msg) {
  HttpResponse r;
  r.status = status;
  r.body = std::string("{\"error\":\"") + msg + "\"}";
  return r;
}

std::string simClock(double t) {
  int day = static_cast<int>(t / 86400.0);
  int hh = static_cast<int>(fmod(t / 3600.0, 24.0));
  int mm = static_cast<int>(fmod(t / 60.0, 60.0));
  char buf[32];
  snprintf(buf, sizeof(buf), "Day %d %02d:%02d", day, hh, mm);
  return buf;
}

}  // namespace

int main(int argc, char** argv) {
  int port = 8080;
  std::string webdir = "web";
  for (int i = 1; i < argc - 1; ++i) {
    if (strcmp(argv[i], "--port") == 0) port = atoi(argv[i + 1]);
    if (strcmp(argv[i], "--web") == 0) webdir = argv[i + 1];
  }

  // シミュレーションスレッド: 実時間100ms刻みで仮想世界を進める
  std::thread([] {
    auto prev = std::chrono::steady_clock::now();
    for (;;) {
      std::this_thread::sleep_for(std::chrono::milliseconds(100));
      auto now = std::chrono::steady_clock::now();
      double dt = std::chrono::duration<double>(now - prev).count();
      prev = now;
      g_world.tick(dt);
    }
  }).detach();

  HttpServer server;

  // UI本体
  server.route("GET", "/", [webdir](const HttpRequest&) {
    std::ifstream in(webdir + "/index.html", std::ios::binary);
    if (!in) {
      return err(404, "web/index.html not found (run from software/simulator/)");
    }
    std::ostringstream ss;
    ss << in.rdbuf();
    HttpResponse r;
    r.content_type = "text/html; charset=utf-8";
    r.body = ss.str();
    return r;
  });

  // 全ステッカーの状態一覧
  server.route("GET", "/api/stickers", [](const HttpRequest&) {
    std::lock_guard<std::mutex> lock(g_world.mu);
    std::ostringstream o;
    o << "{\"sim_time\":" << g_world.sim_t
      << ",\"sim_clock\":\"" << simClock(g_world.sim_t) << "\""
      << ",\"accel\":" << g_world.accel
      << ",\"stickers\":[";
    for (size_t i = 0; i < g_world.stickers.size(); ++i) {
      if (i) o << ",";
      o << stickerJson(g_world.stickers[i], g_world.sim_t);
    }
    o << "]}";
    return ok(o.str());
  });

  // 個別: フレームバッファ取得
  server.route("GET", "/api/stickers/*", [](const HttpRequest& req) {
    int id;
    std::string rest;
    if (!parseStickerPath(req.path, id, rest)) return err(400, "bad path");
    std::lock_guard<std::mutex> lock(g_world.mu);
    VirtualSticker* st = g_world.find(id);
    if (!st) return err(404, "no such sticker");
    if (rest == "frame") {
      std::ostringstream o;
      o << "{\"w\":" << persona::FrameBuffer::kW
        << ",\"h\":" << persona::FrameBuffer::kH
        << ",\"seq\":" << st->frame_seq
        << ",\"bits\":\"" << base64Encode(st->fb.bits, sizeof(st->fb.bits)) << "\"}";
      return ok(o.str());
    }
    return ok(stickerJson(*st, g_world.sim_t));
  });

  // ステッカー追加
  server.route("POST", "/api/stickers", [](const HttpRequest& req) {
    auto m = parseFlatJson(req.body);
    std::string name = "STICKER";
    getStr(m, "name", name);
    std::lock_guard<std::mutex> lock(g_world.mu);
    int id = g_world.addSticker(name);
    return ok("{\"id\":" + std::to_string(id) + "}");
  });

  // 個別への操作: env / shake / config / preset
  server.route("POST", "/api/stickers/*", [](const HttpRequest& req) {
    int id;
    std::string rest;
    if (!parseStickerPath(req.path, id, rest)) return err(400, "bad path");
    auto m = parseFlatJson(req.body);
    std::lock_guard<std::mutex> lock(g_world.mu);
    VirtualSticker* st = g_world.find(id);
    if (!st) return err(404, "no such sticker");

    if (rest == "env") {
      double v;
      bool b;
      if (getNum(m, "temp_c", v)) st->env_temp_c = static_cast<float>(v);
      if (getNum(m, "hum_pct", v)) st->env_hum_pct = static_cast<float>(v);
      if (getNum(m, "lux", v)) st->lux_manual = static_cast<float>(v);
      if (getBool(m, "lux_auto", b)) st->lux_auto = b;
      return ok();
    }
    if (rest == "shake") {
      // strength[g] の振動を注入し、割り込み起床させる(LIS3DH INT相当)
      double strength = 1.5;
      getNum(m, "strength", strength);
      bool sustained = false;
      getBool(m, "sustained", sustained);
      st->pending_vib_g = static_cast<float>(strength);
      st->wake_now = true;
      if (sustained) st->sustained_remaining = 4;
      return ok();
    }
    if (rest == "config") {
      double v;
      std::string s;
      if (getStr(m, "name", s)) {
        snprintf(st->cfg.name, sizeof(st->cfg.name), "%s", s.c_str());
      }
      if (getNum(m, "sensitivity", v)) {
        st->cfg.sensitivity = persona::clampf(static_cast<float>(v), 0.0f, 1.0f);
      }
      if (getStr(m, "temperament", s)) {
        persona::Temperament t;
        if (persona::temperamentFromName(s.c_str(), t)) st->cfg.temperament = t;
      }
      if (getNum(m, "report_interval_s", v) && v >= 5 && v <= 3600) {
        st->cfg.report_interval_s = static_cast<uint16_t>(v);
      }
      if (getNum(m, "cold_c", v)) st->cfg.th.cold_c = static_cast<float>(v);
      if (getNum(m, "hot_c", v)) st->cfg.th.hot_c = static_cast<float>(v);
      if (getNum(m, "lonely_h", v)) st->cfg.th.lonely_h = static_cast<float>(v);
      st->cfg.rev++;
      // 設定変更を即反映するため次の起床を早める
      if (st->next_wake_t > g_world.sim_t + 2.0) st->next_wake_t = g_world.sim_t + 2.0;
      return ok();
    }
    if (rest == "preset") {
      std::string p;
      if (!getStr(m, "preset", p)) return err(400, "preset required");
      g_world.applyPreset(*st, p);
      if (st->next_wake_t > g_world.sim_t + 2.0) st->next_wake_t = g_world.sim_t + 2.0;
      return ok();
    }
    return err(404, "unknown action");
  });

  // 時間加速
  server.route("POST", "/api/time", [](const HttpRequest& req) {
    auto m = parseFlatJson(req.body);
    double v;
    if (getNum(m, "accel", v) && v >= 1 && v <= 86400) {
      std::lock_guard<std::mutex> lock(g_world.mu);
      g_world.accel = v;
    }
    return ok();
  });

  printf("PersonaSticker simulator\n");
  if (!server.run(port)) {
    fprintf(stderr, "failed to start server on port %d\n", port);
    return 1;
  }
  return 0;
}
