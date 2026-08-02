# Android 実機テスト手順 / Android Device Testing

USBデバッグを有効にした検証専用の Android 端末を使った確認手順。

Procedures for testing on a dedicated Android device with USB debugging enabled.

---

## 0. なぜ実機が必要か / Why a device is required

**このプロジェクトの未検証事項のほとんどは、実機以外では答えが出ません。** Unity Editor で PC の Web カメラを使う確認は「ロジックが動くか」までで、以下は実機でしか判定できません。

Most of this project's open questions **cannot be answered anywhere but on a device**. Editor testing with a PC webcam verifies the logic; the following cannot be judged any other way:

| 確認事項 / What | なぜ実機のみ / Why device-only |
| --- | --- |
| **撮影画像の向き** | 縦持ちの端末で `WebCamTexture` が横向きバッファを返すかは端末とOSに依存する。**最初に出ると予想している不具合** |
| カメラ権限の挙動 | Android では権限付与前 `WebCamTexture.devices` が空になる。Editor では再現しない |
| ボタンがタッチに反応するか | `Active Input Handling` の設定ミスは無言で「反応しない」になる。マウス操作では気づけない |
| 日本語が豆腐にならないか | フォントのフォールバックはプラットフォームごとに違う |
| 直射日光下の可読性 | 屋外前提のゲームとして最も重要な UX 要件。PC では検証不能 |
| 片手操作の成立 | タッチ領域の大きさと配置 |
| 位置情報の取得と移動距離チェック | GPS がなければ動かない |
| ストレージの増え方 | 撮影を重ねた実データでしか分からない |
| **屋外で実際に遊べるか** | ゲームの前提そのもの |

**検証専用端末だからこそできること**: 権限を剥がす、ストレージを埋める、機内モードで放置する、セーブデータを壊す — 普段の端末では試しにくい破壊的なテストを気兼ねなく実行できます。これは価値が高いので積極的に使ってください。

**What a throwaway device buys you**: revoking permissions, filling storage, leaving it in airplane mode, corrupting the save file — destructive tests you would hesitate to run on your daily phone. Use it for exactly that.

---

## 1. 端末側の準備 / Device setup

1. **設定 → 端末情報 → ビルド番号を7回タップ**して開発者オプションを有効化(済みの場合は不要)。
   Enable Developer options by tapping **Build number** seven times (skip if already done).
2. **開発者オプション**で以下をオンに:
   In **Developer options**, turn on:
   - **USB デバッグ** / USB debugging
   - **USB経由でのインストールを許可** / Install via USB(端末により名称が異なる)
   - 🔶 **スリープしない**(充電中) / Stay awake while charging — 検証中に画面が消えないので便利
3. PC に USB 接続し、端末側に出る「USBデバッグを許可しますか」で**このPCを常に許可**にチェックして許可。
   Connect over USB and accept the "Allow USB debugging" prompt, ticking *always allow from this computer*.
4. 確認:
   ```sh
   adb devices
   ```
   `device` と表示されれば成功。`unauthorized` なら手順3の許可が済んでいません。
   A line ending in `device` means success; `unauthorized` means step 3 was not accepted.

> `adb` は Unity の Android Build Support に同梱されています。パスが通っていない場合は
> `<Unityインストール先>/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb` にあります。
> `adb` ships with Unity's Android Build Support; if it is not on your PATH, it lives under
> `<Unity install>/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb`.

---

## 2. Unity 側のビルド設定 / Unity build settings

**File → Build Settings** で Platform を **Android** に切り替え(**Switch Platform**)、以下を設定します。

