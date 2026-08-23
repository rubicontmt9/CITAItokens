# hardware/ — カスタムPCB設計 / Custom Hardware Design

**PersonaSticker(モノに人格を与えるステッカー)** のカスタムPCB(基板)設計一式を格納します。設計ツールは KiCad を想定しています(使用バージョンは着手時にここへ明記)。
This directory holds the custom PCB design for PersonaSticker, designed using KiCad (note the KiCad version here once decided).

設計の前提・BOM・電力収支・PCB化方針は **[docs/planning/03_hardware_design.md](../docs/planning/03_hardware_design.md)** を参照してください。
See **[docs/planning/03_hardware_design.md](../docs/planning/03_hardware_design.md)** for design assumptions, BOM, power budget, and the custom-PCB plan.

## フォルダ構成 / Layout

```
hardware/
├── schematic/  # 回路図 (KiCad .kicad_sch など) / Schematic files
├── pcb/        # 基板レイアウト (.kicad_pcb)。製造データは pcb/gerbers/ へ
├── bom/        # 部品表 (BOM, CSV) / Bill of materials
└── README.md   # このファイル / This file
```

## フェーズ / Phasing

- **Phase 1〜3**: 市販の開発ボード(Seeed XIAO ESP32S3)+ モジュールで試作。**このフォルダはまだ使いません**
- **Phase 4**: 仕様確定後、KiCadでカスタムPCBを設計(ESP32-S3-WROOM-1 直載せ、FPC直結電子ペーパー、薄型LiPo。目標 55×45mm・厚10mm以下)

ロードマップ: [docs/planning/05_roadmap.md](../docs/planning/05_roadmap.md)

## 設計ルール / Notes

- 無線モジュールは**技適取得済みの ESP32-S3-WROOM-1** を使用する(アンテナは自作しない)
- 製造用データ(ガーバー等)は `pcb/gerbers/` にまとめて出力する
- ファームウェア側(`software/firmware/`)とのピン割当は planning ドキュメントの表を正とし、変更時は両方を更新する
