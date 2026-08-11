#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CitaiTokens.AI;
using CitaiTokens.Cards;
using UnityEditor;
using UnityEngine;

namespace CitaiTokens.EditorTools
{
    /// <summary>
    /// ディスク上の既存の写真からカードを導出して一覧するエディタウィンドウ。導出ロジックを調整するための反復用ツール。
    /// An Editor window that derives cards from photos already on disk and lists them. This is the iteration tool
    /// for tuning the derivation logic.
    /// </summary>
    /// <remarks>
    /// このツールが存在する理由は、端末ビルド → 屋外で撮影 → 結果を見る、という一周が長すぎることに尽きる。
    /// スマホから書き出した写真フォルダをそのまま食わせられるので、Unity を再生する必要も実機に繋ぐ必要もない。
    /// 一括モードが本命で、1枚ずつ見ても導出の良し悪しは分からない。全部が同じジャンルに寄っている、といった
    /// 失敗は分布を見て初めて気付くため、ジャンル・属性・レアリティの件数を必ず出す。
    ///
    /// The reason this exists is that the build → walk outside → photograph → look at the result loop is far too
    /// slow. A folder exported from a phone can be fed in directly, with no Play mode and no device attached.
    /// The batch mode is the point: one photo at a time tells you nothing about whether the derivation is any
    /// good. Failures such as every photo collapsing onto a single genre are only visible in aggregate, so the
    /// per-genre / per-element / per-rarity counts are always shown.
    ///
    /// <see cref="Texture2D"/> をコードで作る経路があるため、破棄漏れに注意が必要。<see cref="ClearRows"/> が
    /// 自前で作ったテクスチャだけを <c>DestroyImmediate</c> し、ウィンドウを閉じたときとリストを作り直すときの
    /// 両方で必ず呼ばれる。ドラッグで渡されたインポート済みテクスチャは所有していないので破棄しない。
    /// Because this code creates <see cref="Texture2D"/> instances itself, leaking them is a real risk.
    /// <see cref="ClearRows"/> destroys only the textures this window owns, and it runs both when the window
    /// closes and whenever the list is rebuilt. Imported textures handed over by drag-and-drop are not owned and
    /// therefore never destroyed.
    /// </remarks>
    public sealed class CardPreviewWindow : EditorWindow
    {
        /// <summary>サムネイルの一辺 (px)。 / Thumbnail edge length in pixels.</summary>
        private const float ThumbnailSize = 96f;

        /// <summary>1フレームに描く行数の初期上限。 / Initial cap on how many rows are drawn per frame.</summary>
        private const int DefaultRowRenderLimit = 40;

        /// <summary>1回に読み込むファイル数の初期上限。 / Initial cap on how many files are loaded at once.</summary>
        private const int DefaultFileLoadLimit = 60;

        /// <summary>特徴量セルの幅 (px)。 / Width of one feature cell in pixels.</summary>
        private const float FeatureCellWidth = 150f;

        private static readonly WeaponGenre[] AllGenres = (WeaponGenre[])Enum.GetValues(typeof(WeaponGenre));
        private static readonly ElementType[] AllElements = (ElementType[])Enum.GetValues(typeof(ElementType));
        private static readonly Rarity[] AllRarities = (Rarity[])Enum.GetValues(typeof(Rarity));

        private Texture2D sourceTexture;
        private string folderPath = string.Empty;
        private List<PreviewRow> rows;
        private Vector2 scrollPosition;
        private int rowRenderLimit = DefaultRowRenderLimit;
        private int fileLoadLimit = DefaultFileLoadLimit;
        private int imageFilesFound;
        private string lastLoadNote = string.Empty;

        /// <summary>
        /// 次のフレームで解析するフォルダ。ボタンの中で直接解析すると、レイアウト計算の途中でコントロール数が
        /// 変わってしまうため、要求だけ受け取って <see cref="OnGUI"/> の最後で実行する。
        /// The folder to analyse on the next frame. Analysing inside the button handler would change the control
        /// count part-way through the layout pass, so the request is queued and run at the end of <see cref="OnGUI"/>.
        /// </summary>
        private string pendingFolderRequest;

