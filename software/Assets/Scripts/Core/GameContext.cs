using System;
using CitaiTokens.AI;
using CitaiTokens.Capture;
using CitaiTokens.Data;

namespace CitaiTokens.Core
{
    /// <summary>
    /// アプリ全体で共有するサービスの置き場所 (コンポジションルート)。
    /// 起動時のブートストラップが <see cref="Initialize"/> で中身を詰め、各画面はここから取り出す。
    /// Holds the services shared across the app (the composition root). The bootstrap fills it in via
    /// <see cref="Initialize"/> at startup, and screens read their dependencies from here.
    /// </summary>
    public static class GameContext
    {
        private static AppConfig config;
        private static ICardRepository cards;
        private static ICaptureHistory captureHistory;
        private static ICardGenerator cardGenerator;
        private static IPhotoCapture photoCapture;
        private static ThumbnailStore thumbnails;

        /// <summary>
        /// <see cref="Initialize"/> が呼ばれ済みか。 / Whether <see cref="Initialize"/> has been called.
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>調整値を持つ設定アセット。 / The settings asset holding tunable values.</summary>
        public static AppConfig Config => Require(config, nameof(Config));

        /// <summary>カードコレクションの保存先。 / Where the card collection is stored.</summary>
        public static ICardRepository Cards => Require(cards, nameof(Cards));

        /// <summary>直近の撮影記録。 / The most recent capture record.</summary>
        public static ICaptureHistory CaptureHistory => Require(captureHistory, nameof(CaptureHistory));

        /// <summary>写真からカードを生成する処理。 / The service that turns a photo into a card.</summary>
        public static ICardGenerator CardGenerator => Require(cardGenerator, nameof(CardGenerator));

        /// <summary>写真撮影の入口。 / The entry point for taking a photo.</summary>
        public static IPhotoCapture PhotoCapture => Require(photoCapture, nameof(PhotoCapture));

        /// <summary>カード画像ファイルの管理。 / Management of card image files.</summary>
        public static ThumbnailStore Thumbnails => Require(thumbnails, nameof(Thumbnails));

        /// <summary>
        /// 共有サービスを登録する。起動時に一度だけ呼ぶ。
        /// Registers the shared services. Call this exactly once at startup.
        /// </summary>
        public static void Initialize(
            AppConfig config,
            ICardRepository cards,
            ICaptureHistory captureHistory,
            ICardGenerator cardGenerator,
            IPhotoCapture photoCapture,
            ThumbnailStore thumbnails)
        {
            GameContext.config = config;
            GameContext.cards = cards;
            GameContext.captureHistory = captureHistory;
            GameContext.cardGenerator = cardGenerator;
            GameContext.photoCapture = photoCapture;
            GameContext.thumbnails = thumbnails;
            IsInitialized = true;
        }

        /// <summary>
        /// 登録内容を破棄する。テストやシーン再読み込みで使う。
        /// Discards the registered services. Used by tests and on scene reloads.
        /// </summary>
        public static void Clear()
        {
            config = null;
            cards = null;
            captureHistory = null;
            cardGenerator = null;
            photoCapture = null;
            thumbnails = null;
            IsInitialized = false;
        }

        /// <summary>
        /// 未初期化のまま参照された場合に、原因がすぐ分かる例外を投げる。
        /// Throws an exception naming the cause when a service is read before initialization.
        /// </summary>
        private static T Require<T>(T service, string memberName) where T : class
        {
            if (service == null)
            {
                throw new InvalidOperationException(
                    "GameContext." + memberName + " は未設定です。GameContext.Initialize を先に呼んでください。 / "
                    + "GameContext." + memberName + " is not set. Call GameContext.Initialize first.");
            }

            return service;
        }
    }
}
