using System.Collections;
using CitaiTokens.Cards;
using CitaiTokens.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CitaiTokens.UI
{
    /// <summary>
    /// 生成されたカードのお披露目画面。カードは撮影画面で既に保存済みなので、ここには「保存する」操作が無い。
    /// The reveal screen for a freshly generated card. The card was already saved by the capture screen, so
    /// there is deliberately no "save" action here.
    /// </summary>
    public sealed class CardResultScreen : ScreenBase
    {
        /// <summary>お披露目演出の長さ (秒)。 / Duration of the reveal animation, in seconds.</summary>
        public const float RevealSeconds = 0.3f;

        /// <summary>サムネイル表示枠の高さ (px)。 / Height of the thumbnail box, in pixels.</summary>
        private const float ThumbnailBoxHeight = 420f;

        /// <summary>サムネイルの最大表示幅 (px)。 / Maximum displayed width of the thumbnail, in pixels.</summary>
        private const float ThumbnailMaxWidth = 620f;

        private bool built;

        private CanvasGroup cardGroup;
        private RectTransform cardPanel;
        private RawImage thumbnailRaw;
        private Text noImageText;
        private Text nameText;
        private Text metaText;
        private Text statsText;
        private Text flavorText;

        private Card currentCard;
        private Texture2D currentTexture;
        private Coroutine revealRoutine;

        /// <summary>この画面の識別子。 / The identifier of this screen.</summary>
        public override ScreenId Id => ScreenId.CardResult;

        /// <summary>
        /// 画面表示時に呼ばれる。payload は生成されたばかりの <see cref="Card"/>。
        /// Called when the screen is shown; the payload is the freshly generated <see cref="Card"/>.
        /// </summary>
        /// <param name="payload">生成されたカード。 / The generated card.</param>
        public override void OnShow(object payload)
        {
            Build();

            var card = payload as Card;
            if (card == null)
            {
                Debug.LogError(
                    "[CardResultScreen] payload が Card ではありません。タイトルに戻ります。 / "
                    + "The payload is not a Card; returning to the title screen.");
                Navigate(ScreenId.Title, null);
                return;
            }

            currentCard = card;
            Populate(card);
            StartReveal();
        }

        /// <summary>
        /// 画面が隠されるときに呼ばれる。演出コルーチンを止める。
        /// Called when the screen is hidden; stops the reveal coroutine.
        /// </summary>
        public override void OnHide()
        {
            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
                revealRoutine = null;
            }

            ApplyReveal(1f);
        }

        /// <summary>
        /// 破棄時に読み込んだテクスチャを解放する。 / Releases the loaded texture on destruction.
        /// </summary>
        private void OnDestroy()
        {
            ReleaseTexture();
        }

        /// <summary>
        /// UIを1度だけ組み立てる。 / Builds the UI exactly once.
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
                UiFactory.DefaultSpacing,
                new RectOffset(
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding));
            var root = layout.transform;

            var heading = UiFactory.CreateText(
                root,
                "カードができた！",
                UiFactory.FontSizeHeading,
                TextAnchor.MiddleCenter,
                UiFactory.TextColor);
            UiFactory.SetFixedHeight(heading.gameObject, 70f);

            var panelImage = UiFactory.CreateImage(root, "CardPanel", UiFactory.PanelColor);
            cardPanel = panelImage.rectTransform;
            var panelElement = cardPanel.gameObject.AddComponent<LayoutElement>();
            panelElement.flexibleHeight = 1f;
            panelElement.minHeight = 700f;
            cardGroup = cardPanel.gameObject.AddComponent<CanvasGroup>();

            var cardLayout = UiFactory.CreateVerticalLayout(
                cardPanel,
                UiFactory.DefaultSpacing * 0.5f,
                new RectOffset(24, 24, 24, 24));
            var cardRoot = cardLayout.transform;

            var thumbnailBox = UiFactory.CreateRect(cardRoot, "ThumbnailBox");
            UiFactory.SetFixedHeight(thumbnailBox.gameObject, ThumbnailBoxHeight);

            thumbnailRaw = UiFactory.CreateRawImage(thumbnailBox);
            var thumbnailRect = thumbnailRaw.rectTransform;
            thumbnailRect.anchorMin = new Vector2(0.5f, 0.5f);
            thumbnailRect.anchorMax = new Vector2(0.5f, 0.5f);
            thumbnailRect.pivot = new Vector2(0.5f, 0.5f);
            thumbnailRect.anchoredPosition = Vector2.zero;
            thumbnailRect.sizeDelta = new Vector2(ThumbnailBoxHeight, ThumbnailBoxHeight);

            noImageText = UiFactory.CreateText(
                thumbnailBox,
                "(写真を読み込めませんでした)",
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.Stretch(noImageText.rectTransform);
            noImageText.gameObject.SetActive(false);

            nameText = UiFactory.CreateText(
                cardRoot,
                string.Empty,
                UiFactory.FontSizeHeading,
                TextAnchor.MiddleCenter,
                UiFactory.TextColor);
            UiFactory.SetFixedHeight(nameText.gameObject, 110f);

            metaText = UiFactory.CreateText(
                cardRoot,
                string.Empty,
                UiFactory.FontSizeBody,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(metaText.gameObject, 48f);

            statsText = UiFactory.CreateText(
                cardRoot,
                string.Empty,
                UiFactory.FontSizeBody,
                TextAnchor.MiddleCenter,
                UiFactory.TextColor);
            UiFactory.SetFixedHeight(statsText.gameObject, 190f);

            flavorText = UiFactory.CreateText(
                cardRoot,
                string.Empty,
                UiFactory.FontSizeSmall,
                TextAnchor.UpperCenter,
                UiFactory.SubTextColor);
            var flavorElement = UiFactory.SetFlexibleWidth(flavorText.gameObject, 1f);
            flavorElement.flexibleHeight = 1f;
            flavorElement.minHeight = 90f;

            UiFactory.CreateButton(
                root,
                "このカードでバトル",
                UiFactory.PrimaryButtonColor,
                OnBattlePressed);

            UiFactory.CreateButton(
                root,
                "コレクションへ",
                UiFactory.SecondaryButtonColor,
                () => Navigate(ScreenId.Collection, null));
        }

        /// <summary>
        /// カードの内容を表示に反映する。 / Applies the card's contents to the widgets.
        /// </summary>
        private void Populate(Card card)
        {
            nameText.text = string.IsNullOrEmpty(card.DisplayName) ? "名もなきカード" : card.DisplayName;
            metaText.text = CardTextFormatter.ElementAndRarity(card.Element, card.Rarity);
            statsText.text = CardTextFormatter.StatLine(card.Stats);
            flavorText.text = string.IsNullOrEmpty(card.FlavorText) ? string.Empty : card.FlavorText;

            ReleaseTexture();

            if (GameContext.IsInitialized)
            {
                currentTexture = GameContext.Thumbnails.LoadThumbnail(card.ImagePath);
            }

            ApplyThumbnail(currentTexture);
        }

        /// <summary>
        /// サムネイルを縦横比を保って表示枠に収める。読み込めなかった場合は代わりの文言を出す。
        /// Fits the thumbnail into its box while preserving the aspect ratio, or shows a fallback line when the
        /// image could not be loaded.
        /// </summary>
        private void ApplyThumbnail(Texture2D texture)
        {
            if (thumbnailRaw == null)
            {
                return;
            }

            if (texture == null)
            {
                thumbnailRaw.texture = null;
                thumbnailRaw.enabled = false;
                if (noImageText != null)
                {
                    noImageText.gameObject.SetActive(true);
                }

                return;
            }

            if (noImageText != null)
            {
                noImageText.gameObject.SetActive(false);
            }

            var width = Mathf.Max(1, texture.width);
            var height = Mathf.Max(1, texture.height);
            var displayHeight = ThumbnailBoxHeight;
            var displayWidth = displayHeight * (width / (float)height);

            if (displayWidth > ThumbnailMaxWidth)
            {
                var shrink = ThumbnailMaxWidth / displayWidth;
                displayWidth = ThumbnailMaxWidth;
                displayHeight *= shrink;
            }

            thumbnailRaw.texture = texture;
            thumbnailRaw.enabled = true;
            thumbnailRaw.rectTransform.sizeDelta = new Vector2(displayWidth, displayHeight);
        }

        /// <summary>
        /// お披露目演出を始める。演出は短く、押せば飛ばせる長さに留める。
        /// Starts the reveal animation, kept short enough that it never gets in the player's way.
        /// </summary>
        private void StartReveal()
        {
            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
                revealRoutine = null;
            }

            ApplyReveal(0f);
            revealRoutine = StartCoroutine(RevealRoutine());
        }

        /// <summary>
        /// カードパネルをふわっと出す。<see cref="Time.unscaledDeltaTime"/> を使うので時間停止の影響を受けない。
        /// Fades and scales the card panel in, using <see cref="Time.unscaledDeltaTime"/> so a paused time scale
        /// cannot stall it.
        /// </summary>
        private IEnumerator RevealRoutine()
        {
            var elapsed = 0f;
            while (elapsed < RevealSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                ApplyReveal(Mathf.Clamp01(elapsed / RevealSeconds));
                yield return null;
            }

            ApplyReveal(1f);
            revealRoutine = null;
        }

        /// <summary>
        /// 演出の進行度 (0〜1) を表示に反映する。 / Applies the reveal progress, from zero to one.
        /// </summary>
        private void ApplyReveal(float progress)
        {
            var clamped = Mathf.Clamp01(progress);

            if (cardGroup != null)
            {
                cardGroup.alpha = clamped;
            }

            if (cardPanel != null)
            {
                var scale = Mathf.Lerp(0.92f, 1f, clamped);
                cardPanel.localScale = new Vector3(scale, scale, 1f);
            }
        }

        /// <summary>
        /// 読み込んだテクスチャを破棄する。画面を何度も開いてもメモリが積み上がらないようにする。
        /// Destroys the loaded texture, so reopening this screen does not pile up memory.
        /// </summary>
        private void ReleaseTexture()
        {
            if (thumbnailRaw != null)
            {
                thumbnailRaw.texture = null;
            }

            if (currentTexture != null)
            {
                Destroy(currentTexture);
                currentTexture = null;
            }
        }

        /// <summary>
        /// 「このカードでバトル」が押されたときの処理。 / Handles the battle button.
        /// </summary>
        private void OnBattlePressed()
        {
            if (currentCard == null)
            {
                Navigate(ScreenId.Collection, null);
                return;
            }

            Navigate(ScreenId.Battle, currentCard);
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
                    "[CardResultScreen] ScreenRouter が見つかりません。 / ScreenRouter.Instance is null.");
                return;
            }

            ScreenRouter.Instance.Show(id, payload);
        }
    }
}
