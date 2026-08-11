using System.Collections.Generic;
using UnityEngine;

namespace CitaiTokens.Core
{
    /// <summary>
    /// 1シーン内の画面切り替えを担当する。子階層の <see cref="ScreenBase"/> を自動登録し、
    /// 表示中の1枚だけを有効にする。遷移履歴を持つので <see cref="Back"/> で戻れる。
    /// Switches screens inside a single scene. It auto-registers every <see cref="ScreenBase"/> in its
    /// children and keeps exactly one of them active. It records history, so <see cref="Back"/> works.
    /// </summary>
    public sealed class ScreenRouter : MonoBehaviour
    {
        /// <summary>
        /// 唯一のインスタンス。 / The single instance.
        /// </summary>
        public static ScreenRouter Instance { get; private set; }

        private readonly Dictionary<ScreenId, ScreenBase> screens = new Dictionary<ScreenId, ScreenBase>();
        private readonly Stack<ScreenId> history = new Stack<ScreenId>();
        private ScreenBase currentScreen;

        /// <summary>
        /// 表示中の画面。まだ何も表示していない場合は null。
        /// The screen currently shown, or null when nothing has been shown yet.
        /// </summary>
        public ScreenId? Current => currentScreen != null ? currentScreen.Id : (ScreenId?)null;

        /// <summary>
        /// 戻れる履歴が残っているか。 / Whether there is any history left to go back to.
        /// </summary>
        public bool CanGoBack => history.Count > 0;

        /// <summary>
        /// 画面を登録する。子階層に置いてある画面は自動登録されるので、通常は呼ぶ必要がない。
        /// Registers a screen. Screens placed under this router are registered automatically, so calling
        /// this by hand is normally unnecessary.
        /// </summary>
        public void Register(ScreenBase screen)
        {
            if (screen == null)
            {
                Debug.LogError("[ScreenRouter] null の画面は登録できません。 / Cannot register a null screen.");
                return;
            }

            if (screens.ContainsKey(screen.Id) && screens[screen.Id] != screen)
            {
                Debug.LogError(
                    "[ScreenRouter] 同じ ScreenId の画面が既に登録されています / A screen with the same ScreenId is already registered: "
                    + screen.Id);
                return;
            }

            screens[screen.Id] = screen;
        }

        /// <summary>
        /// 指定の画面へ遷移する。現在の画面は履歴に積まれる。未登録のIDを指定した場合は
        /// エラーログを出して何もしない。
        /// Shows the given screen, pushing the current one onto the history stack. When the target is not
        /// registered, this logs an error and does nothing.
        /// </summary>
        /// <param name="id">表示したい画面。 / The screen to show.</param>
        /// <param name="payload">表示先の <see cref="ScreenBase.OnShow"/> に渡すデータ。 / Data handed to the target's <see cref="ScreenBase.OnShow"/>.</param>
        public void Show(ScreenId id, object payload = null)
        {
            ScreenBase target;
            if (!screens.TryGetValue(id, out target) || target == null)
            {
                Debug.LogError(
                    "[ScreenRouter] 未登録の画面へ遷移しようとしました / Tried to show a screen that is not registered: "
                    + id);
                return;
            }

            if (currentScreen == target)
            {
                target.OnShow(payload);
                return;
            }

            if (currentScreen != null)
            {
                history.Push(currentScreen.Id);
            }

            Activate(target, payload);
        }

        /// <summary>
        /// 履歴を1つ戻る。履歴が空の場合は false を返して何もしない。
        /// Goes back one step in the history. Returns false and does nothing when the history is empty.
        /// </summary>
        public bool Back()
        {
            if (history.Count == 0)
            {
                return false;
            }

            var previousId = history.Pop();

            ScreenBase target;
            if (!screens.TryGetValue(previousId, out target) || target == null)
            {
                Debug.LogError(
                    "[ScreenRouter] 履歴の画面が登録されていません / The screen in the history is not registered: "
                    + previousId);
                return false;
            }

            Activate(target, null);
            return true;
        }

        /// <summary>
        /// 遷移履歴を消す。ホーム画面へ戻ったときなどに使う。
        /// Clears the history, e.g. after returning to the title screen.
        /// </summary>
        public void ClearHistory()
        {
            history.Clear();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "[ScreenRouter] ScreenRouter が複数あります。重複分を破棄します。 / "
                    + "More than one ScreenRouter exists; destroying the duplicate.");
                Destroy(this);
                return;
            }

            Instance = this;

            // 子階層の画面を自動登録し、すべて非表示にする。最初の画面はブートストラップが Show で決める。
            // Auto-register child screens and hide them all; the bootstrap picks the first screen via Show.
            var found = GetComponentsInChildren<ScreenBase>(true);
            for (var i = 0; i < found.Length; i++)
            {
                Register(found[i]);
                found[i].gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Activate(ScreenBase target, object payload)
        {
            if (currentScreen != null)
            {
                currentScreen.OnHide();
                currentScreen.gameObject.SetActive(false);
            }

            currentScreen = target;
            target.gameObject.SetActive(true);
            target.OnShow(payload);
        }
    }
}
