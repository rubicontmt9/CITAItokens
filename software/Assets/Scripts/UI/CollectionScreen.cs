using System.Collections.Generic;
using CitaiTokens.Cards;
using CitaiTokens.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CitaiTokens.UI
{
    /// <summary>
    /// コレクション一覧画面。行をタップするとそのカードでバトルに入る。
    /// The collection list screen; tapping a row starts a battle with that card.
    /// </summary>
    public sealed class CollectionScreen : ScreenBase
    {
        /// <summary>
        /// 1度に作る行数の上限。全件を毎回生成すると枚数が増えたときに表示が固まるため上限を設けている。
        /// Maximum number of rows built at once. Building every row would stall the screen once the collection
        /// grows, so the count is capped.
        /// </summary>
        public const int MaxRows = 50;

        private bool built;

        private RectTransform listContent;
        private ScrollRect scrollRect;
        private RectTransform emptyPanel;
        private Text noteText;

        private readonly List<GameObject> rowObjects = new List<GameObject>();
        private readonly List<Texture2D> rowTextures = new List<Texture2D>();

        /// <summary>この画面の識別子。 / The identifier of this screen.</summary>
        public override ScreenId Id => ScreenId.Collection;

        /// <summary>
        /// 画面表示時に呼ばれる。表示のたびに一覧を作り直す (撮影で増えたカードを反映するため)。
        /// Called when the screen is shown. The list is rebuilt every time, so cards added by a capture appear.
        /// </summary>
        /// <param name="payload">この画面は payload を使わない。 / This screen takes no payload.</param>
        public override void OnShow(object payload)
        {
            Build();
            Rebuild();
        }

        /// <summary>
        /// 破棄時に行のテクスチャを解放する。 / Releases the row textures on destruction.
        /// </summary>
        private void OnDestroy()
        {
            ClearRows();
        }

        /// <summary>
        /// UIの骨組みを1度だけ組み立てる。行だけは <see cref="Rebuild"/> で作り直す。
        /// Builds the fixed part of the UI exactly once; only the rows are rebuilt in <see cref="Rebuild"/>.
        /// </summary>
        private void Build()
        {
            if (built)
            {
                return;
            }

            built = true;

            UiFactory.CreateFullScreenPanel(transform, "Background", UiFactory.BackgroundColor);

            var layout = UiFactory.CreateVerticalLayout(
                transform,
                UiFactory.DefaultSpacing * 0.5f,
                new RectOffset(
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding));
            var root = layout.transform;

            var heading = UiFactory.CreateText(
                root,
                "コレクション",
                UiFactory.FontSizeHeading,
                TextAnchor.MiddleCenter,
                UiFactory.TextColor);
            UiFactory.SetFixedHeight(heading.gameObject, 70f);

            noteText = UiFactory.CreateText(
                root,
                string.Empty,
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(noteText.gameObject, 46f);

            var listArea = UiFactory.CreateRect(root, "ListArea");
            var listElement = listArea.gameObject.AddComponent<LayoutElement>();
            listElement.flexibleHeight = 1f;
            listElement.minHeight = 400f;

            scrollRect = UiFactory.CreateScrollView(listArea, out listContent);

            emptyPanel = BuildEmptyPanel(listArea);

            UiFactory.CreateButton(
                root,
                "枝を撮る",
                UiFactory.PrimaryButtonColor,
                () => Navigate(ScreenId.Capture, null));

            UiFactory.CreateButton(
                root,
                "タイトルへ",
                UiFactory.SecondaryButtonColor,
                () => Navigate(ScreenId.Title, null));
        }

        /// <summary>
        /// カードが1枚も無いときの案内を作る。行き先 (撮影) をこの場で示すのが目的。
        /// Builds the guidance shown when the collection is empty; its point is to offer the next step, capture.
        /// </summary>
        private RectTransform BuildEmptyPanel(Transform parent)
        {
            var panel = UiFactory.CreateImage(parent, "EmptyPanel", UiFactory.PanelColor);
            var panelRect = panel.rectTransform;
            UiFactory.Stretch(panelRect);

            var layout = UiFactory.CreateVerticalLayout(
                panelRect,
                UiFactory.DefaultSpacing,
                new RectOffset(32, 32, 48, 48));
            layout.childAlignment = TextAnchor.MiddleCenter;

            var message = UiFactory.CreateText(
                layout.transform,
                "まだカードがありません。\n外に出て、枝や葉、石を1枚撮ってみましょう。",
                UiFactory.FontSizeBody,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(message.gameObject, 160f);

            UiFactory.CreateButton(
                layout.transform,
                "枝を撮りにいく",
                UiFactory.PrimaryButtonColor,
                () => Navigate(ScreenId.Capture, null));

            panelRect.gameObject.SetActive(false);
            return panelRect;
        }

        /// <summary>
        /// 一覧を作り直す。古い行は必ず破棄してから作るので、行が積み上がることはない。
        /// Rebuilds the list, always destroying the old rows first so rows never accumulate.
        /// </summary>
        private void Rebuild()
        {
            ClearRows();

            if (!GameContext.IsInitialized)
            {
                Debug.LogError(
                    "[CollectionScreen] GameContext が初期化されていません。 / GameContext has not been initialized.");
                ShowEmptyState("コレクションを読み込めませんでした。");
                return;
            }

            var cards = GameContext.Cards.GetAll();
            var total = cards != null ? cards.Count : 0;

            if (total == 0)
            {
                ShowEmptyState(string.Empty);
                return;
            }

            if (emptyPanel != null)
            {
                emptyPanel.gameObject.SetActive(false);
            }

            if (scrollRect != null)
            {
                scrollRect.gameObject.SetActive(true);
            }

            var shown = Mathf.Min(total, MaxRows);
            for (var i = 0; i < shown; i++)
            {
                var card = cards[i];
                if (card == null)
                {
                    continue;
                }

                CreateRow(card);
            }

            if (noteText != null)
            {
                noteText.text = shown < total
                    ? "全 " + total + "枚のうち、新しい " + shown + "枚を表示しています。"
                    : "全 " + total + "枚";
            }

            if (scrollRect != null)
            {
                // 先頭 (最新のカード) が見えている状態から始める。 / Start scrolled to the top, i.e. the newest card.
                scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>
        /// 1枚分の行を作る。行全体がボタンなので、指のどこが当たってもバトルに入れる。
        /// Builds one row. The whole row is the button, so any part of it under a finger starts a battle.
        /// </summary>
        private void CreateRow(Card card)
        {
            var rowImage = UiFactory.CreateImage(listContent, "Row-" + card.Id, UiFactory.PanelColor);
            var rowRect = rowImage.rectTransform;
            UiFactory.SetFixedHeight(rowRect.gameObject, UiFactory.RowHeight);

            var rowButton = rowRect.gameObject.AddComponent<Button>();
            rowButton.targetGraphic = rowImage;
            var colors = rowButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            rowButton.colors = colors;

            var selected = card;
            rowButton.onClick.AddListener(() => Navigate(ScreenId.Battle, selected));

            var rowLayout = rowRect.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 20f;
            rowLayout.padding = new RectOffset(16, 16, 16, 16);
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            var thumbnail = UiFactory.CreateRawImage(rowRect);
            var thumbnailElement = UiFactory.SetFixedWidth(
                thumbnail.gameObject,
                UiFactory.RowThumbnailSize);
            thumbnailElement.minHeight = UiFactory.RowThumbnailSize;
            thumbnailElement.preferredHeight = UiFactory.RowThumbnailSize;
            thumbnailElement.flexibleHeight = 0f;

            var texture = GameContext.Thumbnails.LoadThumbnail(card.ImagePath);
            if (texture != null)
            {
                rowTextures.Add(texture);
                thumbnail.texture = texture;
                thumbnail.enabled = true;
            }
            else
            {
                thumbnail.texture = null;
                thumbnail.color = UiFactory.HpTrackColor;
            }

            var textColumn = UiFactory.CreateVerticalLayout(rowRect, 4f, new RectOffset(0, 0, 0, 0));
            textColumn.childAlignment = TextAnchor.MiddleLeft;
            textColumn.childForceExpandWidth = true;
            var columnElement = UiFactory.SetFlexibleWidth(textColumn.gameObject, 1f);
            columnElement.minWidth = 200f;

            var nameText = UiFactory.CreateText(
                textColumn.transform,
                string.IsNullOrEmpty(card.DisplayName) ? "名もなきカード" : card.DisplayName,
                UiFactory.FontSizeBody,
                TextAnchor.MiddleLeft,
                UiFactory.TextColor);
            UiFactory.SetFixedHeight(nameText.gameObject, 46f);

            var metaText = UiFactory.CreateText(
                textColumn.transform,
                CardTextFormatter.ElementAndRarity(card.Element, card.Rarity),
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleLeft,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(metaText.gameObject, 38f);

            var statText = UiFactory.CreateText(
                textColumn.transform,
                CardTextFormatter.CompactStatLine(card.Stats),
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleLeft,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(statText.gameObject, 38f);

            rowObjects.Add(rowRect.gameObject);
        }

        /// <summary>
        /// カードが無い状態を表示する。 / Shows the empty state.
        /// </summary>
        private void ShowEmptyState(string note)
        {
            if (noteText != null)
            {
                noteText.text = note ?? string.Empty;
            }

            if (emptyPanel != null)
            {
                emptyPanel.gameObject.SetActive(true);
            }

            if (scrollRect != null)
            {
                scrollRect.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 生成済みの行とそのテクスチャを破棄する。 / Destroys the built rows and their textures.
        /// </summary>
        private void ClearRows()
        {
            for (var i = 0; i < rowObjects.Count; i++)
            {
                if (rowObjects[i] != null)
                {
                    Destroy(rowObjects[i]);
                }
            }

            rowObjects.Clear();

            for (var i = 0; i < rowTextures.Count; i++)
            {
                if (rowTextures[i] != null)
                {
                    Destroy(rowTextures[i]);
                }
            }

            rowTextures.Clear();
        }

        /// <summary>
        /// 画面遷移する。ルーターが無い場合はエラーログのみで、例外は投げない。
        /// Navigates to another screen, logging an error rather than throwing when the router is absent.
        /// </summary>
        private static void Navigate(ScreenId id, object payload)
        {
            if (ScreenRouter.Instance == null)
            {
                Debug.LogError(
                    "[CollectionScreen] ScreenRouter が見つかりません。 / ScreenRouter.Instance is null.");
                return;
            }

            ScreenRouter.Instance.Show(id, payload);
        }
    }
}
