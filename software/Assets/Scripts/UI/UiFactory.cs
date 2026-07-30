using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CitaiTokens.UI
{
    /// <summary>
    /// 6つの画面が共通で使うUIウィジェットの組み立て係。プレハブを持たない方針なので、
    /// レイアウトの決まりごと (色・文字サイズ・タップ領域の高さ) をここ1か所に集める。
    /// Builds the UI widgets shared by the six screens. The project deliberately ships no prefabs, so every
    /// layout convention (palette, font sizes, touch-target heights) is collected in this one place.
    /// </summary>
    /// <remarks>
    /// TextMeshPro を使わず旧 <see cref="Text"/> を使うのは、本作の文字がすべて日本語だからである。
    /// TMP は事前に焼いたフォントアトラスを要求し、既定のTMPフォントにCJKグリフが無いため、
    /// 日本語はすべて豆腐 (□□□) になる。旧 <see cref="Text"/> の動的フォントはOSフォントに
    /// フォールバックするので、Editor でも Android でも日本語がそのまま出る。
    /// Legacy <see cref="Text"/> is used instead of TextMeshPro because every player-facing string here is
    /// Japanese. TMP needs a pre-baked font atlas and its default font asset has no CJK glyphs, so Japanese
    /// would render as tofu boxes. The legacy dynamic font falls back to OS fonts and renders Japanese both in
    /// the Editor and on Android.
    /// </remarks>
    public static class UiFactory
    {
        /// <summary>画面全体の背景色 (夜の森のような暗い緑)。 / Screen background colour, a dark forest green.</summary>
        public static readonly Color BackgroundColor = new Color(0.078f, 0.113f, 0.094f, 1f);

        /// <summary>カードや行の下地色。 / Fill colour for cards and list rows.</summary>
        public static readonly Color PanelColor = new Color(0.145f, 0.192f, 0.161f, 1f);

        /// <summary>プレビュー上に重ねる半透明の下地色。 / Semi-transparent backing used on top of the camera preview.</summary>
        public static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>本文の文字色。 / Colour of body text.</summary>
        public static readonly Color TextColor = new Color(0.949f, 0.969f, 0.937f, 1f);

        /// <summary>補足情報の文字色。 / Colour of secondary text.</summary>
        public static readonly Color SubTextColor = new Color(0.702f, 0.769f, 0.706f, 1f);

        /// <summary>主要ボタンの色 (若葉色)。 / Colour of primary buttons, a young-leaf green.</summary>
        public static readonly Color PrimaryButtonColor = new Color(0.267f, 0.549f, 0.302f, 1f);

        /// <summary>副ボタンの色。 / Colour of secondary buttons.</summary>
        public static readonly Color SecondaryButtonColor = new Color(0.216f, 0.259f, 0.227f, 1f);

        /// <summary>注意・失敗を伝える文字色。 / Text colour used for warnings and failures.</summary>
        public static readonly Color WarningColor = new Color(1f, 0.792f, 0.353f, 1f);

        /// <summary>HPバーの溝の色。 / Colour of the HP bar track.</summary>
        public static readonly Color HpTrackColor = new Color(0.09f, 0.11f, 0.09f, 1f);

        /// <summary>プレイヤー側HPバーの色。 / Colour of the player's HP bar.</summary>
        public static readonly Color PlayerHpColor = new Color(0.353f, 0.788f, 0.416f, 1f);

        /// <summary>CPU側HPバーの色。 / Colour of the CPU's HP bar.</summary>
        public static readonly Color CpuHpColor = new Color(0.878f, 0.435f, 0.396f, 1f);

        /// <summary>ゲームタイトル用の文字サイズ。 / Font size for the game title.</summary>
        public const int FontSizeTitle = 72;

        /// <summary>勝敗表示など、特に大きく見せたい文字サイズ。 / Font size for the largest statements, e.g. win or lose.</summary>
        public const int FontSizeHuge = 96;

        /// <summary>見出しの文字サイズ。 / Font size for headings.</summary>
        public const int FontSizeHeading = 44;

        /// <summary>本文の文字サイズ。 / Font size for body text.</summary>
        public const int FontSizeBody = 32;

        /// <summary>補足情報の文字サイズ。 / Font size for secondary text.</summary>
        public const int FontSizeSmall = 26;

        /// <summary>
        /// ボタンの高さ (px)。スマホの指で確実に押せる大きさを最低線として固定する。
        /// Button height in pixels, fixed at a size a finger can reliably hit on a phone.
        /// </summary>
        public const float ButtonHeight = 120f;

        /// <summary>コレクション1行の高さ (px)。 / Height of one collection row, in pixels.</summary>
        public const float RowHeight = 180f;

        /// <summary>一覧のサムネイル一辺の長さ (px)。 / Edge length of a list thumbnail, in pixels.</summary>
        public const float RowThumbnailSize = 148f;

        /// <summary>HPバーの高さ (px)。 / Height of an HP bar, in pixels.</summary>
        public const float HpBarHeight = 28f;

        /// <summary>画面外周の余白 (px)。 / Outer screen padding, in pixels.</summary>
        public const int ScreenPadding = 40;

        /// <summary>ウィジェット間の標準の間隔 (px)。 / Default gap between widgets, in pixels.</summary>
        public const float DefaultSpacing = 24f;

        /// <summary>解決済みの組み込みフォント。1度だけ解決してキャッシュする。 / The resolved built-in font, resolved once and cached.</summary>
        private static Font cachedFont;

        /// <summary>フォント解決を試みたか。null が正しい結果の場合に再試行しないため。 / Whether font resolution was attempted, so a null result is not retried forever.</summary>
        private static bool fontResolved;

        /// <summary>
        /// 日本語が表示できる組み込みフォントを返す。Unity のバージョンによって組み込みフォント名が
        /// 変わっている (LegacyRuntime.ttf / Arial.ttf) ため、順に試して最初に見つかったものを使う。
        /// どちらも取れない場合は OS フォントから動的フォントを作り、それも失敗したら警告を出して null を返す
        /// (null でも <see cref="Text"/> は生成でき、文字が出ないだけなので、ここで例外にはしない)。
        /// Returns a built-in font able to display Japanese. The built-in font was renamed across Unity
        /// versions (LegacyRuntime.ttf versus Arial.ttf), so both names are tried in order. If neither
        /// resolves, a dynamic font is created from OS fonts; if that fails too, a warning is logged and null
        /// is returned. Null is tolerated on purpose: a <see cref="Text"/> still builds, it just draws nothing.
        /// </summary>
        public static Font ResolveFont()
        {
            if (fontResolved)
            {
                return cachedFont;
            }

            fontResolved = true;

            cachedFont = TryGetBuiltinFont("LegacyRuntime.ttf");
            if (cachedFont != null)
            {
                return cachedFont;
            }

            cachedFont = TryGetBuiltinFont("Arial.ttf");
            if (cachedFont != null)
            {
                return cachedFont;
            }

            cachedFont = TryCreateOsFont();
            if (cachedFont != null)
            {
                Debug.LogWarning(
                    "[UiFactory] 組み込みフォントが見つからなかったため、OSフォントから動的フォントを作成しました。 / "
                    + "No built-in font resolved; created a dynamic font from OS fonts instead.");
                return cachedFont;
            }

            Debug.LogWarning(
                "[UiFactory] 使用可能なフォントを解決できませんでした。文字が表示されない可能性があります。 / "
                + "Could not resolve any usable font; text may not be visible.");
            return null;
        }

        /// <summary>
        /// 親いっぱいに広がるパネルを作る。各画面の一番下の層として使う。
        /// Creates a panel stretched to fill its parent, used as the bottom layer of each screen.
        /// </summary>
        /// <param name="parent">親となる Transform。 / The parent transform.</param>
        /// <param name="name">GameObject 名。 / Name of the created GameObject.</param>
        /// <param name="background">下地の色。完全に透明にしたい場合はアルファ0を渡す。 / Background colour; pass alpha 0 for a fully transparent panel.</param>
        public static RectTransform CreateFullScreenPanel(Transform parent, string name, Color background)
        {
            var image = CreateImage(parent, name, background);
            Stretch(image.rectTransform);
            return image.rectTransform;
        }

        /// <summary>
        /// 単色の <see cref="Image"/> を作る。スプライトは設定しないので、色だけの矩形になる。
        /// Creates a flat-colour <see cref="Image"/>. No sprite is assigned, so it draws a plain rectangle.
        /// </summary>
        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var rect = CreateRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>
        /// 折り返し有効の <see cref="Text"/> を作る。フォントは <see cref="ResolveFont"/> で解決したものを使う。
        /// Creates a word-wrapping <see cref="Text"/> using the font resolved by <see cref="ResolveFont"/>.
        /// </summary>
        /// <param name="parent">親となる Transform。 / The parent transform.</param>
        /// <param name="content">表示する文字列。 / The string to display.</param>
        /// <param name="fontSize">文字サイズ。 / Font size.</param>
        /// <param name="alignment">揃え方。 / Text alignment.</param>
        /// <param name="color">文字色。 / Text colour.</param>
        public static Text CreateText(
            Transform parent,
            string content,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            var rect = CreateRect(parent, "Text");
            var text = rect.gameObject.AddComponent<Text>();
            text.font = ResolveFont();
            text.text = content ?? string.Empty;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.15f;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 下地と子テキストを持つボタンを作る。高さは <see cref="ButtonHeight"/> に固定する。
        /// Creates a button with a background image and a child label, fixed to <see cref="ButtonHeight"/> tall.
        /// </summary>
        /// <param name="parent">親となる Transform。 / The parent transform.</param>
        /// <param name="label">ボタンの文字列。 / The button label.</param>
        /// <param name="onClick">押されたときの処理。null 可。 / Handler invoked on click; may be null.</param>
        public static Button CreateButton(Transform parent, string label, UnityAction onClick)
        {
            return CreateButton(parent, label, PrimaryButtonColor, onClick);
        }

        /// <summary>
        /// 色を指定してボタンを作る。主要ボタンと副ボタンを描き分けるために使う。
        /// Creates a button with an explicit colour, so primary and secondary buttons can be told apart.
        /// </summary>
        public static Button CreateButton(Transform parent, string label, Color color, UnityAction onClick)
        {
            var image = CreateImage(parent, "Button-" + (label ?? "unnamed"), color);
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(0f, -ButtonHeight * 0.5f);
            rect.offsetMax = new Vector2(0f, ButtonHeight * 0.5f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            button.colors = colors;

            var text = CreateText(rect, label, FontSizeHeading, TextAnchor.MiddleCenter, TextColor);
            Stretch(text.rectTransform);

            SetFixedHeight(rect.gameObject, ButtonHeight);
            return button;
        }

        /// <summary>
        /// カメラプレビュー用の <see cref="RawImage"/> を作る。<see cref="WebCamTexture"/> を直接貼れる。
        /// Creates a <see cref="RawImage"/> for the camera preview; a <see cref="WebCamTexture"/> binds directly to it.
        /// </summary>
        public static RawImage CreateRawImage(Transform parent)
        {
            var rect = CreateRect(parent, "RawImage");
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;
            return raw;
        }

        /// <summary>
        /// 縦積みレイアウトのコンテナを作る。返り値は親いっぱいに広がる。
        /// Creates a vertically stacked layout container, stretched to fill its parent.
        /// </summary>
        /// <param name="parent">親となる Transform。 / The parent transform.</param>
        /// <param name="spacing">要素間の間隔。 / Gap between children.</param>
        /// <param name="padding">内側の余白。null なら余白なし。 / Inner padding; null means no padding.</param>
        public static VerticalLayoutGroup CreateVerticalLayout(Transform parent, float spacing, RectOffset padding)
        {
            var rect = CreateRect(parent, "VerticalLayout");
            Stretch(rect);

            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        /// <summary>
        /// 横並びレイアウトのコンテナを作る。一覧の1行やステータス表示に使う。
        /// Creates a horizontally stacked layout container, used for list rows and stat lines.
        /// </summary>
        public static HorizontalLayoutGroup CreateHorizontalLayout(Transform parent, float spacing, RectOffset padding)
        {
            var rect = CreateRect(parent, "HorizontalLayout");
            Stretch(rect);

            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            return layout;
        }

        /// <summary>
        /// 縦スクロールするビューを作る。<paramref name="content"/> に行を追加していく。
        /// Creates a vertically scrolling view; rows are added under <paramref name="content"/>.
        /// </summary>
        /// <param name="parent">親となる Transform。 / The parent transform.</param>
        /// <param name="content">行を追加する先の RectTransform。 / The RectTransform to add rows to.</param>
        public static ScrollRect CreateScrollView(Transform parent, out RectTransform content)
        {
            var rootRect = CreateRect(parent, "ScrollView");
            Stretch(rootRect);
            var scroll = rootRect.gameObject.AddComponent<ScrollRect>();

            // ビューポート。Mask ではなく RectMask2D を使う (Graphic を持たなくても切り抜ける)。
            // The viewport clips with RectMask2D rather than Mask, so it needs no Graphic of its own.
            var viewport = CreateRect(rootRect, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = CreateRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = DefaultSpacing * 0.5f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 40f;
            return scroll;
        }

        /// <summary>
        /// 空の <see cref="RectTransform"/> を持つ子 GameObject を作る。
        /// Creates a child GameObject carrying an empty <see cref="RectTransform"/>.
        /// </summary>
        public static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "Rect" : name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        /// <summary>
        /// 親いっぱいに広げる (アンカー 0,0 - 1,1、オフセット0)。
        /// Stretches the rect to fill its parent (anchors 0,0 to 1,1 with zero offsets).
        /// </summary>
        public static void Stretch(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        /// <summary>
        /// レイアウトグループの中で高さを固定する。ボタンのタップ領域を潰さないために使う。
        /// Pins a fixed height inside a layout group, so a button's touch target never gets squeezed.
        /// </summary>
        public static LayoutElement SetFixedHeight(GameObject target, float height)
        {
            if (target == null)
            {
                return null;
            }

            var element = target.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = target.AddComponent<LayoutElement>();
            }

            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
            return element;
        }

        /// <summary>
        /// レイアウトグループの中で幅を固定する。 / Pins a fixed width inside a layout group.
        /// </summary>
        public static LayoutElement SetFixedWidth(GameObject target, float width)
        {
            if (target == null)
            {
                return null;
            }

            var element = target.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = target.AddComponent<LayoutElement>();
            }

            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
            return element;
        }

        /// <summary>
        /// レイアウトグループの中で余った横幅を受け取るようにする。
        /// Lets the target absorb the remaining horizontal space inside a layout group.
        /// </summary>
        public static LayoutElement SetFlexibleWidth(GameObject target, float weight)
        {
            if (target == null)
            {
                return null;
            }

            var element = target.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = target.AddComponent<LayoutElement>();
            }

            element.flexibleWidth = weight;
            return element;
        }

        /// <summary>
        /// レイアウトグループの中に高さだけの空白を挿す。
        /// Inserts an empty spacer of the given height into a layout group.
        /// </summary>
        public static RectTransform CreateSpacer(Transform parent, float height)
        {
            var rect = CreateRect(parent, "Spacer");
            SetFixedHeight(rect.gameObject, height);
            return rect;
        }

        /// <summary>
        /// 溝と中身の2枚組でHPバーを作る。中身の横アンカーを動かして残量を表す
        /// (スプライトを持たない <see cref="Image"/> では <c>fillAmount</c> が効かないため)。
        /// Builds an HP bar from a track and a fill. The fill's horizontal anchor expresses the remaining
        /// ratio, because <c>fillAmount</c> has no effect on an <see cref="Image"/> without a sprite.
        /// </summary>
        /// <param name="parent">親となる Transform。 / The parent transform.</param>
        /// <param name="fillColor">中身の色。 / Colour of the bar fill.</param>
        /// <param name="fill">残量を表す <see cref="Image"/>。 / The <see cref="Image"/> representing the remaining amount.</param>
        public static RectTransform CreateHpBar(Transform parent, Color fillColor, out Image fill)
        {
            var track = CreateImage(parent, "HpBarTrack", HpTrackColor);
            SetFixedHeight(track.gameObject, HpBarHeight);

            fill = CreateImage(track.rectTransform, "HpBarFill", fillColor);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            return track.rectTransform;
        }

        /// <summary>
        /// HPバーの残量を設定する。0〜1の範囲に丸める。
        /// Sets the remaining amount on an HP bar, clamping the ratio into the zero-to-one range.
        /// </summary>
        public static void SetHpBarRatio(Image fill, float ratio)
        {
            if (fill == null)
            {
                return;
            }

            var clamped = Mathf.Clamp01(ratio);
            var rect = fill.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(clamped, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 指定した名前の組み込みフォントを取得する。取れない場合は null を返し、例外は外へ出さない。
        /// Loads the named built-in font, returning null instead of letting any exception escape.
        /// </summary>
        private static Font TryGetBuiltinFont(string resourceName)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(resourceName);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    "[UiFactory] 組み込みフォント '" + resourceName + "' の取得に失敗しました / "
                    + "Failed to load the built-in font '" + resourceName + "': " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// OSにインストールされたフォントから動的フォントを作る。日本語が出る候補を順に並べてある。
        /// Creates a dynamic font from the OS-installed fonts, listing Japanese-capable candidates in order.
        /// </summary>
        private static Font TryCreateOsFont()
        {
            string[] candidates =
            {
                "Noto Sans CJK JP",
                "Noto Sans JP",
                "Hiragino Sans",
                "Yu Gothic",
                "MS Gothic",
                "Droid Sans Fallback",
                "Roboto",
                "Arial",
            };

            try
            {
                return Font.CreateDynamicFontFromOSFont(candidates, FontSizeBody);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    "[UiFactory] OSフォントからの動的フォント作成に失敗しました / "
                    + "Failed to create a dynamic font from OS fonts: " + e.Message);
                return null;
            }
        }
    }
}
