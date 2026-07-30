# CITAItokens

> 🎮 ゲームソフトウェア と 🔧 自作ハードウェアを、それぞれ独立したトラックとして開発するプロジェクトです。
> A project developing a game (software) and a custom hardware design as two independent tracks.

## 🌳 ゲームについて / About the Game

屋外で木の枝などの自然物を撮影すると、その写真からステータスを振った「カード」が生成され、そのカードで対戦するスマホゲームです。人を外に出し、運動や自然との触れ合いのきっかけを作ることを目的にしています。生成処理は端末内で完結させ、クラウドには繋ぎません。

A smartphone game where you photograph a tree branch (or other natural object) outdoors, the photo produces a battle card with stats, and you battle with that card. The goal is to get people outside, moving, and in contact with nature. Generation runs on-device — nothing is sent to the cloud.

撮った枝は「武器」になり、**武器のジャンル**と**属性**がステータスに補正をかけます。

The branch you photograph becomes a **weapon**; its **genre** and **element** modify its stats.

| 文書 / Document | 内容 / Contents |
| --- | --- |
| [`docs/game-design.md`](./docs/game-design.md) | **何を作るか** — 体験の核、進行、カード生成の骨格、屋外要素、アート要件、安全・プライバシー |
| [`docs/game-mvp-plan.md`](./docs/game-mvp-plan.md) | **どう作るか** — 技術方針、実装状況と残作業、作業順序 |
| [`docs/setup-unity.md`](./docs/setup-unity.md) | Unity プロジェクトのセットアップ手順 |

このリポジトリは、開発者(あなた)と Claude (AI) が共同作業するための専用環境です。
This repository is a dedicated workspace for collaboration between the owner and Claude (AI).

---

## 📁 リポジトリ構成 / Repository Structure

```
CITAItokens/
├── software/   # ゲーム本体 (Unity / C#)
│               # The game itself (Unity / C#)
├── hardware/   # 自作PCB設計 (KiCad) ※ゲームとは独立した別プロジェクト
│               # Custom PCB design (KiCad) — a separate project, independent of the game
├── docs/       # 設計・実装計画ドキュメント / Design and planning documents
└── README.md   # この案内ファイル / This guide file
```

| フォルダ / Folder | 内容 / Contents | 詳細 / Details |
| --- | --- | --- |
| [`software/`](./software/README.md) | Unity 製ゲーム本体 / Unity-based game | [software/README.md](./software/README.md) |
| [`hardware/`](./hardware/README.md) | 自作PCB設計(ゲームとは独立) / Custom PCB design (independent of the game) | [hardware/README.md](./hardware/README.md) |
| [`docs/`](./docs/game-design.md) | ゲームデザインと実装計画 / Game design and implementation plan | [docs/game-design.md](./docs/game-design.md) |

---

## 🚀 はじめに / Getting Started

1. ゲームソフトウェアの開発は [`software/`](./software/README.md) を参照してください。実装計画は [`docs/game-mvp-plan.md`](./docs/game-mvp-plan.md) にまとまっています。
   For game software development, see [`software/`](./software/README.md). The implementation plan lives in [`docs/game-mvp-plan.md`](./docs/game-mvp-plan.md).
2. ハードウェア設計の開発は [`hardware/`](./hardware/README.md) を参照してください。
   For hardware design development, see [`hardware/`](./hardware/README.md).
3. `software/` と `hardware/` は**現時点では互いに独立した別プロジェクト**です。両者を接続する通信プロトコル等の連携仕様は予定していません。方針が変わった場合はこのREADMEを更新します。
   `software/` and `hardware/` are currently **independent projects with no planned integration**. There is no shared communication protocol between them. This README will be updated if that changes.

## 🗂️ 開発の進め方 / Workflow Notes

- 作業ブランチは `claude/*` 系のブランチで進行します。 / Work happens on `claude/*` branches.
- 大きな設計判断や技術選定はコミットメッセージまたはPR説明に理由を残します。 / Major design decisions and technology choices are recorded in commit messages or PR descriptions.
- 現時点では初期の雛形段階です。進捗はこのREADMEを随時更新して共有します。 / This is currently the initial scaffolding stage. Progress will be reflected here as it happens.

## 📌 ステータス / Status

- [x] リポジトリ雛形構築 / Initial repository scaffolding
- [x] ゲーム企画・MVP実装計画の策定 / Game concept and MVP implementation plan
- [ ] Phase 0: Unity プロジェクト作成・シーン遷移の雛形 / Unity project setup and scene-flow skeleton
- [ ] Phase 1: 撮影とAIカード生成 / Photo capture and AI card generation
- [ ] Phase 2: コレクションとローカル永続化 / Collection and local persistence
- [ ] Phase 3: ターン制バトル / Turn-based battle
- [ ] Phase 4: 屋外撮影の担保 / Outdoor-capture enforcement
- [ ] Phase 5: 仕上げ・プレイテスト / Polish and playtest
- [ ] ハードウェア: PCB設計開始 / Hardware: PCB design kickoff
