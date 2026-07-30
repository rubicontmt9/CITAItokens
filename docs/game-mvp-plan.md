# ゲームMVP実装計画 / Game MVP Implementation Plan

## 1. ゲーム概要 / Game Concept

屋外で木の枝などの自然物を撮影すると、その写真からステータスを振った「カード」が生成され、そのカードで対戦するスマホゲーム。バーコードバトラー系のゲームループを、バーコードの代わりに「自然物の写真」で回す。生成処理はすべて端末内で完結させ、クラウドAIは使わない。

A smartphone game in the vein of Barcode Battler: photograph a tree branch or other natural object outdoors, the photo produces a card with battle stats, and you battle with that card. The barcode is replaced by a photo of nature. All generation runs on-device — no cloud AI.

**目的 / Purpose**: 人を外に出し、運動や自然との触れ合いのきっかけを作ること。これは世界観ではなく設計上の制約であり、「本当に屋外で今撮った写真」であることを担保する仕組みを設計に含める。

Getting people outside and encouraging exercise and contact with nature. This is a design constraint, not just flavor — the design must ensure photos are genuinely taken fresh, outdoors.

**スコープ / Scope**: MVP(タイトル画面から1回のゲームループが遊べる最小限の状態)。アカウント、課金、対人戦、SNS連携はMVP対象外。

**`hardware/` との関係 / Relationship to `hardware/`**: 本ゲームと `hardware/` の自作PCB(「機械に感情を与えるステッカー」)は**現時点では無関係な別プロジェクト**。連携仕様は策定しない。

---

## 2. 技術方針 / Technical Decisions

### 2.1 クライアント / Client

