# simulator/ — PersonaSticker PCシミュレーター

実機がなくても、ファームウェアと**同一のC++感情エンジン・表情描画コード**
(`../firmware/lib/emotion_core/`)をPC上で動かし、ブラウザから仮想ステッカーを
操作・観察できるシミュレーターです。

![動作イメージ] ブラウザに電子ペーパー風の画面が並び、温度スライダーや
「衝撃!」ボタンで表情が変わります。時間加速で「48時間放置→さみしい」や
電池の減りも数秒で確認できます。

## 実行手順(Windows / WSL2)

WSL2 (Ubuntu) を使います。未導入の場合は PowerShell(管理者)で `wsl --install` →
再起動 → Ubuntuの初期設定を済ませてください。

```bash
# WSL2 (Ubuntu) のターミナルで
sudo apt update && sudo apt install -y g++ make git

git clone https://github.com/rubicontmt9/CITAItokens.git   # 初回のみ
cd CITAItokens/software/simulator
make
./persona_sim
```

Windows側のブラウザで **http://localhost:8080** を開きます
(WSL2のlocalhostはWindowsから自動でアクセスできます)。

- ポートを変える場合: `./persona_sim --port 9000`
- 終了: ターミナルで `Ctrl+C`

macOS/Linuxでも同じ手順で動きます(macOSは `xcode-select --install` でclang++を導入)。

## できること

| 操作 | 対応する現実のイベント |
| --- | --- |
| 温度・湿度・照度スライダー | 環境の変化(照度は「昼夜自動」も可) |
| なでる | 微振動(valence上昇 → うれしい) |
| 衝撃! | 単発の強い振動(LIS3DH割り込み起床 → びっくり) |
| ゆらし続ける | 連続する強い振動(→ こわい) |
| プリセット | 冷蔵庫(寒い)/ 観葉植物 / ギターケース(暗所・こわがり) |
| 名前・敏感さ・気質・起床周期 | ゲートウェイWeb UIでの設定変更に相当 |
| 時間加速(×1〜×3600) | 放置(さみしい)や電池消費を短時間で観察 |

各カードには電子ペーパーの実描画(200×200・1bit、FWと同一のレンダラー出力)、
Valence-Arousal平面上の現在の感情、電池残量(docs/planning/03 の電力収支モデル)が
表示されます。

## 構成

```
simulator/
├── Makefile          # g++一発ビルド(emotion_coreのソースを直接コンパイル)
├── src/
│   ├── main.cpp      # 起動・APIルーティング
│   ├── http_server.* # 依存ゼロの極小HTTPサーバー(localhost限定)
│   ├── sim_world.*   # 仮想世界(起床サイクル・昼夜・電池モデル)
│   └── json_util.*   # 最小JSON入出力
└── web/index.html    # ブラウザUI
```

シミュレーターと実機の間にロジックの二重管理はありません。感情の挙動を変えたい
ときは `../firmware/lib/emotion_core/` を変更すれば、FW・シミュレーター・テストの
すべてに反映されます。

## API(参考)

| メソッド | パス | 内容 |
| --- | --- | --- |
| GET | `/api/stickers` | 全ステッカー状態(仮想時刻・加速率含む) |
| GET | `/api/stickers/{id}/frame` | 表示中フレームバッファ(base64) |
| POST | `/api/stickers` | ステッカー追加 `{"name":"..."}` |
| POST | `/api/stickers/{id}/env` | 環境変更 `{"temp_c":5}` など |
| POST | `/api/stickers/{id}/shake` | 振動注入 `{"strength":1.6,"sustained":false}` |
| POST | `/api/stickers/{id}/config` | 性格・設定変更 |
| POST | `/api/stickers/{id}/preset` | プリセット適用 `{"preset":"fridge"}` |
| POST | `/api/time` | 時間加速 `{"accel":3600}` |
