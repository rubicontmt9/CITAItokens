# ゲームデザイン / Game Design

技術的な実装計画は [`game-mvp-plan.md`](./game-mvp-plan.md)、セットアップ手順は [`setup-unity.md`](./setup-unity.md) にあります。このファイルは**何を作るか**を決める文書です。

The technical implementation plan is in [`game-mvp-plan.md`](./game-mvp-plan.md); setup steps are in [`setup-unity.md`](./setup-unity.md). This file decides **what** we are building.

> ⚠️ **未決定の項目には 🔶 を付けています。** 実装前に決める必要があるもの、または「まず作って触ってから決める」と明示的に先送りしたものです。私(Claude)の推奨案を既定として書いていますが、これは覆して構いません。
> Items marked 🔶 are **not yet decided** — either they must be settled before implementation, or they are deliberately deferred until we can play the thing. A recommended default is written in each case; feel free to override.

---

## 1. 体験の核 / The Core Experience

**「その辺の枝が、自分だけのカードになる」** という驚きが体験の中心。

The core is the surprise that **an ordinary twig becomes a card that is uniquely yours.**

この体験が成り立つ条件を、設計上の制約として明示しておく:

| 条件 / Requirement | 理由 / Why |
| --- | --- |
| 生成結果が写真ごとに十分ばらける | 何を撮っても同じようなカードが出ると、撮る動機が消える |
| 同じ写真からは同じカードが出る | 1枚を撮り直して当たりを引く抜け道を塞ぐ。屋外に出ること自体が目的なので譲れない |
| 生成が数秒以内に終わる | 屋外で立ち止まって待つ時間は短くしたい |
| 通信が要らない | 主な利用シーンが電波の弱い屋外。ここが崩れると体験全体が崩れる |
| カードに写真が載る | 「自分が見つけたもの」という所有感は写真そのものが担う。イラストに置き換えると失われる |

**目的**: 人を外に出し、運動や自然との触れ合いのきっかけを作る。これは世界観ではなく設計上の制約であり、機能を足すか削るかの判断基準として使う。「その機能は人を外に出すか?」が問い。

**Purpose**: get people outside, moving, and in contact with nature. This is a design constraint, not flavor — it is the tiebreaker for whether a feature ships. The question is always "does this get someone outdoors?"

### 非目標 / Explicit Non-Goals

これらは**作らない**と決めておく。決めておかないと際限なく膨らむ領域なので明記する。

- **対人戦(PvP)**: MVP対象外。将来やるとしても非同期(相手のカードデータだけを借りて戦う)に限定する。リアルタイム対戦はサーバーが必要になり、「通信不要」という核が崩れる。
- **課金・ガチャ**: 対象外。屋外に出る動機を金で買える設計は目的と矛盾する。
- **AI生成のイラスト・画像**: 使わない。カードは写真そのものを使い、フレーム等の素材は人力で用意する。
- **SNS連携・ランキング**: MVP対象外。競争を持ち込むと不正のインセンティブが生まれ、アンチチートの要求水準が跳ね上がる。
- **正確な植物同定**: 目指さない。「これはコナラです」と言い切る機能は、間違えたときの害(誤食など)が大きすぎる。あくまでゲーム的なカード化に留める。

---

## 2. セッション設計 / Session Design

プレイは**屋外と自宅で非対称**になる。ここを意識せずに作ると、どちらかが空虚になる。

Play is **asymmetric between outdoors and at home**. Designing without acknowledging that leaves one of them hollow.

| 場面 / Context | 時間 / Duration | すること / What happens |
| --- | --- | --- |
| **屋外** | 1回 30秒〜数分 | 枝を見つける → 撮る → カードを見る → ポケットに戻す。バトルはしない(歩きスマホを誘発するため) |
| **自宅・移動中** | 数分〜 | コレクションを眺める、バトルする、次に何を探すか考える |

**設計上の帰結**:
- 撮影画面は**片手で完結**し、明るい屋外でも読める必要がある(コントラストを強く、文字を大きく)。
- カード生成後は「もう1枚撮る」より「しまう」を自然な流れにする。屋外で長居させない。
- バトルは自宅向けの遊びとして設計する。屋外での歩行中プレイを促す演出は入れない。

**Consequences**: the capture screen must work one-handed and stay readable in bright sunlight; after generation, "put it away" should feel more natural than "shoot another"; battle is designed as at-home play, and nothing should encourage playing it while walking.