- **Unity (C#)** — 既存の `software/README.md` の方針通り。
- Unity **2022 LTS** を推奨(安定・情報量が多い)。最新の 6000.x 系は避ける。
- 対象プラットフォームは **Android を優先**(署名・審査の手間がなく実機での反復が速い)。iOS は後追い。
- 撮影は Unity 標準の **`WebCamTexture`** で実装する。サードパーティのネイティブカメラプラグインは使わない(後述の「実装時の設計変更」参照)。いずれの方式でもギャラリーから既存写真を取り込む経路は存在せず、屋外担保の効果は同じ。
- JSONは `Newtonsoft.Json`(Unity Package Manager 経由)を使用。`Card` は非公開フィールド + 読み取り専用プロパティ構成のため、`JsonUtility` ではセーブデータを往復できない。
- 必要パーミッション: `CAMERA`, `INTERNET`(GPSチェックを入れる場合は `ACCESS_FINE_LOCATION`)。

### 2.2 ステータス生成 / Stat Generation

**すべてオンデバイスで完結させる。クラウドAIは使わない。**
All generation runs on-device. No cloud AI is used.

段階的に置き換える方針:
The approach is replaced in stages:

| 段階 / Stage | 生成方式 / Method | 状態 / Status |
| --- | --- | --- |
| 第一テスト / First test | **写真のバイト列から決定的に導出**(`AI/MockCardGenerator`) | 実装済み / Implemented |
| 完成版 / Final | **オンデバイスの機械学習モデル**(Unity Sentis + ONNX を想定) | 未着手 / Not started |

- 第一テストは「ランダム」で十分だが、実装は**写真のバイト列のハッシュから決定的に**ステータスを導出している。同じ写真からは必ず同じカードが出るため、1枚の写真を撮り直して当たりを引くまで繰り返す抜け道が塞がれる。屋外に出ること自体が目的のゲームなので、この性質は残す価値がある。写真が違えば結果は十分にばらけるため、体感は「ランダム」と変わらない。
  The first test only needs randomness, but the implementation derives stats **deterministically from a hash of the photo bytes**. The same photo always yields the same card, which closes the loophole of re-submitting one photo until a good roll appears. Since going outside is the point of the game, that property is worth keeping — and different photos still produce visibly different cards, so it feels random in play.
- 完成版のオンデバイスモデルは、枝の太さ・質感・分岐の複雑さといった視覚的特徴からステータスを導出することを狙う。通信不要になるため、電波の弱い屋外という主な利用シーンとも噛み合う。
  The final on-device model aims to derive stats from visual features such as thickness, texture, and branching. Running locally also suits the main use case of weak signal outdoors.
- **カード画像は撮影した写真そのものを使う。イラストや画像はAI生成せず、必要なものは人力で用意する。**
  Cards use the captured photo itself. Illustrations and images are **not** AI-generated; anything needed is produced by hand.
- 生成方式は `ICardGenerator` インターフェース越しに呼ばれており、実装を差し替えても撮影・コレクション・バトルの各層は変更不要。
  Generation is invoked through the `ICardGenerator` interface, so swapping implementations requires no changes in the capture, collection, or battle layers.

### 2.3 バックエンド / Backend

**MVPにバックエンドは不要。** オンデバイス生成のみなので、通信もサーバーも要らない。

The MVP needs **no backend** — on-device generation requires neither network nor server.

`services/card-proxy/` にクラウドAI経由の生成を行うプロキシ実装が残っているが、これは**MVPの経路ではない参考実装**である。クラウドAIを検討していた段階で作成し、動作確認まで済んでいるため保管してある。使う場合は `AppConfig` の `cardProxyUrl` を設定し `useMockCardGenerator` を切ると有効になる。既定は無効。

`services/card-proxy/` still contains a proxy that generates cards via a cloud AI. It is a **reference implementation, not part of the MVP path** — built and smoke-tested while cloud AI was under consideration, and kept for reference. To use it, set `cardProxyUrl` in `AppConfig` and turn off `useMockCardGenerator`; it is disabled by default.

- クラウド経路を使う場合でも、クライアントから直接AI APIを叩かせない設計は維持する。屋外で実機を持ち歩く前提のため、APIキーをアプリバイナリに埋め込むと抽出される。
  Even on the cloud path, the client must never call the AI API directly: the app is carried outdoors on real devices, and a key embedded in the binary can be extracted.
- **APIキーは絶対にリポジトリにコミットしない。**
  **Never commit API keys to this repository.**

### 2.4 データモデルと永続化 / Data Model & Persistence

```
Card
├── id                : string (GUID)
├── name              : string   (生成 / generated)
├── element           : enum { Wood, Water, Earth }
├── rarity            : enum { Common, Uncommon, Rare, Epic }
├── stats             : { hp, attack, defense, speed : int }
├── flavorText        : string   (生成 / generated)
├── imagePath         : string   (撮影写真のサムネイルへの相対パス)
├── captureTimestamp  : DateTime
├── captureLocation   : (lat, lon)  ※任意 / optional
└── sourcePhotoHash   : string      ※任意 / optional
```

**ローカル保存のみ**(アカウント/クラウド同期なし)。`Application.persistentDataPath` に コレクションJSON + サムネイル画像を保存する。`CardRepository` が読み書きを一手に担い、将来のクラウド同期差し替え時にゲームロジックを触らずに済むようにする。

### 2.5 屋外担保(アンチチート) / Outdoor Enforcement

- **主策**: 撮影経路が `WebCamTexture` のみで、ギャラリーから既存写真を取り込むコードパスがアプリ内に存在しない。設定で無効化できる「ルール」ではなく構造的な担保であり、追加コストがほぼゼロ。
- **副策(余裕があれば)**: EXIFのタイムスタンプが直近数分以内であることを確認。位置情報の許可があれば、前回撮影地点から一定距離(20〜50m目安)離れているかを確認する。「移動して撮る」を後押しする。
- **MVP対象外**: サーバー側の画像鑑識、ライブネス検知、ハッシュのタイムスタンプ証明など。単独プレイでリーダーボードも経済もないMVPでは費用対効果が合わない。

### 2.6 バトルシステム / Battle System

- **属性三すくみ**: 木(Wood) → 土(Earth) → 水(Water) → 木(Wood)。有利 **1.5倍** / 不利 **0.67倍** / 同属性 **1.0倍**。
- **行動順**: `speed` が高い方が先制。同値の場合はプレイヤー側を先手(MVPなのでシンプルに、対人戦もないため公平性の問題なし)。
- **ダメージ計算**:
  ```
  baseDamage  = max(1, attack - defense / 2)
  finalDamage = round(baseDamage * 属性倍率 * random(0.9, 1.1))
  ```
  `finalDamage` は最低1にクランプし、0ダメージでバトルが停滞しないようにする。
- **MVP構成**: プレイヤーの1枚 vs CPUの固定デッキ(1〜3枚、ステータスは事前定義でAI呼び出し不要)。HPが0になった方が負け。
- **MVP対象外**: 状態異常、カード交代、アイテム、特殊技。コアループの検証後に追加する候補。

### 2.7 プロジェクト構成 / Project Layout

```
software/Assets/Scripts/
├── Capture/  # WebCamTextureによる撮影、位置情報取得、鮮度/移動距離の検証
├── AI/       # カード生成(ローカル生成 / 参考実装のプロキシクライアント / 検証・クランプ)
├── Card/     # Cardモデル、属性相性テーブル
├── Data/     # ローカルJSON永続化、サムネイル保存、直近撮影の記録
├── Battle/   # BattleManager、ダメージ計算、CPUデッキ定義
├── UI/       # 各画面のコントローラ、共通ウィジェット(カード表示、HPバー)
└── Core/     # 画面遷移(ScreenRouter)、設定(AppConfig)、合成ルート(GameContext)
```

`Camera/` ではなく `Capture/` としているのは、`CitaiTokens.Camera` という名前空間が `UnityEngine.Camera` と衝突して曖昧参照を招くため。
The folder is `Capture/` rather than `Camera/` because a `CitaiTokens.Camera` namespace would collide with `UnityEngine.Camera` and create ambiguous references.

**画面構成 / Screens**: `Title → Capture → CardResult → Collection → Battle → Result`

画面遷移は `Core/ScreenRouter` に集約する。

---

### 2.8 実装時の設計変更 / Deviations Decided During Implementation

計画策定後、実装に着手する段階で以下を変更した。理由も残す。

Decisions changed once implementation started, with the reasons recorded.

#### 6シーン構成 → 1シーン + プログラム生成UI / Six scenes → one scene with programmatic UI

Unityの `.unity` シーンファイルと `.prefab` はYAML形式だが、GUID参照や内部IDの整合が必要で、手書きで確実に生成できる形式ではない。実装をLLMエージェントに分担させる前提では、シーンファイルを生成物にすると壊れやすい。そのため **1つのシーンに全画面のGameObjectを置き、`Core/ScreenRouter` が表示を切り替える**構成にした。1画面1コントローラという分割は計画通りで、論理構造は変わらない。

`.unity` scene files and `.prefab` assets are YAML, but not a format that can be reliably hand-authored (GUID references and internal ids must stay consistent). With implementation split across LLM agents, generated scene files would be fragile. Instead, **all screens are GameObjects in a single scene and `Core/ScreenRouter` toggles visibility.** The one-controller-per-screen split is unchanged.

#### ネイティブカメラプラグイン → Unity標準 `WebCamTexture` / Native camera plugin → built-in `WebCamTexture`

当初はOSのカメラアプリを呼び出すサードパーティプラグインを想定していたが、`WebCamTexture` に変更した。理由: (1) コンパイル検証できない外部APIへの依存を排除できる、(2) Unity Editor上でPCのWebカメラを使えるため、屋外に出ずに机の上でループ全体をテストできる、(3) `WebCamTexture` にもギャラリー取り込み経路はないため、屋外担保の効果は落ちない。

The plan originally assumed a third-party plugin launching the OS camera app; this changed to `WebCamTexture` because (1) it removes a dependency on an API that cannot be compile-verified here, (2) it works in the Editor with a PC webcam, so the whole loop is testable at a desk, and (3) `WebCamTexture` has no gallery path either, so the outdoor guarantee is unaffected.

#### 自動テスト → Editorメニューからの自己診断 / Automated tests → an Editor-menu self-test

Unity Test Framework を使うには `.asmdef` が必要だが、`.asmdef` を導入するとそのアセンブリから `Assembly-CSharp` 側のコードが見えなくなり構成が複雑化する。MVPでは代わりに **`Tools/CITAItokens/Run Battle Self-Test` メニューから実行できる自己診断**を用意し、ダメージ計算・属性相性・行動順・HPクランプを検証する。

Using the Unity Test Framework requires an `.asmdef`, and introducing one hides `Assembly-CSharp` code from that assembly. The MVP instead ships a **self-test runnable from `Tools/CITAItokens/Run Battle Self-Test`**, covering the damage formula, type advantage, turn order, and HP clamping.

---

## 3. フェーズ計画 / Phased Milestones

| フェーズ / Phase | 内容 / Deliverables | 規模 / Size |
| --- | --- | --- |
| **Phase 0 — 雛形構築** | Unity 2022 LTS プロジェクトを `software/` に作成。`.gitignore` 確認。`Newtonsoft.Json` 導入。シーン生成メニューを実行し、画面遷移だけ通る状態にする。Android実機ビルド確認。 | M |
| **Phase 1 — 撮影・カード生成** | `WebCamTexture` による撮影、`MockCardGenerator` によるローカル生成、CardResult画面。通信もサーバーも不要なため、当初想定していた外部API依存のリスクは無くなった。 | M |
| **Phase 2 — コレクション・永続化** | `CardRepository` のローカル保存/読込、Collection画面、CardResultからのコレクション反映。 | S |
| **Phase 3 — バトルシステム** | `BattleManager`、ダメージ計算、属性相性テーブル、CPU固定デッキ、Battle/Result画面UI。 | M |
| **Phase 4 — 屋外担保の仕上げ** | カメラ経路の検証(構造的にはPhase 1で満たされる想定)、鮮度/移動距離チェックの追加と却下時のメッセージ。 | S |
| **Phase 5 — 仕上げ・プレイテスト** | カード公開演出、被弾フィードバック、ダメージ倍率の実プレイ調整、タイトル画面の説明、手描きのUI素材・アイコン。 | M |
| **Phase 6 — オンデバイスモデル**(MVP後) | 写真の視覚的特徴からステータスを導出するモデルを Unity Sentis + ONNX で組み込み、`ICardGenerator` の実装として差し替える。 | L |

Phase 1〜3 の順序は当初どおり。生成がローカル完結になったことで Phase 1 の不確実性は下がったが、`Card` モデルが固まらないとバトル・コレクションが書けないため、依存関係上この順序が変わらない。

The Phase 1–3 order is unchanged. Local generation lowers Phase 1's risk, but battle and collection still depend on the `Card` model being settled, so the ordering holds.

---

## 4. MVP完了の定義 / Definition of Done

以下を実機で通しプレイできること:

タイトル → 外に出る → 枝を撮影 → カード生成 → 生成されたカードを確認 → コレクションに追加 → CPU戦 → 勝敗決定 → 結果画面 → タイトルに戻る

通信は一切不要。オンデバイスモデル(Phase 6)はMVPの完了条件に含めない。

The whole loop must run on a real device with **no network at all**. The on-device model (Phase 6) is not part of the MVP's definition of done.

---

## 5. 検証方法 / Verification

- **Phase 0**: Android実機にビルドをインストールし、Title → Capture → … → Result までボタンで一周できることを確認。
- **Phase 1**: 実際に屋外で枝を撮影し、カードが生成されることを確認。同じ写真から同じカードが出ること、違う写真では十分にばらけることの両方を確認する(決定的生成の意図どおりか)。
- **Phase 2**: カードを保存 → アプリを再起動 → コレクションに残っていることを確認。`Card` の非公開フィールドがJSONを往復できるかの確認も兼ねる(ここが壊れると無音で空のカードが読み込まれる)。
- **Phase 3**: 既知のステータス組み合わせを使い、属性相性(有利/不利/同属性)とダメージ計算が仕様通りかを `Tools/CITAItokens/Run Battle Self-Test` と手動ケースで確認。
- **最終**: 機内モードのまま「MVP完了の定義」の一連の流れを実機で通しプレイし、通信に依存していないことを確認する。
