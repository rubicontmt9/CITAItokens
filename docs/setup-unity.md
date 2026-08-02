# Unity セットアップ手順 / Unity Setup Guide

このリポジトリをクローンした状態から、実際にゲームが動くところまでの手順です。所要時間はダウンロードを除いて 20〜30 分程度を見込んでください。

This is the path from a fresh clone of this repository to a running game. Budget 20–30 minutes, excluding downloads.

> ⚠️ **このリポジトリのC#コードは、まだ一度もコンパイルされていません。** Unity の無い環境で書かれているためです。最初に Unity Editor で開いた瞬間が、事実上の初回コンパイルになります。
> ⚠️ **The C# code in this repository has never been compiled.** It was written in an environment without Unity, so the first time you open the Editor *is* the first real compile check. See the [known issues](#8-既知の問題実機で確認すべきこと--known-issues--must-verify-on-device) section at the end before you start worrying about errors.

---

## 0. 前提 / Prerequisites

- **Unity Hub** と **Unity 6** (`6000.x`)。当初 2022 LTS を想定していましたが、Unity Hub から入手できないため Unity 6 を使います。本プロジェクトが使う機能はすべて Unity 6 で利用できます。
  **Unity Hub** and **Unity 6** (`6000.x`). The plan originally assumed 2022 LTS, but it is not obtainable from Unity Hub; everything this project uses works on Unity 6.
- **Git**。コマンドラインの `git`、または **GitHub Desktop**(GUI)のどちらでも構いません。
  **Git** — either the `git` command line or **GitHub Desktop** (GUI).
- Android 実機ビルドまで行う場合は、Unity インストール時に **Android Build Support**(OpenJDK と Android SDK & NDK Tools を含む)を追加してください。
  If you intend to build to an Android device, add **Android Build Support** (including OpenJDK and the Android SDK & NDK Tools) when installing Unity.
- 手順 4(Editor でのプレイ)には PC の Web カメラがあると便利です。外に出る必要はありません。
  A PC webcam makes step 4 (playing in the Editor) much easier. You do not need to go outside.

---

## 0.5 GitHub からリポジトリを取得する / Get the repository from GitHub

**Unity には GitHub 連携機能はありません。** Unity は「フォルダの中身」しか見ないので、**Git でリポジトリをクローンし、そのフォルダを Unity で開く**という関係になります。Unity 側で特別な設定は要りません。

**Unity has no GitHub integration.** Unity only sees a folder on disk, so the relationship is: **clone the repository with Git, then open that folder in Unity.** Nothing special is configured on the Unity side.

```
GitHub (リモート)
   ↕ git clone / commit / push        ← Git または GitHub Desktop が担当
ローカルの CITAItokens/              ← ただのフォルダ
   └── software/                     ← これを Unity Hub で開く
```

### 手順 / Steps

**コマンドラインの場合 / Command line:**

```sh
git clone https://github.com/rubicontmt9/CITAItokens.git
cd CITAItokens
git switch claude/game-implementation-plan-uszy2p
```

**GitHub Desktop の場合 / GitHub Desktop:**

1. **File → Clone repository** → `rubicontmt9/CITAItokens` を選択。
   **File → Clone repository** → pick `rubicontmt9/CITAItokens`.
2. 上部の **Current branch** から `claude/game-implementation-plan-uszy2p` を選択。
   Select `claude/game-implementation-plan-uszy2p` from **Current branch**.

⚠️ **`main` ではなく作業ブランチ `claude/game-implementation-plan-uszy2p` を使ってください。** コードと計画はすべてこのブランチにあり、`main` にはまだ雛形しかありません。
⚠️ **Use the working branch, not `main`.** All the code and planning live on `claude/game-implementation-plan-uszy2p`; `main` still holds only the scaffolding.

### 変更を GitHub に戻す / Pushing changes back

Unity で作業した結果(生成された `ProjectSettings/`、`.meta`、シーン、アート素材など)をコミットして push します。