---

## 3. 進行設計 / Progression 🔶

**🔶 未決定。** 進行の軸が決まらないと、セーブデータの形とUIの構成が後から大きく変わる。

**Recommended default: 図鑑コンプリート型 + 軽い挑戦要素**(ハイブリッド)

理由: 図鑑型は「まだ持っていない組み合わせを探す」という動機が屋外に出ることと直結し、実装も軽い。一方それだけでは対戦システムが宝の持ち腐れになるため、勝つと1段ずつ強い相手が出てくる程度の軽い挑戦要素を添える。「習慣形成型(日課)」は目的に最も直結するが、お題の中身を設計する工数が別途かかるため MVP 後に回す。

Rationale: a collection goal maps directly onto going outside and is cheap to build; a light challenge ladder gives the battle system a reason to exist. A daily-quest design serves the purpose best but needs its own content design, so it is deferred past the MVP.

### 図鑑の軸 / Collection Axes

「まだ埋まっていない枠」を作るための軸。

- **武器ジャンル 6 × 属性 3 = 18枠**(§4.0 で採用決定した骨格がそのまま図鑑の軸になる)
- レアリティ 4 を掛けると 72枠
- 🔶 追加候補: 季節(4)、時間帯(朝/昼/夕/夜)。「別の日・別の時間に出る理由」を作るが、検証に実時間がかかる

**推奨**: MVP は **ジャンル × 属性 の 18枠**を軸にする。レアリティは枠として数えず「同じ枠のより良い個体」として扱う。72枠は初見で広すぎて、埋まる気がしないため。

**Recommendation**: use the **18 genre × element slots** for the MVP, treating rarity as "a better specimen of the same slot" rather than a separate slot. 72 slots reads as too large to fill on first sight.

---

## 4. カード生成の設計 / Card Generation Design

### 4.0 生成の骨格: 武器ジャンル × 属性 / Generation Structure: Weapon Genre × Element

**採用決定。** 撮った枝は「武器」になる。**武器のジャンル**と**属性**が、それぞれステータスに補正をかける。

Decided: the branch you photograph becomes a **weapon**. Its **genre** and its **element** each apply modifiers to the stats.

```
写真の視覚的特徴
      │
      ├─→ 武器ジャンル ──→ ステータス補正(大)
      ├─→ 属性         ──→ ステータス補正(小) + 戦闘時の相性倍率
      └─→ 基礎ステータス
                              ↓
                        最終ステータス(範囲内にクランプ)
```

**この構造が良い理由**:
1. **見た目とジャンルが直結する。** 太い枝が棍棒になり、細長い枝が槍になる。「なぜこのステータスなのか」がプレイヤーに納得できる形で説明される。写真ハッシュから数値を振るだけの方式にはこの納得感が無い。
2. **図鑑の軸が一気に増える。** ジャンル 6 × 属性 3 = 18通り。レアリティを掛ければ 72枠。「12枠では薄い」という懸念(§3)がこれで解消する。
3. **収集の目標が具体的になる。** 「水属性の槍が欲しい」という探し方ができる。「レアが欲しい」より行動に落ちやすい。

Why this structure works: appearance maps to genre, so the stats feel *earned* rather than arbitrary; 6 genres × 3 elements gives 18 combinations (72 with rarity), which resolves the "12 slots is too thin" concern in §3; and it makes collecting concrete — "I want a Water spear" is more actionable than "I want a rare".

#### 武器ジャンル / Weapon Genres 🔶

**🔶 ジャンルの種類は要決定。** 以下は写真の特徴から判別しやすさを基準にした提案。

| ジャンル / Genre | 写真の特徴 / Visual cue | 伸びるステータス / Favours | 下がるステータス |
| --- | --- | --- | --- |
| 棍棒 / Club | 太い、短い、ずんぐり | HP・攻撃 | 速さ |
| 槍 / Spear | 細長い、真っ直ぐ | 攻撃・速さ | 防御 |
| 杖 / Staff | 分岐が多い、節がある | 防御・HP | 攻撃 |
| 弓 / Bow | 湾曲している、しなやか | 速さ・攻撃 | HP |
| 盾 / Shield | 平たい、幅が広い(葉・樹皮) | 防御・HP | 攻撃・速さ |
| 短剣 / Dagger | 小さい、先が鋭い | 速さ(大) | HP・防御 |

