# ゲームMVP実装計画 / Game MVP Implementation Plan

## 1. ゲーム概要 / Game Concept

屋外で木の枝などの自然物を撮影すると、AIが写真を解析してステータスを振った「カード」を生成し、そのカードで対戦するスマホゲーム。バーコードバトラー系のゲームループを、バーコードの代わりに「自然物の写真」で回す。

A smartphone game in the vein of Barcode Battler: photograph a tree branch or other natural object outdoors, an AI analyzes the photo and generates a card with battle stats, then battle with that card. The barcode is replaced by a photo of nature.

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
- 撮影は `WebCamTexture` による自作プレビューではなく、**OSのカメラアプリを呼び出すネイティブカメラ連携**を採用(例: `NativeCamera` パッケージ / MIT)。ギャラリー選択の経路がそもそも存在しない構造になり、後述の屋外担保にそのまま効く。
- JSONは `Newtonsoft.Json`(Unity Package Manager 経由)を使用。AIレスポンスの柔軟なパースには `JsonUtility` では不足。
- 必要パーミッション: `CAMERA`, `INTERNET`(GPSチェックを入れる場合は `ACCESS_FINE_LOCATION`)。

### 2.2 AIによるステータス生成 / AI Stat Generation

**マルチモーダルLLM(vision対応モデル)への画像+プロンプト送信**を採用する。

- 採用理由: オンデバイスの画像処理ヒューリスティック(エッジ検出・色ヒストグラム等)を自作するより実装コストが低く、カード名やフレーバーテキストも同時に得られる。
- プロンプトでは「枝の太さ・質感・色・分岐の複雑さ」等の視覚的特徴を根拠として記述させ、**固定スキーマのJSONのみ**を返させる。
- 返却された数値は必ずクランプ/バリデーションする。壊れたJSONや異常値は **1回リトライ → デフォルト値でフォールバック** とし、プレイヤーが行き止まりにならないようにする。
- **通信環境への配慮**: 主な利用シーンが電波の弱い屋外である前提で、撮影自体は完全にオフラインで完結させる。カード生成(通信)は「生成中…」状態とリトライを用意する。MVPでは「カード生成には通信が必要」と割り切り、UIで明示する。

### 2.3 バックエンド / Backend

**最小限のプロキシサービスを1つ挟む**(クライアントからAI APIを直接叩かない)。

- 理由: クライアント直叩きだとAPIキーがアプリバイナリに埋め込まれ、逆コンパイルで抽出できてしまう。屋外で実機を持ち歩いてテストする前提の本ゲームでは、この露出は看過できない。
- 構成: サーバーレス関数1つ(Cloudflare Workers など)。画像を受け取り → AI APIへ転送(APIキーはサーバー側のみ) → レスポンスを再バリデーション → クライアントへカードJSONを返す。
- MVPでは認証・レート制限は省略可。本番化の際に、ユーザー認証、アプリ由来リクエストの検証、レート制限、画像サイズ制限、ログ/監視を追加する。
- **APIキーは絶対にリポジトリにコミットしない。**

### 2.4 データモデルと永続化 / Data Model & Persistence

```
Card
├── id                : string (GUID)
├── name              : string   (AI生成)
├── element           : enum { Wood, Water, Earth }
├── rarity            : enum { Common, Uncommon, Rare, Epic }
├── stats             : { hp, attack, defense, speed : int }
├── flavorText        : string   (AI生成)
├── imagePath         : string   (撮影写真のサムネイルへの相対パス)
├── captureTimestamp  : DateTime
├── captureLocation   : (lat, lon)  ※任意 / optional
└── sourcePhotoHash   : string      ※任意 / optional
```

**ローカル保存のみ**(アカウント/クラウド同期なし)。`Application.persistentDataPath` に コレクションJSON + サムネイル画像を保存する。`CardRepository` が読み書きを一手に担い、将来のクラウド同期差し替え時にゲームロジックを触らずに済むようにする。

### 2.5 屋外担保(アンチチート) / Outdoor Enforcement

