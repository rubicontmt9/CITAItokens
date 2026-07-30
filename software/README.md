# software/ — ゲームソフトウェア / Game Software

Unity (C#) で開発するゲーム本体を格納します。
This directory holds the Unity (C#) based game.

## 想定構成 / Expected Layout

Unity Editor でプロジェクトを新規作成すると、以下のようなフォルダがこの直下に生成されます。
When a new project is created in the Unity Editor, folders like the following will be generated directly under this directory.

```
software/
├── Assets/           # スクリプト・シーン・素材など / Scripts, scenes, assets
│   ├── Scripts/
│   ├── Scenes/
│   └── ...
├── Packages/         # パッケージ依存関係 / Package dependencies
├── ProjectSettings/  # プロジェクト設定 / Project settings
└── README.md         # このファイル / This file
```

## セットアップ手順 / Setup

1. Unity Hub で本フォルダ (`software/`) を既存プロジェクトとして開く、または新規プロジェクトを作成しこの場所に配置してください。
   Open this folder (`software/`) as an existing project in Unity Hub, or create a new project and place it here.
2. 使用する Unity バージョンはプロジェクト作成後に `ProjectSettings/ProjectVersion.txt` に記録されます。バージョンが決まったらこの README にも明記してください。
   The Unity version in use will be recorded in `ProjectSettings/ProjectVersion.txt` once the project is created. Note it here once decided.
3. `Assets/Scripts/` 配下は機能単位でサブフォルダを分けることを推奨します(例: `Player/`, `Hardware/`, `UI/`)。
   Under `Assets/Scripts/`, organize by feature into subfolders (e.g. `Player/`, `Hardware/`, `UI/`).

## ハードウェア連携 / Hardware Integration

`hardware/` の自作PCBと通信する処理は `Assets/Scripts/Hardware/` のようなフォルダにまとめ、通信プロトコルが決まり次第ここに追記します。
Code communicating with the custom PCB in `hardware/` should be grouped under a folder such as `Assets/Scripts/Hardware/`; the communication protocol will be documented here once finalized.