        /// <summary>単一テクスチャの解析要求。 / A queued request to analyse the single texture.</summary>
        private bool pendingTextureRequest;

        /// <summary>一覧の消去要求。 / A queued request to clear the list.</summary>
        private bool pendingClearRequest;

        /// <summary>
        /// ウィンドウを開く。 / Opens the window.
        /// </summary>
        [MenuItem("Tools/CITAItokens/Card Preview")]
        public static void Open()
        {
            GetWindow<CardPreviewWindow>("Card Preview");
        }

        /// <summary>
        /// ウィンドウが閉じられたときとドメインリロードの直前に呼ばれる。ここでテクスチャを解放する。
        /// Called when the window closes and just before a domain reload; textures are released here.
        /// </summary>
        private void OnDisable()
        {
            ClearRows();
        }

        /// <summary>
        /// 念のための二重解放防止つき後始末。<see cref="ClearRows"/> は何度呼んでも安全。
        /// A belt-and-braces cleanup; <see cref="ClearRows"/> is safe to call any number of times.
        /// </summary>
        private void OnDestroy()
        {
            ClearRows();
        }

        private void OnGUI()
        {
            EnsureRows();

            // ウィンドウ全体を1つのスクロール領域に入れる。行が増えても操作部に戻れるようにするため。
            // The whole window lives in one scroll region so the controls stay reachable as rows pile up.
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawInputs();
            DrawSummary();
            DrawRows();

            EditorGUILayout.EndScrollView();

            ProcessPendingRequests();
        }

        /// <summary>
        /// 溜まった要求を描画の外側で実行する。 / Runs queued requests outside the drawing itself.
        /// </summary>
        private void ProcessPendingRequests()
        {
            if (pendingClearRequest)
            {
                pendingClearRequest = false;
                ClearRows();
                lastLoadNote = string.Empty;
                Repaint();
            }

            if (pendingTextureRequest)
            {
                pendingTextureRequest = false;
                AnalyzeSingleTexture(sourceTexture);
            }

            if (!string.IsNullOrEmpty(pendingFolderRequest))
            {
                var requested = pendingFolderRequest;
                pendingFolderRequest = null;
                AnalyzeFolder(requested);
            }
        }

        // -------------------------------------------------------------------------
        // 入力 / Inputs
        // -------------------------------------------------------------------------

