using System;
using System.Collections;
using CitaiTokens.Cards;
using UnityEngine;

namespace CitaiTokens.AI
{
    /// <summary>
    /// 写真を実際に解析してカードを作る <see cref="ICardGenerator"/>。ハッシュだけで振っていた
    /// <see cref="MockCardGenerator"/> を置き換える、このゲームの中心の処理。
    /// The <see cref="ICardGenerator"/> that actually looks at the photo. It replaces the hash-only
    /// <see cref="MockCardGenerator"/> and is the core of the game.
    /// </summary>
    /// <remarks>
    /// 通信はしない。すべて端末内で完結するので、失敗は「この写真では無理」という種類のものだけで、
    /// 再試行しても結果は変わらない。したがって <c>isRetryable</c> は常に false。
    /// 同じ写真からは必ず同じカードが出る (時刻もシードなしの乱数も使っていない)。これは
    /// 「良い結果が出るまで撮り直す」を封じるためで、屋外に出ることがこのゲームの目的だから。
    ///
    /// No networking: everything runs on device, so the only failures are "this photo cannot be used", which a
    /// retry would not change — hence isRetryable is always false. The same photo always produces the same card,
    /// since neither the clock nor an unseeded random is involved. That closes the re-shoot-until-good loophole,
    /// which matters because the point of the game is going outside.
    /// </remarks>
    public sealed class PhotoAnalysisCardGenerator : ICardGenerator
    {
        /// <summary>
        /// JPEGバイト列を解析してカードを生成する。コルーチンとして実行する。例外はコルーチン外に出さない。
        /// Analyzes the JPEG bytes and generates a card. Run as a coroutine; no exception escapes it.
        /// </summary>
        /// <param name="jpegBytes">撮影写真のJPEGバイト列。 / JPEG bytes of the captured photo.</param>
        /// <param name="onComplete">成否を含む結果を受け取るコールバック。 / Callback receiving the result, success or failure.</param>
        public IEnumerator Generate(byte[] jpegBytes, Action<CardGenerationResult> onComplete)
        {
            // 「生成中…」の表示が1フレームも出ないまま終わらないよう、必ず1回はフレームを譲る。
            // Always yield at least once, so the "生成中…" state is genuinely exercised.
            yield return null;

            if (jpegBytes == null || jpegBytes.Length == 0)
            {
                Report(onComplete, CardGenerationResult.Fail("写真データが空でした。もう一度撮影してください。", false));
                yield break;
            }

            // 反復子ブロックでは catch 付きの try の中で yield できないため、解析は同期メソッドに閉じ込める。
            // A C# iterator cannot yield inside a try that has a catch, so the analysis lives in a sync method.
            var result = BuildResult(jpegBytes);

            yield return null;

            Report(onComplete, result);
        }

        /// <summary>
        /// テクスチャの読み込みから組み立てまでを同期で行う。どの経路でも一時テクスチャを破棄する。
        /// Runs load, analysis and assembly synchronously, destroying the temporary texture on every path.
        /// </summary>
        /// <param name="jpegBytes">撮影写真のJPEGバイト列。 / JPEG bytes of the captured photo.</param>
        private static CardGenerationResult BuildResult(byte[] jpegBytes)
        {
            Texture2D texture = null;
            try
            {
                // LoadImage が実際のサイズに作り直すので、初期サイズは何でもよい。
                // LoadImage resizes the texture to the real image, so the initial size does not matter.
                texture = new Texture2D(2, 2);
                if (!texture.LoadImage(jpegBytes))
                {
                    Debug.LogWarning(
                        "[PhotoAnalysisCardGenerator] 画像をデコードできませんでした / Could not decode the image ("
                        + jpegBytes.Length + " bytes).");
                    return CardGenerationResult.Fail("写真を読み込めませんでした。もう一度撮影してください。", false);
                }

                var features = PhotoAnalyzer.Analyze(texture);
                var photoHash = PhotoStatDeriver.ComputeHash(jpegBytes);

                // 実機で「なぜこのカードになったのか」を追える唯一の窓口。ここを消すと調整ができなくなる。
                // The only window into why a card came out the way it did on a real device. Without this log,
                // tuning is guesswork.
                Debug.Log(
                    "[PhotoAnalysisCardGenerator] 特徴量 / features: " + features
                    + " | hash=" + photoHash.ToString("x8")
                    + " | " + texture.width + "x" + texture.height);

                var card = PhotoCardComposer.Compose(features, photoHash, null);

                Debug.Log(
                    "[PhotoAnalysisCardGenerator] 判定 / derived: ジャンル=" + card.WeaponGenre
                    + " 属性=" + card.Element
                    + " レア度=" + card.Rarity
                    + " / " + card.Stats
                    + " / " + card.DisplayName);

                return CardGenerationResult.Ok(card);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[PhotoAnalysisCardGenerator] 解析に失敗しました / Photo analysis failed: " + e);
                return CardGenerationResult.Fail("カードの生成に失敗しました。もう一度撮影してください。", false);
            }
            finally
            {
                if (texture != null)
                {
                    // 端末のメモリは限られている。写真1枚分のテクスチャを残すと数枚で枯渇する。
                    // Device memory is tight: leaking one photo-sized texture per capture exhausts it quickly.
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        /// <summary>
        /// コールバックが null でも落ちないように包む。 / Wraps the callback so a null one cannot break the coroutine.
        /// </summary>
        /// <param name="onComplete">結果を受け取るコールバック。 / The callback receiving the result.</param>
        /// <param name="result">通知する結果。 / The result to report.</param>
        private static void Report(Action<CardGenerationResult> onComplete, CardGenerationResult result)
        {
            if (onComplete != null)
            {
                onComplete(result);
            }
        }
    }
}
