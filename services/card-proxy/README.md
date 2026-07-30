# card-proxy

写真からカードを生成する最小プロキシサービス (Cloudflare Worker / TypeScript)。
A minimal card-generation proxy service (Cloudflare Worker / TypeScript).

---

## 🔑 なぜクライアントから直接AIを呼ばないのか / Why the client does not call the AI directly

**モバイルアプリのバイナリに埋め込んだAPIキーは、取り出せてしまいます。** このゲームは実機を持って屋外に出て遊ぶことが前提なので、APIキーを含むビルドが端末上に置かれる時間も機会も多くなります。抽出されたキーは第三者に使われ、請求はこちらに来ます。

そのため、Unityクライアントは**このプロキシだけ**と通信し、Anthropic APIのキーはサーバー側 (Cloudflare の secret) にのみ存在します。このサービスの存在理由はそれだけです。追加のビジネスロジックは持ちません。

**An API key shipped inside a mobile app binary can be extracted from it.** This game is meant to be carried around outdoors on real devices, so builds containing a key sit on real handsets for a long time. An extracted key gets used by other people, and the bill lands on us.

So the Unity client talks **only to this proxy**, and the Anthropic API key exists only server-side (as a Cloudflare secret). That is the entire reason this service exists; it holds no other business logic.

Cloudflare Worker を選んだ理由は、1ファイルで完結し、運用するインフラが無いためです。
A Cloudflare Worker was chosen because it is a single file with no infrastructure to run.

---

## ⚙️ セットアップ / Setup

```bash
cd services/card-proxy

npm install                              # 依存関係の取得 / install dependencies
npx wrangler login                       # Cloudflare アカウントに接続 / connect your Cloudflare account

# ローカル開発 (.dev.vars を使う) / local development (uses .dev.vars)
cp .dev.vars.example .dev.vars           # そして実際のキーを書き込む / then write your real key into it
npm run dev                              # http://127.0.0.1:8787 で起動 / serves on http://127.0.0.1:8787

# 本番デプロイ / deploy
npx wrangler secret put ANTHROPIC_API_KEY   # キーを secret として登録 / register the key as a secret
npm run deploy

npm run typecheck                        # 型チェックのみ / type-check only
```

`.dev.vars` はローカル開発専用です。デプロイ先が読むのは `wrangler secret put` で登録した値です。
`.dev.vars` is for local development only; the deployed Worker reads the value registered with `wrangler secret put`.

デプロイ確認 / verify a deployment is live:

```bash
curl https://card-proxy.<your-subdomain>.workers.dev/health
# -> {"ok":true}
```

---

## 📡 APIコントラクト / Wire contract

Unityクライアントはこの仕様に対して実装されています。**変更する場合は必ず両方を同時に直してください。**
The Unity client is written against exactly this contract. **If you change it, change both sides together.**

### リクエスト / Request

```
POST /generate
Content-Type: application/json

{ "image_base64": "<base64-encoded JPEG bytes, no data: URI prefix>" }
```

### 成功 / Success — HTTP 200

```json
{
  "name": "苔むした古枝",
  "element": "Wood",
  "rarity": "Rare",
  "stats": { "hp": 120, "attack": 34, "defense": 18, "speed": 22 },
  "flavorText": "湿った森の匂いをまだ覚えている。",
  "reasoning": "thick bark, heavy moss, multiple forks"
}
```

| フィールド / Field | 内容 / Contents |
| --- | --- |
| `name` | 日本語のカード名、24文字以内 / Japanese card name, ≤ 24 characters |
| `element` | `"Wood"` \| `"Earth"` \| `"Water"` のいずれか / exactly one of these |
| `rarity` | `"Common"` \| `"Uncommon"` \| `"Rare"` \| `"Epic"` のいずれか / exactly one of these |
| `stats.hp` | 20–200 の整数 / integer in 20–200 |
| `stats.attack` | 5–60 の整数 / integer in 5–60 |
| `stats.defense` | 0–40 の整数 / integer in 0–40 |
| `stats.speed` | 1–50 の整数 / integer in 1–50 |
| `flavorText` | 日本語の一文、80文字以内 / one Japanese sentence, ≤ 80 characters |
| `reasoning` | ステータス根拠の英語メモ (プロンプト品質のデバッグ用) / short English note on the visual features that drove the stats, for prompt debugging |

数値は**サーバー側でも**範囲にクランプされます (`normalizeCard`)。クライアント側の `StatBlock.Clamped()` と二重に守っており、両者の範囲は一致していなければなりません。AIの生の出力がそのままクライアントに渡ることはありません。

Stats are clamped **server-side too** (`normalizeCard`), duplicating the client's `StatBlock.Clamped()`; the two range tables must stay in sync. Raw model output is never passed straight through to the client.

### エラー / Errors — any non-2xx