| 場所 / Where | 設定 / Setting | 理由 / Why |
| --- | --- | --- |
| Build Settings | **Development Build** ✅ | エラーにスタックトレースが付く。**初回はほぼ必須** |
| Build Settings | **Script Debugging** ✅ | 例外の発生箇所が特定できる |
| Build Settings | **Run Device** = 接続した端末 | |
| Player → Other Settings | **Active Input Handling** = `Both` | これが `Input System Package (New)` のみだと**ボタンが一切反応しない**(`setup-unity.md` 2.5) |
| Player → Other Settings | **Minimum API Level** = 24 以上 | 🔶 暫定 |
| Player → Other Settings | **Package Name** を設定 | 既定の `com.DefaultCompany.*` のままだと adb コマンドで指定しづらい。例: `com.rubicontmt9.citaitokens` |
| Player → Resolution | **Default Orientation** = `Portrait` | コード側でも固定しているが、起動時のちらつきを防ぐ |
| Player → Other Settings | **Write Permission** | `persistentDataPath` の場所が変わる。どちらでも動くが、起動ログで実際のパスを確認すること |

**Build And Run**(`Ctrl+B` / `Cmd+B`)で端末に直接インストールされます。

⚠️ **Development Build は必ず外してから配布用ビルドを作ってください。** 動作が遅く、デバッグ情報を含みます。

⚠️ **Always turn Development Build off for a distributable build** — it is slower and carries debug information.

---

## 3. ログの読み方 / Reading logs

**このコードは初回コンパイル前で、実機で何が起きるか未知です。ログを見ながら進めるのが前提になります。**

```sh
# Unity のログだけを流す(最も使う)
adb logcat -s Unity

# 過去のログを消してから流す(直前の実行だけを見たいとき)
adb logcat -c && adb logcat -s Unity

# ファイルに保存しながら流す
adb logcat -s Unity | tee run.log

# エラーと警告だけ
adb logcat -s Unity:E Unity:W
```

起動時に `GameBootstrap` が以下を出します。ここが出ていなければ起動に失敗しています。

- どちらのカード生成器を選んだか(通常はローカル生成)
- `persistentDataPath = ...` ← **セーブデータと撮影画像の実際の保存先。以降の調査で使います**
- 作成した EventSystem の入力モジュール名(`InputSystemUIInputModule` か `StandaloneInputModule`)

---

## 4. 検証レシピ / Verification Recipes

`<pkg>` は Player Settings で設定した Package Name に読み替えてください。

Replace `<pkg>` with the package name you set in Player Settings.

### 4.1 撮影画像の向き ← 最優先 / Photo orientation — do this first

**予想される不具合の本命。** 縦持ちの端末で `WebCamTexture` は横向きバッファを返すことが多く、保存される JPEG が横倒しになる可能性が高い(未実装の既知課題)。

1. **上下が明確なもの**を撮る。紙に大きく「上」と書いて撮るのが確実。
2. 起動ログの `persistentDataPath` を確認し、そのパスの `captures/` から画像を取り出す:
   ```sh
   # 外部ストレージの場合
   adb shell ls /sdcard/Android/data/<pkg>/files/captures/
   adb pull /sdcard/Android/data/<pkg>/files/captures/ ./captures/

   # 内部ストレージの場合(Development Build なら run-as が使える)
   adb shell run-as <pkg> ls files/captures/
   adb exec-out run-as <pkg> cat files/captures/<ファイル名>.jpg > out.jpg
   ```
3. PC で開いて向きを確認する。横倒しなら向き補正の実装が必要。
4. 同時に**カードのサムネイル**(`cards/`)も確認する。プレビュー画面が正しく見えていても保存画像が回っていることがあるため、**画面の見た目ではなく保存されたファイルで判定してください。**

**Judge from the saved file, not from what the preview looked like** — the preview can be corrected while the stored JPEG is not.

### 4.2 権限拒否の経路 / Permission denial path

検証専用端末なので、権限を剥がして何度でも再現できます。

```sh
# カメラ権限を剥がす
adb shell pm revoke <pkg> android.permission.CAMERA
# 位置情報を剥がす
adb shell pm revoke <pkg> android.permission.ACCESS_FINE_LOCATION
# 付与し直す
adb shell pm grant <pkg> android.permission.CAMERA
```

確認すること: 拒否したときに**日本語で状況と次にすべきことが表示され、行き止まりにならないか**。「カメラが使えません」で終わって戻れない、が最悪の状態です。

