using CitaiTokens.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CitaiTokens.UI
{
    /// <summary>
    /// タイトル画面。このゲームが「屋外に出て枝を撮る」ゲームであることを最初の一画面で伝える役目を持つ。
    /// The title screen. Its job is to make clear, in the very first screen, that this is a game about going
    /// outside and photographing a branch.
    /// </summary>
    /// <remarks>
    /// UIは <see cref="OnShow"/> の中で組み立てる。<see cref="ScreenRouter"/> は自身の <c>Awake</c> で
    /// 全画面を非アクティブにするため、各画面の <c>Awake</c>/<c>Start</c> が最初の <see cref="OnShow"/> より
    /// 前に走っている保証がない。
    /// The UI is built inside <see cref="OnShow"/>. <see cref="ScreenRouter"/> deactivates every screen during
    /// its own <c>Awake</c>, so a screen's <c>Awake</c> and <c>Start</c> are not guaranteed to have run before
    /// its first <see cref="OnShow"/>.
    /// </remarks>
    public sealed class TitleScreen : ScreenBase
    {
        private bool built;
        private Text collectionCountText;

        /// <summary>この画面の識別子。 / The identifier of this screen.</summary>
        public override ScreenId Id => ScreenId.Title;

        /// <summary>
        /// 画面表示時に呼ばれる。初回はUIを組み立て、以降は所持枚数だけを更新する。
        /// Called when the screen is shown: builds the UI on the first call and refreshes the card count after that.
        /// </summary>
        /// <param name="payload">この画面は payload を使わない。 / This screen takes no payload.</param>
        public override void OnShow(object payload)
        {
            Build();
            RefreshCollectionCount();
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
                    UiFactory.ScreenPadding * 2,
                    UiFactory.ScreenPadding * 2));
            layout.childAlignment = TextAnchor.MiddleCenter;
            var root = layout.transform;

            var title = UiFactory.CreateText(
                root,
                "CITAItokens",
                UiFactory.FontSizeTitle,
                TextAnchor.MiddleCenter,
                UiFactory.TextColor);
            UiFactory.SetFixedHeight(title.gameObject, 110f);

            var subtitle = UiFactory.CreateText(
                root,
                "外に出て、枝や葉、石を1枚撮ろう。\n撮った自然物がカードになって、バトルできる。",
                UiFactory.FontSizeBody,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(subtitle.gameObject, 130f);

            collectionCountText = UiFactory.CreateText(
                root,
                string.Empty,
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(collectionCountText.gameObject, 50f);

            UiFactory.CreateSpacer(root, UiFactory.DefaultSpacing);

            UiFactory.CreateButton(
                root,
                "枝を撮る",
                UiFactory.PrimaryButtonColor,
                () => Navigate(ScreenId.Capture, null));

            UiFactory.CreateButton(
                root,
                "コレクション",
                UiFactory.SecondaryButtonColor,
                () => Navigate(ScreenId.Collection, null));

            var hint = UiFactory.CreateText(
                root,
                "※ 撮影はその場で撮った1枚だけが使えます (アルバムからは選べません)。",
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(hint.gameObject, 70f);
        }

        /// <summary>
        /// 所持枚数を読み直して表示する。撮影から戻ってきたときに数字が古いままにならないようにする。
        /// Re-reads and shows the number of owned cards, so the count is never stale after a capture.
        /// </summary>
        private void RefreshCollectionCount()
        {
            if (collectionCountText == null)
            {
                return;
            }

            if (!GameContext.IsInitialized)
            {
                collectionCountText.text = "コレクションを読み込めませんでした。";
                Debug.LogError(
                    "[TitleScreen] GameContext が初期化されていません。 / GameContext has not been initialized.");
                return;
            }

            var cards = GameContext.Cards.GetAll();
            var count = cards != null ? cards.Count : 0;
            collectionCountText.text = "手持ちのカード: " + count + "枚";
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
                    "[TitleScreen] ScreenRouter が見つかりません。 / ScreenRouter.Instance is null.");
                return;
            }

            ScreenRouter.Instance.Show(id, payload);
        }
    }
}
