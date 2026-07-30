# hardware/ — 専用ハードウェア設計 / Custom Hardware Design

ゲーム専用の自作PCB(基板)設計一式を格納します。設計ツールは KiCad を想定しています。
This directory holds the custom PCB (circuit board) design for the game's dedicated hardware, designed using KiCad.

## フォルダ構成 / Layout

```
hardware/
├── schematic/  # 回路図 (KiCad .kicad_sch など) / Schematic files
├── pcb/        # 基板レイアウト (KiCad .kicad_pcb など) / PCB layout files
├── bom/        # 部品表 (BOM) / Bill of materials
└── README.md   # このファイル / This file
```

## セットアップ手順 / Setup

1. KiCad で新規プロジェクトを作成し、`schematic/` に回路図、`pcb/` に基板レイアウトを配置してください。
   Create a new KiCad project and place schematics under `schematic/` and PCB layout under `pcb/`.
2. 部品表(BOM)は `bom/` に CSV などの形式で保存してください。
   Save the bill of materials (BOM) under `bom/`, e.g. as CSV.
3. 製造用データ(ガーバーファイル等)を出力する場合は `pcb/gerbers/` のようなサブフォルダにまとめてください。
   When exporting manufacturing data (e.g. Gerber files), group it under a subfolder such as `pcb/gerbers/`.
4. 使用する KiCad のバージョンが決まったらこの README に明記してください。
   Once the KiCad version in use is decided, note it here.

## ソフトウェア連携 / Software Integration

ゲーム本体 (`software/`) との通信インターフェース(シリアル通信のピン配置・プロトコルなど)は、仕様が固まり次第このファイルに追記します。
The communication interface with the game (`software/`) — e.g. serial pinout and protocol — will be documented here once the spec is finalized.