```sh
git add -A
git status          # ← 何が入るか必ず確認する
git commit -m "chore: Unity プロジェクトを作成し初回コンパイルを通す"
git push
```

push するとこのブランチの Pull Request (#1) が自動的に更新されます。新しく PR を作る必要はありません。

Pushing updates Pull Request #1 for this branch automatically; there is no need to open a new one.

### コミットして良いもの / 除外されるもの

`.gitignore` は既に設定済みなので、基本は `git add -A` で問題ありません。中身の判断は以下です。

`.gitignore` is already configured, so `git add -A` is normally safe. For reference:

| コミットする / Commit | 除外される / Ignored |
| --- | --- |
| `ProjectSettings/` | `Library/`(巨大な再生成キャッシュ) |
| `Packages/manifest.json`, `packages-lock.json` | `Temp/`, `Logs/`, `obj/`, `Build/` |
| **すべての `.meta`** (GUIDが入っている。必須) | `*.csproj`, `*.sln`(IDE用の生成物) |
| `Assets/` 配下のスクリプト・シーン・素材 | `Assets/Resources/AppConfig.asset`(環境ごとの設定) |

⚠️ `git status` で意図しないファイルが入っていないか確認する習慣をつけてください。特に**APIキーや個人設定を含むファイルを push しないよう**注意してください。

⚠️ Get into the habit of checking `git status` before committing — in particular, **never push a file containing an API key or personal configuration.**

### 画像素材が増えてきたら / Once art assets pile up 🔶

PNG などのバイナリは Git と相性が悪く(差分が取れず履歴が膨らむ)、**Git LFS** の導入を検討する価値があります。ただし後から導入すると履歴の書き換えが必要になるため、**アート素材を本格的に入れ始める前に判断**してください。MVP の段階では不要です。

Binary assets like PNGs bloat Git history. **Git LFS** is worth considering, but retrofitting it requires rewriting history — so decide **before** art assets start landing in bulk. Not needed at the MVP stage.

---

## 1. Unity プロジェクトを `software/` に作る / Create the Unity project in `software/`

このリポジトリには **Unity が生成する部分が含まれていません**。`software/Assets/Scripts/` と `software/Assets/Editor/` にC#コードだけが入っており、`ProjectSettings/`、`Packages/`、`Assets/Scenes/`、`Library/` はまだ存在しません。あなたが作る Unity プロジェクトと**同じ場所に重なる**ように作成してください。

This repository contains **only the parts Unity does not generate**. `software/Assets/Scripts/` and `software/Assets/Editor/` hold the C# code; `ProjectSettings/`, `Packages/`, `Assets/Scenes/` and `Library/` do not exist yet. The Unity project you create must **overlap the existing folder**, not sit next to it.

> ⚠️ **いきなり `Add project from disk` で `software/` を選ぶと「Unityプロジェクトが無い」と言われます。** Unity Hub はフォルダを **`ProjectSettings/ProjectVersion.txt` の有無**でプロジェクトと判定しますが、このリポジトリには Unity が生成する部分を入れていないため、まだ存在しません。**下記の 1〜3 を先に済ませてから 4 で追加してください。**
> ⚠️ **Pointing `Add project from disk` at `software/` first will fail with "no Unity project found".** Unity Hub identifies a project by the presence of **`ProjectSettings/ProjectVersion.txt`**, which this repository intentionally does not contain. Do steps 1–3 first, then add it in step 4.

**Unity Hub は既存の空でないフォルダに新規プロジェクトを作れないことがあります。** そのため「別の場所に作ってから、Unity が生成した部分だけを `software/` へ移す」という手順を取ります。遠回りに見えますが、こちらが確実です。

**Unity Hub may refuse to create a project in a folder that is not empty**, so the reliable route is to create it elsewhere and move only the Unity-generated parts into `software/`.

1. Unity Hub → **New project** → バージョン **Unity 6**、テンプレートは **2D**。
   **Built-In Render Pipeline** 版があればそちらを選んでください。URP(Universal 2D)版はレンダリング設定のアセットへの参照を持つため、移動する対象が増えます。本プロジェクトのUIは Canvas のオーバーレイのみで、URP の機能を使いません。
   プロジェクト名と場所は**リポジトリの外の適当な場所**(例: `~/citai-temp`)にします。
   Unity Hub → **New project** → version **Unity 6**, template **2D** — prefer the **Built-In Render Pipeline** variant if offered, since the URP (Universal 2D) one adds render-pipeline assets to move and this project's UI is Canvas-overlay only. Put it **outside the repository** (e.g. `~/citai-temp`).
2. 一度開いたら Unity を**閉じます**。
   Once it opens, **close** Unity.
3. 作成された一時プロジェクトから、以下を `software/` に**移動**します(`software/Assets/` の既存の中身は消さずに残す)。
   Move the following from the temporary project into `software/`, leaving the existing contents of `software/Assets/` intact:

   | 移動するもの / Move | 移動先 / To | 備考 / Note |
   | --- | --- | --- |
   | `ProjectSettings/` | `software/ProjectSettings/` | **これが本命。**Unity Hub がプロジェクトと認識するのに必要 |
   | `Packages/` | `software/Packages/` | パッケージ構成。`UnityEngine.UI` などの依存が入っている |
   | `Assets/` 配下にテンプレートが作ったフォルダ | `software/Assets/` | `Settings/`、`Scenes/` など。既存の `Scripts/` `Editor/` とは衝突しない |

   `Library/`、`Logs/`、`Temp/`、`obj/`、`UserSettings/` は**移動しません**(Unity が再生成し、`.gitignore` で除外済み)。
   Do **not** move `Library/`, `Logs/`, `Temp/`, `obj/`, `UserSettings/` — Unity regenerates them and they are git-ignored.
4. Unity Hub → **Add** → **Add project from disk** → `software/` を選択して開きます。今度は認識されます。
   Unity Hub → **Add** → **Add project from disk** → select `software/`. It is recognized now.
5. **確認:** Project ウィンドウに `Assets/Scripts/`(`AI`, `Battle`, `Capture`, `Card`, `Core`, `Data`, `UI`)と `Assets/Editor/` が見えていること。見えていなければプロジェクトルートの位置がずれています。
   **Check:** the Project window must show `Assets/Scripts/` (`AI`, `Battle`, `Capture`, `Card`, `Core`, `Data`, `UI`) and `Assets/Editor/`.
6. 一時プロジェクトのフォルダは削除して構いません。
   The temporary project folder can be deleted.

> **`.meta` ファイルは必ずコミットしてください。** リポジトリには含まれておらず、Unity が初回インポート時に生成します。`.meta` には各アセットのGUIDが入っており、**コミットし忘れると別のクローンで参照が壊れます**(スクリプトがコンポーネントから外れる等)。「生成物だから無視する」は誤りです。
> **Always commit the `.meta` files.** They are not in the repository; Unity generates them on first import. Each one carries the asset's GUID, so **omitting them breaks references in any other clone** (scripts detach from components). They are generated, but they are not disposable.

---

## 2. Newtonsoft.Json を追加する / Add the Newtonsoft.Json dependency

**これが無いとコードはコンパイルできません。** 2D (Core) テンプレートには含まれていないので、必ず追加してください。`Assets/Scripts/Data/LocalCardRepository.cs`、`Assets/Scripts/AI/CardProxyResponse.cs`、`Assets/Scripts/AI/CardProxyClient.cs` が `Newtonsoft.Json` を直接使っています(`JsonUtility` では `Card` / `StatBlock` の private フィールドと AI レスポンスの柔軟なパースに対応できないため)。

**The code will not compile without it.** The 2D (Core) template does not include it, so this step is mandatory. `Assets/Scripts/Data/LocalCardRepository.cs`, `Assets/Scripts/AI/CardProxyResponse.cs` and `Assets/Scripts/AI/CardProxyClient.cs` all use `Newtonsoft.Json` directly (`JsonUtility` cannot handle the private fields of `Card` / `StatBlock`, nor the flexible parsing of the AI response).

1. **Window → Package Manager** を開く。
   Open **Window → Package Manager**.
2. 左上の **`+`** → **Add package by name...**。
   Click **`+`** (top-left) → **Add package by name...**.
3. 名前に `com.unity.nuget.newtonsoft-json` を入力して **Add**。バージョンは空欄のままで構いません。
   Enter `com.unity.nuget.newtonsoft-json` and click **Add**. Leave the version field empty.
4. インポートが終わったら、Console に `Newtonsoft` 関連のコンパイルエラーが残っていないことを確認します。
   When the import finishes, confirm the Console no longer shows `Newtonsoft`-related compile errors.

---

## 2.5 Unity 6 で必須の設定 / Required settings on Unity 6

**Unity 6 の新規プロジェクトは新 Input System が既定**になっており、そのままだと不具合が2つ出ます。設定を1つ変えて両方回避します。

New Unity 6 projects default to the **new Input System only**, which causes two problems. One setting avoids both.

1. **Edit → Project Settings → Player → Other Settings → Active Input Handling** を **`Both`** に変更します。
   Set **Edit → Project Settings → Player → Other Settings → Active Input Handling** to **`Both`**.
2. 変更すると Unity の再起動を求められます。再起動してください。
   Unity will ask to restart. Restart it.

**なぜ必要か / Why:**

| 問題 / Problem | 症状 / Symptom |
| --- | --- |
| `StandaloneInputModule` が新 Input System 下で機能しない | **ボタンが一切反応しない。**エラーも警告も出ないため原因が分かりにくい |
| `Input.location` が使えない可能性 | 位置情報による移動距離チェックが動かない |

1つ目はコード側でも対応済みです(`GameBootstrap` が `ENABLE_INPUT_SYSTEM` を見て `InputSystemUIInputModule` に切り替えます)。ただし2つ目はコードで回避できないため、`Both` の設定が必要です。

The first is also handled in code (`GameBootstrap` switches to `InputSystemUIInputModule` when `ENABLE_INPUT_SYSTEM` is defined), but the second cannot be worked around in code, so `Both` is required.

### Git と併用するための設定 / Settings for working with Git

**Edit → Project Settings → Editor** で以下を確認してください。差分が読める形になり、コンフリクトが起きにくくなります。

Check these under **Edit → Project Settings → Editor** so that diffs are readable and conflicts are rarer:

- **Version Control → Mode** = `Visible Meta Files`
- **Asset Serialization → Mode** = `Force Text`

---

## 3. メインシーンを作る / Create the main scene

シーンファイル (`.unity`) はリポジトリに入れていません(YAML を手書きすると GUID 参照が壊れやすいため)。代わりにエディタメニューから生成します。

Scene files (`.unity`) are not committed, because hand-authored YAML breaks easily on GUID references. They are generated from an Editor menu instead.

1. メニューから **`Tools → CITAItokens → Create Main Scene`** を実行します。
   Run **`Tools → CITAItokens → Create Main Scene`** from the menu bar.
2. `Assets/Scenes/Main.unity` が作られ、**Build Settings の先頭・有効状態**で登録されます。シーンの中身は `GameBootstrap` コンポーネントを持つ GameObject が **1つだけ**です。カメラも Canvas も EventSystem も、実行時に `GameBootstrap` が組み立てます。
   This creates `Assets/Scenes/Main.unity` and registers it as the **first, enabled entry in Build Settings**. The scene contains exactly **one** GameObject, carrying the `GameBootstrap` component; the canvas, the EventSystem and all six screens are built at runtime by `GameBootstrap`.
3. すでに `Main.unity` がある場合は上書き確認のダイアログが出ます。手で調整したシーンを失いたくないときは **やめる / Cancel** を選んでください。
   If `Main.unity` already exists, a confirmation dialog appears. Choose **Cancel** when you do not want to lose a scene you have edited by hand.

---

## 4. Play を押す / Press Play

`Main.unity` を開いた状態で **Play** を押します。カード生成は**既定で端末内の画像解析**(`PhotoAnalysisCardGenerator`)なので、**通信もスマホも不要**です。PC の Web カメラだけで、机の上でゲームループが一周します。

With `Main.unity` open, press **Play**. Card generation uses **on-device image analysis by default** (`PhotoAnalysisCardGenerator`), so **no network and no phone are required**: the whole loop runs at your desk with a PC webcam.

- Console に `Assets/Resources/AppConfig.asset が見つかりません` という警告が出ますが、**これは正常です**。設定アセットが無い場合は既定値で起動します(手順 6 で作れます)。
  The Console warns that `Assets/Resources/AppConfig.asset` is missing. **This is expected**: the game starts with defaults when the asset is absent (you can create it in step 6).
- Console に `MockCardGenerator を使います` と出るので、どちらの生成器が選ばれたかは毎回ログで確認できます。
  The Console logs `MockCardGenerator を使います`, so which generator was selected is always visible in the log.
- 期待される流れ: タイトル → 撮影(カメラ許可ダイアログが出たら許可)→ 生成中 → カード確認 → コレクションに追加 → バトル → 結果 → タイトル。
  Expected flow: title → capture (allow the webcam prompt) → generating → card result → add to collection → battle → result → back to the title.
- 同じ被写体を撮ると毎回同じカードになります。モックは写真のバイト列のハッシュから決定論的に生成しているためで、不具合ではありません。
  Photographing the same thing twice gives the same card: the mock derives everything deterministically from a hash of the photo bytes. That is by design, not a bug.

---

## 5. バトル計算の自己診断を走らせる / Run the battle self-test

**`Tools → CITAItokens → Run Battle Self-Test`** を実行します。Unity Test Framework は使わず(`.asmdef` を増やしたくないため)、エディタメニューから手動で走らせる自己診断です。

Run **`Tools → CITAItokens → Run Battle Self-Test`**. There is no Unity Test Framework here (it would require an extra `.asmdef`); the battle maths is verified by a self-test you run by hand from the menu.

- 成功時: Console に `Battle self-test: N passed, 0 failed` が **Log** として出ます。
  On success the Console shows `Battle self-test: N passed, 0 failed` as a normal **Log** line.
- 失敗時: 失敗した項目ごとに理由が **LogError** で出ます。バトル計算を変更したら必ずこれを流してください。
  On failure each failing check is reported with its reason as a **LogError**. Always run this after touching the battle maths.

---

## 6. (参考・MVP対象外) クラウドAI経路につなぐ / (Reference, not the MVP path) Switch to the cloud AI route

**この手順は通常不要です。** カード生成は端末内で完結させる方針で、完成版もオンデバイスのモデルに置き換える予定です(`docs/game-mvp-plan.md` 2.2)。`services/card-proxy/` のクラウドAI実装は、方針が固まる前に作った**参考実装**として残してあります。比較や実験のために使う場合だけ、この手順を行ってください。

**You normally do not need this.** Card generation is meant to stay on-device, and the final version will use an on-device model (see `docs/game-mvp-plan.md` §2.2). The cloud AI implementation in `services/card-proxy/` is kept as a **reference implementation** from before that decision. Follow these steps only to compare or experiment.

1. プロキシをデプロイします。`services/card-proxy/` に Cloudflare Worker の実装と手順があります(`npm install` → `npx wrangler secret put ANTHROPIC_API_KEY` → `npm run deploy`)。**APIキーはプロキシ側の secret にのみ置きます。**
   Deploy the proxy. The Cloudflare Worker and its instructions live in [`services/card-proxy/`](../services/card-proxy/README.md) (`npm install` → `npx wrangler secret put ANTHROPIC_API_KEY` → `npm run deploy`). **The API key lives only in the proxy's secrets.**
2. `/health` が `{"ok":true}` を返すことを確認します。
   Confirm `/health` returns `{"ok":true}`.
3. **`Tools → CITAItokens → Create AppConfig Asset`** を実行します。`Assets/Resources/AppConfig.asset` が作られ、Project ウィンドウで選択された状態になります(既に存在する場合は選択のみで、**上書きはしません**)。
   Run **`Tools → CITAItokens → Create AppConfig Asset`**. It creates `Assets/Resources/AppConfig.asset` and selects it in the Project window (if it already exists it is only selected and **never overwritten**).
4. Inspector で **Card Proxy Url** にデプロイ先のベースURLを入れます。**末尾のスラッシュは付けないでください**(クライアントが `/generate` を連結します)。
   In the Inspector, set **Card Proxy Url** to the deployed base URL. **Do not add a trailing slash** — the client appends `/generate` itself.
5. **Use Mock Card Generator** のチェックを外します。URLが空のままだとモックへ自動フォールバックするので、両方を正しく設定する必要があります。
   Uncheck **Use Mock Card Generator**. A blank URL falls back to the mock automatically, so both fields must be right.
6. ⚠️ このアセットは環境ごとの値を持つため **`.gitignore` で除外されています**(`software/Assets/Resources/AppConfig.asset`)。**URL 以外の秘密情報を絶対に入れないでください。** APIキーの置き場所はプロキシの secret だけです。
   ⚠️ This asset is **git-ignored on purpose** (`software/Assets/Resources/AppConfig.asset`) because it holds per-environment values. **Never put anything secret beyond a URL in it.** API keys belong only in the proxy's secrets.

---

## 7. Android 実機ビルド / Building for an Android device

1. **プラットフォーム切り替え / Switch platform**: **File → Build Settings** → **Android** を選び **Switch Platform**。初回はテクスチャの再インポートに時間がかかります。
   **File → Build Settings** → select **Android** → **Switch Platform**. The first switch re-imports textures and takes a while.
2. **画面の向き / Orientation**: **Edit → Project Settings → Player → Resolution and Presentation** で **Default Orientation** を **Portrait** にします。`GameBootstrap` が実行時に `Screen.orientation = ScreenOrientation.Portrait` を設定しますが、起動直後の一瞬を横向きにしないためにビルド設定側でも縦に固定します。
   In **Edit → Project Settings → Player → Resolution and Presentation**, set **Default Orientation** to **Portrait**. `GameBootstrap` also sets `Screen.orientation` at runtime, but pinning it in the Player settings avoids a landscape flash at launch.
3. **最小APIレベル / Minimum API level**: **Player → Other Settings** の **Minimum API Level** を **Android 7.0 (API 24)** 以上にします。カメラと `Application.persistentDataPath` の挙動が安定する範囲です。**Target API Level** は **Automatic (highest installed)** のままで構いません。
   Under **Player → Other Settings**, set **Minimum API Level** to **Android 7.0 (API 24)** or higher, where the camera and `Application.persistentDataPath` behaviour is dependable. **Target API Level** can stay at **Automatic (highest installed)**.
4. **パーミッション / Permissions**: `WebCamTexture` を使っているため、Unity は `CAMERA` を自動でマニフェストに入れます。**ビルド後に必ず確認してください。**
   Unity adds `CAMERA` to the manifest automatically because the project uses `WebCamTexture`. **Verify this after the build.**
   - 確認方法 / How to verify: `Temp/gradleOut/launcher/build/intermediates/merged_manifests/` 以下の `AndroidManifest.xml`(Export した場合は Export 先の同名ファイル)を開き、`android.permission.CAMERA` があることを確認します。
     Open the merged `AndroidManifest.xml` under `Temp/gradleOut/launcher/build/intermediates/merged_manifests/` (or the exported project's equivalent) and confirm `android.permission.CAMERA` is present.
   - `INTERNET` はネットワークコードがある時点で自動的に入ります。
     `INTERNET` is added automatically once the project contains networking code.
   - **`ACCESS_FINE_LOCATION` は、位置情報チェックを有効にする場合(`AppConfig.requireLocationCheck = true`)だけ必要です。** 有効にしたら、マニフェストに入っていることと、実機で位置情報の許可ダイアログが出ることを確認してください。使わない場合は要求しないほうが良いです(不要な権限はインストール時の心理的ハードルになります)。
     **`ACCESS_FINE_LOCATION` is needed only when the location check is enabled** (`AppConfig.requireLocationCheck = true`). If you enable it, confirm it reaches the manifest and that the runtime permission dialog appears on device. Leave it out otherwise — an unnecessary permission is a real install-time deterrent.
5. 端末を USB で接続し、開発者オプションと USB デバッグを有効にして **Build And Run**。
   Connect the device over USB with developer options and USB debugging enabled, then **Build And Run**.
6. 実機のログは `adb logcat -s Unity` で読めます。`GameBootstrap` はどちらの生成器を選んだかを起動時にログへ出すので、意図した経路で動いているかここで確認できます(通常はローカル生成)。
   Read device logs with `adb logcat -s Unity`. `GameBootstrap` logs which generator it selected at startup, so you can confirm the intended route is active (normally the local generator).

---

## 8. 既知の問題・実機で確認すべきこと / Known issues & must verify on device

> **前提として、このリポジトリのコードは一度もコンパイルされていません。** Unity の無い環境で書かれているため、**初回の Editor 起動が事実上の初回コンパイル**です。そこで出るエラー(名前空間の綴り、API シグネチャの不一致、`using` の不足など)は**想定内**であり、異常ではありません。落ち着いて Console の1件目から順に潰してください。
> **To be clear up front: the code in this repository has never been compiled.** It was written in an environment without Unity, so **the first Editor open is the first real compile check.** Errors there (misspelled namespaces, mismatched API signatures, missing `using` directives) are **expected, not alarming.** Work through the Console from the first error down.

以下は書いた時点で分かっている懸念です。いずれも実機/Editor で確認が必要です。

The following concerns are known at the time of writing and all need verifying in the Editor or on a device.

1. **カードの保存が本当に往復するか / Does a saved card really round-trip?**
   `Card` と `StatBlock` はデータを `[SerializeField] private` フィールドに持ち、公開プロパティは読み取り専用です。`LocalCardRepository` は Newtonsoft の `IgnoreSerializableAttribute = false` でフィールドベースの契約に切り替えて対応していますが、**実際に往復することは未検証**です。ここが静かに壊れると、**中身が空のカードが読み込まれます**。確認方法: カードを1枚保存 → Editor を停止 → 再度 Play → コレクションに名前・ステータス・画像が残っていること。ついでに `Application.persistentDataPath/collection.json` を開いて、値が入っているかを目で見てください。
   `Card` and `StatBlock` keep their data in `[SerializeField] private` fields behind read-only properties. `LocalCardRepository` handles this by switching Newtonsoft to a field-based contract (`IgnoreSerializableAttribute = false`), but **the round-trip is unverified**. A silent failure here **loads blank cards.** To verify: save one card → stop play → press Play again → confirm the name, stats and image survived in the collection. Also open `Application.persistentDataPath/collection.json` and check the values are actually in there.

2. **撮影JPEGの向きを補正していない / Captured JPEG orientation is not corrected**
   `WebCamPhotoCapture` は `WebCamTexture` のフレームをそのまま JPEG にしています。`videoRotationAngle` / `videoVerticallyMirrored` は公開していますが、**保存する画像には適用していません**。縦持ちのスマホで撮った写真が横倒しで保存される可能性があります。カード絵として見栄えが悪いだけで、ゲーム進行には影響しません。
   `WebCamPhotoCapture` encodes the raw `WebCamTexture` frame. It exposes `videoRotationAngle` / `videoVerticallyMirrored` but **does not apply them to the saved image**, so photos taken on a portrait-held phone may be saved sideways. It only looks wrong as card art; it does not block progress.

3. **カメラ許可の前に `WebCamTexture.devices` は空 / `WebCamTexture.devices` is empty before permission is granted**
   Android では `CAMERA` が許可されるまでデバイス一覧が空になります。つまり `IsSupported` を先に見ると「カメラの無い端末」と誤判定します。**必ず先に `RequestPermission` を呼び、その後でカメラの有無を判定してください。** 許可を拒否した状態と、本当にカメラが無い状態を、実機で両方試すべきです。
   On Android the device list is empty until `CAMERA` is granted, so checking `IsSupported` first misreports the phone as having no camera. **Always request permission before checking camera availability.** Test both a denied permission and a genuinely camera-less device.

4. **日本語フォント / A font with CJK glyphs is required**
   Unity 組み込みのフォントには日本語の字形が含まれません。UI をそのままビルドすると、日本語が**豆腐(□)や空白**になる可能性があります。CJK を含むフォント(Noto Sans JP など、ライセンスを確認のうえ)を `Assets/` に入れて UI に割り当ててください。Editor 上では OS のフォールバックで読めてしまい、**実機で初めて気づく**種類の問題です。
   Unity's built-in fonts contain no Japanese glyphs, so Japanese text may render as **tofu boxes or blanks** in a build. Add a CJK-capable font (e.g. Noto Sans JP, licence permitting) under `Assets/` and assign it to the UI. The Editor often papers over this with an OS fallback, so **it typically shows up first on device.**

5. **撮影画像が溜まり続ける / Captures are never pruned**
   撮影した元画像は `Application.persistentDataPath/captures/` に1枚ずつ保存され、**削除する処理がありません**。カードのサムネイル(`cards/` 以下、長辺512pxのJPEG)とは別に、フル解像度の写真が残り続けます。長時間プレイテストすると端末の空き容量を食います。当面は手動で消してください。
   Every source photo is written to `Application.persistentDataPath/captures/` and **nothing ever deletes them.** These full-resolution files accumulate alongside the card thumbnails in `cards/` (512 px JPEGs). A long playtest will eat storage; clear the folder by hand for now.

6. **シーンにカメラが無い / The scene contains no Camera**
   `GameBootstrap` が作るのは EventSystem と Screen Space - Overlay の Canvas だけです。Overlay の UI はカメラ無しでも描画されますが、Game ビューに `No cameras rendering` が出ますし、UI が覆っていない領域の背景色は保証されません。気になる場合は各画面が全画面の背景を持つようにするか、シーンに Camera を1つ足してください。
   `GameBootstrap` creates only the EventSystem and a Screen Space - Overlay canvas. Overlay UI draws without a camera, but the Game view reports `No cameras rendering` and the background behind any uncovered area is not guaranteed. Either give each screen a full-screen background, or add a single Camera to the scene.

7. **入力システムの選択 / Which input system is active**
   `GameBootstrap` は従来の `StandaloneInputModule` を使います。プロジェクトに **Input System パッケージ**が入っていて `Active Input Handling` が **Input System Package (New)** だけになっていると、`StandaloneInputModule` は機能せず**ボタンが一切反応しません**。その場合は `Active Input Handling` を **Both** にするか、EventSystem のモジュールを `InputSystemUIInputModule` に差し替えてください。
   `GameBootstrap` uses the legacy `StandaloneInputModule`. If the project has the **Input System package** and `Active Input Handling` is set to **Input System Package (New)** only, `StandaloneInputModule` does nothing and **no button responds at all.** Either set `Active Input Handling` to **Both**, or replace the module with `InputSystemUIInputModule`.

---

## 関連ドキュメント / Related documents

- [`docs/game-mvp-plan.md`](./game-mvp-plan.md) — 企画・技術方針・フェーズ計画 / concept, technical decisions, phased plan
- [`software/README.md`](../software/README.md) — ゲーム側の構成 / layout of the game project
- [`services/card-proxy/README.md`](../services/card-proxy/README.md) — プロキシのデプロイとAPIコントラクト / proxy deployment and wire contract
