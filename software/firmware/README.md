# firmware/ — PersonaSticker ファームウェア

XIAO ESP32S3 向けのファームウェアです(PlatformIO + Arduino framework)。
設計の詳細は [docs/planning/04_firmware_design.md](../../docs/planning/04_firmware_design.md) を参照。

## 構成

```
firmware/
├── platformio.ini        # env:node / env:gateway / env:native(テスト)
├── lib/emotion_core/     # 共有コア(純粋C++。シミュレーターとテストも共用)
│   └── src/
│       ├── persona_config.h    # 設定・閾値・Face/Temperament定義
│       ├── sensor_sample.h     # センサースナップショット
│       ├── persona_features.*  # 特徴量抽出
│       ├── emotion_engine.*    # Valence-Arousalモデル → 表情選択
│       └── face_renderer.*     # 200x200 1bpp 表情の手続き描画
├── src/                  # ESP32側(Arduino)
│   ├── main.cpp          # ノードの起床サイクル / ゲートウェイ分岐
│   ├── pins.h            # ピン割当(docs/planning/03の表と一致させる)
│   ├── sensors.*         # AHT20 / LIS3DH / BH1750
│   ├── display_epd.*     # GxEPD2(GDEH0154D67)
│   ├── power_mgr.*       # ディープスリープ・RTCメモリ
│   ├── config_store.*    # NVS永続化
│   ├── net_protocol.*    # report/config JSON(ArduinoJson)
│   ├── mesh_net.*        # painlessMesh(ノード同期サイクル)
│   └── gateway/          # ゲートウェイ専用(-DROLE_GATEWAY時のみ有効)
│       ├── gateway_main.cpp    # メッシュルート+ルーターブリッジ+NTP/mDNS
│       ├── state_registry.*    # 全ノード状態・設定の保持
│       ├── web_server.*        # Web UI/REST/WebSocket
│       └── setup_portal.*      # 初回Wi-Fi設定AP
├── data/index.html       # ゲートウェイWeb UI(LittleFSへ書き込む)
└── test/test_emotion/    # 共有コアのユニットテスト(native)
```

## ビルド・書き込み(実機)

PlatformIO(VSCode拡張 または `pip install platformio`)を用意して:

```bash
cd software/firmware

# ノード(電池駆動のステッカー)
pio run -e node -t upload

# ゲートウェイ(USB常時給電。1台だけ)
pio run -e gateway -t upload
pio run -e gateway -t uploadfs     # Web UI(data/)をLittleFSへ

# 共有コアのユニットテスト(PC上で実行)
pio test -e native
```

> 注意: この開発環境(クラウドセッション)ではPlatformIOレジストリへの接続が
> ネットワークポリシーで遮断されているため、ESP32向けビルドは未実施です。
> 共有コアはg++でのコンパイルと15件のユニットテスト、およびシミュレーターで
> 検証済みですが、**ESP32側コード(src/)は初回ビルド時にライブラリAPIの
> 差異による修正が必要になる可能性があります**。

## 初回セットアップの流れ

1. ゲートウェイに書き込み(`gateway` + `uploadfs`)、USB給電で起動
2. 未設定時はAP `PersonaSticker-Setup` が立つ → スマホで接続し、家のWi-Fi情報を入力
3. 再起動後、ゲートウェイがメッシュルート+ルーターブリッジとして稼働
4. ノードに書き込み(`node`)。起動すると自動でメッシュに参加して報告を始める
5. スマホのブラウザで `http://persona.local`(同一LAN)を開く

## 動作の要点

- **ノードは常時ディープスリープ**。起床周期(デフォルト120秒)ごとに
  センシング→感情更新→(表情が変われば)表示→メッシュ同期→再スリープ
- **LIS3DHの動作割り込み(GPIO8)で即時起床**(「びっくり」の即応)
- 設定(名前・性格・閾値)は**ゲートウェイが正**。ノードは報告への応答として
  受け取り、revが新しいときだけNVSに保存して適用する
- メッシュ認証情報は `src/mesh_net.h` の `kMeshPrefix` / `kMeshPassword`。
  **実運用では必ず変更すること**
- 電池運用で電力を詰める際は `Serial` 出力の無効化を検討(main.cpp)

## 既知の未確定事項(実機で確認)

- XIAO ESP32S3の実際のディープスリープ電流(→ docs/planning/03 の電力収支を実測で更新)
- GPIO8(LIS3DH INT)でのext0起床の実機確認
- painlessMesh 1.5系とarduino-esp32コアの組み合わせ(espressif32@6.9.0で固定済み)
- 電池電圧ADC(GPIO4 + 100k:100k分圧)の実装(基板外付け)
