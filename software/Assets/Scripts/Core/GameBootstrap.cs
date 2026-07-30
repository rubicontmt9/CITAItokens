using System;
using CitaiTokens.AI;
using CitaiTokens.Capture;
using CitaiTokens.Data;
using CitaiTokens.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CitaiTokens.Core
{
    /// <summary>
    /// アプリ唯一の入口。シーンにはこのGameObjectだけを置き、サービスもUI階層も全て実行時に組み立てる。
    /// シーンファイル (.unity) とプレハブはYAMLの手書きが現実的でないため、シーンには何も作り込まない方針。
    /// The single entry point of the app. The scene holds only this GameObject; every service and the whole
    /// UI hierarchy are built at runtime, because hand-authoring .unity scene files and prefabs is not
    /// practical, so nothing is baked into the scene.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        /// <summary>UIの基準解像度 (縦持ちスマホ)。 / Reference resolution of the UI (portrait phone).</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);

        /// <summary>
        /// 端末の電池を考えたフレームレート上限。カードゲームに60fpsは不要で、屋外では充電できない。
        /// Frame-rate cap chosen for battery life: a card game does not need 60 fps and the player is
        /// outdoors, away from a charger.
        /// </summary>
        public const int TargetFrameRate = 30;

        /// <summary>
        /// 起動失敗表示のCanvasのソート順。他のCanvasより必ず前に出す。
        /// Sorting order of the fatal-error canvas, so it always draws in front of any other canvas.
        /// </summary>
        private const int FatalErrorCanvasSortingOrder = 32000;

        private ScreenRouter router;

        /// <summary>
        /// 起動処理の全体。例外は外に出さず、画面に見える形で報告する。
        /// Runs the whole startup sequence. No exception escapes; failures are reported on screen.
        /// </summary>
        private void Awake()
        {
            try
            {
                ApplyPlayerSettings();
                Bootstrap();
            }
            catch (Exception e)
            {
                // 何が起きても「真っ黒なアプリ」で終わらせない。ログとオンスクリーン表示の両方に残す。
                // Whatever happens, never leave the player with a silently black app: log it and show it.
                Debug.LogException(e);
                ShowFatalError(e.Message);
            }
        }

        /// <summary>
        /// 端末側の実行時設定。縦持ち固定、30fps上限、スリープはOS設定に従う。
        /// Runtime device settings: locked to portrait, capped at 30 fps, sleep left to the OS setting.
        /// </summary>
        private static void ApplyPlayerSettings()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            Application.targetFrameRate = TargetFrameRate;

            // 撮影中に画面が消えるのは困るが、プレイヤーの端末設定を上書きするほどではない。
            // A screen blanking mid-capture is annoying, but not enough to override the player's own setting.
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }

        /// <summary>
        /// サービスを構築して <see cref="GameContext"/> に登録し、UI階層を組み立ててタイトル画面を開く。
        /// Builds the services, registers them in <see cref="GameContext"/>, assembles the UI hierarchy and
        /// opens the title screen.
        /// </summary>
        private void Bootstrap()
        {
            var config = AppConfig.LoadOrDefault();

            var cards = new LocalCardRepository();
            var thumbnails = new ThumbnailStore();
            var captureHistory = new PlayerPrefsCaptureHistory();
            var cardGenerator = CreateCardGenerator(config);

            // WebCamPhotoCapture は MonoBehaviour なので new できない。コンポーネントとして載せる。
            // WebCamPhotoCapture is a MonoBehaviour, so it cannot be new'ed; it is added as a component.
            var photoCapture = gameObject.AddComponent<WebCamPhotoCapture>();

            GameContext.Initialize(
                config,
                cards,
                captureHistory,
                cardGenerator,
                photoCapture,
                thumbnails);

            CreateEventSystem();
            router = CreateUserInterface();

            // 最初の画面はブートストラップが決める。ScreenRouter.Awake は何も表示しない。
            // The bootstrap decides the first screen; ScreenRouter.Awake shows nothing by itself.
            router.Show(ScreenId.Title);

            Debug.Log(
                "[GameBootstrap] 起動が完了しました。 / Bootstrap finished; showing the title screen.");
        }

        /// <summary>
        /// カード生成の実装を選ぶ。設定でモックが有効な場合、およびプロキシURLが空の場合はモックを使う。
        /// URLが空のときにモックへ倒すのは、設定アセットを持たない新規クローンで撮影画面が通信エラーの
        /// 行き止まりになるのを防ぐため。選択理由はプレイテスト中にコンソールで確認できるよう必ずログに出す。
        /// Chooses the card-generation implementation: the mock when the config asks for it, and also when the
        /// proxy URL is blank. Falling back on a blank URL matters because a fresh clone with no config asset
        /// would otherwise dead-end at the capture screen with a network error. The choice is always logged so
        /// it is visible in the console during playtests.
        /// </summary>
        private static ICardGenerator CreateCardGenerator(AppConfig config)
        {
            if (config.UseMockCardGenerator)
            {
                Debug.Log(
                    "[GameBootstrap] MockCardGenerator を使います (AppConfig.useMockCardGenerator が true)。"
                    + "通信は行いません。 / Using MockCardGenerator because AppConfig.useMockCardGenerator is "
                    + "true; no network calls will be made.");
                return new MockCardGenerator();
            }

            if (string.IsNullOrWhiteSpace(config.CardProxyUrl))
            {
                Debug.Log(
                    "[GameBootstrap] プロキシURLが未設定のため MockCardGenerator にフォールバックします。"
                    + "実際のAI生成を使うには Assets/Resources/AppConfig.asset に cardProxyUrl を設定してください。 / "
                    + "Falling back to MockCardGenerator because the proxy URL is empty. Set cardProxyUrl in "
                    + "Assets/Resources/AppConfig.asset to use real AI generation.");
                return new MockCardGenerator();
            }

            Debug.Log(
                "[GameBootstrap] CardProxyClient を使います / Using CardProxyClient against: "
                + config.CardProxyUrl);
            return new CardProxyClient(config.CardProxyUrl);
        }

        /// <summary>
        /// EventSystem を作る。これが無いとゲーム内のボタンがタッチに一切反応せず、他に作る場所もない。
        /// Creates the EventSystem. Without it no button in the game responds to touch, and nothing else
        /// creates one.
        /// </summary>
        private static void CreateEventSystem()
        {
            var existing = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (existing != null)
            {
                Debug.Log(
                    "[GameBootstrap] EventSystem が既に存在するため再利用します。 / "
                    + "An EventSystem already exists; reusing it.");
                return;
            }

            var eventSystemObject = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));

            Debug.Log(
                "[GameBootstrap] " + eventSystemObject.name
                + " を作成しました (StandaloneInputModule)。 / Created the EventSystem with a StandaloneInputModule.");
        }

        /// <summary>
        /// Canvas と6画面を組み立て、画面を登録した <see cref="ScreenRouter"/> を返す。
        /// Builds the canvas and the six screens, returning the <see cref="ScreenRouter"/> they are registered on.
        /// </summary>
        private static ScreenRouter CreateUserInterface()
        {
            var canvasObject = new GameObject(
                "Canvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            // 縦持ち前提なので高さ基準でスケールさせる (1 = 高さに合わせる)。
            // The game is held in portrait, so scale to height (1 = match height).
            scaler.matchWidthOrHeight = 1f;

            // ルーターを先に載せ、各画面は Register で明示的に登録する。理由:
            //  (1) ScreenRouter.Awake の「子階層を自動登録する」動作に依存しない。実行時に AddComponent で
            //      組み立てる構成では、ルーターの Awake がいつ走ったかに登録結果を左右されたくない。
            //      明示的な Register なら Unity のコールバック順序がどうであっても6画面が必ず登録される。
            //  (2) 先にルーターを載せておくと ScreenRouter.Instance が各画面の Awake より前に確定するので、
            //      画面側が Awake の時点でルーターを参照しても null にならない。
            // 自動登録が走った場合と二重になるが、同一インスタンスの再登録は辞書の上書きだけで無害。
            // 全画面を非表示に揃えるのもここで自分で行う (Awake の自動登録に任せない)。
            // The router is added first and each screen is registered explicitly, because:
            //  (1) it does not depend on ScreenRouter.Awake's auto-registration of children. When the hierarchy is
            //      assembled at runtime via AddComponent, registration must not hinge on when the router's Awake
            //      happened to run; explicit Register calls guarantee all six screens land regardless of Unity's
            //      component-callback ordering.
            //  (2) adding the router first means ScreenRouter.Instance is already set before any screen's Awake
            //      runs, so a screen touching the router from Awake never sees null.
            // If the auto-registration does also run, re-registering the same instance merely overwrites the
            // dictionary entry, which is harmless. Deactivating every screen is likewise done here explicitly
            // rather than left to Awake.
            var createdRouter = canvasObject.AddComponent<ScreenRouter>();

            var screens = new ScreenBase[]
            {
                CreateScreen<TitleScreen>(canvasObject.transform),
                CreateScreen<CaptureScreen>(canvasObject.transform),
                CreateScreen<CardResultScreen>(canvasObject.transform),
                CreateScreen<CollectionScreen>(canvasObject.transform),
                CreateScreen<BattleScreen>(canvasObject.transform),
                CreateScreen<ResultScreen>(canvasObject.transform),
            };

            for (var i = 0; i < screens.Length; i++)
            {
                createdRouter.Register(screens[i]);

                // 表示する1枚は Show が決める。ここでは全て閉じた状態に揃える。
                // Show decides which single screen is visible; here they all start closed.
                screens[i].gameObject.SetActive(false);
            }

            return createdRouter;
        }

        /// <summary>
        /// 1画面ぶんの全画面GameObjectを作る。RectTransform は上下左右いっぱいに伸ばす。
        /// この「画面ごとに引き伸ばした RectTransform」はUI側が前提にしている取り決めなので厳密に守る。
        /// Creates one full-screen GameObject for a screen, with its RectTransform stretched to fill.
        /// This stretched-RectTransform-per-screen arrangement is a contract the UI code builds against, so it
        /// is applied exactly.
        /// </summary>
        /// <param name="parent">Canvas の Transform。 / The canvas transform.</param>
        private static T CreateScreen<T>(Transform parent) where T : ScreenBase
        {
            // GameObject名はクラス名に揃える。Hierarchyとコードを1対1で読めるようにするため。
            // The GameObject is named after its class, so the hierarchy and the code read one-to-one.
            var screenObject = new GameObject(typeof(T).Name, typeof(RectTransform));
            screenObject.transform.SetParent(parent, false);

            var rect = screenObject.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = screenObject.AddComponent<RectTransform>();
            }

            StretchToFill(rect);

            return screenObject.AddComponent<T>();
        }

        /// <summary>
        /// RectTransform を親いっぱいに引き伸ばす。 / Stretches a RectTransform to fill its parent.
        /// </summary>
        private static void StretchToFill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// 起動に失敗したときの最小限の表示。UI側のファクトリに依存せず、この場で組み立てる
        /// (UI構築そのものが失敗している可能性があるため)。
        /// A minimal on-screen report for a failed bootstrap, built inline rather than through the UI layer's
        /// factory, because building the UI is itself one of the things that may have failed.
        /// </summary>
        private static void ShowFatalError(string message)
        {
            try
            {
                var canvasObject = new GameObject(
                    "FatalErrorCanvas",
                    typeof(Canvas),
                    typeof(CanvasScaler));

                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = FatalErrorCanvasSortingOrder;

                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = ReferenceResolution;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;

                // 背景を敷いておかないと、カメラの無いシーンでは文字が読めない可能性がある。
                // Without a background the text may be unreadable in a scene that has no camera.
                var backgroundObject = new GameObject("Background", typeof(RectTransform));
                backgroundObject.transform.SetParent(canvasObject.transform, false);
                StretchToFill(backgroundObject.GetComponent<RectTransform>());
                var background = backgroundObject.AddComponent<Image>();
                background.color = new Color(0.08f, 0.05f, 0.05f, 1f);

                var textObject = new GameObject("FatalErrorText", typeof(RectTransform));
                textObject.transform.SetParent(canvasObject.transform, false);
                var textRect = textObject.GetComponent<RectTransform>();
                StretchToFill(textRect);
                textRect.offsetMin = new Vector2(48f, 48f);
                textRect.offsetMax = new Vector2(-48f, -48f);

                var text = textObject.AddComponent<Text>();
                text.font = LoadFallbackFont();
                text.fontSize = 32;
                text.color = Color.white;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.text =
                    "起動に失敗しました。\nStartup failed.\n\n" + message
                    + "\n\n詳細はコンソール/ログを確認してください。 / See the console or log for details.";
            }
            catch (Exception e)
            {
                // 表示自体に失敗したらログだけが頼りになる。ここで例外を投げ直しても誰も救われない。
                // If even this fails, the log is all that is left; rethrowing here would help nobody.
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 組み込みフォントを取得する。Unity 2022 では LegacyRuntime.ttf、それ以前は Arial.ttf。
        /// どちらも取れなければ null を返す (文字は出ないがクラッシュはしない)。
        /// Gets a built-in font: LegacyRuntime.ttf on Unity 2022, Arial.ttf on older versions. Returns null when
        /// neither is available, in which case the text is invisible but nothing crashes.
        /// </summary>
        private static Font LoadFallbackFont()
        {
            var font = TryGetBuiltinFont("LegacyRuntime.ttf");
            if (font == null)
            {
                font = TryGetBuiltinFont("Arial.ttf");
            }

            if (font == null)
            {
                Debug.LogWarning(
                    "[GameBootstrap] 組み込みフォントが見つかりませんでした。エラー文字が表示されない可能性があります。 / "
                    + "No built-in font was found; the error text may not be visible.");
            }

            return font;
        }

        /// <summary>
        /// 組み込みリソースを名前で読む。名前がそのUnityバージョンに無い場合に備えて例外を飲む。
        /// Loads a built-in resource by name, swallowing failures in case that name does not exist in this
        /// Unity version.
        /// </summary>
        private static Font TryGetBuiltinFont(string resourceName)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(resourceName);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[GameBootstrap] 組み込みフォントを読み込めませんでした / Could not load the built-in font '"
                    + resourceName + "': " + e.Message);
                return null;
            }
        }
    }
}