```json
{ "error": "message" }
```

| Status | いつ / When | `error` の内容 / Contents of `error` |
| --- | --- | --- |
| **400** | `image_base64` が無い / 不正 / `data:` 付き / 約5MB超、またはボディがJSONとして壊れている | 英語の診断メッセージ / English diagnostic message |
| **405** | `POST /generate` 以外へのアクセス / anything that is not `POST /generate` | 英語 / English |
| **422** | **写真が自然物ではない** / the photographed subject is not a natural object | **プレイヤーにそのまま表示する日本語** / player-facing Japanese, displayed verbatim |
| **500** | `ANTHROPIC_API_KEY` 未設定 (サーバー側にログを出す) / key not configured (logged server-side) | 汎用メッセージのみ / generic message only |
| **502** | 上流のAI呼び出しが失敗、または使えない出力が返った (1回リトライ後) | 英語 / English |

**422 が重要です。** このゲームの目的は人を外に出して自然を見させることなので、机やPC画面を撮った写真はカードにせず拒否します。返る日本語は以下の固定文で、クライアントはこれをそのまま表示します:

**The 422 case matters.** The whole point of the game is getting people outdoors looking at nature, so a photo of a desk or a screen is refused rather than turned into a card. The Japanese string is fixed (below) and the client shows it directly to the player:

```
自然のものが写っていないみたい。木の枝や葉っぱ、草花、石など、外にある自然物を撮ってみてね。
```

判定はモデル自身に行わせ、`{"rejected": true, "rejectReason": "..."}` を返させてこの422にマッピングしています。モデルが書いた `rejectReason` はサーバーログにのみ残り、プレイヤーには見せません (生成物をそのまま表示しないため)。

The model makes the judgement itself and returns `{"rejected": true, "rejectReason": "..."}`, which is mapped to this 422. The model's own `rejectReason` is logged server-side only and never shown to the player.

---

## 🎮 Unityクライアントの接続先設定 / Pointing the Unity client at it

`Assets/Resources/AppConfig.asset` の **`cardProxyUrl`** に、デプロイしたWorkerのURLを**末尾のスラッシュなし**で設定します。クライアントが `/generate` を付けるためです。

Set **`cardProxyUrl`** on the game's `AppConfig` asset (`Assets/Resources/AppConfig.asset`) to the deployed Worker URL, **without a trailing slash** — the client appends `/generate` itself.

```
✅ https://card-proxy.<your-subdomain>.workers.dev
❌ https://card-proxy.<your-subdomain>.workers.dev/          ← 末尾スラッシュ / trailing slash
❌ https://card-proxy.<your-subdomain>.workers.dev/generate  ← /generate は付けない / do not append
```

`AppConfig` の `useMockCardGenerator` を `false` にすると実際にこのプロキシを叩きます。`true` の間は通信せずローカル生成のままです。
Set `useMockCardGenerator` to `false` on the same asset to actually call this proxy; while it is `true` the client generates cards locally with no network call.

---

## 🧪 curl での動作確認 / Testing with curl

```bash
# base64画像を含むJSONを組み立てて投げる (macOS / Linux)
# Build the JSON with a base64 image and post it (macOS / Linux)
PROXY=https://card-proxy.<your-subdomain>.workers.dev   # ローカルなら http://127.0.0.1:8787

# macOS: base64 -i branch.jpg   /   Linux: base64 -w0 branch.jpg
B64=$(base64 -w0 branch.jpg 2>/dev/null || base64 -i branch.jpg)

printf '{"image_base64":"%s"}' "$B64" > /tmp/card-req.json

curl -sS -X POST "$PROXY/generate" \
  -H "Content-Type: application/json" \
  --data-binary @/tmp/card-req.json | jq
```

エラー系の確認 / checking the error paths:

```bash
curl -sS -i "$PROXY/generate"                                   # -> 405
curl -sS -i -X POST "$PROXY/generate" -H 'Content-Type: application/json' -d '{}'      # -> 400
curl -sS -i -X POST "$PROXY/generate" -H 'Content-Type: application/json' -d 'not json' # -> 400
curl -sS "$PROXY/health"                                        # -> {"ok":true}
```

机やPC画面を撮った写真を投げて **422 と日本語メッセージ**が返ることも確認してください。ここがゲームの主旨に直結する挙動です。
Also post a photo of a desk or a monitor and confirm you get **422 with the Japanese message** — that behaviour is what ties the service to the point of the game.

---

## 🤖 AI呼び出しの構成 / How the AI call is configured

`src/index.ts` の先頭の定数にまとまっています。 / All of it lives in the constants at the top of `src/index.ts`.

