// ゲートウェイ: 初回セットアップ用APモード + キャプティブポータル(ROLE_GATEWAYのみ)
// ルーターのSSID/パスワードが未設定のとき、AP "PersonaSticker-Setup" を立てて
// スマホから入力してもらう。保存後は再起動して通常動作に入る。
#pragma once

namespace fw {

// ポータルを起動する(ノンブロッキング。loopでsetupPortalLoop()を回すこと)
void setupPortalBegin();
void setupPortalLoop();

}  // namespace fw
