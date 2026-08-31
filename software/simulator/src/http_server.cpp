#include "http_server.h"

#include <arpa/inet.h>
#include <netinet/in.h>
#include <string.h>
#include <sys/socket.h>
#include <unistd.h>

#include <cstdio>
#include <thread>

namespace sim {

void HttpServer::route(const std::string& method, const std::string& path,
                       Handler h) {
  bool prefix = !path.empty() && path.back() == '*';
  std::string p = prefix ? path.substr(0, path.size() - 1) : path;
  routes_.push_back({method, p, prefix, std::move(h)});
}

bool HttpServer::run(int port) {
  int fd = socket(AF_INET, SOCK_STREAM, 0);
  if (fd < 0) return false;
  int one = 1;
  setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &one, sizeof(one));

  sockaddr_in addr{};
  addr.sin_family = AF_INET;
  addr.sin_port = htons(static_cast<uint16_t>(port));
  addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);  // localhost限定
  if (bind(fd, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) < 0) {
    perror("bind");
    close(fd);
    return false;
  }
  if (listen(fd, 16) < 0) {
    close(fd);
    return false;
  }
  printf("listening on http://localhost:%d\n", port);
  fflush(stdout);

  for (;;) {
    int cfd = accept(fd, nullptr, nullptr);
    if (cfd < 0) continue;
    std::thread(&HttpServer::handleClient, this, cfd).detach();
  }
}

static bool readRequest(int fd, HttpRequest& req) {
  std::string data;
  char buf[4096];
  size_t header_end = std::string::npos;
  // ヘッダー終端まで読む
  while (header_end == std::string::npos) {
    ssize_t n = recv(fd, buf, sizeof(buf), 0);
    if (n <= 0) return false;
    data.append(buf, static_cast<size_t>(n));
    header_end = data.find("\r\n\r\n");
    if (data.size() > 1 << 20) return false;  // 1MB上限
  }

  // リクエストライン
  size_t line_end = data.find("\r\n");
  std::string line = data.substr(0, line_end);
  size_t sp1 = line.find(' ');
  size_t sp2 = line.find(' ', sp1 + 1);
  if (sp1 == std::string::npos || sp2 == std::string::npos) return false;
  req.method = line.substr(0, sp1);
  std::string target = line.substr(sp1 + 1, sp2 - sp1 - 1);
  size_t q = target.find('?');
  req.path = target.substr(0, q);
  if (q != std::string::npos) req.query = target.substr(q + 1);

  // Content-Length
  size_t content_length = 0;
  std::string headers = data.substr(0, header_end);
  for (size_t pos = headers.find("\r\n"); pos != std::string::npos;) {
    size_t next = headers.find("\r\n", pos + 2);
    std::string h = headers.substr(pos + 2, (next == std::string::npos ? headers.size() : next) - pos - 2);
    if (h.size() > 15) {
      std::string lower;
      for (char c : h) lower += static_cast<char>(tolower(static_cast<unsigned char>(c)));
      if (lower.rfind("content-length:", 0) == 0) {
        content_length = static_cast<size_t>(atol(h.c_str() + 15));
      }
    }
    pos = next;
  }
  if (content_length > 1 << 20) return false;

  // ボディ
  std::string body = data.substr(header_end + 4);
  while (body.size() < content_length) {
    ssize_t n = recv(fd, buf, sizeof(buf), 0);
    if (n <= 0) return false;
    body.append(buf, static_cast<size_t>(n));
  }
  req.body = body.substr(0, content_length);
  return true;
}

static void writeResponse(int fd, const HttpResponse& res) {
  const char* status_text = res.status == 200 ? "OK"
                            : res.status == 404 ? "Not Found"
                            : res.status == 400 ? "Bad Request"
                                                : "Error";
  char header[512];
  int n = snprintf(header, sizeof(header),
                   "HTTP/1.1 %d %s\r\n"
                   "Content-Type: %s\r\n"
                   "Content-Length: %zu\r\n"
                   "Cache-Control: no-store\r\n"
                   "Connection: close\r\n\r\n",
                   res.status, status_text, res.content_type.c_str(),
                   res.body.size());
  send(fd, header, static_cast<size_t>(n), 0);
  size_t sent = 0;
  while (sent < res.body.size()) {
    ssize_t w = send(fd, res.body.data() + sent, res.body.size() - sent, 0);
    if (w <= 0) break;
    sent += static_cast<size_t>(w);
  }
}

void HttpServer::handleClient(int fd) {
  HttpRequest req;
  if (readRequest(fd, req)) {
    HttpResponse res;
    bool matched = false;
    for (const auto& r : routes_) {
      bool path_ok = r.prefix ? (req.path.rfind(r.path, 0) == 0)
                              : (req.path == r.path);
      if (r.method == req.method && path_ok) {
        res = r.handler(req);
        matched = true;
        break;
      }
    }
    if (!matched) {
      res.status = 404;
      res.body = "{\"error\":\"not found\"}";
    }
    writeResponse(fd, res);
  }
  close(fd);
}

}  // namespace sim