| 項目 / Item | 値 / Value | 理由 / Why |
| --- | --- | --- |
| モデル / Model | `claude-opus-5` | 既定の推奨モデル。判定品質を優先 / the recommended default; prioritises judgement quality |
| `max_tokens` | 4096 | Opus 5 は thinking が既定でON。`max_tokens` は thinking と本文の合計上限なので余裕を持たせる / thinking is on by default and shares this budget with the response |
| `output_config.effort` | `low` | 短い画像判定タスクなので、レイテンシとコストを抑える / short visual-classification task; keeps latency and cost down |
| `output_config.format` | `json_schema` | 構造化出力でスキーマ準拠を保証。加えて防御的パースも残している / guarantees a schema-valid object; the defensive parser is kept as a second line of defence |
| 出力の安定性 / Output consistency | プロンプト + 低effort / prompt + low effort | **`temperature` は Claude Opus 5 では送れません (400になります)。** 「低いtemperatureで安定させる」意図はプロンプトの指示と effort で実現しています / **`temperature` is rejected with a 400 on Claude Opus 5**, so the "low temperature for consistency" intent is expressed through the prompt and effort instead |
| プロンプトキャッシュ / Prompt caching | system ブロックに `cache_control` | プロンプトは固定なので、繰り返し呼び出しで入力コストを下げられる / the prompt never varies, so repeated calls read it from cache |
| リトライ / Retry | 1回 / once | 出力がパースできない・切り詰められた・通信が瞬断した場合のみ。それ以上は502 / only for unparseable or truncated output and transport blips; beyond that, 502 |
| `fallbacks` | `"default"` (beta) | Opus 5 は安全分類器でリクエストを拒否しうるため / Claude Opus 5 can decline a request via safety classifiers |

`fallbacks` は **beta機能**です (`ENABLE_REFUSAL_FALLBACK`)。アカウントで有効でない場合は上流が400を返し、全リクエストが502になります。その場合は `src/index.ts` の `ENABLE_REFUSAL_FALLBACK` を `false` にするだけで外せます。枝の写真が分類器に引っかかることはほぼ無いため、外しても実質的な損失はありません。

`fallbacks` is a **beta feature** (`ENABLE_REFUSAL_FALLBACK`). If the account is not enrolled, the upstream call 400s and every request becomes a 502 — flip `ENABLE_REFUSAL_FALLBACK` to `false` in `src/index.ts` to remove it. Photos of branches are very unlikely to trip a classifier, so nothing meaningful is lost.

---

## 🚧 公開前に必ず追加すること / Before going public

現状は **MVP・開発者1人・開発用途のみ**の想定です。認証なし、DBなし、レート制限はサイズガードだけ。**このままURLを公開すると、誰でもあなたのAPIキーで課金を発生させられます。**

This is scoped for **MVP, single developer, dev-only**: no user auth, no database, no rate limiting beyond a basic size guard. **If you publish this URL as-is, anyone can spend your API budget.**

- [ ] **認証 / Auth** — エンドポイントが誰にでも開いている状態をやめる。最低でも共有シークレットヘッダ、本来はプレイヤー単位のトークン。 / Close the open endpoint: at minimum a shared secret header, properly a per-player token.
- [ ] **ユーザー単位のレート制限 / Per-user rate limiting** — 1端末あたりの1日の生成回数に上限を設ける (Cloudflare Rate Limiting や KV/Durable Objects でカウント)。 / Cap generations per device per day (Cloudflare Rate Limiting, or a counter in KV / Durable Objects).
- [ ] **リクエストの真正性検証 / Request attestation** — 本物のアプリからの呼び出しだけを通す (Play Integrity API / App Attest)。 / Ensure only the real app can call it (Play Integrity API / App Attest).
- [ ] **可観測性 / Observability** — 成功率・レイテンシ・422率・502率・1日あたりコストを見られるようにする (Workers Analytics、Logpush、または外部サービス)。今は `console` ログのみで、`wrangler tail` を見ないと何も分からない。 / Track success rate, latency, 422 rate, 502 rate, and daily cost (Workers Analytics, Logpush, or an external service). Today there is only `console` logging, visible via `wrangler tail`.

### 🔐 APIキーの扱い / API keys

**APIキーをこのリポジトリにコミットしてはいけません。** `wrangler.toml` の `[vars]` にも書かないこと (コミットされます)。キーの置き場所は次の2つだけです:

**API keys must never be committed to this repository.** Do not put one under `[vars]` in `wrangler.toml` either — that file is committed. There are exactly two places a key belongs:

1. ローカル開発 / local development → `.dev.vars` (このディレクトリの `.gitignore` で除外済み / git-ignored by this directory's `.gitignore`)
2. デプロイ先 / the deployed Worker → `npx wrangler secret put ANTHROPIC_API_KEY`

万一コミットしてしまったら、履歴から消すよりも先に**キーをローテーション (無効化して再発行)** してください。
If a key is ever committed, **rotate it first** — revoke and reissue — before worrying about rewriting history.