- 判別に使う特徴量(縦横比、面積、湾曲、分岐数)は、学習なしの画像解析でも算出できる範囲に収める(`game-mvp-plan.md` §8 の推奨案A)。
- 🔶 第一テスト段階では写真ハッシュからジャンルを割り当て、見た目との対応は Phase 6 で入れる。**構造だけ先に入れておけば、後から判別ロジックを差し替えるだけで済む。**
- ⏸️ 補正の具体的な倍率は**バランス調整として保留**。上の表は「どのステータスが伸びるか」の方向だけを決めたもの。

#### 属性の補正 / Element Modifiers ⚠️ 注意点

属性は**すでに戦闘の相性倍率(×1.5 / ×0.67)で強く効いている**。ここにステータス補正まで加えると、属性の影響が二重になる。

The element **already has a strong effect through the battle multipliers** (×1.5 / ×0.67). Adding stat modifiers on top makes the element double-dip.

**推奨**: 属性のステータス補正は**小さく留める**(方向づけ程度)。ジャンルが「どんな武器か」を決め、属性は「どんな性質か」を薄く添える役割にする。

| 属性 / Element | 性質 / Character | 補正の方向 / Direction |
| --- | --- | --- |
| 木 / Wood | 粘る | HP 寄り |
| 土 / Earth | 硬い | 防御 寄り |
| 水 / Water | 速い | 速さ 寄り |

- ⏸️ 倍率は保留。ジャンル補正より明確に小さくすること、という方針だけ決めておく。
- こうすると「土属性の盾」は防御に極端に寄り、「水属性の短剣」は速さに極端に寄る、という尖った組み合わせが自然に生まれる。ジャンルと属性が同じ方向を向いたときが当たり、という設計。

#### データモデルへの影響 / Impact on the Data Model

⚠️ **`Card` に武器ジャンルのフィールドを追加する必要がある。** 既存の実装には無い。

これは `game-mvp-plan.md` §7 の**セーブデータのスキーマバージョン導入を先に済ませるべき理由が実際に発生した例**。フィールド追加は既存セーブデータの読み込みに影響するため、バージョン管理の仕組みを入れてから行う。

This is a concrete instance of why the schema versioning in `game-mvp-plan.md` §7 should land first: adding a field affects loading existing saves, so the versioning mechanism needs to exist before the field is added.

追加する内容:
- `Card` に `WeaponGenre weaponGenre` を追加(enum、`Card/WeaponGenre.cs`)
- 生成側(`MockCardGenerator` および将来の実装)でジャンルを決定し、補正を適用
- UI 側でジャンルを表示(CardResult、Collection、Battle) + ジャンルアイコン 6種が**アート素材として必要**(§7 に追記)
- 図鑑の軸にジャンルを追加(§3)

### 4.1 ステータス総量の予算制 — ⏸️ 保留(バランス調整は後回し)

> **⏸️ この節の数値調整は後回しと決定。** 欠陥の内容と対処方針だけ記録し、実際の調整は動くものを触ってから行う。実装順としては、まず現状のまま通しプレイできる状態を作る。
> **Deferred.** The defect and the intended fix are recorded here, but the actual tuning waits until we can play the game. Implementation proceeds with the current ranges for now.

**現在の実装には欠陥がある。** ステータス範囲(HP 20-200 / ATK 5-60 / DEF 0-40 / SPD 1-50)が互いに独立しているため、成立しないバトルが生成できてしまう:

- `ATK 60` vs `DEF 0, HP 20` → 1撃で終わる
- `HP 200` vs `ATK 5, DEF 40` → 1ダメージ×200ターン → 打ち切り上限(50ラウンド)に達する

**The current implementation has a flaw.** The four stat ranges are independent, so degenerate matchups are generatable — a one-hit kill at one extreme, and a 1-damage grind that hits the 50-round cap at the other.

**修正案: ステータスに総量予算を設ける。**

各ステータスを共通の「ポイント」に換算し、合計をレアリティで決まる予算に収める。

```
points = hp / 5  +  attack  +  defense  +  speed / 2
```

| レアリティ / Rarity | 予算 / Budget | 出現率(目安) / Rate |
| --- | --- | --- |
| Common | 80 pt | 55% |
| Uncommon | 95 pt | 28% |
| Rare | 110 pt | 13% |
| Epic | 125 pt | 4% |

