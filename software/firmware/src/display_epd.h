// 電子ペーパー(GDEH0154D67 / Waveshare 1.54" V2)表示
#pragma once
#include "face_renderer.h"

namespace fw {

// フレームバッファを表示する。
// full_refresh=true で全面リフレッシュ(残像防止、約2秒)、
// false で部分書き換え(約0.3秒)。表示後はパネルをhibernateさせる。
void displayShow(const persona::FrameBuffer& fb, bool full_refresh);

}  // namespace fw
