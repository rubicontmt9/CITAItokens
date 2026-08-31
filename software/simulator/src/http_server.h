// 依存ゼロの極小HTTPサーバー(シミュレーター専用・localhost限定)
// GET/POSTと小さなJSON/HTMLの応答だけを想定した実装。
#pragma once
#include <functional>
#include <string>
#include <vector>

namespace sim {

struct HttpRequest {
  std::string method;
  std::string path;   // クエリ文字列除去済み
  std::string query;
  std::string body;
};

struct HttpResponse {
  int status = 200;
  std::string content_type = "application/json; charset=utf-8";
  std::string body;
};

using Handler = std::function<HttpResponse(const HttpRequest&)>;

class HttpServer {
 public:
  // pathの末尾が '*' なら前方一致、それ以外は完全一致
  void route(const std::string& method, const std::string& path, Handler h);
  // 127.0.0.1:port で待ち受け(ブロッキング)。bind失敗などでfalse。
  bool run(int port);

 private:
  struct Route {
    std::string method;
    std::string path;
    bool prefix;
    Handler handler;
  };
  std::vector<Route> routes_;
  void handleClient(int fd);
};

}  // namespace sim