**この設計が効く理由**: 予算が一定だと、同レアリティ同士の戦闘が常に4〜6回の攻撃で決着する。HPを盛れば攻撃が下がり、攻撃を盛ればHPが下がるため、極端な構成が自動的に排除される。レアリティ差はそのまま有利さになるが、同格対戦のテンポは崩れない。

Why this works: with a fixed budget, same-rarity fights consistently resolve in 4–6 attacks. Piling into HP costs attack and vice versa, so degenerate builds are excluded automatically. Rarity still confers an advantage without wrecking the pacing of an even match.

**検算 / Worked check** (Common 80pt, HP100/ATK30/DEF15/SPD30 = 20+30+15+15 = 80):
`baseDamage = 30 - 15/2 = 23` → `HP100 / 23 ≈ 4.3 攻撃`。属性有利(×1.5)なら約3回、不利(×0.67)なら約6.5回。狙いどおりの範囲。

Epic 同士(125pt, HP140/ATK45/DEF24/SPD46 = 28+45+24+23 = 120):
`45 - 12 = 33` → `140 / 33 ≈ 4.2 攻撃`。レアリティが上がってもテンポは一定。

- ⏸️ 予算値と換算係数は**後回し**。上の数値は出発点として記録しておくだけで、今は実装しない。
- 導入する際は、各ステータスの上下限は現状の `StatBlock` の範囲を維持し、予算制はその内側で働く二重の制約とする。
- **当面の割り切り**: 極端な組み合わせが出ても、打ち切り上限(50ラウンド)があるためゲームが停止することはない。プレイ体験としては不自然だが、通しプレイの検証は可能。

### 4.2 属性の割り当て / Element Assignment

- 第一テスト段階: 写真ハッシュから均等に3分割。
- 完成版: 写真の色調から導出する(緑が優勢 → 木、茶/灰 → 土、青/濡れた質感 → 水)。「見た目と属性が噛み合っている」感覚は体験の質に直結するため、ここは早めに色ベースに移す価値がある。
  - 🔶 実装時期: MVP に入れるか Phase 6 に回すか未決定。**推奨**: MVP に入れる。平均色の算出だけなので軽く、体験への効きが大きい。

### 4.3 カード名とフレーバー / Names and Flavor Text

語のテーブルを組み合わせて生成する(接頭辞 × 接尾辞 + 属性語)。

- 🔶 語彙は**人力で用意する**。現在の実装は仮の語彙。ここは文章の質がそのままゲームの雰囲気になるため、時間をかける価値がある。
- 必要量の目安: 接頭辞 30語 × 接尾辞 20語 = 600通り程度あれば、同じ名前に当たる体験は稀になる。
- フレーバーは属性 × レアリティの 12通り分を各3〜5本用意すれば当面足りる。

---

## 5. バトルの設計 / Battle Design

### 5.1 現行ルール(実装済み)

- 属性三すくみ: 木 → 土 → 水 → 木。有利 ×1.5 / 不利 ×0.67 / 同属性 ×1.0
- 行動順: SPD が高い方が先制。同値はプレイヤー先手
- `baseDamage = max(1, ATK - DEF/2)`、`finalDamage = round(baseDamage × 属性倍率 × 乱数[0.9-1.1])`、最低1
- 1ラウンド = 両者が1回攻撃(先手で倒れたら後手は攻撃しない)
- 打ち切り: 50ラウンドで HP 割合の高い方の勝ち

> **⏸️ 以下 5.2〜5.4 の数値・段階設計は後回し。** 現行ルールのまま実装を進め、触ってから詰める。ここに書いてあるのは「何を後で決める必要があるか」の一覧。
> **Deferred (§5.2–5.4).** Implementation proceeds on the current rules; these sections record what will need deciding later.

### 5.2 設計上の含意 / What the rules imply

- **属性相性が勝敗の主要因**。×1.5 と ×0.67 の差は 2.2 倍あり、4〜6攻撃で決着する設計ではほぼ覆せない。これは意図どおり(「相性の良いカードを探して外に出る」動機になる)だが、プレイヤーに**相性が見えている**必要がある。バトル画面に相性表示は必須。
- **乱数の幅 ±10% は小さい**。実力差がそのまま出る。運で覆る展開を望むなら幅を広げるが、その場合「良いカードを探す」動機が薄まる。🔶 **推奨**: 現状の ±10% を維持。
- **プレイヤーの選択がほぼ無い**。現行は「攻撃」ボタンを押すだけで、戦術的な判断が存在しない。MVP としては許容範囲だが、繰り返し遊ぶには薄い。

