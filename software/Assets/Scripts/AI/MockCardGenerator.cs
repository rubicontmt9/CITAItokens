using System;
using System.Collections;
using CitaiTokens.Cards;
using UnityEngine;

namespace CitaiTokens.AI
{
    /// <summary>
    /// プロキシが無くてもゲームループを回せるオフライン版の <see cref="ICardGenerator"/>。
    /// 写真バイト列のハッシュから決定論的に生成するので、同じ写真は必ず同じカードになり、
    /// 違う写真は目に見えて違うカードになる (プレイテストで「AIっぽさ」を確認できる)。
    /// An offline <see cref="ICardGenerator"/> that keeps the game loop playable before the proxy exists.
    /// Everything is derived deterministically from a hash of the photo bytes, so the same photo always yields
    /// the same card and different photos yield visibly different ones — enough to playtest the real feel.
    /// </summary>
    public sealed class MockCardGenerator : ICardGenerator
    {
        /// <summary>生成中の演出を体感させるための待ち時間 (秒)。 / Seconds of fake work, so the "generating" UI state is exercised.</summary>
        public const float SimulatedWorkSeconds = 0.4f;

        /// <summary>FNV-1a 32bit のオフセット基底。 / FNV-1a 32-bit offset basis.</summary>
        private const uint FnvOffsetBasis = 2166136261u;

        /// <summary>FNV-1a 32bit の素数。 / FNV-1a 32-bit prime.</summary>
        private const uint FnvPrime = 16777619u;

        /// <summary>カード名の前半。 / First half of a generated card name.</summary>
        private static readonly string[] NamePrefixes =
        {
            "苔むした",
            "湿った",
            "陽だまりの",
            "朝露の",
            "静かな",
            "風化した",
            "ひび割れた",
            "眠たげな",
            "野ざらしの",
            "きらめく",
            "冷たい",
            "夕暮れの",
        };

        /// <summary>カード名の後半。 / Second half of a generated card name.</summary>
        private static readonly string[] NameSuffixes =
        {
            "古枝",
            "落ち葉",
            "小石",
            "木の実",
            "羊歯",
            "苔玉",
            "杉皮",
            "枯れ枝",
            "松かさ",
            "赤い実",
            "根",
            "岩肌",
        };

        /// <summary>フレーバーテキストの候補。 / Candidate flavor texts.</summary>
        private static readonly string[] FlavorTexts =
        {
            "湿った森の匂いをまだ覚えている。",
            "何度も雨に打たれて、それでも折れなかった。",
            "拾い上げると、思ったより軽かった。",
            "日の当たる側だけ、色が濃い。",
            "誰も見ていない場所で、長い時間を過ごしてきた。",
            "手のひらに、まだ土の冷たさが残る。",
            "風が通るたびに、小さく鳴っていた。",
            "季節がひとつ過ぎた跡が刻まれている。",
        };

        /// <summary>
        /// 写真バイト列から決定論的にカードを組み立てる。コルーチンとして実行する。
        /// Builds a card deterministically from the photo bytes. Run as a coroutine.
        /// </summary>
        /// <param name="jpegBytes">撮影写真のJPEGバイト列。 / JPEG bytes of the captured photo.</param>
        /// <param name="onComplete">成否を含む結果を受け取るコールバック。 / Callback receiving the result, success or failure.</param>
        public IEnumerator Generate(byte[] jpegBytes, Action<CardGenerationResult> onComplete)
        {
            // 「生成中…」の表示が一瞬で消えてしまわないよう、本番相当の待ちを挟む。
            // Wait a little, so the "生成中…" state is exercised the way it will be in production.
            yield return null;
            yield return null;
            yield return new WaitForSeconds(SimulatedWorkSeconds);

            if (jpegBytes == null || jpegBytes.Length == 0)
            {
                if (onComplete != null)
                {
                    onComplete(CardGenerationResult.Fail("写真データが空でした。もう一度撮影してください。", false));
                }

                yield break;
            }

            CardGenerationResult result;
            try
            {
                result = CardGenerationResult.Ok(BuildCard(jpegBytes));
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[MockCardGenerator] 疑似生成に失敗しました / Mock generation failed: " + e);
                result = CardGenerationResult.Fail("カードの生成に失敗しました。もう一度お試しください。", false);
            }

            if (onComplete != null)
            {
                onComplete(result);
            }
        }

        /// <summary>
        /// ハッシュを種にしたカード1枚の組み立て。ステータスは必ず <see cref="StatBlock.Clamped"/> を通す。
        /// Builds one card from the hash-seeded random source; stats always go through <see cref="StatBlock.Clamped"/>.
        /// </summary>
        private static Card BuildCard(byte[] jpegBytes)
        {
            var hash = Fnv1a32(jpegBytes);
            var random = new System.Random(unchecked((int)hash));

            var element = (ElementType)random.Next(0, 3);
            var rarity = DrawRarity(random);
            var tier = (int)rarity;

            var stats = new StatBlock(
                random.Next(45, 121) + (tier * 20),
                random.Next(9, 31) + (tier * 7),
                random.Next(2, 19) + (tier * 5),
                random.Next(5, 27) + (tier * 5)).Clamped();

            var displayName = NamePrefixes[random.Next(0, NamePrefixes.Length)]
                + NameSuffixes[random.Next(0, NameSuffixes.Length)];
            var flavorText = FlavorTexts[random.Next(0, FlavorTexts.Length)];

            var card = new Card(
                Guid.NewGuid().ToString(),
                displayName,
                element,
                rarity,
                stats,
                flavorText,
                null,
                DateTime.UtcNow);

            card.SourcePhotoHash = hash.ToString("x8");
            return card;
        }

        /// <summary>
        /// レアリティを重み付きで引く。高レアが出にくいことで、屋外で何枚も撮る動機になる。
        /// Draws a rarity with weights; high rarities staying rare is what keeps the player taking more photos outdoors.
        /// </summary>
        private static Rarity DrawRarity(System.Random random)
        {
            var roll = random.Next(0, 100);
            if (roll < 55)
            {
                return Rarity.Common;
            }

            if (roll < 82)
            {
                return Rarity.Uncommon;
            }

            if (roll < 96)
            {
                return Rarity.Rare;
            }

            return Rarity.Epic;
        }

        /// <summary>
        /// FNV-1a (32bit) でバイト列をハッシュする。外部依存が無く決定論的なので疑似生成の種に向く。
        /// Hashes the bytes with FNV-1a (32-bit): dependency-free and deterministic, ideal as a mock seed.
        /// </summary>
        public static uint Fnv1a32(byte[] bytes)
        {
            var hash = FnvOffsetBasis;
            if (bytes == null)
            {
                return hash;
            }

            unchecked
            {
                for (var i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
            }

            return hash;
        }
    }
}
