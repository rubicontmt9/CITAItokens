using System;
using System.Collections;
using System.Text;
using CitaiTokens.Cards;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace CitaiTokens.AI
{
    /// <summary>
    /// カード生成プロキシを叩く <see cref="ICardGenerator"/> 実装。APIキーは端末に置かず、プロキシ側に留める。
    /// 失敗はすべて <see cref="CardGenerationResult"/> に変換し、コルーチンの外へ例外を出さない。
    /// An <see cref="ICardGenerator"/> that calls the card-generation proxy. The API key never lives on the
    /// device; it stays on the proxy. Every failure is converted into a <see cref="CardGenerationResult"/>
    /// and no exception ever escapes the coroutine.
    /// </summary>
    public sealed class CardProxyClient : ICardGenerator
    {
        /// <summary>生成エンドポイントのパス。 / Path of the generation endpoint.</summary>
        public const string GeneratePath = "/generate";

        /// <summary>既定のタイムアウト秒数。 / Default timeout in seconds.</summary>
        public const int DefaultTimeoutSeconds = 30;

        /// <summary>
        /// 応答が壊れていたときの最大試行回数。1回だけやり直して、それでも駄目なら諦める。
        /// Maximum attempts when the response body is malformed: retry the whole request once, then give up.
        /// </summary>
        public const int MaxAttempts = 2;

        private readonly string baseUrl;
        private readonly int timeoutSeconds;

        /// <summary>
        /// プロキシのベースURLとタイムアウトを指定して初期化する。
        /// Creates the client with the proxy base URL and a timeout.
        /// </summary>
        /// <param name="proxyBaseUrl">プロキシのベースURL (末尾スラッシュは任意)。 / Base URL of the proxy; a trailing slash is optional.</param>
        /// <param name="requestTimeoutSeconds">1リクエストのタイムアウト秒数。 / Timeout for a single request, in seconds.</param>
        public CardProxyClient(string proxyBaseUrl, int requestTimeoutSeconds = DefaultTimeoutSeconds)
        {
            baseUrl = proxyBaseUrl == null ? null : proxyBaseUrl.Trim();
            timeoutSeconds = requestTimeoutSeconds > 0 ? requestTimeoutSeconds : DefaultTimeoutSeconds;
        }

        /// <summary>使用するプロキシのベースURL。 / The proxy base URL in use.</summary>
        public string BaseUrl => baseUrl;

        /// <summary>1リクエストのタイムアウト秒数。 / Timeout for a single request, in seconds.</summary>
        public int TimeoutSeconds => timeoutSeconds;

        /// <summary>
        /// JPEGをbase64にしてプロキシへPOSTし、返ってきたカードを組み立てる。コルーチンとして実行する。
        /// Base64-encodes the JPEG, POSTs it to the proxy and builds the returned card. Run as a coroutine.
        /// </summary>
        /// <param name="jpegBytes">撮影写真のJPEGバイト列。 / JPEG bytes of the captured photo.</param>
        /// <param name="onComplete">成否を含む結果を受け取るコールバック。 / Callback receiving the result, success or failure.</param>
        public IEnumerator Generate(byte[] jpegBytes, Action<CardGenerationResult> onComplete)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                Report(
                    onComplete,
                    CardGenerationResult.Fail("生成サーバーのURLが設定されていません。設定を確認してください。", false));
                yield break;
            }

            if (jpegBytes == null || jpegBytes.Length == 0)
            {
                Report(
                    onComplete,
                    CardGenerationResult.Fail("写真データが空でした。もう一度撮影してください。", false));
                yield break;
            }

            byte[] requestBody = null;
            string buildError = null;
            try
            {
                var payload = new GenerateRequest { ImageBase64 = Convert.ToBase64String(jpegBytes) };
                requestBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            }
            catch (Exception e)
            {
                buildError = e.Message;
            }

            if (buildError != null || requestBody == null || requestBody.Length == 0)
            {
                Debug.LogError(
                    "[CardProxyClient] リクエストの組み立てに失敗しました / Failed to build the request body: "
                    + buildError);
                Report(
                    onComplete,
                    CardGenerationResult.Fail("送信データの準備に失敗しました。もう一度お試しください。", false));
                yield break;
            }

            var url = BuildEndpointUrl();

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                CardGenerationResult outcome = null;
                var shouldRetry = false;

                using (var request = new UnityWebRequest(url, "POST"))
                {
                    string setupError = null;
                    try
                    {
                        request.uploadHandler = new UploadHandlerRaw(requestBody);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");
                        request.timeout = timeoutSeconds;
                    }
                    catch (Exception e)
                    {
                        setupError = e.Message;
                    }

                    if (setupError != null)
                    {
                        Debug.LogError(
                            "[CardProxyClient] リクエストの設定に失敗しました / Failed to configure the request: "
                            + setupError);
                        Report(
                            onComplete,
                            CardGenerationResult.Fail("送信の準備に失敗しました。もう一度お試しください。", false));
                        yield break;
                    }

                    yield return request.SendWebRequest();

                    outcome = Interpret(request, attempt < MaxAttempts, out shouldRetry);
                }

                if (shouldRetry)
                {
                    Debug.LogWarning(
                        "[CardProxyClient] 応答が読めなかったため1回だけ再送します / "
                        + "The response was unreadable; retrying the request once.");
                    continue;
                }

                Report(onComplete, outcome);
                yield break;
            }

            Report(
                onComplete,
                CardGenerationResult.Fail("カードの生成結果を読み取れませんでした。もう一度お試しください。", false));
        }

        /// <summary>
        /// 応答を分類して結果に変換する。再試行の可否がリトライUIの挙動を決めるので分類は厳密に行う。
        /// 通信断とタイムアウトは再試行可、5xx も再試行可、4xx は再試行不可 (写真自体の問題なので)。
        /// Classifies the response into a result. This classification drives the retry UI, so it is explicit:
        /// connectivity loss and timeouts are retryable, 5xx is retryable, 4xx is not (the photo itself is the problem).
        /// </summary>
        /// <param name="request">送信済みのリクエスト。 / The request that has already been sent.</param>
        /// <param name="retryAllowed">壊れた応答に対して再送してよいか。 / Whether a malformed body may be retried.</param>
        /// <param name="shouldRetry">再送すべきときに true。 / Set to true when the caller should retry.</param>
        /// <returns>報告すべき結果。再送する場合は null。 / The result to report, or null when retrying.</returns>
        private static CardGenerationResult Interpret(
            UnityWebRequest request,
            bool retryAllowed,
            out bool shouldRetry)
        {
            shouldRetry = false;

            var responseCode = request.responseCode;

            // 通信断・タイムアウトはどちらも ConnectionError として現れる。電波が戻れば解決しうるので再試行可。
            // Both a dropped connection and a timeout surface as ConnectionError. Signal may come back, so retryable.
            if (request.result == UnityWebRequest.Result.ConnectionError || responseCode == 0)
            {
                Debug.LogWarning(
                    "[CardProxyClient] 通信に失敗しました / The request failed to reach the proxy: " + request.error);
                return CardGenerationResult.Fail(
                    "サーバーに接続できませんでした。電波の良い場所でもう一度お試しください。", true);
            }

            string bodyText = null;
            try
            {
                if (request.downloadHandler != null)
                {
                    bodyText = request.downloadHandler.text;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[CardProxyClient] 応答本文を読めませんでした / Could not read the response body: " + e.Message);
            }

            // 5xx はサーバー側の一時的な問題として再試行可にする。
            // 5xx is treated as a transient server-side problem, so it is retryable.
            if (responseCode >= 500)
            {
                Debug.LogWarning(
                    "[CardProxyClient] サーバーエラー / Server error " + responseCode + ": " + bodyText);
                return CardGenerationResult.Fail(
                    "サーバーが混み合っています。少し待ってからもう一度お試しください。", true);
            }

            // 4xx は再試行しても同じ結果になる。422 の説明文だけはプレイヤー向けの日本語なので
            // そのまま提示するが、それ以外の 4xx はクライアント側の不具合を示す開発者向け診断
            // (英語) なので、プレイヤーには出さずログにのみ残す。
            // 4xx would fail identically on retry. Only the 422 body is player-facing Japanese, so it is
            // shown verbatim; other 4xx bodies are English developer diagnostics indicating a client-side
            // bug, so they stay in the log rather than reaching the player.
            if (responseCode >= 400)
            {
                Debug.LogWarning(
                    "[CardProxyClient] リクエストが拒否されました / The request was rejected with "
                    + responseCode + ": " + bodyText);

                if (responseCode == 422)
                {
                    var serverMessage = ExtractServerError(bodyText);
                    return CardGenerationResult.Fail(
                        string.IsNullOrEmpty(serverMessage)
                            ? "この写真ではカードを作れませんでした。木の枝や葉、石などの自然物を撮ってみてください。"
                            : serverMessage,
                        false);
                }

                return CardGenerationResult.Fail(
                    "カードを作れませんでした。もう一度撮影してみてください。", false);
            }

            if (responseCode >= 200 && responseCode <= 299)
            {
                CardProxyResponse parsed = null;
                string failureDetail = null;

                if (string.IsNullOrEmpty(bodyText))
                {
                    failureDetail = "empty response body";
                }
                else
                {
                    try
                    {
                        parsed = JsonConvert.DeserializeObject<CardProxyResponse>(bodyText);
                    }
                    catch (Exception e)
                    {
                        failureDetail = e.Message;
                    }
                }

                if (failureDetail == null && parsed == null)
                {
                    failureDetail = "response deserialized to null";
                }

                if (failureDetail == null)
                {
                    Card card;
                    string cardError;
                    if (parsed.TryToCard(out card, out cardError))
                    {
                        if (!string.IsNullOrEmpty(parsed.Reasoning))
                        {
                            Debug.Log("[CardProxyClient] AIの判断根拠 / AI reasoning: " + parsed.Reasoning);
                        }

                        return CardGenerationResult.Ok(card);
                    }

                    failureDetail = cardError;
                }

                Debug.LogWarning(
                    "[CardProxyClient] 応答を解釈できませんでした / Could not interpret the response: "
                    + failureDetail);

                if (retryAllowed)
                {
                    shouldRetry = true;
                    return null;
                }

                return CardGenerationResult.Fail(
                    "カードの生成結果を読み取れませんでした。もう一度お試しください。", false);
            }

            Debug.LogWarning(
                "[CardProxyClient] 想定外のステータスコード / Unexpected status code " + responseCode + ": " + bodyText);
            return CardGenerationResult.Fail(
                "サーバーから想定外の応答が返りました。時間をおいてお試しください。", false);
        }

        /// <summary>
        /// エラーレスポンスの error フィールドを取り出す。取り出せなければ null。
        /// Extracts the error field from an error response body; null when it cannot be extracted.
        /// </summary>
        private static string ExtractServerError(string bodyText)
        {
            if (string.IsNullOrEmpty(bodyText))
            {
                return null;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<CardProxyError>(bodyText);
                if (parsed != null && !string.IsNullOrEmpty(parsed.Error))
                {
                    return parsed.Error.Trim();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[CardProxyClient] エラー応答を解釈できませんでした / Could not parse the error body: "
                    + e.Message);
            }

            return null;
        }

        /// <summary>
        /// ベースURLと生成パスを連結する。末尾スラッシュの有無を吸収する。
        /// Joins the base URL and the generation path, tolerating a trailing slash.
        /// </summary>
        private string BuildEndpointUrl()
        {
            var trimmed = baseUrl.TrimEnd('/');
            return trimmed + GeneratePath;
        }

        /// <summary>
        /// コールバックが null でも安全に結果を渡す。 / Delivers the result, tolerating a null callback.
        /// </summary>
        private static void Report(Action<CardGenerationResult> onComplete, CardGenerationResult result)
        {
            if (onComplete != null)
            {
                onComplete(result);
            }
        }

        /// <summary>
        /// 生成リクエストの本文。プロキシ側と共有する固定のワイヤ形式。
        /// Body of the generation request. This is a fixed wire format shared with the proxy.
        /// </summary>
        [Serializable]
        private sealed class GenerateRequest
        {
            /// <summary>JPEGのbase64表現。<c>data:</c> の接頭辞は付けない。 / Base64 of the JPEG, with no <c>data:</c> URI prefix.</summary>
            [JsonProperty("image_base64")]
            public string ImageBase64 { get; set; }
        }
    }
}
