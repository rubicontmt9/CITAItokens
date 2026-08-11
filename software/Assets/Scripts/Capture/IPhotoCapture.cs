using System;

namespace CitaiTokens.Capture
{
    /// <summary>
    /// 写真撮影を抽象化する。ギャラリーからの取り込み経路は意図的に持たない
    /// (「今、屋外で撮った写真」であることを構造的に担保するため)。
    /// Abstracts taking a photo. There is deliberately no gallery-import path, so that
    /// "a photo taken just now, outdoors" is guaranteed structurally rather than by a rule.
    /// </summary>
    public interface IPhotoCapture
    {
        /// <summary>この端末で撮影が可能か。 / Whether capture is possible on this device.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// OSのカメラを起動して1枚撮影する。結果はコールバックで返る。
        /// Launches the OS camera to take a single photo; the result arrives via callback.
        /// </summary>
        void TakePhoto(Action<PhotoCaptureResult> onComplete);
    }

    /// <summary>
    /// 撮影結果。 / Result of a capture attempt.
    /// </summary>
    public sealed class PhotoCaptureResult
    {
        private PhotoCaptureResult(bool success, string filePath, bool cancelled, string errorMessage)
        {
            Success = success;
            FilePath = filePath;
            Cancelled = cancelled;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }

        /// <summary>撮影された画像ファイルの絶対パス。 / Absolute path to the captured image file.</summary>
        public string FilePath { get; }

        /// <summary>プレイヤーが撮影をキャンセルしたか。 / Whether the player cancelled the capture.</summary>
        public bool Cancelled { get; }

        public string ErrorMessage { get; }

        public static PhotoCaptureResult Ok(string filePath)
        {
            return new PhotoCaptureResult(true, filePath, false, null);
        }

        public static PhotoCaptureResult Cancel()
        {
            return new PhotoCaptureResult(false, null, true, null);
        }

        public static PhotoCaptureResult Fail(string errorMessage)
        {
            return new PhotoCaptureResult(false, null, false, errorMessage);
        }
    }
}
