#include "json_util.h"

#include <stdio.h>
#include <stdlib.h>

namespace sim {

std::string jsonEscape(const std::string& s) {
  std::string out;
  out.reserve(s.size() + 8);
  for (unsigned char c : s) {
    switch (c) {
      case '"': out += "\\\""; break;
      case '\\': out += "\\\\"; break;
      case '\n': out += "\\n"; break;
      case '\r': out += "\\r"; break;
      case '\t': out += "\\t"; break;
      default:
        if (c < 0x20) {
          char buf[8];
          snprintf(buf, sizeof(buf), "\\u%04x", c);
          out += buf;
        } else {
          out += static_cast<char>(c);
        }
    }
  }
  return out;
}

std::string base64Encode(const unsigned char* data, size_t len) {
  static const char tbl[] =
      "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  std::string out;
  out.reserve((len + 2) / 3 * 4);
  for (size_t i = 0; i < len; i += 3) {
    unsigned v = static_cast<unsigned>(data[i]) << 16;
    if (i + 1 < len) v |= static_cast<unsigned>(data[i + 1]) << 8;
    if (i + 2 < len) v |= data[i + 2];
    out += tbl[(v >> 18) & 63];
    out += tbl[(v >> 12) & 63];
    out += (i + 1 < len) ? tbl[(v >> 6) & 63] : '=';
    out += (i + 2 < len) ? tbl[v & 63] : '=';
  }
  return out;
}

namespace {

void skipWs(const std::string& s, size_t& i) {
  while (i < s.size() && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) ++i;
}

bool parseString(const std::string& s, size_t& i, std::string& out) {
  if (i >= s.size() || s[i] != '"') return false;
  ++i;
  out.clear();
  while (i < s.size()) {
    char c = s[i++];
    if (c == '"') return true;
    if (c == '\\' && i < s.size()) {
      char e = s[i++];
      switch (e) {
        case 'n': out += '\n'; break;
        case 't': out += '\t'; break;
        case 'r': out += '\r'; break;
        case 'u':
          // \uXXXX はASCII範囲のみ対応(それ以外は '?')
          if (i + 4 <= s.size()) {
            unsigned code = static_cast<unsigned>(strtoul(s.substr(i, 4).c_str(), nullptr, 16));
            out += (code < 0x80) ? static_cast<char>(code) : '?';
            i += 4;
          }
          break;
        default: out += e;
      }
    } else {
      out += c;
    }
  }
  return false;
}

}  // namespace

std::map<std::string, std::string> parseFlatJson(const std::string& body) {
  std::map<std::string, std::string> m;
  size_t i = 0;
  skipWs(body, i);
  if (i >= body.size() || body[i] != '{') return m;
  ++i;
  for (;;) {
    skipWs(body, i);
    if (i >= body.size()) break;
    if (body[i] == '}') break;
    std::string key;
    if (!parseString(body, i, key)) break;
    skipWs(body, i);
    if (i >= body.size() || body[i] != ':') break;
    ++i;
    skipWs(body, i);
    if (i >= body.size()) break;
    if (body[i] == '"') {
      std::string val;
      if (!parseString(body, i, val)) break;
      m[key] = val;
    } else {
      // number / true / false / null を区切り文字まで読む
      size_t start = i;
      while (i < body.size() && body[i] != ',' && body[i] != '}' &&
             body[i] != ' ' && body[i] != '\n' && body[i] != '\r' &&
             body[i] != '\t')
        ++i;
      m[key] = body.substr(start, i - start);
    }
    skipWs(body, i);
    if (i < body.size() && body[i] == ',') { ++i; continue; }
    break;
  }
  return m;
}

bool getStr(const std::map<std::string, std::string>& m, const char* key,
            std::string& out) {
  auto it = m.find(key);
  if (it == m.end()) return false;
  out = it->second;
  return true;
}

bool getNum(const std::map<std::string, std::string>& m, const char* key,
            double& out) {
  auto it = m.find(key);
  if (it == m.end()) return false;
  char* end = nullptr;
  double v = strtod(it->second.c_str(), &end);
  if (end == it->second.c_str()) return false;
  out = v;
  return true;
}

bool getBool(const std::map<std::string, std::string>& m, const char* key,
             bool& out) {
  auto it = m.find(key);
  if (it == m.end()) return false;
  if (it->second == "true" || it->second == "1") { out = true; return true; }
  if (it->second == "false" || it->second == "0") { out = false; return true; }
  return false;
}

}  // namespace sim