### 4.3 セーブデータの往復 / Save round-trip

`Card` は非公開フィールド + 読み取り専用プロパティの構成なので、**Newtonsoft が正しく往復できていないと無音で空のカードが読み込まれます。**目視で気づきにくい種類の不具合です。

```sh
# セーブファイルの中身を見る
adb shell cat /sdcard/Android/data/<pkg>/files/collection.json
# または
adb exec-out run-as <pkg> cat files/collection.json
```

確認すること:
- カードの名前・属性・レアリティ・4つのステータスがすべて JSON に入っているか
- 値が `0` や `null` になっていないか
- アプリを完全終了 → 再起動して、コレクションにカードが残るか

### 4.4 オフライン動作 / Offline

**機内モードのまま**通しプレイする。カード生成はローカル完結なので、通信が要らないことが設計上の要件です。

Play the whole loop in **airplane mode**. Local generation means no network is needed — that is a design requirement, not a nice-to-have.

### 4.5 屋外での可読性と片手操作 / Outdoor readability

実際に屋外に持ち出し、**直射日光下**で撮影画面が読めるか、片手で撮影まで完結するかを確認します。ここは数値では測れないので、実際に外で試すしかありません。

`adb shell screenrecord` で録画しておくと後から見直せます(USB接続が切れる屋外では下記のワイヤレスデバッグを使用)。

### 4.6 ストレージの増え方 / Storage growth

```sh
adb shell du -sh /sdcard/Android/data/<pkg>/files/
```

`captures/` に元画像が溜まり続ける既知の課題(削除処理が未実装)の実測に使えます。20〜30枚撮ってから測ると増加ペースが分かります。

---

## 5. ワイヤレスデバッグ / Wireless debugging

**屋外テストでは USB を繋いだままにできません。** ログを見ながら歩き回るにはワイヤレス接続が有効です。

**Android 11 以降**: 開発者オプション → **ワイヤレスデバッグ** → ペア設定コードでペアリング
```sh
adb pair <端末に表示されるIP:ポート>
adb connect <端末のIP:5555>
```

**Android 10 以前**: USB 接続した状態で
```sh
adb tcpip 5555
adb connect <端末のIP>:5555
# USB を抜いても接続が維持される
```

同じ Wi-Fi に接続している必要があるため、屋外では自宅の Wi-Fi が届く範囲(庭・ベランダ程度)が限界です。それより遠くへ出る場合はログをファイルに書く方式を検討します(🔶 未実装)。

Both devices must be on the same Wi-Fi, so this covers a garden or balcony rather than a real walk. For anything further, writing logs to a file would be needed (🔶 not implemented).

---

## 6. 検証専用端末の活かし方 / Making the most of a dedicated device

- **常設の検証機にする**: 開発者オプションを入れたまま、画面ロックを解除し、充電したまま置いておく。`Build And Run` で即座に確認できる状態を保つ。
- **破壊的なテストを躊躇なく行う**: セーブファイルを手で壊して復旧するか確認する(`collection.corrupt-*.json` に退避して空から始まる実装になっているはず):
  ```sh
  adb shell "echo 'broken json' > /sdcard/Android/data/<pkg>/files/collection.json"
  ```
- 🔶 **デバッグ用の操作パネルを入れる**: 検証専用端末があるなら、開発ビルドに限って「移動距離チェックを無視する」「レアリティを指定して生成する」等のパネルを入れる価値があります。屋外要素をオンにすると、机の上で検証するのが難しくなるため。**要検討事項として記録**(実装は未着手)。

---

## 7. 関連文書 / Related

- [`setup-unity.md`](./setup-unity.md) — Unity プロジェクトのセットアップ、GitHub との接続
- [`game-mvp-plan.md`](./game-mvp-plan.md) — 実装状況と残作業(§6)、既知の課題
- [`game-design.md`](./game-design.md) — 屋外要素の設計(§6)、安全とプライバシー(§8)
