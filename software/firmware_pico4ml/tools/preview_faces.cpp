// 液晶に出る 80x80 の表情を、実機なしで PC で確かめるためのツール。
// ファームと同じ face_renderer(共有コア)と face_lcd.h(縮め方・色)を使い、表情ごとに PPM を書き出す。
//
//   g++ -std=c++17 -I../../firmware/lib/emotion_core/src -I../src preview_faces.cpp \
//       ../../firmware/lib/emotion_core/src/face_renderer.cpp \
//       ../../firmware/lib/emotion_core/src/persona_features.cpp -o preview_faces
//   ./preview_faces out_dir     → out_dir/face_<name>.ppm(80x80)
#include <stdio.h>
#include <string>

#include "face_lcd.h"
#include "face_renderer.h"
#include "persona_config.h"

using namespace persona;

int main(int argc, char** argv) {
  const std::string dir = argc > 1 ? argv[1] : ".";
  static FrameBuffer fb;
  static uint16_t px[80 * 80];
  for (int i = 0; i < int(Face::Count); ++i) {
    const Face f = Face(i);
    StatusInfo info;
    info.name = "STICKER";
    renderFace(fb, f, info);
    lcdface::faceTo80(fb, lcdface::faceColor(f), px);
    const std::string path = dir + "/face_" + faceName(f) + ".ppm";
    FILE* fp = fopen(path.c_str(), "wb");
    if (!fp) return 1;
    fprintf(fp, "P6\n80 80\n255\n");
    for (uint16_t v : px) {
      const unsigned char rgb[3] = {(unsigned char)(((v >> 11) & 0x1F) << 3),
                                    (unsigned char)(((v >> 5) & 0x3F) << 2),
                                    (unsigned char)((v & 0x1F) << 3)};
      fwrite(rgb, 1, 3, fp);
    }
    fclose(fp);
    printf("%s\n", path.c_str());
  }
  return 0;
}
