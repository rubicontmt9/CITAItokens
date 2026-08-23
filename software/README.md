# software/ — ファームウェア + Web UI / Firmware + Web UI

**PersonaSticker(モノに人格を与えるステッカー)** のソフトウェア一式を格納します。
This directory holds the software for PersonaSticker.

設計の詳細は **[docs/planning/04_firmware_design.md](../docs/planning/04_firmware_design.md)** を参照してください。
See **[docs/planning/04_firmware_design.md](../docs/planning/04_firmware_design.md)** for the design details.

## 想定構成 / Expected Layout

```
software/
├── firmware/           # PlatformIO プロジェクト (ESP32-S3 / Arduino framework)
│   ├── platformio.ini
│   ├── src/            # sensors / emotion_engine / display / mesh_net / web_ui / power_mgr
│   ├── data/           # Web UI アセット (LittleFS: HTML/JS/CSS)
│   └── test/           # 感情エンジンのネイティブユニットテスト
└── README.md           # このファイル / This file
```

## 方針 / Notes

- **開発環境**: PlatformIO + Arduino framework(主要ライブラリ: painlessMesh, GxEPD2, ESPAsyncWebServer, ArduinoJson ほか)
- **ノードとゲートウェイは同一ファームウェア**で、ビルドフラグ / NVS設定で役割を切替えます
- **感情エンジンは純粋ロジックとして分離**し、PC上でネイティブユニットテストを実行できるようにします
- Wi-Fi認証情報などの秘密情報はNVSに保存し、**リポジトリにはコミットしません**
- スマホからの確認・設定は、ゲートウェイが配信するWeb UI(`http://persona.local`)で行います(専用アプリなし)

セットアップ手順は Phase 1 でPlatformIOプロジェクトを作成した際にここへ追記します。
Setup instructions will be added here when the PlatformIO project is created in Phase 1.
