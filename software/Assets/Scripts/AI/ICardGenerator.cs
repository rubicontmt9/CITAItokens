using System;
using System.Collections;
using CitaiTokens.Cards;

namespace CitaiTokens.AI
{
    /// <summary>
    /// 写真からカードを生成する処理を抽象化する。実装はプロキシサービス経由でAIを呼ぶ。
    /// Abstracts turning a photo into a card. Implementations call the AI through the proxy service.
    /// </summary>
    public interface ICardGenerator
    {
        /// <summary>
        /// JPEGバイト列からカードを生成する。コルーチンとして実行する。
        /// Generates a card from JPEG bytes. Run this as a coroutine.
        /// </summary>
        /// <param name="jpegBytes">撮影写真のJPEGバイト列。 / JPEG bytes of the captured photo.</param>
        /// <param name="onComplete">成否を含む結果を受け取るコールバック。 / Callback receiving the result, success or failure.</param>
        IEnumerator Generate(byte[] jpegBytes, Action<CardGenerationResult> onComplete);
    }

    /// <summary>
    /// カード生成の結果。失敗時は <see cref="IsRetryable"/> でリトライ可否を判断する。
    /// Result of a card generation attempt. On failure, <see cref="IsRetryable"/> says whether retrying makes sense.
    /// </summary>
    public sealed class CardGenerationResult
    {
        private CardGenerationResult(bool success, Card card, string errorMessage, bool isRetryable)
        {
            Success = success;
            Card = card;
            ErrorMessage = errorMessage;
            IsRetryable = isRetryable;
        }

        public bool Success { get; }

        /// <summary>成功時の生成カード。失敗時は null。 / The generated card on success; null on failure.</summary>
        public Card Card { get; }

        /// <summary>プレイヤーに提示できる失敗理由。 / A player-facing reason for the failure.</summary>
        public string ErrorMessage { get; }

        /// <summary>通信断など、再試行で解決しうる失敗か。 / Whether the failure may resolve on retry (e.g. lost connectivity).</summary>
        public bool IsRetryable { get; }

        public static CardGenerationResult Ok(Card card)
        {
            return new CardGenerationResult(true, card, null, false);
        }

        public static CardGenerationResult Fail(string errorMessage, bool isRetryable)
        {
            return new CardGenerationResult(false, null, errorMessage, isRetryable);
        }
    }
}
