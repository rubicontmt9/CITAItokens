# CITAItokens

> 🎮 ゲームソフトウェア × 🔧 専用ハードウェアを同時開発するプロジェクトです。
> A project developing a game (software) and its dedicated custom hardware controller in parallel.

このリポジトリは、開発者(あなた)と Claude (AI) が共同作業するための専用環境です。
This repository is a dedicated workspace for collaboration between the owner and Claude (AI).

---

## 📁 リポジトリ構成 / Repository Structure

```
CITAItokens/
├── software/   # ゲーム本体 (Unity / C#)
│               # The game itself (Unity / C#)
├── hardware/   # 専用ハードウェア設計 (自作PCB / KiCad)
│               # Dedicated custom hardware design (custom PCB / KiCad)
└── README.md   # この案内ファイル / This guide file
```

| フォルダ / Folder | 内容 / Contents | 詳細 / Details |
| --- | --- | --- |
| [`software/`](./software/README.md) | Unity 製ゲーム本体 / Unity-based game | [software/README.md](./software/README.md) |
| [`hardware/`](./hardware/README.md) | 自作PCBハードウェア設計 / Custom PCB hardware design | [hardware/README.md](./hardware/README.md) |

---

## 🚀 はじめに / Getting Started

1. ゲームソフトウェアの開発は [`software/`](./software/README.md) を参照してください。
   For game software development, see [`software/`](./software/README.md).
2. ハードウェア設計の開発は [`hardware/`](./hardware/README.md) を参照してください。
   For hardware design development, see [`hardware/`](./hardware/README.md).
3. ソフトウェアとハードウェアが連携する仕様(通信プロトコルなど)は、決まり次第このREADMEまたは各フォルダのREADMEに追記していきます。
   Integration specs between software and hardware (e.g. communication protocol) will be documented here or in the respective folder READMEs as they are decided.

## 🗂️ 開発の進め方 / Workflow Notes

- 作業ブランチは `claude/*` 系のブランチで進行します。 / Work happens on `claude/*` branches.
- 大きな設計判断や技術選定はコミットメッセージまたはPR説明に理由を残します。 / Major design decisions and technology choices are recorded in commit messages or PR descriptions.
- 現時点では初期の雛形段階です。進捗はこのREADMEを随時更新して共有します。 / This is currently the initial scaffolding stage. Progress will be reflected here as it happens.

## 📌 ステータス / Status

- [x] リポジトリ雛形構築 / Initial repository scaffolding
- [ ] ゲームソフトウェア: Unity プロジェクト作成 / Game software: Unity project setup
- [ ] ハードウェア: PCB設計開始 / Hardware: PCB design kickoff
- [ ] ソフトウェア・ハードウェア連携仕様策定 / Software-hardware integration spec
