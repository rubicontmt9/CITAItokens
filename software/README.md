# software/ — ゲームソフトウェア / Game Software

Unity (C#) で開発するゲーム本体を格納します。
This directory holds the Unity (C#) based game.

屋外で自然物を撮影し、AIがステータスを振ったカードを生成して対戦するスマホゲームです。企画・技術方針・フェーズ計画の詳細は [`../docs/game-mvp-plan.md`](../docs/game-mvp-plan.md) を参照してください。

A smartphone game: photograph nature outdoors, an AI generates a battle card with stats, and you battle with it. See [`../docs/game-mvp-plan.md`](../docs/game-mvp-plan.md) for the concept, technical decisions, and phased plan.

## 想定構成 / Expected Layout

Unity Editor でプロジェクトを新規作成すると、以下のようなフォルダがこの直下に生成されます。
When a new project is created in the Unity Editor, folders like the following will be generated directly under this directory.

```
software/
├── Assets/
│   ├── Scripts/
│   │   ├── Camera/   # ネイティブカメラ連携、撮影フロー / Native camera capture flow
│   │   ├── AI/       # カード生成プロキシのAPIクライアント / Card-generation proxy client
│   │   ├── Card/     # Cardモデル、属性相性 / Card model, type advantage
│   │   ├── Data/     # ローカル永続化 / Local persistence
│   │   ├── Battle/   # ターン制バトル / Turn-based battle
│   │   ├── UI/       # 各シーンのUI / Per-scene UI controllers
│   │   └── Core/     # シーン遷移・設定 / Scene flow, config
│   └── Scenes/       # Title, Capture, CardResult, Collection, Battle, Result
├── Packages/         # パッケージ依存関係 / Package dependencies
├── ProjectSettings/  # プロジェクト設定 / Project settings
└── README.md         # このファイル / This file
```

## セットアップ手順 / Setup

1. Unity Hub で本フォルダ (`software/`) を既存プロジェクトとして開く、または新規プロジェクトを作成しこの場所に配置してください。
   Open this folder (`software/`) as an existing project in Unity Hub, or create a new project and place it here.
2. Unity バージョンは **Unity 6 (6000.x)** を使用します。プロジェクト作成後、実際のバージョンが `ProjectSettings/ProjectVersion.txt` に記録されるので、確定したらこの README にも明記してください。
   **Unity 6 (6000.x)** is the version in use. The actual version is recorded in `ProjectSettings/ProjectVersion.txt` once the project is created; note it here when confirmed.
   ⚠️ Unity 6 では **Player Settings → Active Input Handling を `Both`** にする必要があります。既定の新 Input System のみだとボタンが反応せず、位置情報も取得できない可能性があります(詳細は [`../docs/setup-unity.md`](../docs/setup-unity.md))。
   ⚠️ On Unity 6, **Player Settings → Active Input Handling must be `Both`** — with the default new-Input-System-only setting, buttons do not respond and location reads may fail. See [`../docs/setup-unity.md`](../docs/setup-unity.md).
3. ビルド対象は **Android を優先**します(実機での反復が速いため)。iOS は後追いです。
   Build target is **Android first** (faster on-device iteration); iOS comes later.
4. 必要パッケージ: ネイティブカメラ連携用パッケージ、`Newtonsoft.Json`。
   Required packages: a native-camera plugin, and `Newtonsoft.Json`.
5. 必要パーミッション: `CAMERA`, `INTERNET`(位置情報チェックを有効にする場合は `ACCESS_FINE_LOCATION`)。
   Required permissions: `CAMERA`, `INTERNET` (plus `ACCESS_FINE_LOCATION` if the location check is enabled).

## AIカード生成 / AI Card Generation

撮影した写真からカードのステータスを生成する処理は、**AI APIを直接呼ばず、小さなプロキシサービス経由**で行います。クライアントにAPIキーを埋め込むと、ビルド済みアプリから抽出できてしまうためです。

Card stats are generated from the captured photo via a **small proxy service — the client never calls the AI API directly**, because an API key embedded in the client can be extracted from a built app.

- プロキシのURLはリポジトリにコミットしない設定ファイル(または `ScriptableObject`)経由でクライアントに渡します。
  The proxy URL is supplied to the client via a config file (or `ScriptableObject`) that is **not** committed to the repository.
- **APIキーをこのリポジトリにコミットしないでください。** キーはプロキシ側の環境変数/シークレットにのみ保持します。
  **Never commit API keys to this repository.** Keys live only in the proxy's environment variables/secrets.
- 撮影自体はオフラインで完結します。カード生成は通信が必要なため、失敗時のリトライUIを用意します。
  Capture works fully offline; card generation requires connectivity, so a retry UI is provided for failures.

詳細は [`../docs/game-mvp-plan.md`](../docs/game-mvp-plan.md) の該当セクションを参照してください。
See the relevant sections of [`../docs/game-mvp-plan.md`](../docs/game-mvp-plan.md) for details.

## ハードウェアとの関係 / Relationship to `hardware/`

`hardware/` の自作PCBは本ゲームとは独立した別プロジェクトであり、連携処理は実装しません。
The custom PCB in `hardware/` is a separate project, independent of this game; no integration code is planned.
