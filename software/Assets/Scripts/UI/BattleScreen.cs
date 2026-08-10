using System.Collections;
using System.Collections.Generic;
using CitaiTokens.Battle;
using CitaiTokens.Cards;
using CitaiTokens.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CitaiTokens.UI
{
    /// <summary>
    /// バトル画面。プレイヤーの選んだカードで <see cref="BattleManager"/> を1ラウンドずつ進め、
    /// 結果をHPバー・数値・ターンログの3つで同時に見せる。
    /// The battle screen. It steps a <see cref="BattleManager"/> one round at a time with the card the player
    /// chose, and shows each result through the HP bars, the HP numbers and the turn log at once.
    /// </summary>
    /// <remarks>
    /// 戦闘の進行そのものは <see cref="BattleManager"/> が持ち、この画面は表示だけを担当する。
    /// 演出中はモデルが先に進んでいて表示が追いかけている状態になるため、演出中は攻撃ボタンを無効にする。
    /// 二度押しでラウンドが二重に解決されると、HPバーの値とモデルの値が食い違ったまま戻らなくなる。
    /// The battle itself lives in <see cref="BattleManager"/>; this screen only displays it. During the
    /// animation the model is already ahead of the display, so the attack button is disabled for the duration:
    /// a double tap would resolve two rounds and leave the bars permanently out of step with the model.
    /// </remarks>
    public sealed class BattleScreen : ScreenBase
    {
        /// <summary>攻撃1回ぶんの間 (秒)。 / The pause between one attack and the next, in seconds.</summary>
        public const float AttackBeatSeconds = 0.5f;

        /// <summary>決着してから結果画面へ移るまでの間 (秒)。 / The pause between the final blow and the result screen, in seconds.</summary>
        public const float FinishDelaySeconds = 0.9f;

        /// <summary>
        /// ターンログに残す行数の上限。50ラウンド戦うと200行を超えるため、古い行から捨てる。
        /// Maximum number of turn-log rows kept. A fifty-round battle produces over two hundred lines, so the
        /// oldest rows are dropped.
        /// </summary>
        public const int MaxLogLines = 100;

        /// <summary>カード絵の一辺の長さ (px)。 / Edge length of a card portrait, in pixels.</summary>
        private const float PortraitSize = 180f;

        /// <summary>片側ぶんのパネルの高さ (px)。 / Height of one combatant's panel, in pixels.</summary>
        private const float SidePanelHeight = 220f;

        /// <summary>
        /// 1ラウンドの先頭に出るヘッダ行の数 (「--- ラウンド n ---」)。
        /// Number of header lines written at the start of a round (the "--- round n ---" line).
        /// </summary>
        private const int RoundHeaderLogLines = 1;

        private bool built;

        private SideView cpuView;
        private SideView playerView;
        private Text matchupText;
        private Text hintText;
        private ScrollRect logScroll;
        private RectTransform logContent;
        private Button attackButton;

        private readonly List<GameObject> logLines = new List<GameObject>();

        private BattleManager battle;
        private Texture2D playerTexture;
        private Coroutine playRoutine;

        /// <summary>ターンログに反映済みの <see cref="BattleManager.Log"/> の行数。 / How many <see cref="BattleManager.Log"/> lines are already on screen.</summary>
        private int loggedLineCount;

        /// <summary>この画面の識別子。 / The identifier of this screen.</summary>
        public override ScreenId Id => ScreenId.Battle;

        /// <summary>
        /// 画面表示時に呼ばれる。payload はプレイヤーが選んだ <see cref="Card"/>。
        /// Called when the screen is shown; the payload is the <see cref="Card"/> the player chose.
        /// </summary>
        /// <param name="payload">プレイヤーのカード。 / The player's card.</param>
        public override void OnShow(object payload)
        {
            Build();

            var card = payload as Card;
            if (card == null)
            {
                Debug.LogError(
                    "[BattleScreen] payload が Card ではありません。タイトルに戻ります。 / "
                    + "The payload is not a Card; returning to the title screen.");
                Navigate(ScreenId.Title, null);
                return;
            }

            StartBattle(card);
        }

        /// <summary>
        /// 画面が隠されるときに呼ばれる。演出コルーチンを止め、死んだUIを触り続けないようにする。
        /// Called when the screen is hidden; stops the animation coroutine so it cannot keep mutating dead UI.
        /// </summary>
        public override void OnHide()
        {
            StopPlayRoutine();

            if (attackButton != null)
            {
                attackButton.interactable = true;
            }
        }

        /// <summary>
        /// 破棄時に読み込んだテクスチャを解放する。 / Releases the loaded texture on destruction.
        /// </summary>
        private void OnDestroy()
        {
            ReleaseTexture();
        }

        /// <summary>
        /// UIを1度だけ組み立てる。<see cref="ScreenRouter"/> は自身の Awake で画面を無効化するため、
        /// 組み立ては Awake ではなく最初の <see cref="OnShow"/> で行う。
        /// Builds the UI exactly once. <see cref="ScreenRouter"/> deactivates screens from its own Awake, so the
        /// widgets are built on the first <see cref="OnShow"/> rather than in Awake.
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
                UiFactory.DefaultSpacing * 0.75f,
                new RectOffset(
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding));
            var root = layout.transform;

            cpuView = BuildSide(root, "CpuPanel", UiFactory.CpuHpColor, false);

            // 相性の行は飾りではない。倍率が 1.5 と 0.67 で、攻撃4〜6回で決着する以上、
            // 相性がほぼ勝敗を決める。ここが見えないと、プレイヤーは数字が動くのを眺めるだけになる。
            // The matchup line is not decoration. With multipliers of 1.5 and 0.67 in a battle that ends in
            // four to six attacks, the matchup largely decides the result; without this line the player is
            // only watching numbers happen.
            matchupText = UiFactory.CreateText(
                root,
                string.Empty,
                UiFactory.FontSizeBody,
                TextAnchor.MiddleCenter,
                UiFactory.WarningColor);
            UiFactory.SetFixedHeight(matchupText.gameObject, 48f);

            hintText = UiFactory.CreateText(
                root,
                CardTextFormatter.ElementCycleHint(),
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleCenter,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(hintText.gameObject, 38f);

            playerView = BuildSide(root, "PlayerPanel", UiFactory.PlayerHpColor, true);

            BuildLogArea(root);

            attackButton = UiFactory.CreateButton(
                root,
                "攻撃",
                UiFactory.PrimaryButtonColor,
                OnAttackPressed);

            UiFactory.CreateButton(
                root,
                "にげる",
                UiFactory.SecondaryButtonColor,
                () => Navigate(ScreenId.Collection, null));
        }

        /// <summary>
        /// 片側 (プレイヤーまたはCPU) の表示をまとめて作る。
        /// Builds the whole display for one side, either the player's or the CPU's.
        /// </summary>
        /// <param name="parent">親となる Transform。 / The parent transform.</param>
        /// <param name="name">パネルの GameObject 名。 / Name of the panel GameObject.</param>
        /// <param name="hpColor">HPバーの色。 / Colour of the HP bar.</param>
        /// <param name="withThumbnail">写真を表示できる側か。CPUは写真を持たない。 / Whether this side can show a photo; the CPU has none.</param>
        private static SideView BuildSide(Transform parent, string name, Color hpColor, bool withThumbnail)
        {
            var view = new SideView();

            var panel = UiFactory.CreateImage(parent, name, UiFactory.PanelColor);
            UiFactory.SetFixedHeight(panel.gameObject, SidePanelHeight);

            var row = UiFactory.CreateHorizontalLayout(
                panel.rectTransform,
                UiFactory.DefaultSpacing * 0.75f,
                new RectOffset(20, 20, 16, 16));

            var portraitBox = UiFactory.CreateRect(row.transform, "PortraitBox");
            var portraitElement = UiFactory.SetFixedWidth(portraitBox.gameObject, PortraitSize);
            portraitElement.minHeight = PortraitSize;
            portraitElement.preferredHeight = PortraitSize;
            portraitElement.flexibleHeight = 0f;

            // 写真が無い側 (CPU) や、写真を読めなかった側でも穴を空けない。色つきの下地に属性の一文字を置く。
            // Neither the side without a photo (the CPU) nor a side whose photo failed to load leaves a hole:
            // a coloured plate carries the element's single character instead.
            view.Placeholder = UiFactory.CreateImage(portraitBox, "Placeholder", UiFactory.HpTrackColor);
            UiFactory.Stretch(view.Placeholder.rectTransform);

            view.PlaceholderLabel = UiFactory.CreateText(
                view.Placeholder.rectTransform,
                string.Empty,
                UiFactory.FontSizeTitle,
                TextAnchor.MiddleCenter,
                hpColor);
            UiFactory.Stretch(view.PlaceholderLabel.rectTransform);

            if (withThumbnail)
            {
                view.Thumbnail = UiFactory.CreateRawImage(portraitBox);
                UiFactory.Stretch(view.Thumbnail.rectTransform);
                view.Thumbnail.enabled = false;
            }

            var column = UiFactory.CreateVerticalLayout(row.transform, 8f, new RectOffset(0, 0, 0, 0));
            column.childAlignment = TextAnchor.MiddleLeft;
            var columnElement = UiFactory.SetFlexibleWidth(column.gameObject, 1f);
            columnElement.minWidth = 300f;

            view.NameText = UiFactory.CreateText(
                column.transform,
                string.Empty,
                UiFactory.FontSizeBody,
                TextAnchor.MiddleLeft,
                UiFactory.TextColor);
            UiFactory.SetFixedHeight(view.NameText.gameObject, 48f);

            view.MetaText = UiFactory.CreateText(
                column.transform,
                string.Empty,
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleLeft,
                UiFactory.SubTextColor);
            UiFactory.SetFixedHeight(view.MetaText.gameObject, 38f);

            Image fill;
            UiFactory.CreateHpBar(column.transform, hpColor, out fill);
            view.HpFill = fill;

            view.HpText = UiFactory.CreateText(
                column.transform,
                string.Empty,
                UiFactory.FontSizeSmall,
                TextAnchor.MiddleLeft,
                UiFactory.TextColor);
            UiFactory.SetFixedHeight(view.HpText.gameObject, 38f);

            return view;
        }

        /// <summary>
        /// ターンログのスクロール領域を作る。余った高さはすべてここが受け取る。
        /// Builds the scrolling turn log, which absorbs whatever height the rest of the screen leaves over.
        /// </summary>
        private void BuildLogArea(Transform parent)
        {
            var logArea = UiFactory.CreateRect(parent, "LogArea");
            var areaElement = logArea.gameObject.AddComponent<LayoutElement>();
            areaElement.minHeight = 260f;
            areaElement.flexibleHeight = 1f;

            logScroll = UiFactory.CreateScrollView(logArea, out logContent);

            // ログの行はどれも raycastTarget を持たないため、このままでは指のドラッグがどこにも当たらない。
            // ScrollRect と同じ GameObject に下地の Image を置き、下地への当たりでスクロールできるようにする。
            // None of the log rows is a raycast target, so a finger drag would hit nothing at all. Putting a
            // backing Image on the same GameObject as the ScrollRect makes the whole area draggable.
            var backing = logScroll.gameObject.AddComponent<Image>();
            backing.color = UiFactory.PanelColor;
        }

        /// <summary>
        /// 渡されたカードで新しい戦闘を始める。表示のたびに相手を選び直す。
        /// Starts a fresh battle with the given card, picking a new opponent every time the screen is shown.
        /// </summary>
        private void StartBattle(Card playerCard)
        {
            StopPlayRoutine();
            ClearLogLines();
            loggedLineCount = 0;

            var cpuCard = CpuDecks.PickForPlayerCard(playerCard);
            if (cpuCard == null)
            {
                Debug.LogError(
                    "[BattleScreen] 対戦相手を用意できませんでした。コレクションに戻ります。 / "
                    + "No opponent could be prepared; returning to the collection screen.");
                Navigate(ScreenId.Collection, null);
                return;
            }

            battle = new BattleManager(playerCard, cpuCard);

            PopulateSide(cpuView, cpuCard, battle.Cpu, false);
            PopulateSide(playerView, playerCard, battle.Player, true);

            // 相手が変わるたびに相性も変わるので、対戦カードを組み直したこの場で必ず引き直す。
            // The matchup changes with every new opponent, so it is re-read here, where the pairing is decided.
            matchupText.text = CardTextFormatter.MatchupDescription(playerCard.Element, cpuCard.Element);
            hintText.text = CardTextFormatter.ElementCycleHint();

            FlushLogTo(battle.Log.Count);
            ScrollToNewest();

            if (attackButton != null)
            {
                attackButton.interactable = true;
            }
        }

        /// <summary>
        /// 片側の表示にカードの内容を反映する。 / Applies one card's contents to one side of the display.
        /// </summary>
        /// <param name="view">反映先。 / The side being filled in.</param>
        /// <param name="card">元になるカード。 / The backing card.</param>
        /// <param name="combatant">戦闘状態。HPの初期値に使う。 / The battle state, used for the initial HP.</param>
        /// <param name="loadPhoto">写真を読み込む側か。 / Whether this side loads a photo.</param>
        private void PopulateSide(SideView view, Card card, BattleCombatant combatant, bool loadPhoto)
        {
            if (view == null || card == null || combatant == null)
            {
                return;
            }

            view.NameText.text = string.IsNullOrEmpty(card.DisplayName) ? "名もなきカード" : card.DisplayName;
            view.MetaText.text = GenreName(card.WeaponGenre)
                + " ・ " + CardTextFormatter.ElementAndRarity(card.Element, card.Rarity);
            view.PlaceholderLabel.text = CardTextFormatter.ElementName(card.Element);

            SetHp(view, combatant.CurrentHp, combatant.MaxHp);

            if (!loadPhoto || view.Thumbnail == null)
            {
                view.Placeholder.gameObject.SetActive(true);
                return;
            }

            ReleaseTexture();

            if (GameContext.IsInitialized)
            {
                playerTexture = GameContext.Thumbnails.LoadThumbnail(card.ImagePath);
            }

            if (playerTexture != null)
            {
                view.Thumbnail.texture = playerTexture;
                view.Thumbnail.enabled = true;
                view.Placeholder.gameObject.SetActive(false);
            }
            else
            {
                view.Thumbnail.texture = null;
                view.Thumbnail.enabled = false;
                view.Placeholder.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// HPバーと数値を同時に更新する。片方だけ動くと、どちらが本当かが分からなくなる。
        /// Updates the HP bar and the HP numbers together; moving only one of them would leave the player
        /// unable to tell which is true.
        /// </summary>
        private static void SetHp(SideView view, int currentHp, int maxHp)
        {
            if (view == null)
            {
                return;
            }

            var safeMax = Mathf.Max(1, maxHp);
            var safeCurrent = Mathf.Clamp(currentHp, 0, safeMax);

            UiFactory.SetHpBarRatio(view.HpFill, safeCurrent / (float)safeMax);

            if (view.HpText != null)
            {
                view.HpText.text = "HP " + safeCurrent + " / " + safeMax;
            }
        }

        /// <summary>
        /// 「攻撃」が押されたときの処理。演出中は無効化されているので二重には入らない。
        /// Handles the attack button. It is disabled while an animation runs, so this cannot re-enter.
        /// </summary>
        private void OnAttackPressed()
        {
            if (battle == null)
            {
                Debug.LogError(
                    "[BattleScreen] 戦闘が初期化されていません。コレクションに戻ります。 / "
                    + "The battle was not initialized; returning to the collection screen.");
                Navigate(ScreenId.Collection, null);
                return;
            }

            if (playRoutine != null)
            {
                return;
            }

            playRoutine = StartCoroutine(PlayRoundRoutine());
        }

        /// <summary>
        /// 1ラウンドを解決し、攻撃1回ずつ間を置いて見せる。モデルは先に進んでいるので、
        /// この処理は <see cref="BattleRoundResult"/> の内容を順に表示へ写しているだけである。
        /// Resolves one round and replays it attack by attack with a pause between them. The model has already
        /// advanced, so this only copies the contents of the <see cref="BattleRoundResult"/> onto the display.
        /// </summary>
        private IEnumerator PlayRoundRoutine()
        {
            if (attackButton != null)
            {
                attackButton.interactable = false;
            }

            var result = battle.AdvanceRound();

            var target = loggedLineCount + RoundHeaderLogLines;
            FlushLogTo(target);
            ScrollToNewest();

            for (var i = 0; i < result.Attacks.Count; i++)
            {
                yield return new WaitForSeconds(AttackBeatSeconds);

                var attack = result.Attacks[i];
                var defenderView = attack.AttackerIsPlayer ? cpuView : playerView;
                var defenderMaxHp = attack.AttackerIsPlayer ? battle.Cpu.MaxHp : battle.Player.MaxHp;
                SetHp(defenderView, attack.DefenderHpAfter, defenderMaxHp);

                target += ExpectedLogLineCount(attack);
                FlushLogTo(target);
                ScrollToNewest();
            }

            // 想定した行数と実際のログがずれていても取りこぼさないよう、残りをここで出し切る。
            // Flush whatever is left, so a mismatch between the expected line count and the real log never
            // swallows a line.
            FlushLogTo(battle.Log.Count);
            ScrollToNewest();

            if (battle.IsFinished)
            {
                yield return new WaitForSeconds(FinishDelaySeconds);

                // 遷移で OnHide が走る前に手放しておく。自分自身を止めさせないため。
                // Released before the navigation triggers OnHide, so this coroutine is not asked to stop itself.
                playRoutine = null;

                Navigate(
                    ScreenId.Result,
                    new BattleResultPayload(
                        battle.Outcome,
                        battle.Player.DisplayName,
                        battle.Cpu.DisplayName,
                        battle.Player.CurrentHp,
                        battle.Cpu.CurrentHp,
                        battle.RoundNumber));
                yield break;
            }

            playRoutine = null;

            if (attackButton != null)
            {
                attackButton.interactable = true;
            }
        }

        /// <summary>
        /// 攻撃1回でログに増える行数を見積もる。<see cref="BattleManager"/> が書く行と対応させてある。
        /// Estimates how many log lines one attack adds, mirroring the lines <see cref="BattleManager"/> writes.
        /// </summary>
        /// <remarks>
        /// 内訳は「攻撃とダメージ」「残りHP」の2行に、相性の一言と撃破の一言を足したもの。
        /// 見積もりが外れても行が消えることはない (呼び出し側が最後に残りを出し切る)。演出の間合いがずれるだけである。
        /// The breakdown is the damage line plus the remaining-HP line, plus an effectiveness line and a defeat
        /// line when they apply. A wrong estimate never loses a line, because the caller flushes the remainder
        /// at the end; only the pacing shifts.
        /// </remarks>
        private static int ExpectedLogLineCount(BattleAttackResult attack)
        {
            var count = 2;

            if (attack.WasAdvantage || attack.WasDisadvantage)
            {
                count++;
            }

            if (attack.DefeatedDefender)
            {
                count++;
            }

            return count;
        }

        /// <summary>
        /// 未表示のログを指定の行数まで追記する。毎回ログ全体を作り直さないのは、行数が数百に達するためである。
        /// Appends the not-yet-shown log lines up to the given count. The whole log is never re-rendered,
        /// because it can reach several hundred lines.
        /// </summary>
        /// <param name="lineCount">ここまで表示したい行数。 / The number of lines that should be on screen.</param>
        private void FlushLogTo(int lineCount)
        {
            if (battle == null || logContent == null)
            {
                return;
            }

            var limit = Mathf.Min(lineCount, battle.Log.Count);
            for (; loggedLineCount < limit; loggedLineCount++)
            {
                AppendLogLine(battle.Log[loggedLineCount]);
            }
        }

        /// <summary>
        /// ログの行を1つ作る。上限を超えた分は古い行から捨てる。
        /// Adds one log row, dropping the oldest rows once the cap is exceeded.
        /// </summary>
        private void AppendLogLine(string line)
        {
            var text = UiFactory.CreateText(
                logContent,
                line,
                UiFactory.FontSizeSmall,
                TextAnchor.UpperLeft,
                UiFactory.TextColor);

            // 高さは固定しない。日本語の行は幅次第で2行に折り返すので、縦積みレイアウトに任せる。
            // The height is not pinned: a Japanese line wraps to two rows depending on the width, so the
            // vertical layout is left to size it.
            var element = text.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 36f;

            logLines.Add(text.gameObject);

            while (logLines.Count > MaxLogLines)
            {
                var oldest = logLines[0];
                logLines.RemoveAt(0);
                if (oldest != null)
                {
                    Destroy(oldest);
                }
            }
        }

        /// <summary>
        /// 一番新しい行が見えるところまでスクロールする。レイアウトを先に確定させてから位置を動かす。
        /// Scrolls so the newest line is visible, forcing the layout to settle before moving the position.
        /// </summary>
        private void ScrollToNewest()
        {
            if (logScroll == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            logScroll.verticalNormalizedPosition = 0f;
        }

        /// <summary>
        /// 生成済みのログ行を破棄する。 / Destroys the log rows that were built.
        /// </summary>
        private void ClearLogLines()
        {
            for (var i = 0; i < logLines.Count; i++)
            {
                if (logLines[i] != null)
                {
                    Destroy(logLines[i]);
                }
            }

            logLines.Clear();
        }

        /// <summary>
        /// 演出コルーチンを止める。 / Stops the animation coroutine.
        /// </summary>
        private void StopPlayRoutine()
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }
        }

        /// <summary>
        /// 読み込んだテクスチャを破棄する。画面を開き直してもメモリが積み上がらないようにする。
        /// Destroys the loaded texture, so reopening this screen does not pile up memory.
        /// </summary>
        private void ReleaseTexture()
        {
            if (playerView != null && playerView.Thumbnail != null)
            {
                playerView.Thumbnail.texture = null;
            }

            if (playerTexture != null)
            {
                Destroy(playerTexture);
                playerTexture = null;
            }
        }

        /// <summary>
        /// 武器ジャンルの日本語名。属性・レア度と並べて1行に出すために使う。
        /// The Japanese name of a weapon genre, shown on one line next to the element and the rarity.
        /// </summary>
        private static string GenreName(WeaponGenre genre)
        {
            switch (genre)
            {
                case WeaponGenre.Club:
                    return "棍棒";
                case WeaponGenre.Spear:
                    return "槍";
                case WeaponGenre.Staff:
                    return "杖";
                case WeaponGenre.Bow:
                    return "弓";
                case WeaponGenre.Shield:
                    return "盾";
                case WeaponGenre.Dagger:
                    return "短剣";
                default:
                    return "?";
            }
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
                    "[BattleScreen] ScreenRouter が見つかりません。 / ScreenRouter.Instance is null.");
                return;
            }

            ScreenRouter.Instance.Show(id, payload);
        }

        /// <summary>
        /// 片側ぶんのウィジェットへの参照をまとめた入れ物。更新のたびに探し直さないために持つ。
        /// Holds the widget references for one side, so nothing has to be looked up again on each update.
        /// </summary>
        private sealed class SideView
        {
            /// <summary>カード名。 / The card's name.</summary>
            public Text NameText;

            /// <summary>ジャンル・属性・レア度の行。 / The genre, element and rarity line.</summary>
            public Text MetaText;

            /// <summary>HPバーの中身。 / The fill of the HP bar.</summary>
            public Image HpFill;

            /// <summary>現在HPと最大HPの数値。 / The current and maximum HP numbers.</summary>
            public Text HpText;

            /// <summary>写真。CPU側は持たない。 / The photo; the CPU side has none.</summary>
            public RawImage Thumbnail;

            /// <summary>写真が無いときの下地。 / The plate shown when there is no photo.</summary>
            public Image Placeholder;

            /// <summary>下地に置く属性の一文字。 / The single element character drawn on the plate.</summary>
            public Text PlaceholderLabel;
        }
    }
}