        private void DrawInputs()
        {
            GUILayout.Label(
                "写真からカードを導出して確認する / Derive cards from photos and inspect the result",
                EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "導出コードを書き換えたら、同じフォルダをもう一度読み込むだけで結果が更新されます。"
                + "実機ビルドも Play も要りません。 / "
                + "After editing the derivation code, just load the same folder again. No device build, no Play mode.",
                MessageType.Info);

            EditorGUILayout.Space();
            GUILayout.Label(
                "1. プロジェクト内のテクスチャ / A texture inside the project",
                EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            sourceTexture = EditorGUILayout.ObjectField("Texture2D", sourceTexture, typeof(Texture2D), false)
                as Texture2D;
            EditorGUI.BeginDisabledGroup(sourceTexture == null);
            if (GUILayout.Button("解析 / Analyse", GUILayout.Width(140f)))
            {
                pendingTextureRequest = true;
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            GUILayout.Label(
                "2. ディスク上のフォルダ (Assets の外でも可) / A folder anywhere on disk, including outside Assets",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.BeginHorizontal();
            folderPath = EditorGUILayout.TextField("Folder", folderPath);
            if (GUILayout.Button("選ぶ / Browse", GUILayout.Width(110f)))
            {
                var picked = EditorUtility.OpenFolderPanel(
                    "写真フォルダを選ぶ / Pick a photo folder",
                    folderPath,
                    string.Empty);

                if (!string.IsNullOrEmpty(picked))
                {
                    folderPath = picked;
                    pendingFolderRequest = picked;
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(folderPath));
            if (GUILayout.Button("フォルダを読み込んで一括解析 / Load folder and analyse all"))
            {
                pendingFolderRequest = folderPath;
            }

            EditorGUI.EndDisabledGroup();

            GUILayout.Label(
                "対応拡張子: .jpg / .jpeg / .png (大文字小文字は区別しない) / "
                + "Extensions: .jpg / .jpeg / .png, case-insensitive",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            fileLoadLimit = EditorGUILayout.IntSlider(
                "読み込む枚数の上限 / Files to load",
                fileLoadLimit,
                1,
                400);
            rowRenderLimit = EditorGUILayout.IntSlider(
                "描く行数の上限 / Rows to draw",
                rowRenderLimit,
                5,
                400);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(rows.Count == 0);
            if (GUILayout.Button("TSVでコピー / Copy results as TSV"))
            {
                CopyResultsAsTsv();
            }

            if (GUILayout.Button("消去 / Clear", GUILayout.Width(140f)))
            {
                pendingClearRequest = true;
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(lastLoadNote))
            {
                GUILayout.Label(lastLoadNote, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space();
        }

        // -------------------------------------------------------------------------
        // 分布の要約 / Distribution summary
        // -------------------------------------------------------------------------

        /// <summary>
        /// ジャンル・属性・レアリティの件数を出す。導出の調整で最も見る数字なので、常に一覧の上に置く。
        /// Shows the per-genre, per-element and per-rarity counts. These are the numbers most looked at while
        /// tuning, so they always sit above the list.
        /// </summary>
        private void DrawSummary()
        {
            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "まだ何も解析していません。上でテクスチャかフォルダを指定してください。 / "
                    + "Nothing analysed yet. Pick a texture or a folder above.",
                    MessageType.Info);
                return;
            }

            var succeeded = 0;
            var failed = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].Succeeded)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("分布 / Distribution", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "解析できた枚数 / Analysed",
                succeeded + " (失敗 / failed: " + failed + ")");

            if (succeeded == 0)
            {
                EditorGUILayout.HelpBox(
                    "1枚も解析できていません。各行のエラーを確認してください。 / "
                    + "Nothing was analysed successfully; check the per-row errors below.",
                    MessageType.Error);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("武器ジャンル / Weapon genre", EditorStyles.boldLabel);
            var distinctGenres = 0;
            for (var i = 0; i < AllGenres.Length; i++)
            {
                var count = CountGenre(AllGenres[i]);
                if (count > 0)
                {
                    distinctGenres++;
                }

                EditorGUILayout.LabelField(AllGenres[i].ToString(), FormatCount(count, succeeded));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("属性 / Element", EditorStyles.boldLabel);
            var distinctElements = 0;
            for (var i = 0; i < AllElements.Length; i++)
            {
                var count = CountElement(AllElements[i]);
                if (count > 0)
                {
                    distinctElements++;
                }

                EditorGUILayout.LabelField(AllElements[i].ToString(), FormatCount(count, succeeded));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("レアリティ / Rarity", EditorStyles.boldLabel);
            for (var i = 0; i < AllRarities.Length; i++)
            {
                EditorGUILayout.LabelField(
                    AllRarities[i].ToString(),
                    FormatCount(CountRarity(AllRarities[i]), succeeded));
            }

            // 偏りは1枚ずつ見ていても分からない。ここで名指しする。
            // A collapsed distribution is invisible photo by photo, so it is called out explicitly here.
            if (succeeded >= 2 && distinctGenres <= 1)
            {
                EditorGUILayout.HelpBox(
                    "すべての写真が同じジャンルになりました。導出が写真の違いを拾えていません。 / "
                    + "Every photo landed on the same genre: the derivation is not responding to differences "
                    + "between photos.",
                    MessageType.Warning);
            }

            if (succeeded >= 3 && distinctElements <= 1)
            {
                EditorGUILayout.HelpBox(
                    "すべての写真が同じ属性になりました。色の閾値を見直してください。 / "
                    + "Every photo landed on the same element; revisit the colour thresholds.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private static string FormatCount(int count, int total)
        {
            var percent = total <= 0 ? 0f : 100f * count / total;
            return count + " (" + percent.ToString("0.0") + "%)";
        }

        private int CountGenre(WeaponGenre genre)
        {
            var count = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].Succeeded && rows[i].DerivedCard.WeaponGenre == genre)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountElement(ElementType element)
        {
            var count = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].Succeeded && rows[i].DerivedCard.Element == element)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountRarity(Rarity rarity)
        {
            var count = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].Succeeded && rows[i].DerivedCard.Rarity == rarity)
                {
                    count++;
                }
            }

            return count;
        }

        // -------------------------------------------------------------------------
        // 一覧 / The list
        // -------------------------------------------------------------------------

        private void DrawRows()
        {
            if (rows.Count == 0)
            {
                return;
            }

            var shown = Mathf.Min(rows.Count, rowRenderLimit);

            // 黙って打ち切るとデータが消えたように見えるので、必ず「何件中何件」を出す。
            // A silently truncated list reads as data loss, so always state how many of the total are drawn.
            EditorGUILayout.LabelField(
                "一覧 / Rows",
                shown + " / " + rows.Count + " 件を表示 (shown of total)",
                EditorStyles.boldLabel);

            if (shown < rows.Count)
            {
                EditorGUILayout.HelpBox(
                    "残り " + (rows.Count - shown) + " 件は描画上限のため表示していません (TSVコピーには全件含まれます)。"
                    + "上の「描く行数の上限」を上げれば表示されます。 / "
                    + (rows.Count - shown) + " more row(s) are not drawn because of the render cap. The TSV copy "
                    + "still contains every row; raise \"Rows to draw\" above to see them here.",
                    MessageType.Info);
            }

            for (var i = 0; i < shown; i++)
            {
                DrawRow(i, rows[i]);
            }
        }

        private void DrawRow(int index, PreviewRow row)
        {
            if (row == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            // サムネイル。ScaleToFit なので縦横比は保たれる。 / Thumbnail; ScaleToFit preserves the aspect ratio.
            EditorGUILayout.BeginVertical(GUILayout.Width(ThumbnailSize));
            var thumbnailRect = GUILayoutUtility.GetRect(ThumbnailSize, ThumbnailSize);
            if (row.Texture != null)
            {
                GUI.DrawTexture(thumbnailRect, row.Texture, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.Box(thumbnailRect, "no image");
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            DrawRowBody(index, row);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawRowBody(int index, PreviewRow row)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label((index + 1) + ". " + row.Label, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(row.SourcePath) && GUILayout.Button("場所 / Reveal", GUILayout.Width(110f)))
            {
                EditorUtility.RevealInFinder(row.SourcePath);
            }

            EditorGUILayout.EndHorizontal();

            if (!row.Succeeded)
            {
                // 1枚の失敗で一括処理を止めない。行の中に理由を出して次へ進む。
                // One bad file must not stop the batch: the reason is shown inline and the batch continues.
                EditorGUILayout.HelpBox(
                    "この写真は処理できませんでした / This photo could not be processed:\n"
                    + (string.IsNullOrEmpty(row.Error) ? "(理由不明 / no reason recorded)" : row.Error),
                    MessageType.Error);

                if (row.HasFeatures)
                {
                    DrawFeatures(row.Features);
                }

                return;
            }

            // 長い文字列は GUILayout.Label + wordWrappedLabel で折り返す。EditorGUILayout.LabelField は
            // 1行分の高さしか確保しないため、折り返すと下が切れる。
            // Long strings are wrapped with GUILayout.Label + wordWrappedLabel: EditorGUILayout.LabelField only
            // reserves a single line of height, so wrapped text would be clipped.
            var card = row.DerivedCard;
            GUILayout.Label(
                "ジャンル / Genre: " + card.WeaponGenre
                + "    属性 / Element: " + card.Element
                + "    レア / Rarity: " + card.Rarity,
                EditorStyles.wordWrappedLabel);
            GUILayout.Label("ステータス / Stats: " + card.Stats, EditorStyles.wordWrappedLabel);
            GUILayout.Label("名前 / Name: " + card.DisplayName, EditorStyles.wordWrappedLabel);
            GUILayout.Label("フレーバー / Flavor: " + card.FlavorText, EditorStyles.wordWrappedLabel);
            GUILayout.Label("写真ハッシュ / Photo hash: " + row.PhotoHash, EditorStyles.miniLabel);

            DrawFeatures(row.Features);
        }

        /// <summary>
        /// 9つの特徴量をすべて出す。要約すると「なぜこのカードになったのか」が追えなくなるため、必ず全部出す。
        /// Draws all nine feature values. Summarising them away would make it impossible to follow why a card
        /// came out the way it did, so every one is always shown.
        /// </summary>
        private static void DrawFeatures(PhotoFeatures features)
        {
            EditorGUILayout.LabelField("特徴量 / Features (0-1)", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            DrawFeatureCell("緑 Green", features.GreenRatio);
            DrawFeatureCell("茶 Brown", features.BrownRatio);
            DrawFeatureCell("青灰 BlueGray", features.BlueGrayRatio);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawFeatureCell("彩度 Saturation", features.MeanSaturation);
            DrawFeatureCell("明度 Brightness", features.MeanBrightness);
            DrawFeatureCell("コントラスト Contrast", features.Contrast);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawFeatureCell("エッジ量 EdgeDensity", features.EdgeDensity);
            DrawFeatureCell("方向性 EdgeDir", features.EdgeDirectionality);
            DrawFeatureCell("暗さ DarkRatio", features.DarkRatio);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawFeatureCell(string label, float value)
        {
            GUILayout.Label(
                label + " " + value.ToString("0.000"),
                EditorStyles.miniLabel,
                GUILayout.Width(FeatureCellWidth));
        }

        // -------------------------------------------------------------------------
        // 解析 / Analysis
        // -------------------------------------------------------------------------

        /// <summary>
        /// 1枚のテクスチャを解析して一覧を作り直す。 / Analyses a single texture, rebuilding the list.
        /// </summary>
        private void AnalyzeSingleTexture(Texture2D texture)
        {
            ClearRows();

            if (texture == null)
            {
                return;
            }

            rows.Add(BuildRowFromTexture(texture));
            imageFilesFound = 1;
            lastLoadNote = "テクスチャ1枚を解析しました / Analysed a single texture.";
            Repaint();
        }

        /// <summary>
        /// フォルダ内の画像をまとめて解析する。1枚の失敗で全体を止めない。
        /// Analyses every image in a folder. One failure never stops the whole batch.
        /// </summary>
        private void AnalyzeFolder(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            {
                Debug.LogError(
                    "[CardPreviewWindow] フォルダが見つかりません / Folder not found: " + directoryPath);
                return;
            }

            ClearRows();

            string[] allFiles;
            try
            {
                allFiles = Directory.GetFiles(directoryPath);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[CardPreviewWindow] フォルダを読めませんでした / Could not read the folder '"
                    + directoryPath + "': " + e.Message);
                return;
            }

            // パターン指定 (*.jpg 等) を使わず全件取ってから拡張子で絞る。パターンだと大文字小文字や
            // 短縮名の扱いがプラットフォームで違い、同じファイルが二重に出ることがあるため。
            // All files are listed once and filtered by extension rather than using patterns such as *.jpg:
            // pattern matching differs across platforms in case handling and short names, which can yield the
            // same file twice.
            var imageFiles = new List<string>();
            for (var i = 0; i < allFiles.Length; i++)
            {
                if (IsSupportedImagePath(allFiles[i]))
                {
                    imageFiles.Add(allFiles[i]);
                }
            }

            imageFiles.Sort(StringComparer.OrdinalIgnoreCase);
            imageFilesFound = imageFiles.Count;

            if (imageFilesFound == 0)
            {
                lastLoadNote = "対応する画像が見つかりませんでした / No supported images found in: " + directoryPath;
                Debug.LogWarning("[CardPreviewWindow] " + lastLoadNote);
                Repaint();
                return;
            }

            var loadCount = Mathf.Min(imageFilesFound, fileLoadLimit);

            try
            {
                for (var i = 0; i < loadCount; i++)
                {
                    EditorUtility.DisplayProgressBar(
                        "Card Preview",
                        Path.GetFileName(imageFiles[i]) + " (" + (i + 1) + "/" + loadCount + ")",
                        loadCount == 0 ? 1f : (float)i / loadCount);

                    rows.Add(BuildRowFromFile(imageFiles[i]));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            lastLoadNote = imageFilesFound + " 件中 " + loadCount + " 件を読み込みました / Loaded "
                + loadCount + " of " + imageFilesFound + " image file(s) found in: " + directoryPath;

            if (loadCount < imageFilesFound)
            {
                Debug.LogWarning("[CardPreviewWindow] " + lastLoadNote
                    + " — 上限を上げると残りも読み込めます / raise the file limit to load the rest.");
            }

            Repaint();
        }

        /// <summary>
        /// ファイル1件を読み込んで導出する。例外はこの中で受け止め、行のエラーとして残す。
        /// Loads and derives one file. Exceptions are caught here and recorded on the row.
        /// </summary>
        private static PreviewRow BuildRowFromFile(string path)
        {
            var row = new PreviewRow
            {
                Label = Path.GetFileName(path),
                SourcePath = path,
            };

            try
            {
                var bytes = File.ReadAllBytes(path);

                // 破棄漏れを防ぐため、LoadImage の成否より先に行へ登録する。 / Registered before LoadImage so
                // the texture is destroyed even when decoding fails.
                var texture = new Texture2D(2, 2);
                row.Texture = texture;
                row.OwnsTexture = true;

                if (!texture.LoadImage(bytes))
                {
                    row.Error = "画像として復号できませんでした (jpg / png のみ対応) / "
                        + "Could not decode as an image; only jpg and png are supported.";
                    return row;
                }

                texture.name = row.Label;
                Derive(row, texture, bytes, path);
            }
            catch (Exception e)
            {
                row.Error = e.GetType().Name + ": " + e.Message;
            }

            return row;
        }

        /// <summary>
        /// ドラッグで渡されたテクスチャから行を作る。元ファイルが辿れる場合は、端末と同じ経路を通すため読み直す。
        /// Builds a row from a dragged-in texture. When the original file can be located it is re-read from disk,
        /// so the pipeline matches what runs on the device.
        /// </summary>
        private static PreviewRow BuildRowFromTexture(Texture2D texture)
        {
            var assetPath = AssetDatabase.GetAssetPath(texture);

            if (!string.IsNullOrEmpty(assetPath) && IsSupportedImagePath(assetPath))
            {
                // インポート済みテクスチャは圧縮・縮小・読み取り不可の設定が効いてしまい、実機と違う結果になる。
                // 元ファイルのバイト列から読み直せば、フォルダ経路と完全に同じ処理になる。
                // An imported texture carries compression, size limits and possibly no CPU read access, all of
                // which would diverge from the device. Re-reading the original bytes makes this identical to the
                // folder path.
                var absolutePath = ToAbsoluteAssetPath(assetPath);
                if (!string.IsNullOrEmpty(absolutePath) && File.Exists(absolutePath))
                {
                    var fileRow = BuildRowFromFile(absolutePath);
                    fileRow.Label = assetPath;
                    return fileRow;
                }
            }

            var row = new PreviewRow
            {
                Label = string.IsNullOrEmpty(texture.name) ? "(unnamed texture)" : texture.name,
                SourcePath = assetPath,
                Texture = texture,
                OwnsTexture = false,
            };

            try
            {
                // 元ファイルが無い場合だけ、テクスチャを再エンコードしてハッシュ用のバイト列を作る。
                // ここで得るハッシュは元ファイルのハッシュとは一致しない点に注意。
                // Only when there is no original file: re-encode the texture to get bytes for the hash. Note the
                // resulting hash will not match the hash of any original file.
                var bytes = texture.EncodeToPNG();
                if (bytes == null || bytes.Length == 0)
                {
                    row.Error = "テクスチャを読み出せませんでした。インポート設定の Read/Write Enabled を確認してください。 / "
                        + "Could not read the texture back; check Read/Write Enabled in its import settings.";
                    return row;
                }

                Derive(row, texture, bytes, assetPath);
            }
            catch (Exception e)
            {
                row.Error = e.GetType().Name + ": " + e.Message
                    + " (インポート設定の Read/Write Enabled が原因のことが多い / "
                    + "usually caused by Read/Write Enabled being off in the import settings)";
            }

            return row;
        }

        /// <summary>
        /// 特徴量抽出からカード合成までを走らせる。 / Runs feature extraction through to card composition.
        /// </summary>
        private static void Derive(PreviewRow row, Texture2D texture, byte[] bytes, string imagePath)
        {
            row.Features = PhotoAnalyzer.Analyze(texture);
            row.HasFeatures = true;

            row.PhotoHash = PhotoStatDeriver.ComputeHash(bytes);
            row.DerivedCard = PhotoCardComposer.Compose(row.Features, row.PhotoHash, imagePath);

            if (row.DerivedCard == null)
            {
                row.Error = "Compose が null を返しました / Compose returned null.";
                return;
            }

            row.Succeeded = true;
        }

        private static bool IsSupportedImagePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            extension = extension.ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png";
        }

        /// <summary>
        /// <c>Assets/...</c> 形式のパスを絶対パスへ変換する。Assets 配下でない場合は null。
        /// Converts an <c>Assets/...</c> path into an absolute path; null when the path is not under Assets.
        /// </summary>
        private static string ToAbsoluteAssetPath(string assetPath)
        {
            // Application.dataPath は <project>/Assets を指す。 / Application.dataPath points at <project>/Assets.
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return null;
            }

            return Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
        }

        // -------------------------------------------------------------------------
        // TSV
        // -------------------------------------------------------------------------

        /// <summary>
        /// 全件をTSVでクリップボードに入れる。表計算に貼って調整するための出口。
        /// Puts every row on the clipboard as TSV, so results can be pasted into a spreadsheet for tuning.
        /// </summary>
        private void CopyResultsAsTsv()
        {
            var builder = new StringBuilder();
            builder.Append("index\tfile\tstatus\terror\tgenre\telement\trarity\thp\tattack\tdefense\tspeed")
                .Append("\tphotoHash\tname\tflavor")
                .Append("\tgreen\tbrown\tblueGray\tsaturation\tbrightness\tcontrast")
                .Append("\tedgeDensity\tedgeDirectionality\tdarkRatio")
                .Append('\n');

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null)
                {
                    continue;
                }

                builder.Append(i + 1).Append('\t');
                builder.Append(Sanitize(row.Label)).Append('\t');
                builder.Append(row.Succeeded ? "ok" : "failed").Append('\t');
                builder.Append(Sanitize(row.Error)).Append('\t');

                if (row.Succeeded)
                {
                    var card = row.DerivedCard;
                    builder.Append(card.WeaponGenre.ToString()).Append('\t');
                    builder.Append(card.Element.ToString()).Append('\t');
                    builder.Append(card.Rarity.ToString()).Append('\t');
                    builder.Append(card.Stats.Hp).Append('\t');
                    builder.Append(card.Stats.Attack).Append('\t');
                    builder.Append(card.Stats.Defense).Append('\t');
                    builder.Append(card.Stats.Speed).Append('\t');
                    builder.Append(row.PhotoHash).Append('\t');
                    builder.Append(Sanitize(card.DisplayName)).Append('\t');
                    builder.Append(Sanitize(card.FlavorText)).Append('\t');
                }
                else
                {
                    // 失敗行も列数を揃えて出す。表計算で列がずれると読めなくなるため。
                    // Failed rows keep the same column count; a spreadsheet with shifted columns is unreadable.
                    builder.Append("\t\t\t\t\t\t\t\t\t\t");
                }

                AppendFeatures(builder, row);
                builder.Append('\n');
            }

            EditorGUIUtility.systemCopyBuffer = builder.ToString();
            Debug.Log(
                "[CardPreviewWindow] " + rows.Count
                + " 件をTSVとしてクリップボードにコピーしました / Copied " + rows.Count
                + " row(s) to the clipboard as TSV.");
        }

        private static void AppendFeatures(StringBuilder builder, PreviewRow row)
        {
            if (!row.HasFeatures)
            {
                builder.Append("\t\t\t\t\t\t\t\t");
                return;
            }

            var features = row.Features;
            AppendFloat(builder, features.GreenRatio);
            AppendFloat(builder, features.BrownRatio);
            AppendFloat(builder, features.BlueGrayRatio);
            AppendFloat(builder, features.MeanSaturation);
            AppendFloat(builder, features.MeanBrightness);
            AppendFloat(builder, features.Contrast);
            AppendFloat(builder, features.EdgeDensity);
            AppendFloat(builder, features.EdgeDirectionality);
            builder.Append(FormatFloat(features.DarkRatio));
        }

        private static void AppendFloat(StringBuilder builder, float value)
        {
            builder.Append(FormatFloat(value)).Append('\t');
        }

        /// <summary>
        /// 表計算に貼る前提なので、小数点は必ず '.' にする。 / The decimal separator is always '.', for pasting.
        /// </summary>
        private static string FormatFloat(float value)
        {
            return value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// TSVの区切りを壊す文字を空白に置き換える。 / Replaces characters that would break the TSV with spaces.
        /// </summary>
        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        // -------------------------------------------------------------------------
        // 後始末 / Cleanup
        // -------------------------------------------------------------------------

        /// <summary>
        /// ドメインリロード後は private フィールドが初期値に戻るため、使う前に必ず通す。
        /// Private fields reset to their defaults after a domain reload, so this runs before the list is used.
        /// </summary>
        private void EnsureRows()
        {
            if (rows == null)
            {
                rows = new List<PreviewRow>();
            }
        }

        /// <summary>
        /// 一覧を破棄する。自前で作ったテクスチャだけを <c>DestroyImmediate</c> する。
        /// Drops the list, calling <c>DestroyImmediate</c> only on textures this window created.
        /// </summary>
        private void ClearRows()
        {
            if (rows == null)
            {
                rows = new List<PreviewRow>();
                return;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || !row.OwnsTexture || row.Texture == null)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(row.Texture);
                row.Texture = null;
            }

            rows.Clear();
            imageFilesFound = 0;
        }

        /// <summary>
        /// 一覧の1行。1枚の写真に対する解析結果と、その表示に必要なものだけを持つ。
        /// One row of the list: the analysis result for a single photo plus what is needed to display it.
        /// </summary>
        private sealed class PreviewRow
        {
            /// <summary>表示名 (ファイル名)。 / Display name, i.e. the file name.</summary>
            public string Label;

            /// <summary>元ファイルの絶対パスまたはアセットパス。 / Absolute path or asset path of the source file.</summary>
            public string SourcePath;

            /// <summary>プレビュー用テクスチャ。 / Texture used for the preview.</summary>
            public Texture2D Texture;

            /// <summary>
            /// このウィンドウがテクスチャを作ったか。true のものだけ破棄する。
            /// Whether this window created the texture; only those are destroyed.
            /// </summary>
            public bool OwnsTexture;

            /// <summary>カードまで導出できたか。 / Whether derivation reached a card.</summary>
            public bool Succeeded;

            /// <summary>特徴量を取れたか。カード生成が失敗しても特徴量だけは見たいことがある。
            /// Whether features were extracted; they are worth seeing even when composition failed.</summary>
            public bool HasFeatures;

            /// <summary>失敗理由。 / Why it failed.</summary>
            public string Error;

            /// <summary>抽出した特徴量。 / The extracted features.</summary>
            public PhotoFeatures Features;

            /// <summary>写真のハッシュ。 / Hash of the photo.</summary>
            public uint PhotoHash;

            /// <summary>導出されたカード。 / The derived card.</summary>
            public Card DerivedCard;
        }
    }
}
#endif