### 5.3 プレイヤーの選択を増やす案 🔶

MVP 後の候補。いずれも「屋外で集める意味」を強める方向のものを優先する。

- **手札3枚から選んで出す**: 相手の属性を見てから選ぶ。コレクションを増やす動機に直結する。**最も推奨**。
- **1回だけ使える行動**(踏ん張る/狙う等): 実装は軽いが、集める動機とは無関係。
- **カードを2枚組ませる**: 組み合わせの妙が生まれるが、バランス設計の複雑さが跳ね上がる。

**推奨**: MVP では 1vs1 のまま出し、プレイテストで薄さを確認してから「手札3枚」を追加する。

### 5.4 CPU の段階 / CPU Ladder 🔶

現在は固定3体(Wood/Earth/Water、Common〜Rare)。進行設計を「軽い挑戦要素」にする場合、段階を用意する必要がある。

**推奨構成**: 5段階 × 3属性 = 15体。予算 70pt(入門)から 130pt(最上位)まで。プレイヤーの手持ち最強カードに応じて段を選ぶのではなく、**勝つと次の段が開く**方式にする(手持ちに応じた自動調整は、強いカードを引いた喜びを打ち消すため)。

- 🔶 15体分のステータスと名前は人力で用意する必要がある。

---

## 6. 屋外要素の組み込み / Outdoor Mechanics 🔶

現在実装済み:
- 撮影経路が `WebCamTexture` のみ = ギャラリー取り込みのコードパスが存在しない(構造的な担保)
- 写真の鮮度チェック(既定10分以内)
- 前回撮影地点からの移動距離チェック(既定30m、**既定オフ**)

**🔶 どれを有効にして出すか未決定。推奨:**

| 要素 / Mechanic | 推奨 / Recommendation | 理由 / Reason |
| --- | --- | --- |
| 移動距離チェック | **オンにする**(30m) | 「歩く」ことをゲームに組み込む最も直接的な手段。実装済みで追加コストがない |
| 1日の撮影上限 | **入れる**(🔶 枚数は要決定、10枚程度から) | 一度に大量に撮って飽きるのを防ぎ、毎日少しずつ外に出る動機になる。実装は軽い |
| 時間帯・季節の反映 | **MVP後** | 繰り返し外に出る強い理由になるが、検証に実時間がかかる(季節は1年) |
| 歩数・移動距離の累積 | **MVP後** | 目的に最も直結するが、常時計測は電池とパーミッションの負担が大きく、設計を別途詰める必要がある |

**移動距離チェックの注意点**: 位置情報が取得できない場合(屋内、許可なし、GPS不良)は**必ず通す**実装になっている。取れないことでプレイヤーを詰ませない方針は維持する。副作用として、位置情報を許可しないプレイヤーはこの制約を回避できるが、単独プレイで競争もないMVPでは許容する。

---

## 7. アート・素材要件 / Art Asset Requirements

**イラスト・画像はAI生成せず人力で用意する。** 必要なものを洗い出しておく。

| 素材 / Asset | 用途 / Use | 必須度 |
| --- | --- | --- |
| カードフレーム(4レアリティ分) | 写真を囲む枠。レアリティが一目で分かる必要がある | 必須 |
| 属性アイコン(3種) | 木・土・水。バトル画面とコレクションで多用する | 必須 |
| 武器ジャンルアイコン(🔶 6種) | 棍棒・槍・杖・弓・盾・短剣。カードの性格を一目で示す(§4.0) | 必須 |
| ステータスアイコン(4種) | HP/攻撃/防御/速さ | 推奨 |
| ボタン・パネルの背景 | 現在はプログラム生成の無地。屋外の直射日光下で読める配色が要る | 必須 |
| CPU対戦相手の絵(🔶 15体) | 写真がない相手側のカード表現。代替として属性アイコンの拡大でも成立する | 要検討 |
| タイトル画面 | ゲームの第一印象 | 必須 |
| アプリアイコン | ストア掲載に必須 | 必須 |
| 日本語フォント | **TextMeshPro は CJK グリフを持たないため豆腐になる。** 現在は Unity 標準の `Text` でシステムフォントにフォールバックさせている。デザインを作り込むなら CJK 対応フォントの選定とライセンス確認が必要 | 要検討 |

