# ゲームMVP実装計画 / Game MVP Implementation Plan

## 1. ゲーム概要 / Game Concept

屋外で木の枝などの自然物を撮影すると、その写真からステータスを振った「カード」が生成され、そのカードで対戦するスマホゲーム。バーコードバトラー系のゲームループを、バーコードの代わりに「自然物の写真」で回す。生成処理はすべて端末内で完結させ、クラウドAIは使わない。

A smartphone game in the vein of Barcode Battler: photograph a tree branch or other natural object outdoors, the photo produces a card with battle stats, and you battle with that card. The barcode is replaced by a photo of nature. All generation runs on-device — no cloud AI.

**目的 / Purpose**: 人を外に出し、運動や自然との触れ合いのきっかけを作ること。これは世界観ではなく設計上の制約であり、「本当に屋外で今撮った写真」であることを担保する仕組みを設計に含める。

Getting people outside and encouraging exercise and contact with nature. This is a design constraint, not just flavor — the design must ensure photos are genuinely taken fresh, outdoors.

**スコープ / Scope**: MVP(タイトル画面から1回のゲームループが遊べる最小限の状態)。アカウント、課金、対人戦、SNS連携はMVP対象外。

**`hardware/` との関係 / Relationship to `hardware/`**: 本ゲームと `hardware/` の自作PCB(「機械に感情を与えるステッカー」)は**現時点では無関係な別プロジェクト**。連携仕様は策定しない。

**ゲームデザイン / Game design**: 何を作るか(進行、屋外要素、アート要件、安全・プライバシー)は [`game-design.md`](./game-design.md) にまとめている。この文書は**どう作るか**を扱う。

What to build — progression, outdoor mechanics, art requirements, safety and privacy — is in [`game-design.md`](./game-design.md). This document covers **how** to build it.

---

## 2. 技術方針 / Technical Decisions

### 2.1 クライアント / Client