- **主策**: ネイティブカメラ連携により、ギャラリーから既存写真を取り込む経路がアプリ内に存在しない。設定で無効化できる「ルール」ではなく構造的な担保であり、追加コストがほぼゼロ。
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
├── Camera/   # ネイティブカメラ連携、撮影フロー、EXIF/時刻チェック
├── AI/       # プロキシへのAPIクライアント、レスポンスのパース/バリデーション
├── Card/     # Cardモデル、CardFactory(AI結果→Card変換)、属性相性テーブル
├── Data/     # CardRepository(ローカルJSON永続化)、直近撮影地点/時刻の保持
├── Battle/   # BattleManager、ダメージ計算、CPUデッキ定義
├── UI/       # 各シーンのUIコントローラ、共通ウィジェット(カード表示、HPバー)
└── Core/     # シーン遷移、設定(プロキシURL等)
```

**シーン構成 / Scenes**: `Title → Capture → CardResult → Collection → Battle → Result`

シーン遷移は `Core` のシーン遷移ヘルパーに集約し、各シーンから `SceneManager.LoadScene` を直接散らばらせない。

---

## 3. フェーズ計画 / Phased Milestones

| フェーズ / Phase | 内容 / Deliverables | 規模 / Size |
| --- | --- | --- |
| **Phase 0 — 雛形構築** | Unity 2022 LTS プロジェクトを `software/` に作成。`.gitignore` 確認。NativeCamera / Newtonsoft.Json 導入。6シーンをプレースホルダのボタンで繋ぎ、遷移だけ通る状態にする。Android実機ビルド確認。 | M |
| **Phase 1 — 撮影・カード生成** ⚠️最大リスク | カメラ連携、最小プロキシのデプロイ、AIクライアント、`CardFactory`、CardResult画面。外部API依存とJSON信頼性が最大の不確実性のため、バトルより先に単独で動作確認する。 | L |
| **Phase 2 — コレクション・永続化** | `CardRepository` のローカル保存/読込、Collection画面、CardResultからの「コレクションに追加」。 | S |
| **Phase 3 — バトルシステム** | `BattleManager`、ダメージ計算、属性相性テーブル、CPU固定デッキ、Battle/Result画面UI。 | M |
| **Phase 4 — 屋外担保の仕上げ** | カメラ経路の検証(構造的にはPhase 1で満たされる想定)、EXIF/GPSチェックの追加と却下時のメッセージ。 | S |
| **Phase 5 — 仕上げ・プレイテスト** | カード公開演出、被弾フィードバック、通信失敗時のリトライUX、ダメージ倍率の実プレイ調整、タイトル画面の説明、アプリアイコン。 | M |

Phase 1 をバトルより先に置くのは、AIパイプラインの信頼性(プロンプト品質、JSONパースの堅牢性、プロキシのレイテンシ)が最大の未知であり、バトルはCardモデルが固まれば機械的に実装できる低リスク領域であるため。

---

## 4. MVP完了の定義 / Definition of Done

以下を実機で(モックなし・実際のAI通信込みで)通しプレイできること:

タイトル → 外に出る → 枝を撮影 → AI解析待ち → 生成されたカードを確認 → コレクションに追加 → CPU戦 → 勝敗決定 → 結果画面 → タイトルに戻る

---

## 5. 検証方法 / Verification

- **Phase 0**: Android実機にビルドをインストールし、Title → Capture → … → Result までボタンで一周できることを確認。
- **Phase 1**: 実際に屋外で枝を撮影し、プロキシ経由でカードJSONが返ることを確認。加えて、意図的に不正なJSON/範囲外の数値を返すテストを行い、クランプとフォールバックが機能することを確認。
- **Phase 3**: 既知のステータス組み合わせを使い、属性相性(有利/不利/同属性)とダメージ計算が仕様通りかを手動テストケースで確認。
- **最終**: 上記「MVP完了の定義」の一連の流れを実機で通しプレイ。オフライン→通信復帰時のリトライ挙動も確認する。