---

## 8. 安全・プライバシー / Safety and Privacy

このゲームは**屋外に人を出し、カメラと位置情報を使う**。設計段階で決めておかないと、リリース直前に手戻りする領域。

### 8.1 安全 / Safety

- 初回起動時と撮影画面に**歩きスマホの注意**を出す。屋外に出すことを目的にしたゲームである以上、これは責任の範囲。
- 「危険な場所・私有地に入らない」旨の注意を初回に表示する。
- レアなカードを求めて危険な場所に行く動機を作らない。具体的には、**特定の場所でしか出ないカードは作らない**(位置に紐づくレア要素は立ち入り事故を誘発する)。
- 夜間の要素を入れる場合(🔶 時間帯軸)、安全上の注意をより強く出す。

### 8.2 プライバシー / Privacy

| 扱うデータ / Data | 保存先 / Stored | 送信 / Transmitted |
| --- | --- | --- |
| 撮影した写真 | 端末内のみ(`persistentDataPath`) | **しない** |
| 撮影位置(緯度経度) | 端末内のみ、任意 | **しない** |
| 撮影時刻 | 端末内のみ | **しない** |

- **通信しない設計そのものが最大のプライバシー保護**になっている。オンデバイス生成を選んだ副次的な利点として明記しておく価値がある。
- ただし**ストア申告は必要**。カメラと位置情報のパーミッションを要求するため、プライバシーポリシーの掲示が Google Play / App Store 双方で必須。「端末外に送信しない」と明記する。
- 位置情報は**任意**であり、拒否してもゲームは遊べる実装になっている。この性質はストア申告でも説明しやすい。
- 🔶 **未成年の利用**: 想定ユーザーに子どもが含まれるなら、年齢別レーティングと保護者向けの説明が必要。位置情報を扱う点は特に説明を要する。**要確認事項**。

---

## 9. 画面フローと各画面の要件 / Screen Flow and Requirements

1シーン + `ScreenRouter` による画面切り替え(`game-mvp-plan.md` 2.8 参照)。

```
Title ──┬─→ Capture ──→ CardResult ──┬─→ Battle ──→ Result ──┬─→ Collection
        │                             │                        │
        └─→ Collection ───────────────┴────→ Battle            └─→ Title
```

| 画面 / Screen | 必須要件 / Requirements |
| --- | --- |
| **Title** | 所持カード数の表示。「外に出て枝を撮る」という前提が初見で分かる説明。初回は安全上の注意 |
| **Capture** | 片手操作。直射日光下で読める。カメラ許可の拒否・カメラ無し・鮮度切れ・移動不足の各状態で何をすべきか分かるメッセージ。生成中の待ち表示 |
| **CardResult** | 写真・名前・属性・レアリティ・4ステータス・フレーバー。レアリティが一目で分かること。「バトルする」と「しまう(コレクションへ)」 |
| **Collection** | 新しい順の一覧。件数が増えても破綻しないこと(🔶 現在は50件で打ち切り + 総数表示)。空のときは撮りに行く導線 |
| **Battle** | 両者のHPバーと数値。**属性相性の表示(必須)**。1ラウンドずつ進む操作。ターンログ |
| **Result** | 勝敗・残HP・ラウンド数。コレクションへ / タイトルへ |

**カードは生成時点で自動保存される**(CardResult の「追加」ボタン待ちにしない)。屋外まで歩いて手に入れたカードを誤操作で失わせないため。

---

## 10. 対応環境 / Target Environment

- Android を優先(実機での反復が速い)、iOS は後追い。
- 🔶 最低対応バージョン: Android 7.0 (API 24) を暫定とする。カメラと位置情報が使えれば足りるため、要件は低い。
- ストレージ: 写真1枚あたりサムネイル約 50-150KB。1000枚で 150MB 程度。
  - ⚠️ **現在の実装は撮影元画像を `captures/` に溜め続けており、削除処理が無い。** 長期プレイで容量を食う。要対応(`game-mvp-plan.md` の残作業に記載)。
- 電池: バトルもコレクションも軽い。カメラプレビューを開いている時間が最大の消費源なので、撮影画面を離れたら確実にカメラを止める(実装済み)。
