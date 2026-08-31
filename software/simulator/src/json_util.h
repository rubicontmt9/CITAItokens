// シミュレーター用の最小JSONユーティリティ
// 出力: 文字列連結ベースのライター / 入力: フラットなオブジェクトのみ対応
#pragma once
#include <map>
#include <string>

namespace sim {

// JSON文字列エスケープ("を含む値を安全に埋め込む)
std::string jsonEscape(const std::string& s);

// base64エンコード(フレームバッファ転送用)
std::string base64Encode(const unsigned char* data, size_t len);

// フラットなJSONオブジェクト {"key": value, ...} をパースする。
// 値は string / number / bool のみ対応(ネストは不可)。
// 文字列値はアンエスケープ済み、それ以外は生テキストで返す。
std::map<std::string, std::string> parseFlatJson(const std::string& body);

// parseFlatJson結果からの取り出しヘルパー
bool getStr(const std::map<std::string, std::string>& m, const char* key,
            std::string& out);
bool getNum(const std::map<std::string, std::string>& m, const char* key,
            double& out);
bool getBool(const std::map<std::string, std::string>& m, const char* key,
             bool& out);

}  // namespace sim
