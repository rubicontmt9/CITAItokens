using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace CitaiTokens.Capture
{
    /// <summary>
    /// Unity標準の <see cref="WebCamTexture"/> だけで撮影を行う <see cref="IPhotoCapture"/> 実装。
    /// 端末のギャラリーを開く経路は一切持たず、「今この場で撮った1枚」しか取得できない。
    /// エディタではノートPCのウェブカメラでそのまま動くので、机の上でゲームループを試せる。
    /// An <see cref="IPhotoCapture"/> implementation built only on Unity's built-in <see cref="WebCamTexture"/>.
    /// It has no path to the device gallery at all: the only thing it can produce is a frame taken right now.
    /// It also works in the Editor with a laptop webcam, so the whole loop is testable at a desk.
    /// </summary>
    public sealed class WebCamPhotoCapture : MonoBehaviour, IPhotoCapture
    {
        /// <summary>撮影画像を置くサブディレクトリ名。 / Name of the sub-directory holding captured images.</summary>
        public const string CaptureDirectoryName = CaptureFileStore.DirectoryName;

        /// <summary>JPEGエンコード品質。 / JPEG encoding quality.</summary>
        public const int JpegQuality = 85;

        /// <summary>
        /// 起動直後の <see cref="WebCamTexture"/> はダミーサイズを返すため、これを超えるまで「未準備」とみなす。
        /// A freshly started <see cref="WebCamTexture"/> reports a placeholder size, so anything at or below
        /// this width counts as "not ready yet".
        /// </summary>
        public const int MinSaneTextureWidth = 16;

        /// <summary>プレビューが実サイズを報告するまで待つ上限秒数。 / Seconds to wait for the preview to report a real size.</summary>
        private const float PreviewReadyTimeoutSeconds = 8f;

        /// <summary>権限ダイアログの応答を待つ上限秒数。 / Seconds to wait for the permission dialog to be answered.</summary>
        private const float PermissionTimeoutSeconds = 60f;

        [Header("Requested preview resolution")]
        [SerializeField] private int requestedWidth = 1280;
        [SerializeField] private int requestedHeight = 720;

        private WebCamTexture webCamTexture;

        private CaptureFileStore captureFiles;

        /// <summary>
        /// 撮影した元画像ファイルを所有するストア。保存先・保持枚数・容量の診断はすべてこちらが持つ。
        /// The store owning the captured original files: destination, retention and storage diagnostics.
        /// </summary>
        public CaptureFileStore CaptureFiles
        {
            get
            {
                if (captureFiles == null)
                {
                    captureFiles = new CaptureFileStore();
                }

                return captureFiles;
            }
        }

        /// <summary>
        /// UIがプレビュー表示にバインドするテクスチャ。未起動時は null。
        /// The texture the UI binds its preview to; null while the preview is not running.
        /// </summary>
        public WebCamTexture PreviewTexture => webCamTexture;

        /// <summary>プレビューが再生中か。 / Whether the preview is currently running.</summary>
        public bool IsPreviewing => webCamTexture != null && webCamTexture.isPlaying;

        /// <summary>
        /// この端末で撮影が可能か。カメラデバイスが1台でも見つかれば true。
        /// Whether capture is possible on this device: true when at least one camera device is visible.
        /// </summary>
        public bool IsSupported
        {
            get
            {
                var devices = WebCamTexture.devices;
                return devices != null && devices.Length > 0;
            }
        }

        /// <summary>
        /// 直近の撮影のJPEGバイト列。ファイルを読み直さずそのまま <c>ICardGenerator</c> に渡せる。
        /// JPEG bytes of the most recent capture, so the caller can hand them straight to <c>ICardGenerator</c>
        /// without re-reading the file.
        /// </summary>
        public byte[] LastCaptureJpeg { get; private set; }

        /// <summary>直近の撮影ファイルの絶対パス。 / Absolute path of the most recent capture file.</summary>
        public string LastCapturePath { get; private set; }

        /// <summary>直近の撮影時刻 (UTC)。鮮度チェックに使う。 / Time of the most recent capture (UTC), for the freshness check.</summary>
        public DateTime LastCaptureUtc { get; private set; }

        /// <summary>撮影画像を保存するディレクトリの絶対パス。 / Absolute path of the directory holding captured images.</summary>
        public string CaptureDirectory => CaptureFiles.DirectoryPath;

        /// <summary>
        /// プレビュー映像の回転角。UI側でRawImageを回す判断に使う。
        /// Rotation angle of the preview feed, so the UI can decide how to rotate its RawImage.
        /// </summary>
        public int PreviewRotationAngle => webCamTexture != null ? webCamTexture.videoRotationAngle : 0;

        /// <summary>
        /// プレビュー映像が上下反転しているか。UI側の表示補正に使う。
        /// Whether the preview feed is vertically mirrored, for the UI's display correction.
        /// </summary>
        public bool PreviewVerticallyMirrored => webCamTexture != null && webCamTexture.videoVerticallyMirrored;

        /// <summary>
        /// カメラ権限を要求する。コルーチンとして実行し、許可されたかどうかをコールバックで返す。
        /// Requests camera permission. Run as a coroutine; the callback receives whether it was granted.
        /// </summary>
        /// <param name="onGranted">許可されたかを受け取るコールバック。 / Callback receiving whether permission was granted.</param>
        public IEnumerator RequestPermission(Action<bool> onGranted)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                if (onGranted != null)
                {
                    onGranted(true);
                }

                yield break;
            }

            Permission.RequestUserPermission(Permission.Camera);

            var waited = 0f;
            while (waited < PermissionTimeoutSeconds
                && !Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }

            var granted = Permission.HasUserAuthorizedPermission(Permission.Camera);
            if (!granted)
            {
                Debug.LogWarning(
                    "[WebCamPhotoCapture] カメラ権限が許可されませんでした / Camera permission was not granted.");
            }

            if (onGranted != null)
            {
                onGranted(granted);
            }
#else
            if (Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                if (onGranted != null)
                {
                    onGranted(true);
                }

                yield break;
            }

            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            var granted = Application.HasUserAuthorization(UserAuthorization.WebCam);
            if (!granted)
            {
                Debug.LogWarning(
                    "[WebCamPhotoCapture] カメラの使用許可が得られませんでした / Webcam authorization was not granted.");
            }

            if (onGranted != null)
            {
                onGranted(granted);
            }
#endif
        }

        /// <summary>
        /// プレビューを開始する。背面カメラがあればそれを選び、テクスチャが実サイズを報告するまで待つ。
        /// 起動直後の数フレームはダミーサイズが返るため、この待機を省くと真っ黒な写真が撮れてしまう。
        /// Starts the preview, preferring a rear-facing device, and waits until the texture reports a real size.
        /// The first few frames report a placeholder size, so skipping this wait yields a black photo.
        /// </summary>
        public IEnumerator StartPreview()
        {
            if (IsPreviewing)
            {
                yield break;
            }

            if (!IsSupported)
            {
                Debug.LogWarning(
                    "[WebCamPhotoCapture] 利用可能なカメラがありません / No camera device is available.");
                yield break;
            }

            if (webCamTexture == null)
            {
                var deviceName = SelectDeviceName();
                string createError = null;
                try
                {
                    webCamTexture = string.IsNullOrEmpty(deviceName)
                        ? new WebCamTexture(requestedWidth, requestedHeight)
                        : new WebCamTexture(deviceName, requestedWidth, requestedHeight);
                }
                catch (Exception e)
                {
                    createError = e.Message;
                }

                if (createError != null)
                {
                    Debug.LogError(
                        "[WebCamPhotoCapture] カメラの初期化に失敗しました / Failed to create the WebCamTexture: "
                        + createError);
                    webCamTexture = null;
                    yield break;
                }
            }

            string startError = null;
            try
            {
                webCamTexture.Play();
            }
            catch (Exception e)
            {
                startError = e.Message;
            }

            if (startError != null)
            {
                Debug.LogError(
                    "[WebCamPhotoCapture] カメラの起動に失敗しました / Failed to start the camera: " + startError);
                ReleaseTexture();
                yield break;
            }

            var waited = 0f;
            while (waited < PreviewReadyTimeoutSeconds
                && webCamTexture != null
                && webCamTexture.width <= MinSaneTextureWidth)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }

            if (webCamTexture != null && webCamTexture.width <= MinSaneTextureWidth)
            {
                Debug.LogWarning(
                    "[WebCamPhotoCapture] プレビューが実サイズを報告しませんでした / "
                    + "The preview never reported a sane size; capture will report a failure until it does.");
            }
        }

        /// <summary>
        /// プレビューを停止してカメラデバイスを解放する。テクスチャは再利用のため保持する。
        /// Stops the preview and releases the camera device, keeping the texture object for reuse.
        /// </summary>
        public void StopPreview()
        {
            if (webCamTexture == null)
            {
                return;
            }

            try
            {
                if (webCamTexture.isPlaying)
                {
                    webCamTexture.Stop();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[WebCamPhotoCapture] カメラの停止中に例外が発生しました / "
                    + "Exception while stopping the camera: " + e.Message);
            }
        }

        /// <summary>
        /// 現在のプレビューフレームを1枚JPEGとして保存する。この経路以外に写真の入手手段はない。
        /// 例外はすべて <see cref="PhotoCaptureResult.Fail"/> に変換し、外へ投げない。
        /// Saves the current preview frame as a single JPEG. There is no other way to obtain a photo.
        /// Every exception is converted into <see cref="PhotoCaptureResult.Fail"/> and never thrown out.
        /// </summary>
        /// <param name="onComplete">撮影結果を受け取るコールバック。 / Callback receiving the capture result.</param>
        public void TakePhoto(Action<PhotoCaptureResult> onComplete)
        {
            PhotoCaptureResult result;
            try
            {
                result = CaptureCurrentFrame();
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[WebCamPhotoCapture] 撮影中に例外が発生しました / Exception while capturing: " + e);
                result = PhotoCaptureResult.Fail("撮影に失敗しました。もう一度お試しください。");
            }

            if (onComplete != null)
            {
                onComplete(result);
            }
        }

        /// <summary>
        /// 実際のフレーム取得・エンコード・保存。失敗はすべて結果オブジェクトで表現する。
        /// Does the actual frame grab, encode and save; every failure is expressed as a result object.
        /// </summary>
        private PhotoCaptureResult CaptureCurrentFrame()
        {
            if (!IsSupported)
            {
                return PhotoCaptureResult.Fail("カメラが見つかりません。端末のカメラを確認してください。");
            }

            if (webCamTexture == null || !webCamTexture.isPlaying)
            {
                return PhotoCaptureResult.Fail("カメラが起動していません。もう一度カメラを開いてください。");
            }

            var width = webCamTexture.width;
            var height = webCamTexture.height;
            if (width <= MinSaneTextureWidth || height <= MinSaneTextureWidth)
            {
                return PhotoCaptureResult.Fail("カメラの準備がまだ整っていません。少し待ってから撮影してください。");
            }

            var pixels = webCamTexture.GetPixels32();
            if (pixels == null || pixels.Length < width * height)
            {
                return PhotoCaptureResult.Fail("カメラ映像の取得に失敗しました。もう一度お試しください。");
            }

            // 回転・反転の情報は「今この瞬間の」WebCamTexture から読む。端末を回すと値が変わるため、
            // 起動時にキャッシュした値を使うと撮影のたびに古い向きで補正してしまう。
            // Read the rotation and mirror flags from the live WebCamTexture at this instant: they change when
            // the device is rotated, so a value cached at start-up would correct against a stale orientation.
            var rotationAngle = webCamTexture.videoRotationAngle;
            var mirrored = webCamTexture.videoVerticallyMirrored;

            int outputWidth;
            int outputHeight;
            var oriented = ApplyOrientation(
                pixels, width, height, rotationAngle, mirrored, out outputWidth, out outputHeight);

            // 実機ではこのログだけが「補正が走ったが結果が誤り」と「補正がそもそも走っていない」を区別する材料になる。
            // On a device this log is the only way to tell "the correction ran and was wrong" from
            // "the correction never ran at all".
            Debug.Log(
                "[WebCamPhotoCapture] 向き補正 / Orientation correction: angle=" + rotationAngle
                + " mirrored=" + mirrored
                + " size " + width + "x" + height + " -> " + outputWidth + "x" + outputHeight);

            byte[] jpeg = null;
            Texture2D temporary = null;
            try
            {
                temporary = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);
                temporary.SetPixels32(oriented);
                temporary.Apply();
                jpeg = temporary.EncodeToJPG(JpegQuality);
            }
            finally
            {
                DestroyTexture(temporary);
            }

            if (jpeg == null || jpeg.Length == 0)
            {
                return PhotoCaptureResult.Fail("画像の変換に失敗しました。もう一度お試しください。");
            }

            var capturedAtUtc = DateTime.UtcNow;
            var absolutePath = CaptureFiles.Write(jpeg, capturedAtUtc);
            if (string.IsNullOrEmpty(absolutePath))
            {
                return PhotoCaptureResult.Fail("画像の保存に失敗しました。端末の空き容量を確認してください。");
            }

            LastCaptureJpeg = jpeg;
            LastCapturePath = absolutePath;
            LastCaptureUtc = capturedAtUtc;

            // 整理は「保存が成功した後」だけ。上限に達していても今撮った1枚が最も新しいので消えることはない。
            // Prune only after the write succeeded; the photo just taken is the newest file, so it is never
            // a deletion candidate even when the cap is already reached.
            CaptureFiles.Prune();

            return PhotoCaptureResult.Ok(absolutePath);
        }

        /// <summary>
        /// 撮影フレームの画素配列に回転と上下反転を適用し、補正後の画素配列を返す (幅・高さは out で返す)。
        /// <see cref="Texture2D"/> を中間生成せず、<see cref="Color32"/> 配列のまま添字計算だけで詰め替える。
        /// Applies the rotation and the vertical mirror to a captured frame's pixel array, returning the
        /// corrected pixels and reporting the new size through the out parameters. It works on the
        /// <see cref="Color32"/> array with index arithmetic alone, creating no intermediate
        /// <see cref="Texture2D"/>.
        /// </summary>
        /// <remarks>
        /// 【CaptureScreen のプレビュー補正とは別の関心事】
        /// <c>CaptureScreen.UpdatePreviewTransform</c> は RawImage の Transform を回して「画面上の見え方」を
        /// 直しているだけで、テクスチャの中身には一切触れない。こちらは保存される画素そのものを並べ替える。
        /// 片方を消しても他方は代われない。プレビューだけ直すと保存画像が横倒しになり、こちらだけ直しても
        /// プレビューは横倒しのまま見える。重複に見えても消さないこと。
        /// This and the preview transform are separate concerns. <c>CaptureScreen.UpdatePreviewTransform</c>
        /// rotates a RawImage's Transform to fix how the feed *looks on screen* and never touches the texture's
        /// contents; this method reorders the pixels that actually get *stored*. Neither can substitute for the
        /// other: delete this and the saved JPEG comes out sideways, delete that and the preview does. They are
        /// not duplicates.
        ///
        /// 【座標系】
        /// <c>WebCamTexture.GetPixels32</c> と <c>Texture2D.SetPixels32</c> は
        /// どちらも「左下原点・行優先」で、添字は <c>y * width + x</c> (y は上向き)。両者で同じ規約なので、
        /// ここでは終始このテクスチャ座標系で考える。以下、W=<paramref name="sourceWidth"/>、
        /// H=<paramref name="sourceHeight"/>、(dx, dy) は出力側の座標。
        /// Both <c>WebCamTexture.GetPixels32</c> and <c>Texture2D.SetPixels32</c> use a
        /// bottom-left origin in row-major order, indexed as <c>y * width + x</c> with y pointing up. The two
        /// agree, so everything below stays in that texture space. W is <paramref name="sourceWidth"/>,
        /// H is <paramref name="sourceHeight"/>, and (dx, dy) is a destination coordinate.
        ///
        /// 【出力→入力の対応 (すべて時計回りの回転)】
        ///   0度  : destW = W, destH = H,  srcX = dx,            srcY = dy
        ///   90度 : destW = H, destH = W,  srcX = (W - 1) - dy,  srcY = dx
        ///   180度: destW = W, destH = H,  srcX = (W - 1) - dx,  srcY = (H - 1) - dy
        ///   270度: destW = H, destH = W,  srcX = dy,            srcY = (H - 1) - dx
        /// 90度と270度では幅と高さが入れ替わる。dy は 0..destH-1、90/270 では destH = W なので
        /// srcX の範囲は 0..W-1 に収まり、dx は 0..destW-1 = 0..H-1 なので srcY も 0..H-1 に収まる。
        /// Destination-to-source mapping for a clockwise rotation, as listed above. Width and height swap at
        /// 90 and 270. There, dy runs 0..destH-1 = 0..W-1 so srcX stays within 0..W-1, and dx runs
        /// 0..destW-1 = 0..H-1 so srcY stays within 0..H-1: every index is in range at both ends.
        ///
        /// 【上下反転は回転より「前」】
        /// プレビュー側は Transform の localScale (反転) → localRotation (回転) の順で合成される。つまり
        /// 正しい順序は「元画像の座標系で上下反転してから回す」。ここでは出力から入力を引く逆向きの計算なので、
        /// 逆順すなわち「回転の逆写像を解いてから、最後に srcY を (H-1)-srcY へ折り返す」と等価になる。
        /// 反転は自分自身が逆写像なので、折り返しはそのまま使える。
        /// The mirror is applied before the rotation. A Transform composes as rotation × scale, so the preview
        /// flips the texture in its own frame first and rotates the result; the stored pixels must follow the
        /// same order. Because this method walks destination-to-source it applies the inverse chain in reverse:
        /// solve the rotation's inverse first, then fold srcY to (H-1)-srcY last. A flip is its own inverse, so
        /// the same fold serves both directions.
        ///
        /// 想定外の角度 (0/90/180/270 以外) は回転なしとして扱う。無理に近い角度へ丸めるより、補正しなかった
        /// ことがログとファイルから読み取れる方が実機調査で役に立つ。
        /// An angle that is not 0/90/180/270 is treated as no rotation: leaving it visibly uncorrected is more
        /// useful on a device than silently snapping it to a nearby angle.
        ///
        /// 補正が不要な場合 (0度かつ反転なし) は確保を省いて <paramref name="source"/> をそのまま返す。
        /// 返った配列は読み取り専用として扱うこと。
        /// When no correction is needed (0 degrees and no mirror) it skips the allocation and returns
        /// <paramref name="source"/> itself, so the returned array must be treated as read-only.
        /// </remarks>
        /// <param name="source">元の画素配列 (長さは W*H 以上)。 / Source pixels, at least W*H long.</param>
        /// <param name="sourceWidth">元の幅 W。 / Source width W.</param>
        /// <param name="sourceHeight">元の高さ H。 / Source height H.</param>
        /// <param name="rotationAngle">時計回りに必要な回転角。 / Clockwise rotation angle needed.</param>
        /// <param name="verticallyMirrored">上下反転が必要か。 / Whether a vertical flip is needed.</param>
        /// <param name="resultWidth">補正後の幅。 / Width after correction.</param>
        /// <param name="resultHeight">補正後の高さ。 / Height after correction.</param>
        private static Color32[] ApplyOrientation(
            Color32[] source,
            int sourceWidth,
            int sourceHeight,
            int rotationAngle,
            bool verticallyMirrored,
            out int resultWidth,
            out int resultHeight)
        {
            var normalizedAngle = ((rotationAngle % 360) + 360) % 360;
            if (normalizedAngle != 90 && normalizedAngle != 180 && normalizedAngle != 270)
            {
                normalizedAngle = 0;
            }

            var swapped = normalizedAngle == 90 || normalizedAngle == 270;
            resultWidth = swapped ? sourceHeight : sourceWidth;
            resultHeight = swapped ? sourceWidth : sourceHeight;

            if (normalizedAngle == 0 && !verticallyMirrored)
            {
                return source;
            }

            var lastX = sourceWidth - 1;
            var lastY = sourceHeight - 1;
            var result = new Color32[sourceWidth * sourceHeight];

            // どの分岐でも dy を 0..resultHeight-1、dx を 0..resultWidth-1 で回し、
            // 出力添字 dy * resultWidth + dx へ「ちょうど一度ずつ」書き込む。抜けも二重書き込みも起きない。
            // Every branch walks dy over 0..resultHeight-1 and dx over 0..resultWidth-1, writing the destination
            // index dy * resultWidth + dx exactly once, so there are neither gaps nor double writes.
            switch (normalizedAngle)
            {
                case 90:
                    // srcX = (W - 1) - dy, srcY = dx
                    for (var dy = 0; dy < resultHeight; dy++)
                    {
                        var destRow = dy * resultWidth;
                        var srcX = lastX - dy;
                        for (var dx = 0; dx < resultWidth; dx++)
                        {
                            var srcY = verticallyMirrored ? lastY - dx : dx;
                            result[destRow + dx] = source[(srcY * sourceWidth) + srcX];
                        }
                    }

                    break;

                case 180:
                    // srcX = (W - 1) - dx, srcY = (H - 1) - dy
                    for (var dy = 0; dy < resultHeight; dy++)
                    {
                        var destRow = dy * resultWidth;
                        var rotatedY = lastY - dy;
                        var srcY = verticallyMirrored ? lastY - rotatedY : rotatedY;
                        var srcRow = srcY * sourceWidth;
                        for (var dx = 0; dx < resultWidth; dx++)
                        {
                            result[destRow + dx] = source[srcRow + (lastX - dx)];
                        }
                    }

                    break;

                case 270:
                    // srcX = dy, srcY = (H - 1) - dx
                    for (var dy = 0; dy < resultHeight; dy++)
                    {
                        var destRow = dy * resultWidth;
                        var srcX = dy;
                        for (var dx = 0; dx < resultWidth; dx++)
                        {
                            var rotatedY = lastY - dx;
                            var srcY = verticallyMirrored ? lastY - rotatedY : rotatedY;
                            result[destRow + dx] = source[(srcY * sourceWidth) + srcX];
                        }
                    }

                    break;

                default:
                    // 回転なし・上下反転のみ: srcX = dx, srcY = (H - 1) - dy
                    // No rotation, mirror only: srcX = dx, srcY = (H - 1) - dy
                    for (var dy = 0; dy < resultHeight; dy++)
                    {
                        var destRow = dy * resultWidth;
                        var srcRow = (lastY - dy) * sourceWidth;
                        for (var dx = 0; dx < resultWidth; dx++)
                        {
                            result[destRow + dx] = source[srcRow + dx];
                        }
                    }

                    break;
            }

            return result;
        }

        /// <summary>
        /// 背面カメラを優先して1台選ぶ。背面が無ければ最初のデバイスを返す。
        /// Picks one device, preferring a rear-facing camera and falling back to the first device.
        /// </summary>
        private static string SelectDeviceName()
        {
            var devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                return null;
            }

            for (var i = 0; i < devices.Length; i++)
            {
                if (!devices[i].isFrontFacing)
                {
                    return devices[i].name;
                }
            }

            return devices[0].name;
        }

        /// <summary>
        /// 一時テクスチャを破棄する。再生中かどうかで適切なAPIを選ぶ。
        /// Destroys a temporary texture, choosing the right API for play mode versus edit mode.
        /// </summary>
        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// カメラを止めてテクスチャ自体も破棄する。デバイスを掴んだままにしないための後始末。
        /// Stops the camera and destroys the texture itself, so the device is never left held open.
        /// </summary>
        private void ReleaseTexture()
        {
            if (webCamTexture == null)
            {
                return;
            }

            StopPreview();

            var texture = webCamTexture;
            webCamTexture = null;

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// 非表示になったらカメラを止める。バックグラウンドでデバイスを占有し続けないようにする。
        /// Stops the camera when this component is disabled, so the device is not occupied in the background.
        /// </summary>
        private void OnDisable()
        {
            StopPreview();
        }

        /// <summary>
        /// 破棄時にカメラとテクスチャを完全に解放する。
        /// Fully releases the camera and its texture on destruction.
        /// </summary>
        private void OnDestroy()
        {
            ReleaseTexture();
        }
    }
}
