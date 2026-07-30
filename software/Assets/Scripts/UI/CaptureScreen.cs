using System;
using System.Collections;
using CitaiTokens.AI;
using CitaiTokens.Capture;
using CitaiTokens.Cards;
using CitaiTokens.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CitaiTokens.UI
{
    /// <summary>
    /// 撮影画面。カメラ権限の取得、プレビュー、撮影、検証、カード生成までを1画面で通す。
    /// The capture screen. It takes camera permission, previews, captures, validates and generates a card, all
    /// within one screen.
    /// </summary>
    /// <remarks>
    /// 設計上の決め事: 生成に成功した時点でカードはコレクションに保存する。次の画面に「保存する」ボタンは置かない。
    /// プレイヤーはこの1枚のために実際に外を歩いているので、誤タップで失われる余地を作らない。
    /// Design decision: the card is saved to the collection the moment generation succeeds; the next screen has
    /// no "save it" button. The player physically walked outside for this card, so a mis-tap must never be able
    /// to discard it.
    /// </remarks>
    public sealed class CaptureScreen : ScreenBase
    {
        /// <summary>位置情報の取得を待つ上限秒数。屋外で数十秒固まらせないため短くしてある。 / Seconds to wait for a location fix, kept short so the player is not frozen outdoors.</summary>
        public const float LocationTimeoutSeconds = 5f;

        /// <summary>プレビュー領域の下端 (画面高に対する割合)。 / Bottom edge of the preview area, as a fraction of screen height.</summary>
        private const float PreviewBottomAnchor = 0.34f;

        /// <summary>プレビュー領域の上端 (画面高に対する割合)。 / Top edge of the preview area, as a fraction of screen height.</summary>
        private const float PreviewTopAnchor = 0.9f;

        private bool built;

        private WebCamPhotoCapture capture;
        private LocationProbe locationProbe;

        private RectTransform previewFrame;
        private RawImage previewRaw;
        private Text statusText;
        private Text messageText;
        private RectTransform messagePanel;
        private Button shootButton;
        private Button retryGenerateButton;
        private Button retakeButton;
        private Button backButton;

        private Coroutine activeRoutine;
        private bool busy;

        // 生成に使う「今撮った1枚」。再試行では同じバイト列を使い回す (屋外で電波が弱いだけの場合に撮り直させない)。
        // The single photo just taken, reused verbatim on retry so weak signal outdoors never forces a re-shoot.
        private byte[] pendingJpeg;
        private string pendingPhotoPath;
        private DateTime pendingCaptureUtc;
        private bool pendingHasLocation;
        private double pendingLatitude;
        private double pendingLongitude;

        private int lastTextureWidth;
        private int lastTextureHeight;
        private int lastRotationAngle = int.MinValue;
        private bool lastMirrored;
        private Vector2 lastFrameSize;

        /// <summary>この画面の識別子。 / The identifier of this screen.</summary>
        public override ScreenId Id => ScreenId.Capture;

        /// <summary>
        /// 画面表示時に呼ばれる。UIを組み立て、カメラ起動シーケンスを開始する。
        /// Called when the screen is shown: builds the UI and starts the camera startup sequence.
        /// </summary>
        /// <param name="payload">この画面は payload を使わない。 / This screen takes no payload.</param>
        public override void OnShow(object payload)
        {
            Build();
            ResetPendingCapture();
            StartRoutine(StartCameraRoutine());
        }

        /// <summary>
        /// 画面が隠されるときに呼ばれる。カメラデバイスを確実に手放す。
        /// Called when the screen is hidden; makes sure the camera device is released.
        /// </summary>
        public override void OnHide()
        {
            StopAllCoroutines();
            activeRoutine = null;
            busy = false;

            if (capture != null)
            {
                capture.StopPreview();
            }

            if (previewRaw != null)
            {
                previewRaw.texture = null;
                previewRaw.enabled = false;
            }

            InvalidatePreviewTransformCache();
        }

        /// <summary>
        /// プレビューの向きと大きさを毎フレーム確認する。変化がなければ何もしない。
        /// Checks the preview's orientation and size each frame, doing nothing when nothing changed.
        /// </summary>
        private void Update()
        {
            if (capture != null && capture.IsPreviewing)
            {
                UpdatePreviewTransform();
            }
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

            // プレビュー領域。RawImage は実際の映像アスペクトに合わせて中央に置き直す。
            // The preview area; the RawImage is re-centred to match the real video aspect ratio.
            previewFrame = UiFactory.CreateRect(transform, "PreviewFrame");
            previewFrame.anchorMin = new Vector2(0f, PreviewBottomAnchor);
            previewFrame.anchorMax = new Vector2(1f, PreviewTopAnchor);
            previewFrame.offsetMin = Vector2.zero;
            previewFrame.offsetMax = Vector2.zero;

            previewRaw = UiFactory.CreateRawImage(previewFrame);
            var previewRect = previewRaw.rectTransform;
            previewRect.anchorMin = new Vector2(0.5f, 0.5f);
            previewRect.anchorMax = new Vector2(0.5f, 0.5f);
            previewRect.pivot = new Vector2(0.5f, 0.5f);
            previewRect.anchoredPosition = Vector2.zero;
            previewRect.sizeDelta = new Vector2(100f, 100f);
            previewRaw.enabled = false;

            // 上部の状態表示。 / The status strip along the top.
            var statusPanel = UiFactory.CreateImage(transform, "StatusPanel", UiFactory.OverlayColor);
            var statusRect = statusPanel.rectTransform;
            statusRect.anchorMin = new Vector2(0f, PreviewTopAnchor);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;

            statusText = UiFactory.CreateText(
                statusRect,
                string.Empty,
                UiFactory.FontSizeBody,
                TextAnchor.MiddleCenter,
                UiFactory.TextColor);
            var statusTextRect = statusText.rectTransform;
            statusTextRect.anchorMin = new Vector2(0f, 0f);
            statusTextRect.anchorMax = new Vector2(1f, 1f);
            statusTextRect.offsetMin = new Vector2(UiFactory.ScreenPadding, 8f);
            statusTextRect.offsetMax = new Vector2(-UiFactory.ScreenPadding, -8f);

            // 中央のメッセージ。検証失敗や生成失敗をここに出す。 / The centre message, used for validation and generation failures.
            var messageBacking = UiFactory.CreateImage(transform, "MessagePanel", UiFactory.OverlayColor);
            messagePanel = messageBacking.rectTransform;
            messagePanel.anchorMin = new Vector2(0.04f, 0.42f);
            messagePanel.anchorMax = new Vector2(0.96f, 0.72f);
            messagePanel.offsetMin = Vector2.zero;
            messagePanel.offsetMax = Vector2.zero;

            messageText = UiFactory.CreateText(
                messagePanel,
                string.Empty,
                UiFactory.FontSizeBody,
                TextAnchor.MiddleCenter,
                UiFactory.WarningColor);
            var messageTextRect = messageText.rectTransform;
            messageTextRect.anchorMin = new Vector2(0f, 0f);
            messageTextRect.anchorMax = new Vector2(1f, 1f);
            messageTextRect.offsetMin = new Vector2(24f, 24f);
            messageTextRect.offsetMax = new Vector2(-24f, -24f);
            messagePanel.gameObject.SetActive(false);

            // 下部の操作エリア。 / The control area along the bottom.
            var controlBacking = UiFactory.CreateImage(transform, "ControlPanel", UiFactory.OverlayColor);
            var controlRect = controlBacking.rectTransform;
            controlRect.anchorMin = new Vector2(0f, 0f);
            controlRect.anchorMax = new Vector2(1f, PreviewBottomAnchor);
            controlRect.offsetMin = Vector2.zero;
            controlRect.offsetMax = Vector2.zero;

            var controls = UiFactory.CreateVerticalLayout(
                controlRect,
                UiFactory.DefaultSpacing,
                new RectOffset(
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding,
                    UiFactory.ScreenPadding / 2,
                    UiFactory.ScreenPadding / 2));
            controls.childAlignment = TextAnchor.LowerCenter;
            var controlsRoot = controls.transform;

            shootButton = UiFactory.CreateButton(
                controlsRoot,
                "撮影",
                UiFactory.PrimaryButtonColor,
                OnShootPressed);
            shootButton.interactable = false;

            var retryRow = UiFactory.CreateHorizontalLayout(
                controlsRoot,
                UiFactory.DefaultSpacing * 0.5f,
                new RectOffset(0, 0, 0, 0));
            retryRow.childForceExpandWidth = true;
            UiFactory.SetFixedHeight(retryRow.gameObject, UiFactory.ButtonHeight);

            retryGenerateButton = UiFactory.CreateButton(
                retryRow.transform,
                "再試行",
                UiFactory.PrimaryButtonColor,
                OnRetryGeneratePressed);

            retakeButton = UiFactory.CreateButton(
                retryRow.transform,
                "撮り直す",
                UiFactory.SecondaryButtonColor,
                OnRetakePressed);

            backButton = UiFactory.CreateButton(
                controlsRoot,
                "戻る",
                UiFactory.SecondaryButtonColor,
                () => Navigate(ScreenId.Title, null));

            SetRetryButtonsVisible(false, false);
        }

        /// <summary>
        /// カメラを使えるようにするまでの一連の手順。権限 → 対応確認 → プレビュー開始の順は入れ替えられない。
        /// The sequence that makes the camera usable. Permission, then support, then preview: the order is fixed.
        /// </summary>
        /// <remarks>
        /// Android では CAMERA 権限が許可されるまで <c>WebCamTexture.devices</c> が空のままなので、
        /// 権限より先に <c>IsSupported</c> を見ると「カメラが無い」と誤判定する。
        /// On Android <c>WebCamTexture.devices</c> stays empty until the CAMERA permission is granted, so
        /// checking <c>IsSupported</c> before asking would wrongly report that there is no camera.
        /// </remarks>
        private IEnumerator StartCameraRoutine()
        {
            SetRetryButtonsVisible(false, false);
            HideMessage();

            if (!GameContext.IsInitialized)
            {
                Debug.LogError(
                    "[CaptureScreen] GameContext が初期化されていません。 / GameContext has not been initialized.");
                ShowMessage("アプリの初期化に失敗しました。タイトルに戻ってやり直してください。");
                SetStatus(string.Empty);
                shootButton.interactable = false;
                yield break;
            }

            capture = GameContext.PhotoCapture as WebCamPhotoCapture;
            if (capture == null)
            {
                Debug.LogError(
                    "[CaptureScreen] PhotoCapture が WebCamPhotoCapture ではありません。 / "
                    + "GameContext.PhotoCapture is not a WebCamPhotoCapture.");
                ShowMessage("この端末ではカメラを利用できません。ブートストラップの設定を確認してください。");
                SetStatus(string.Empty);
                shootButton.interactable = false;
                yield break;
            }

            SetStatus("カメラの使用許可を確認しています…");

            var granted = false;
            yield return StartCoroutine(capture.RequestPermission(value => granted = value));

            if (!granted)
            {
                shootButton.interactable = false;
                SetStatus(string.Empty);
                ShowMessage(
                    "カメラの使用が許可されていません。\n"
                    + "このゲームは「今その場で撮った写真」だけでカードを作るため、カメラが必須です。\n"
                    + "端末の設定からカメラの許可をオンにして、もう一度お試しください。");
                yield break;
            }

            if (!capture.IsSupported)
            {
                shootButton.interactable = false;
                SetStatus(string.Empty);
                ShowMessage("使えるカメラが見つかりませんでした。カメラのある端末でお試しください。");
                yield break;
            }

            SetStatus("カメラを起動しています…");
            yield return StartCoroutine(capture.StartPreview());

            if (!capture.IsPreviewing || capture.PreviewTexture == null)
            {
                shootButton.interactable = false;
                SetStatus(string.Empty);
                ShowMessage("カメラを起動できませんでした。ほかのアプリがカメラを使っていないか確認してください。");
                yield break;
            }

            InvalidatePreviewTransformCache();
            previewRaw.texture = capture.PreviewTexture;
            previewRaw.enabled = true;
            UpdatePreviewTransform();

            HideMessage();
            SetStatus("枝・葉・石など、自然のものにカメラを向けて「撮影」を押してください。");
            shootButton.interactable = true;
            activeRoutine = null;
        }

        /// <summary>
        /// 「撮影」が押されたときの処理。撮影自体は同期的に終わるので、後続の検証だけコルーチンにする。
        /// Handles the shoot button. The capture itself completes synchronously, so only the validation that
        /// follows runs as a coroutine.
        /// </summary>
        private void OnShootPressed()
        {
            if (busy || capture == null)
            {
                return;
            }

            busy = true;
            shootButton.interactable = false;
            HideMessage();
            SetStatus("撮影しています…");

            PhotoCaptureResult result = null;
            capture.TakePhoto(value => result = value);

            if (result == null || !result.Success)
            {
                var reason = result != null && !string.IsNullOrEmpty(result.ErrorMessage)
                    ? result.ErrorMessage
                    : "撮影に失敗しました。もう一度お試しください。";
                busy = false;
                shootButton.interactable = true;
                SetStatus("もう一度「撮影」を押してください。");
                ShowMessage(reason);
                return;
            }

            pendingPhotoPath = capture.LastCapturePath;
            pendingCaptureUtc = capture.LastCaptureUtc;
            pendingJpeg = capture.LastCaptureJpeg;

            StartRoutine(ValidateAndGenerateRoutine());
        }

        /// <summary>
        /// 撮った1枚を検証し、通ればカードを生成する。検証で落ちた場合は写真を消費せず撮り直させる。
        /// Validates the photo just taken and generates a card when it passes. A failed validation does not
        /// consume the photo: the player simply retakes it.
        /// </summary>
        private IEnumerator ValidateAndGenerateRoutine()
        {
            SetStatus("写真を確認しています…");

            var config = GameContext.Config;
            var validator = new CaptureValidator(
                GameContext.CaptureHistory,
                config.FreshPhotoMaxAgeMinutes,
                config.MinMetersBetweenCaptures,
                config.RequireLocationCheck);

            var freshness = validator.ValidateFreshness(pendingCaptureUtc, DateTime.UtcNow);
            if (!freshness.IsValid)
            {
                ShowRetakePrompt(freshness.Message);
                yield break;
            }

            pendingHasLocation = false;
            pendingLatitude = 0d;
            pendingLongitude = 0d;

            if (config.RequireLocationCheck)
            {
                SetStatus("現在地を確認しています…");

                LocationProbeResult location = null;
                yield return StartCoroutine(
                    EnsureLocationProbe().TryGetLocation(LocationTimeoutSeconds, value => location = value));

                if (location != null && location.Success)
                {
                    pendingHasLocation = true;
                    pendingLatitude = location.Latitude;
                    pendingLongitude = location.Longitude;
                }
                else if (location != null)
                {
                    // 位置情報が取れないことは失敗ではない。移動チェックは通過扱いになる。
                    // A missing fix is not a failure; the movement check then passes by design.
                    Debug.Log(
                        "[CaptureScreen] 位置情報を取得できませんでした / Could not obtain a location: "
                        + location.ErrorMessage);
                }

                var movement = validator.ValidateMovement(pendingHasLocation, pendingLatitude, pendingLongitude);
                if (!movement.IsValid)
                {
                    ShowRetakePrompt(movement.Message);
                    yield break;
                }
            }

            yield return StartCoroutine(GenerateRoutine());
        }

        /// <summary>
        /// 生成サービスを呼び、成功したらカードを保存して次の画面へ進む。
        /// Calls the generation service and, on success, saves the card and moves to the next screen.
        /// </summary>
        private IEnumerator GenerateRoutine()
        {
            busy = true;
            shootButton.interactable = false;
            SetRetryButtonsVisible(false, false);
            HideMessage();
            SetStatus("カードを生成中…");

            if (pendingJpeg == null || pendingJpeg.Length == 0)
            {
                ShowRetakePrompt("撮影データが見つかりませんでした。もう一度撮影してください。");
                yield break;
            }

            CardGenerationResult generation = null;
            yield return StartCoroutine(
                GameContext.CardGenerator.Generate(pendingJpeg, value => generation = value));

            if (generation == null)
            {
                ShowGenerationFailure("カードの生成結果を受け取れませんでした。", true);
                yield break;
            }

            if (!generation.Success || generation.Card == null)
            {
                var reason = string.IsNullOrEmpty(generation.ErrorMessage)
                    ? "カードの生成に失敗しました。"
                    : generation.ErrorMessage;
                ShowGenerationFailure(reason, generation.IsRetryable);
                yield break;
            }

            var card = generation.Card;
            SaveGeneratedCard(card);

            busy = false;
            activeRoutine = null;

            if (capture != null)
            {
                capture.StopPreview();
            }

            Navigate(ScreenId.CardResult, card);
        }

        /// <summary>
        /// 生成できたカードを保存する。保存の失敗でカード自体を失わせないため、例外は握ってログに落とす。
        /// Persists the generated card. Exceptions are caught and logged so a storage failure never costs the
        /// player the card they walked outside for.
        /// </summary>
        private void SaveGeneratedCard(Card card)
        {
            try
            {
                var relativePath = GameContext.Thumbnails.SaveThumbnail(card.Id, pendingPhotoPath);
                if (!string.IsNullOrEmpty(relativePath))
                {
                    card.ImagePath = relativePath;
                }

                if (pendingHasLocation)
                {
                    card.SetLocation(pendingLatitude, pendingLongitude);
                }

                GameContext.Cards.Add(card);

                if (pendingHasLocation)
                {
                    GameContext.CaptureHistory.RecordCapture(
                        pendingCaptureUtc,
                        pendingLatitude,
                        pendingLongitude);
                }
                else
                {
                    GameContext.CaptureHistory.RecordCapture(pendingCaptureUtc);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[CaptureScreen] カードの保存に失敗しました / Failed to persist the generated card: " + e);
            }
        }

        /// <summary>
        /// 「再試行」が押されたときの処理。同じJPEGバイト列で生成だけをやり直す。
        /// Handles the retry button, re-running only the generation with the very same JPEG bytes.
        /// </summary>
        private void OnRetryGeneratePressed()
        {
            if (busy)
            {
                return;
            }

            StartRoutine(GenerateRoutine());
        }

        /// <summary>
        /// 「撮り直す」が押されたときの処理。プレビューが止まっていれば起動しなおす。
        /// Handles the retake button, restarting the preview when it is no longer running.
        /// </summary>
        private void OnRetakePressed()
        {
            if (busy)
            {
                return;
            }

            ResetPendingCapture();
            HideMessage();
            SetRetryButtonsVisible(false, false);

            if (capture != null && capture.IsPreviewing)
            {
                SetStatus("枝・葉・石など、自然のものにカメラを向けて「撮影」を押してください。");
                shootButton.interactable = true;
                return;
            }

            StartRoutine(StartCameraRoutine());
        }

        /// <summary>
        /// 撮り直しを促す状態にする。写真は破棄されるので、次の撮影が新しい1枚になる。
        /// Switches to the "please retake" state; the photo is discarded, so the next shot is a fresh one.
        /// </summary>
        private void ShowRetakePrompt(string message)
        {
            busy = false;
            activeRoutine = null;
            ResetPendingCapture();
            SetStatus("もう一度「撮影」を押してください。");
            ShowMessage(string.IsNullOrEmpty(message) ? "写真を確認できませんでした。" : message);
            SetRetryButtonsVisible(false, false);
            shootButton.interactable = capture != null && capture.IsPreviewing;
        }

        /// <summary>
        /// 生成失敗の状態にする。再試行できる失敗のときだけ「再試行」を出す。
        /// Switches to the generation-failure state, offering the retry button only for retryable failures.
        /// </summary>
        private void ShowGenerationFailure(string message, bool isRetryable)
        {
            busy = false;
            activeRoutine = null;
            SetStatus(isRetryable
                ? "通信状況を確かめて「再試行」を押してください。写真はそのまま使えます。"
                : "「撮り直す」でもう一度撮影してください。");
            ShowMessage(message);
            SetRetryButtonsVisible(isRetryable, true);
            shootButton.interactable = false;
        }

        /// <summary>
        /// 保持している撮影データを捨てる。 / Drops the photo currently held for generation.
        /// </summary>
        private void ResetPendingCapture()
        {
            pendingJpeg = null;
            pendingPhotoPath = null;
            pendingCaptureUtc = DateTime.MinValue;
            pendingHasLocation = false;
            pendingLatitude = 0d;
            pendingLongitude = 0d;
        }

        /// <summary>
        /// 実行中のコルーチンを1本に保つ。前のものは必ず止める。
        /// Keeps exactly one coroutine running, always stopping the previous one first.
        /// </summary>
        private void StartRoutine(IEnumerator routine)
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }

            activeRoutine = StartCoroutine(routine);
        }

        /// <summary>
        /// 位置情報の取得役を用意する。設定で有効なときにしか作らない。
        /// Prepares the location helper, created only when the config opted in.
        /// </summary>
        private LocationProbe EnsureLocationProbe()
        {
            if (locationProbe == null)
            {
                locationProbe = GetComponent<LocationProbe>();
            }

            if (locationProbe == null)
            {
                locationProbe = gameObject.AddComponent<LocationProbe>();
            }

            return locationProbe;
        }

        /// <summary>
        /// 上部の状態表示を更新する。 / Updates the status strip along the top.
        /// </summary>
        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        /// <summary>
        /// 中央のメッセージを出す。 / Shows the centre message.
        /// </summary>
        private void ShowMessage(string message)
        {
            if (messageText == null || messagePanel == null)
            {
                return;
            }

            messageText.text = message ?? string.Empty;
            messagePanel.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        /// <summary>
        /// 中央のメッセージを消す。 / Hides the centre message.
        /// </summary>
        private void HideMessage()
        {
            if (messagePanel != null)
            {
                messagePanel.gameObject.SetActive(false);
            }

            if (messageText != null)
            {
                messageText.text = string.Empty;
            }
        }

        /// <summary>
        /// 「再試行」と「撮り直す」の表示を切り替える。 / Toggles the retry and retake buttons.
        /// </summary>
        private void SetRetryButtonsVisible(bool retryGenerate, bool retake)
        {
            if (retryGenerateButton != null)
            {
                retryGenerateButton.gameObject.SetActive(retryGenerate);
            }

            if (retakeButton != null)
            {
                retakeButton.gameObject.SetActive(retake);
            }
        }

        /// <summary>
        /// プレビューの向き・大きさを再計算する。値が前回と同じなら何もしない。
        /// Recomputes the preview's orientation and size, doing nothing when every input is unchanged.
        /// </summary>
        /// <remarks>
        /// <c>videoRotationAngle</c> は「映像を正しい向きにするために必要な回転角」なので、
        /// UI側は逆方向 (負の角度) に回す。90/270度のときは回転前の幅と高さを入れ替えて指定する。
        /// <c>videoRotationAngle</c> is the rotation needed to bring the feed upright, so the UI rotates by the
        /// negative of it. At 90 or 270 degrees the pre-rotation width and height are swapped.
        /// </remarks>
        private void UpdatePreviewTransform()
        {
            if (previewRaw == null || previewFrame == null || capture == null)
            {
                return;
            }

            var texture = capture.PreviewTexture;
            if (texture == null
                || texture.width <= WebCamPhotoCapture.MinSaneTextureWidth
                || texture.height <= WebCamPhotoCapture.MinSaneTextureWidth)
            {
                return;
            }

            var frameSize = previewFrame.rect.size;
            if (frameSize.x <= 1f || frameSize.y <= 1f)
            {
                return;
            }

            var angle = capture.PreviewRotationAngle;
            var mirrored = capture.PreviewVerticallyMirrored;

            if (texture.width == lastTextureWidth
                && texture.height == lastTextureHeight
                && angle == lastRotationAngle
                && mirrored == lastMirrored
                && Mathf.Approximately(frameSize.x, lastFrameSize.x)
                && Mathf.Approximately(frameSize.y, lastFrameSize.y))
            {
                return;
            }

            lastTextureWidth = texture.width;
            lastTextureHeight = texture.height;
            lastRotationAngle = angle;
            lastMirrored = mirrored;
            lastFrameSize = frameSize;

            var normalizedAngle = ((angle % 360) + 360) % 360;
            var swapped = normalizedAngle == 90 || normalizedAngle == 270;

            var displayedWidth = swapped ? (float)texture.height : texture.width;
            var displayedHeight = swapped ? (float)texture.width : texture.height;

            var scale = Mathf.Min(frameSize.x / displayedWidth, frameSize.y / displayedHeight);
            var fittedWidth = displayedWidth * scale;
            var fittedHeight = displayedHeight * scale;

            var rect = previewRaw.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = swapped
                ? new Vector2(fittedHeight, fittedWidth)
                : new Vector2(fittedWidth, fittedHeight);
            rect.localEulerAngles = new Vector3(0f, 0f, -normalizedAngle);
            rect.localScale = new Vector3(1f, mirrored ? -1f : 1f, 1f);
        }

        /// <summary>
        /// プレビュー補正のキャッシュを無効化する。次回に必ず再計算させる。
        /// Invalidates the preview-correction cache so the next check always recomputes.
        /// </summary>
        private void InvalidatePreviewTransformCache()
        {
            lastTextureWidth = 0;
            lastTextureHeight = 0;
            lastRotationAngle = int.MinValue;
            lastMirrored = false;
            lastFrameSize = Vector2.zero;
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
                    "[CaptureScreen] ScreenRouter が見つかりません。 / ScreenRouter.Instance is null.");
                return;
            }

            ScreenRouter.Instance.Show(id, payload);
        }
    }
}
