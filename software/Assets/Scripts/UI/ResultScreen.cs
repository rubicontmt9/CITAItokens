using CitaiTokens.Battle;
using CitaiTokens.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CitaiTokens.UI
{
    /// <summary>
    /// バトル結果画面。<see cref="BattleResultPayload"/> の内容を見せるだけで、戦闘を進める手段は持たない。
    /// The battle result screen. It only displays the contents of a <see cref="BattleResultPayload"/> and has
    /// no way to advance a battle.
    /// </summary>
    public sealed class ResultScreen : ScreenBase
    {
        /// <summary>結果パネルの最小の高さ (px)。 / Minimum height of the result panel, in pixels.</summary>
        private const float PanelMinHeight = 460f;

        private bool built;

        private Text headlineText;
        private Text commentText;
        private Text noticeText;
        private Text playerText;
        private Text cpuText;
        private Text roundsText;

        /// <summary>この画面の識別子。 / The identifier of this screen.</summary>
        public override ScreenId Id => ScreenId.Result;

        /// <summary>
        /// 画面表示時に呼ばれる。payload はバトル画面が作った <see cref="BattleResultPayload"/>。
        /// Called when the screen is shown; the payload is the <see cref="BattleResultPayload"/> built by the
        /// battle screen.
        /// </summary>
        /// <param name="payload">バトルの結果。 / The battle result.</param>
        public override void OnShow(object payload)
        {
            Build();

            var result = payload as BattleResultPayload;
            if (result == null)
            {
                Debug.LogError(
                    "[ResultScreen] payload が BattleResultPayload ではありません。タイトルに戻ります。 / "
                    + "The payload is not a BattleResultPayload; returning to the title screen.");
                Navigate(ScreenId.Title, null);
                return;
            }

            Populate(result);
        }

        /// <summary>
        /// UIを1度だけ組み立てる。<see cref="ScreenRouter"/> は自身の Awake で画面を無効化するため、
        /// 組み立ては <see cref="Awake"/> ではなく最初の <see cref="OnShow"/> で行う。
        /// Builds the UI exactly once. <see cref="ScreenRouter"/> deactivates screens from its own Awake, so the
        /// widgets are built on the first <see cref="OnShow"/> rather than in <see cref="Awake"/>.
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

            UiFactory.CreateSpacer(root, 40f);

            headlineText = UiFactory.CreateText(
                root,
                string.Empty,
                UiFactory.FontSizeHuge,
                TextAnchor.MiddleCenter,
                UiFactory.TextColor);
            UiFactory.SetFixedHeight(headlineText.gameObject, 170f);

            commentText = UiFactory.CreateText(
                root,
                string.Empty,
                UiFactory.FontSizeBody,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(commentText.gameObject, 60f);

            noticeText = UiFactory.CreateText(
                root,
                string.Empty,
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleCenter,
                UiFactory.WarningColor);
            UiFactory.SetFixedHeight(noticeText.gameObject, 44f);
            noticeText.gameObject.SetActive(false);

            var panelImage = UiFactory.CreateImage(root, "ResultPanel", UiFactory.PanelColor);
            var panelElement = panelImage.gameObject.AddComponent<LayoutElement>();
            panelElement.minHeight = PanelMinHeight;
            panelElement.flexibleHeight = 1f;

            var panelLayout = UiFactory.CreateVerticalLayout(
                panelImage.rectTransform,
                UiFactory.DefaultSpacing * 0.5f,
                new RectOffset(32, 32, 32, 32));
            panelLayout.childAlignment = TextAnchor.MiddleCenter;
            var panelRoot = panelLayout.transform;

            playerText = UiFactory.CreateText(
                panelRoot,
                string.Empty,
                UiFactory.FontSizeBody,
                TextAnchor.MiddleCenter,
                UiFactory.PlayerHpColor);
            UiFactory.SetFixedHeight(playerText.gameObject, 100f);

            var versusText = UiFactory.CreateText(
                panelRoot,
                "vs",
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(versusText.gameObject, 44f);

            cpuText = UiFactory.CreateText(
                panelRoot,
                string.Empty,
                UiFactory.FontSizeBody,
                TextAnchor.MiddleCenter,
                UiFactory.CpuHpColor);
            UiFactory.SetFixedHeight(cpuText.gameObject, 100f);

            roundsText = UiFactory.CreateText(
                panelRoot,
                string.Empty,
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(roundsText.gameObject, 44f);

            UiFactory.CreateButton(
                root,
                "コレクションへ",
                UiFactory.PrimaryButtonColor,
                () => Navigate(ScreenId.Collection, null));

            UiFactory.CreateButton(
                root,
                "タイトルへ",
                UiFactory.SecondaryButtonColor,
                () => Navigate(ScreenId.Title, null));
        }

        /// <summary>
        /// 結果の内容を表示に反映する。 / Applies the result to the widgets.
        /// </summary>
        private void Populate(BattleResultPayload result)
        {
            headlineText.text = CardTextFormatter.OutcomeHeadline(result.Outcome);
            headlineText.color = HeadlineColor(result.Outcome);
            commentText.text = CardTextFormatter.OutcomeComment(result.Outcome);

            // 未決着の結果はここへ来ないはずなので、来たら記録に残す。勝ったことにしてしまうより、
            // 「決着していない」とそのまま伝えるほうが、あとから原因を追える。
            // An undecided result should never reach this screen, so it is recorded when it does. Saying the
            // battle did not finish is both honest and traceable, unlike quietly claiming a win.
            var undecided = result.Outcome != BattleOutcome.PlayerWin && result.Outcome != BattleOutcome.PlayerLose;
            if (undecided)
            {
                Debug.LogWarning(
                    "[ResultScreen] 未決着の結果を受け取りました / Received an undecided outcome: " + result.Outcome);
            }

            noticeText.text = undecided ? "バトルの決着を確認できませんでした。" : string.Empty;
            noticeText.gameObject.SetActive(undecided);

            playerText.text = CardName(result.PlayerCardName) + "\n残りHP " + result.PlayerHpRemaining;
            cpuText.text = CardName(result.CpuCardName) + "\n残りHP " + result.CpuHpRemaining;
            roundsText.text = "ラウンド数 " + result.Rounds;
        }

        /// <summary>
        /// 見出しの色。勝ちと負けを色でも見分けられるようにする。
        /// Colour of the headline, so a win and a loss are told apart by colour as well as by wording.
        /// </summary>
        private static Color HeadlineColor(BattleOutcome outcome)
        {
            switch (outcome)
            {
                case BattleOutcome.PlayerWin:
                    return UiFactory.PlayerHpColor;
                case BattleOutcome.PlayerLose:
                    return UiFactory.CpuHpColor;
                default:
                    return UiFactory.WarningColor;
            }
        }

        /// <summary>
        /// 名前が空のカードにも表示名を与える。 / Gives a display name to a card whose name is empty.
        /// </summary>
        private static string CardName(string name)
        {
            return string.IsNullOrEmpty(name) ? "名もなきカード" : name;
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
                    "[ResultScreen] ScreenRouter が見つかりません。 / ScreenRouter.Instance is null.");
                return;
            }

            ScreenRouter.Instance.Show(id, payload);
        }
    }
}
