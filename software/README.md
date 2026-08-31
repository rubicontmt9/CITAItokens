# software/ — ファームウェア + シミュレーター / Firmware + Simulator

**PersonaSticker(モノに人格を与えるステッカー)** のソフトウェア一式です。
設計の詳細は [docs/planning/04_firmware_design.md](../docs/planning/04_firmware_design.md) を参照。

## 構成 / Layout

```
software/
├── firmware/    # ESP32-S3 ファームウェア (PlatformIO / Arduino framework)
│   ├── lib/emotion_core/   # ★共有コア: 感情エンジン+表情描画(純粋C++)
│   ├── src/                # センサー・表示・スリープ・メッシュ・ゲートウェイ
│   ├── data/               # ゲートウェイWeb UI (LittleFS)
│   └── test/               # 共有コアのユニットテスト (pio test -e native)
└── simulator/   # PCシミュレーター(共有コアをそのまま実行、ブラウザUI)
```

## 使い分け

| やりたいこと | 場所 |
| --- | --- |
| 実機なしで感情・表情の挙動を見る/調整する | [`simulator/`](./simulator/README.md) — `make && ./persona_sim` |
| 実機(XIAO ESP32S3)へ書き込む | [`firmware/`](./firmware/README.md) — `pio run -e node -t upload` |
| 感情エンジンや表情デザインを変更する | `firmware/lib/emotion_core/` — **FW・シミュレーター・テスト全部に反映される** |
| ロジックの回帰テスト | `firmware/` で `pio test -e native` |

## 方針 / Notes

- **共有コア(emotion_core)はArduino API禁止の純粋C++17**。プラットフォーム依存
  (センサーI/O、時刻、乱数)はすべて呼び出し側から値として渡す
- ノードとゲートウェイは同一ファームウェアで、ビルド環境(`env:node` / `env:gateway`)で役割を切替
- Wi-Fi認証情報などの秘密情報はNVSに保存し、**リポジトリにはコミットしない**
- スマホからの確認・設定は、ゲートウェイが配信するWeb UI(`http://persona.local`)で行う(専用アプリなし)
