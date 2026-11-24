# simulator/ — 人間 vs AI 仮想トレードシミュレータ

株・ETF・為替の**仮想**売買ゲーム。人間(ブラウザ / Unity)と AI(MCP / REST)が同じ市場・同じ条件で総資産を競う。
**実際のお金・証券口座には一切接続しない。**

## 起動(ローカル)
```bash
cd simulator
pip install -r requirements.txt
python -m server.app                       # 遅延実株価 (Yahoo Finance)
SIM_FEED=synthetic python -m server.app    # 合成相場 (24h動く・オフライン可)
```
ブラウザで http://127.0.0.1:8000 を開き、名前を登録すれば参加できる。

## AI の参加方法
| 方法 | 手順 |
|---|---|
| MCP (Claude Desktop / Claude Code 等) | 下記設定を追加。初回呼び出し時に `SIM_NAME` で自動登録 |
| LM Studio | ローカルサーバ起動後 `python bots/lmstudio_bot.py 名前 モデル名` |
| ルールBot(基準用) | `python bots/rule_bot.py 名前` |
| 任意のプログラム | 下記 REST API |

```json
{ "mcpServers": { "citai-market": {
  "command": "python", "args": ["<このフォルダの絶対パス>/server/mcp_server.py"],
  "env": { "SIM_NAME": "claude" } } } }
```
Claude Code なら: `claude mcp add citai-market -e SIM_NAME=claude -- python "$PWD/server/mcp_server.py"`(simulator/ で実行)

## ルール
- 初期資金 1,000万円(仮想)。総資産 = 現金 + 保有評価額(円換算、USD銘柄は USDJPY で換算)
- 成行は即時約定(買いは Ask、売りは Bid)。指値は価格到達時に約定。手数料 0.05%
- 空売り不可。売買単位(日本株100株、為替1,000通貨など)の倍数のみ
- **速度差込み**:発注の速さも実力。レート制限は DoS 対策(既定 50回/秒)のみ
- データ遅延:Yahoo の遅延値(東証は約20分)。**他所のリアルタイム株価を見て売買するのは禁止**(技術的には防げないため紳士協定)
- フィードが一定時間更新されない銘柄は自動で取引停止(古い価格での裁定防止)
- 1人1アカウント。`kind`(人間/AI)は自己申告

## 設定 (環境変数)
| 変数 | 既定 | 内容 |
|---|---|---|
| `SIM_FEED` | yahoo | `yahoo` / `synthetic` |
| `SIM_POLL` | 20 / 1 | 価格更新間隔(秒) |
| `SIM_SPEED` | 60 | 合成相場の時間倍率 |
| `SIM_SEED` | なし | 合成相場の乱数シード(指定すると再現可能=予測可能になる) |
| `SIM_CASH` | 10000000 | 初期資金 |
| `SIM_FEE_BPS` | 5 | 手数料 (bp) |
| `SIM_STATE` | data/state.json | 保存先。データソースや初期資金を変えるときは別ファイルにする |
| `SIM_HOST` / `SIM_PORT` | 127.0.0.1 / 8000 | 待受 |
| `SIM_HOSTS` | 127.0.0.1,localhost | 許可 Host ヘッダ(DNSリバインディング対策) |

新しいゲームを始める = サーバ停止 → `data/` を削除 → 起動。

## REST API
`Authorization: Bearer <token>` が必要なものは 🔒。リストは常に `{"items": [...]}`(Unity JsonUtility 対応)。
| メソッド | パス | 内容 |
|---|---|---|
| GET | `/api/quotes` | 全銘柄の価格・Bid/Ask・単位 |
| GET | `/api/history/{sym}?n=500` | 価格履歴 |
| GET | `/api/leaderboard` | ランキング |
| GET | `/api/info` | 設定・フィードエラー |
| POST | `/api/register` `{name, kind}` | 登録 → token |
| GET 🔒 | `/api/me` | 口座 |
| GET 🔒 | `/api/orders` | 注文履歴 |
| POST 🔒 | `/api/orders` `{sym, side, qty, type, limit}` | 発注 |
| DELETE 🔒 | `/api/orders/{id}` | 取消 |
| WS | `/ws` | 毎ティック `{type:"tick", quotes, board}` を配信 |

## Unity 連携
`unity/MarketClient.cs` を `software/Assets/Scripts/Economy/` にコピーし、GameObject に付ける。
- Unity 2021.2 以降(C# 8)
- Player Settings → *Allow downloads over HTTP* = *Always allowed*(localhost 接続のため)
```csharp
StartCoroutine(market.PlaceOrder("7203.T", "buy", 100, 0, o => Debug.Log(o.status), Debug.LogError));
```

## テスト
`python -m pytest -q tests`

## 注意
- Yahoo Finance の非公式エンドポイントを使用。個人・ローカル利用に限ること。仕様変更で取得できなくなる可能性あり(その場合は `SIM_FEED=synthetic`)
- 市場時間外は価格が動かない(東証・米国市場の取引時間のみ変動)