- **Unity (C#)** — 既存の `software/README.md` の方針通り。
- Unity **6 (6000.x 系)** を使用する。当初 2022 LTS を推奨していたが、Unity Hub から 2022 LTS が入手できないため変更した。2022 LTS を選んだ理由は「安定・情報量が多い」だけで必須要件ではなく、本プロジェクトが使う機能(`WebCamTexture`、uGUI、位置情報)はいずれも Unity 6 で利用できる。
  Unity **6 (the 6000.x line)**. The plan originally recommended 2022 LTS, but it is not obtainable from Unity Hub. That recommendation was only about stability and available documentation, not a hard requirement — everything this project uses (`WebCamTexture`, uGUI, location services) is available in Unity 6.
- ⚠️ **Player Settings → Active Input Handling は `Both` にする。** Unity 6 の新規プロジェクトは新 Input System が既定だが、そのままだと (1) `StandaloneInputModule` が機能せずボタンが一切反応しない、(2) `Input.location` による位置情報取得が動かない可能性がある。`Both` にすれば両方回避できる。(1) はコード側でも入力モジュールを分岐させて対応済み。
  ⚠️ **Set Player Settings → Active Input Handling to `Both`.** New Unity 6 projects default to the new Input System only, which would (1) leave every button dead because `StandaloneInputModule` does nothing, and (2) possibly break location reads through `Input.location`. `Both` avoids both. The code also branches on the input module for (1).
- 対象プラットフォームは **Android を優先**(署名・審査の手間がなく実機での反復が速い)。iOS は後追い。
- 撮影は Unity 標準の **`WebCamTexture`** で実装する。サードパーティのネイティブカメラプラグインは使わない(後述の「実装時の設計変更」参照)。いずれの方式でもギャラリーから既存写真を取り込む経路は存在せず、屋外担保の効果は同じ。
- JSONは `Newtonsoft.Json`(Unity Package Manager 経由)を使用。`Card` は非公開フィールド + 読み取り専用プロパティ構成のため、`JsonUtility` ではセーブデータを往復できない。
- 必要パーミッション: `CAMERA`, `INTERNET`(GPSチェックを入れる場合は `ACCESS_FINE_LOCATION`)。

### 2.2 ステータス生成 / Stat Generation

**すべてオンデバイスで完結させる。クラウドAIは使わない。**
All generation runs on-device. No cloud AI is used.

段階的に置き換える方針:
The approach is replaced in stages:

| 生成方式 / Method | 用途 / Use | 状態 / Status |
| --- | --- | --- |
| **写真の見た目から導出**(`AI/PhotoAnalysisCardGenerator`) | **既定。**色相分布・エッジ量・方向・明暗から武器ジャンル/属性/レアリティ/ステータスを導出(下記 8) | 実装済み / Implemented |
| 写真のバイト列のハッシュから導出(`AI/MockCardGenerator`) | 画像解析側の不具合を切り分けるとき only。`AppConfig.useHashOnlyGenerator` で有効化 | 実装済み / Implemented |
| 被写体の切り出し(セグメンテーション) | 太さ・分岐数を実際に測る。代理指標で足りなければ | 🔶 未着手 / Not started |

- **同じ写真からは必ず同じカードが出る。** 導出に時刻も未シードの乱数も使っていない。1枚の写真を撮り直して当たりを引くまで繰り返す抜け道が塞がれる。屋外に出ること自体が目的のゲームなので、この性質は譲れない。
  **The same photo always yields the same card** — no clock and no unseeded randomness anywhere in the derivation. That closes the loophole of re-submitting one photo until a good roll appears, which matters because going outside is the point of the game.
- 導出の反復は **Editor の `Tools → CITAItokens → Card Preview`** で行う。手持ちの写真フォルダを一括解析するため、カメラ・権限・シーン・実機のいずれも不要。
  Iteration happens in the Editor via `Tools → CITAItokens → Card Preview`, which batch-analyses a folder of existing photos — no camera, permissions, scene, or device needed.
- **カード画像は撮影した写真そのものを使う。イラストや画像はAI生成せず、必要なものは人力で用意する。**
  Cards use the captured photo itself. Illustrations and images are **not** AI-generated; anything needed is produced by hand.
- 生成方式は `ICardGenerator` インターフェース越しに呼ばれており、実装を差し替えても撮影・コレクション・バトルの各層は変更不要。
  Generation is invoked through the `ICardGenerator` interface, so swapping implementations requires no changes in the capture, collection, or battle layers.

### 2.3 バックエンド / Backend

**MVPにバックエンドは不要。** オンデバイス生成のみなので、通信もサーバーも要らない。

The MVP needs **no backend** — on-device generation requires neither network nor server.

`services/card-proxy/` にクラウドAI経由の生成を行うプロキシ実装が残っているが、これは**MVPの経路ではない参考実装**である。クラウドAIを検討していた段階で作成し、動作確認まで済んでいるため保管してある。使う場合は `AppConfig` の `cardProxyUrl` を設定すると有効になる。既定は空欄で無効。

`services/card-proxy/` still contains a proxy that generates cards via a cloud AI. It is a **reference implementation, not part of the MVP path** — built and smoke-tested while cloud AI was under consideration, and kept for reference. To use it, set `cardProxyUrl` in `AppConfig`; it is empty and therefore disabled by default.

- クラウド経路を使う場合でも、クライアントから直接AI APIを叩かせない設計は維持する。屋外で実機を持ち歩く前提のため、APIキーをアプリバイナリに埋め込むと抽出される。
  Even on the cloud path, the client must never call the AI API directly: the app is carried outdoors on real devices, and a key embedded in the binary can be extracted.
- **APIキーは絶対にリポジトリにコミットしない。**
  **Never commit API keys to this repository.**

### 2.4 データモデルと永続化 / Data Model & Persistence

```
Card
├── id                : string (GUID)
├── name              : string   (生成 / generated)
├── weaponGenre       : enum { Club, Spear, Staff, Bow, Shield, Dagger }  ← game-design.md 4.0
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
| **Phase 0 — 雛形構築** | Unity 6 プロジェクトを `software/` に作成。`Active Input Handling` を `Both` に設定。`Newtonsoft.Json` 導入。シーン生成メニューを実行し、画面遷移だけ通る状態にする。Android実機ビルド確認。 | M |
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

---

## 6. 現在の実装状況 / Current Implementation Status

**⚠️ このリポジトリのコードは一度もコンパイルされていない。** Unity が無い環境で書かれているため、初回に Unity で開いた時点が最初のコンパイル検証になる。エラーが出るのは想定内。

**⚠️ None of this code has ever been compiled** — it was written in an environment without Unity. The first time you open the project in Unity *is* the first compile check; errors there are expected.

| 層 / Layer | ファイル / Files | 状態 / Status |
| --- | --- | --- |
| 共有契約 | `Card/` (Card, StatBlock, ElementType, Rarity, TypeAdvantage), `Data/ICardRepository`, `Data/ICaptureHistory`, `AI/ICardGenerator`, `Capture/IPhotoCapture` | ✅ 完了 |
| データ・永続化 | `Data/LocalCardRepository`, `ThumbnailStore`, `PlayerPrefsCaptureHistory` | ✅ 完了 |
| 設定・合成ルート | `Core/AppConfig`, `GameContext`, `ScreenRouter`, `ScreenBase`, `ScreenId`, `GameBootstrap` | ✅ 完了 |
| 撮影 | `Capture/WebCamPhotoCapture`, `CaptureValidator`, `LocationProbe` | ✅ 完了 |
| カード生成 | `AI/MockCardGenerator`(ローカル生成), `CardProxyClient` + `CardProxyResponse`(参考実装) | ✅ 完了 |
| バトル | `Battle/BattleManager`, `DamageCalculator`, `BattleCombatant`, `BattleRoundResult`, `CpuDecks` | ✅ 完了 |
| UI | `UI/UiFactory`, `CardTextFormatter`, `TitleScreen`, `CaptureScreen`, `CardResultScreen`, `CollectionScreen`, `BattleResultPayload` | ⚠️ 一部完了 |
| UI(未着手) | `UI/BattleScreen`, `UI/ResultScreen` | ❌ **未作成** |
| Editor ツール | `Editor/SceneBuilder`, `Editor/BattleSelfTest` | ✅ 完了 |
| Unity プロジェクト本体 | `ProjectSettings/`, `Packages/` | ❌ 未作成(人力で作成: `setup-unity.md`) |
| 参考実装のプロキシ | `services/card-proxy/` | ✅ 動作確認済み(MVP対象外) |

### 残作業 / Remaining Work

**コンパイルを通すために必須 / Required to compile:**
1. `UI/BattleScreen.cs` と `UI/ResultScreen.cs` の作成。**この2つが無いと `GameBootstrap` はコンパイルできない**(6画面すべてを参照している)。
2. Unity プロジェクトの作成と `Newtonsoft.Json` の導入(`setup-unity.md` 手順1〜2)。

**動くようにするために必須 / Required to run:**
3. `Tools/CITAItokens/Create Main Scene` の実行。
4. 初回コンパイルで出るエラーの修正。各エージェントが「自信がない」と申告したAPIが候補(下記)。

**既知の課題 / Known issues:**
5. ✅ 実装済み(**未検証**): 撮影画像の向き補正。JPEG化の前に画素そのものを回転させる。参照実装との突き合わせ(4角度 × ミラー有無 × 6種のアスペクト比)で演算の一致は確認したが、**`videoRotationAngle` を時計回りと解釈してよいかは実機でしか確かめられない**。誤っていれば上下逆さまになる。Editor では PCカメラが `angle=0` を返すため回転の分岐が実行されず、検証にならない。切り分け表は `android-testing.md` 4.1。
6. ✅ 実装済み: `captures/` の自動削除。直近20件を残し、撮影成功後に古いものから削除する。即時削除にしないのは、サムネイル書き込み前に消すと撮ったばかりの写真を失うため。
7. **セーブデータにスキーマバージョンが無い**(下記 7 参照)。
8. `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` — Unity のバージョンによって組み込みフォント名が異なる。`Arial.ttf` へのフォールバックを入れてあるが、両方失敗すると文字が出ない。Unity 6 では `LegacyRuntime.ttf` が正しい名前。
9. ✅ 対応済み: `Active Input Handling` が「Input System Package (New)」のみだと `StandaloneInputModule` が機能せずボタンが反応しない問題。`GameBootstrap` が `ENABLE_INPUT_SYSTEM` を見て `InputSystemUIInputModule` を使うよう分岐させた。ただし `Input.location`(位置情報)は分岐で救えないため、**Active Input Handling は `Both` にする**必要がある(2.1 参照)。
10. シーンにカメラが無い。オーバーレイUIは描画されるが Game ビューに警告が出る。
11. **Unity 6 での未検証API。** このコードは Unity 2022 LTS 想定で書かれており、Unity 6 で非推奨・削除されたAPIが残っている可能性がある。`FindObjectOfType`(→ `EventSystem.current` に置換済み)以外にも初回コンパイルで出る可能性がある。出たら都度置き換える。
    **APIs unverified on Unity 6.** This code was written against Unity 2022 LTS, so it may still use APIs that Unity 6 deprecated or removed. `FindObjectOfType` was already replaced with `EventSystem.current`; expect others to surface on the first compile.

---

## 7. セーブデータのスキーマバージョン / Save Schema Versioning

**✅ 対応済み(`LocalCardRepository.CurrentSchemaVersion = 1`)。** 武器ジャンルの追加がまさに「フィールドを足すと既存セーブが読めなくなる」ケースだったため、同時に導入した。

**Done** (`LocalCardRepository.CurrentSchemaVersion = 1`). Adding the weapon genre was exactly the "a new field breaks old saves" case, so the versioning landed with it.

導入前は `collection.json` が `List<Card>` をそのままの形で保存しており、バージョン番号を持たなかった。

`Card` に項目を足す・意味を変える改修が入った時点で、既存のセーブデータが読めなくなるか、無音で不正な値が入る。**プレイヤーが屋外を歩いて集めたカードを失う**ため、これは軽い問題ではない。

**対応方針**(実装前に入れておくのが安い):

```
{
  "schemaVersion": 1,
  "cards": [ ... ]
}
```

- 読み込み時に `schemaVersion` を見て、必要なら移行処理を通す。
- 未知の(より新しい)バージョンを読んだ場合は、**上書き保存せず**エラーを出す。古いアプリで新しいセーブを壊さないため。
- 破損時は既存実装どおり `collection.corrupt-<timestamp>.json` に退避して空から始める(既に実装済み)。

実装した挙動:
- バージョン付きのラッパーを読む → 通常どおり読み込む
- **バージョン導入前の素の配列**を読む → v0 として読み込み、移行した旨をログに出す。プレイテストで溜めたデータを捨てないため
- **現在より新しいバージョン**を読む → 読み込まず、**ファイルも上書きしない**。古いビルドが新しいセーブを壊さないため
- パース不能 → `collection.corrupt-<timestamp>.json` に退避して空から開始(従来どおり)

Implemented behaviour: a versioned wrapper loads normally; a bare pre-versioning array loads as v0 with a migration log (so existing playtest data is not thrown away); a **newer** version is neither loaded **nor overwritten**, so an older build cannot clobber a newer save; unparseable files are set aside as `collection.corrupt-<timestamp>.json` and the collection starts empty.

---

## 8. 写真からのステータス導出 / Deriving Stats from the Photo

**✅ 方針決定: 案A(学習なしの画像解析)を採用し、最優先で実装する。** MVP の後回しではなく、ここがゲームの核であるため最初の作業単位にした(§12 参照)。

**Decided: option A (image analysis without training), implemented first.** This is the core of the game rather than a post-MVP item, so it is the first unit of work (see §12).

### 実装の構成 / Structure

```
写真(既存のJPEG/PNGでもよい)
  → PhotoAnalyzer      : 9個の特徴量に還元(色相分布・彩度・明度・コントラスト・
  │                       エッジ量・エッジ方向の揃い方・暗部の割合)
  → PhotoStatDeriver   : 特徴量 → 武器ジャンル / 属性 / レアリティ / ステータス
  → PhotoCardComposer  : 名前とフレーバーを付けて Card を組み立てる
```

- 反復は **Editor の `Tools → CITAItokens → Card Preview`** で行う。手持ちの写真フォルダを一括解析し、9個の特徴量と結果を並べて表示する。カメラ・権限・シーン・実機のいずれも不要。
  Iteration happens in the Editor via `Tools → CITAItokens → Card Preview`, which batch-analyses a folder of existing photos and shows all nine features beside the result. No camera, permissions, scene, or device involved.
- **一括表示が重要。** 「どの写真も同じジャンルになる」という失敗は1枚ずつ見ても気づけず、分布を見ないと分からない。
  **Batch view matters**: "every photo yields the same genre" is invisible one photo at a time and only shows up in the distribution.

### この方式の限界 / What this approach cannot do

⚠️ **枝の「太さ」や「分岐数」を正確に測るには被写体の切り出し(セグメンテーション)が必要で、これは実装が重い。** 現在の実装は画像全体の統計量を代理指標として使っている:

- エッジ量が多い → 分岐が多い被写体、と解釈する
- エッジの向きが揃っている → 真っ直ぐな棒、と解釈する
- 暗部の割合が大きい → 被写体が画面を占めている(=太い)、と解釈する

**背景が写真ごとに違えば代理指標はぶれる。** 草地で撮った枝と、コンクリートで撮った枝では同じ枝でも結果が変わりうる。実際の写真で検証して、許容範囲かを判断する必要がある。許容できない場合の次の手は被写体の切り出しだが、工数は跳ね上がる。

⚠️ These are **proxies, not measurements** — accurate thickness or fork counting needs subject segmentation, which is a much larger job. Because the proxies read the whole frame, the same branch photographed on grass and on concrete can yield different results. This must be validated against real photos; if it is not acceptable, segmentation is the next step, at a significantly higher cost.

### 検討した他の案 / Options considered

3案あり、工数が極端に違う:

| 案 / Option | 内容 | 工数 | 学習データ |
| --- | --- | --- | --- |
| **A. 学習なしの画像解析**(✅ 採用) | 色相分布・エッジ量・明暗差などを自前で計算し、ステータスに写像する | 小 | 不要 |
| B. 既存の学習済みモデル流用 | 一般物体認識の公開モデル(MobileNet 等)を ONNX で組み込み、分類結果をステータスに写像 | 中 | 不要 |
| C. 自前で学習 | 写真を集めてラベル付けし、モデルを学習させる | 大 | 数百〜数千枚の収集とアノテーションが必要 |

**推奨は A。** 理由: 学習データの収集が不要で、「写真の見た目が結果に反映される」という体験上の要件を満たせる。属性を色から決める(`game-design.md` 4.2)のと同じ延長線上にあり、Unity Sentis も ONNX も要らない。B は「枝かどうか」の判定には使えるが、太さや質感からステータスを導く用途には粒度が合わない。C は本質的だが、このプロジェクトの規模に対して重い。

Recommended: **option A**. It needs no training data, satisfies the experiential requirement that the photo's appearance drives the result, sits on the same path as deriving the element from colour, and requires neither Sentis nor ONNX. B suits "is this a branch?" but not deriving thickness or texture. C is the most faithful but disproportionately heavy for this project.

- どの案でも `ICardGenerator` の実装を差し替えるだけで済む。撮影・コレクション・バトルの各層は変更不要。この抽象化のおかげで、ハッシュ方式(`MockCardGenerator`)から画像解析方式(`PhotoAnalysisCardGenerator`)への差し替えが設定1つで済む。
- 将来 B や C に進む場合も、同じ差し替えで済む。A で不足が明確になってから判断する。

---

## 9. 非機能要件 / Non-Functional Requirements

| 項目 / Item | 目標 / Target | 備考 / Notes |
| --- | --- | --- |
| カード生成時間 | 3秒以内 | 屋外で立ち止まる時間を短くする。通信しないので達成は容易 |
| 起動時間 | 5秒以内 | `GameBootstrap` が全UIを実行時に構築するため、画面数が増えると伸びる。増えたら遅延構築に変える |
| ストレージ | 1000枚で 200MB 以内 | サムネイル1枚 50-150KB。元画像の削除(残作業6)が前提 |
| フレームレート | 30fps 固定 | `Application.targetFrameRate = 30`(実装済み)。カードゲームに60fpsは不要で、屋外=充電できない環境では電池を優先 |
| 対応OS | Android 7.0 (API 24) 以上 | 🔶 暫定。カメラと位置情報が使えれば足りる |
| オフライン動作 | 完全にオフラインで全機能が動く | 機内モードでの通しプレイを検証項目にする |

---

## 10. テスト計画 / Test Plan

自動テストは `.asmdef` の制約で導入していない(2.8 参照)。代わりに以下で担保する。

| 対象 / Target | 方法 / Method |
| --- | --- |
| バトルの計算 | `Tools/CITAItokens/Run Battle Self-Test`(実装済み、約70項目) |
| 永続化の往復 | 保存 → アプリ再起動 → 読み込みを手動で確認。**`Card` の非公開フィールドが Newtonsoft で往復できるかが要点**(壊れると無音で空のカードになる) |
| 撮影フローの分岐 | 許可拒否 / カメラ無し / 鮮度切れ / 移動不足 / 生成失敗 の各状態を意図的に作り、メッセージと復帰手段を確認 |
| 決定的生成 | 同じ写真から同じカードが出ること、違う写真では十分ばらけることの両方 |
| オフライン | 機内モードで通しプレイ |
| 実機での見え方 | 直射日光下での可読性、片手操作、日本語が豆腐にならないこと |

🔶 将来 `.asmdef` を導入して Unity Test Framework に移行する選択肢は残す。その場合、プラグインを使わない現在の構成なら副作用は小さい。

---

## 11. リリースに向けて / Toward Release

MVP の完了条件には含めないが、着手前に把握しておく項目。

- **プライバシーポリシーの掲示が必須。** カメラと位置情報のパーミッションを要求するため、Google Play / App Store 双方で要求される。「端末外に送信しない」と明記できるのは強い(`game-design.md` 8.2)。
- **ストアのデータ収集申告**: 写真・位置情報を「収集しない(端末内処理のみ)」と申告する。実装がその通りであることが前提。
- 🔶 **年齢レーティング**: 想定ユーザーに子どもが含まれるかで要件が変わる。位置情報を扱う点は説明を要する。**要確認**。
- **アプリアイコンとストア掲載素材**は人力で用意する(`game-design.md` 7)。
- iOS は Apple Developer Program の登録が必要。Android 優先の理由の一つ。

---

## 12. 作業順序と依存関係 / Work Order and Dependencies

実装を再開する際の順序。**依存関係で決まっている部分と、選択の余地がある部分を分けて書く。**

**最優先は「写真 → ステータス」の実装。** ここが面白くなければ他のすべてが無意味になるため、通しプレイより先に着手する。Editor 上のプレビューツールで完結させれば、カメラ・権限・シーン・実機のいずれも不要で高速に反復できる。

**The photo → stats derivation comes first.** If that is not interesting, nothing else matters. It is built behind an Editor preview tool so it can be iterated without a camera, permissions, a scene, or a device.

```
[第1弾・写真からステータスを振る ← 最優先]
  1. セーブスキーマバージョン導入 (LocalCardRepository)   ← 後から入れると移行が必要
  2. 武器ジャンルの導入 (Card に WeaponGenre 追加)
  3. 写真の特徴量抽出 (PhotoAnalyzer)                     ← 色相分布・エッジ量・方向・明暗
  4. 特徴量 → ジャンル/属性/レアリティ/ステータス (PhotoStatDeriver)
  5. Editor プレビューツール (Tools → CITAItokens → Card Preview)
                                                          ← 手持ちの写真フォルダを一括解析
  6. 実際の棒の写真で反復調整                             ← ここが本番。実機不要

[第2弾・通しプレイを成立させる]
  7. UI/BattleScreen + UI/ResultScreen 作成               ← 無いとコンパイル不可
  8. Unity プロジェクト作成 + Newtonsoft 導入 (人力)
  9. Create Main Scene → 初回コンパイル → エラー修正      ← 最初の実質的な検証
 10. Editor 上で通しプレイ (PCのWebカメラ)

[必須・5以降ならいつでも]
  7. 撮影画像の向き補正                                    ← 実機で最初に出る不具合の本命
  8. captures/ の古い画像の削除
  9. Android 実機ビルド → 屋外で実際に撮影

[選択・上記が通ってから]
 10. 屋外要素の有効化 (移動距離チェック、1日の上限)        ← game-design.md 6
 11. アート素材の差し込み (ジャンル/属性アイコン等)         ← game-design.md 7
 12. 図鑑 (ジャンル × 属性 18枠)                           ← game-design.md 3
 13. バランス調整 (ジャンル/属性の補正値、予算制)          ← ⏸️ 保留中
 14. 写真の見た目からジャンルを判別 (Phase 6)              ← 🔶 方針未決定、10-13と並行可
```

**5 が最大の関門。** コンパイルされたことがないコードなので、ここで一定量のエラーが出る。7 の向き補正も実機でしか判断できない。この2点を通過するまでは、その先の作業(アート、バランス、図鑑)に着手しても手戻りする可能性がある。

**Step 5 is the real gate.** None of this code has been compiled, so expect a batch of errors there. Step 7 can only be judged on a real device. Until both are through, work further down the list risks being redone.

**2 を早い段階に置いた理由**: 武器ジャンルは `Card` の形を変える。カードは永続化されるため、プレイテストでデータが溜まった後に追加すると移行処理が必要になる。図鑑もジャンルを軸にするため、後続の作業が依存している。

**Why step 2 is early**: the weapon genre changes the shape of `Card`. Cards are persisted, so adding it after playtest data accumulates would require a migration — and the collection screen depends on it as an axis.
