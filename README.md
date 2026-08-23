# CITAItokens — PersonaSticker

> 🏷️ **モノに人格を与えるステッカー** を開発するプロジェクトです。
> A project developing "PersonaSticker" — a smart sticker that gives objects a personality.

貼り付けた対象物(冷蔵庫・観葉植物・ギターケースなど)の温度・振動・照度をセンサーで観測して状況を推察し、**感情を生成して電子ペーパー画面に表情として表示**します。複数のステッカーは **Wi-Fiメッシュネットワーク** を形成し、ゲートウェイ経由でオンライン接続。**スマホのブラウザ** から確認・設定ができます。

このリポジトリは、開発者(あなた)と Claude (AI) が共同作業するための専用環境です。
This repository is a dedicated workspace for collaboration between the owner and Claude (AI).

---

## 📁 リポジトリ構成 / Repository Structure

```
CITAItokens/
├── docs/
│   └── planning/   # プランニングドキュメント(要求仕様〜ロードマップ)
│                   # Planning documents (requirements → roadmap)
├── software/       # ファームウェア (PlatformIO / ESP32-S3) + Web UI
│                   # Firmware (PlatformIO / ESP32-S3) + Web UI
├── hardware/       # カスタムPCB設計 (KiCad)
│                   # Custom PCB design (KiCad)
└── README.md       # この案内ファイル / This guide file
```

| フォルダ / Folder | 内容 / Contents |
| --- | --- |
| [`docs/planning/`](./docs/planning/01_requirements.md) | 要求仕様・システム構成・HW/FW設計・ロードマップ / Requirements, architecture, HW/FW design, roadmap |
| [`software/`](./software/README.md) | ファームウェア + Web UI / Firmware + Web UI |
| [`hardware/`](./hardware/README.md) | カスタムPCB設計(KiCad)/ Custom PCB design (KiCad) |

## 📐 主要な技術選定 / Key Decisions

| 項目 | 決定 | 理由 |
| --- | --- | --- |
| 表示 | 電子ペーパー 1.54" (200×200) | 薄い・省電力・スリープ中も表情が残る |
| MCU / メッシュ | ESP32-S3 + Wi-Fiメッシュ (painlessMesh) | 実績・情報量、BLE併用可 |
| 進め方 | 開発ボード試作 → カスタムPCB (KiCad) | 手戻り最小化 |
| スマホ連携 | Wi-Fi + Webアプリ(ゲートウェイ内蔵) | アプリ開発不要、iOS/Android両対応 |

詳細は [docs/planning/](./docs/planning/01_requirements.md) を参照してください。

## 🗂️ 開発の進め方 / Workflow Notes

- 作業ブランチは `claude/*` 系のブランチで進行します。 / Work happens on `claude/*` branches.
- 大きな設計判断や技術選定はコミットメッセージまたはPR説明に理由を残します。 / Major design decisions and technology choices are recorded in commit messages or PR descriptions.
- 実測・実機確認で判明した事実は planning ドキュメントへ随時反映します。 / Facts learned from real measurements are reflected back into the planning docs.

## 📌 ステータス / Status(ロードマップ: [05_roadmap.md](./docs/planning/05_roadmap.md))

- [x] Phase 0: プランニングドキュメント / Planning documents
- [ ] Phase 1: 単体試作(センサー→感情→表情表示)/ Single-node prototype
- [ ] Phase 2: メッシュ + ゲートウェイ + Webアプリ / Mesh + gateway + web app
- [ ] Phase 3: 電力最適化(電池2週間)/ Power optimization
- [ ] Phase 4: カスタムPCB + ステッカー筐体 / Custom PCB + sticker enclosure
- [ ] Phase 5: クラウド連携(任意)/ Cloud connectivity (optional)
