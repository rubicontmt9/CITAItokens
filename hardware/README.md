# hardware/ — 自作ハードウェア設計 / Custom Hardware Design

自作PCB(基板)設計一式を格納します。設計ツールは KiCad を想定しています。
This directory holds a custom PCB (circuit board) design, created with KiCad.

> ℹ️ このトラックは `software/` のゲームとは独立した別プロジェクトです。
> ℹ️ This track is a separate project, independent of the game in `software/`.

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

## ソフトウェアとの関係 / Relationship to `software/`

このハードウェアトラックは、`software/` のゲームとは**独立した別プロジェクト**です。両者を接続する通信プロトコル等の連携は現時点では予定していません。方針が変わった場合はこのファイルを更新します。

This hardware track is an **independent project**, separate from the game in `software/`. No communication protocol or integration between them is currently planned. This file will be updated if that changes.
