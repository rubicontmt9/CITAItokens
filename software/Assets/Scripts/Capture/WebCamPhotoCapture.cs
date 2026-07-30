using System;
using System.Collections;
using System.Globalization;
using System.IO;
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
        public const string CaptureDirectoryName = "captures";

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
        public string CaptureDirectory => Path.Combine(Application.persistentDataPath, CaptureDirectoryName);

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

            byte[] jpeg = null;
            Texture2D temporary = null;
            try
            {
                temporary = new Texture2D(width, height, TextureFormat.RGBA32, false);
                temporary.SetPixels32(pixels);
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

            var directory = CaptureDirectory;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var capturedAtUtc = DateTime.UtcNow;
            var fileName = "capture-" + capturedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ".jpg";
            var absolutePath = Path.Combine(directory, fileName);
            File.WriteAllBytes(absolutePath, jpeg);

            LastCaptureJpeg = jpeg;
            LastCapturePath = absolutePath;
            LastCaptureUtc = capturedAtUtc;

            return PhotoCaptureResult.Ok(absolutePath);
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
