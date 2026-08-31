// painlessMesh によるメッシュ通信(ノード側の同期サイクル)
#pragma once
#include <Arduino.h>

namespace fw {

// メッシュ認証情報(全ステッカーとゲートウェイで共通)
// 注意: 実運用ではパスワードを変更すること
constexpr char kMeshPrefix[] = "PersonaMesh";
constexpr char kMeshPassword[] = "persona-sticker-mesh";
constexpr uint16_t kMeshPort = 5555;

// ノードの同期サイクル:
//   メッシュに参加 → report をブロードキャスト → config 応答を待つ → 切断
// timeout_ms 以内に config を受信したら config_json_out に格納して true。
// (参加できなかった/応答が無かった場合も false で正常終了する)
bool nodeSyncCycle(const String& report_json, String& config_json_out,
                   uint32_t timeout_ms);

}  // namespace fw
