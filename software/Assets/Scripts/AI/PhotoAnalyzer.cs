using System;
using UnityEngine;

namespace CitaiTokens.AI
{
    /// <summary>
    /// 写真1枚から <see cref="PhotoFeatures"/> を取り出す解析器。学習モデルを使わない画像統計だけで構成する。
    /// Extracts <see cref="PhotoFeatures"/> from a single photo using nothing but image statistics — no model.
    /// </summary>
    /// <remarks>
    /// ここで測っているのは「枝の太さ」や「分岐の数」そのものではない。それらを測るには被写体の切り出しが必要で、
    /// 本作の範囲外。代わりに画像全体の統計量を代理指標として使う:
    ///   ・太さ・面積 → <see cref="PhotoFeatures.DarkRatio"/> (明るい地面や草を背景にした暗い塊の割合から近似)
    ///   ・分岐の多さ → <see cref="PhotoFeatures.EdgeDensity"/> (勾配の平均強度から近似)
    ///   ・真っ直ぐさ → <see cref="PhotoFeatures.EdgeDirectionality"/> (勾配方向の揃い方から近似)
    /// 背景が暗ければ被写体が細くても DarkRatio は上がる。つまりこの代理指標は背景に強く依存する。
    /// 実写での検証が必須。
    ///
    /// This does not measure branch thickness or fork count. Doing that needs subject segmentation, which is out
    /// of scope. Global image statistics stand in as proxies instead: DarkRatio approximates how much of the frame
    /// a dark subject occupies against bright ground, EdgeDensity approximates how busy/forked the subject is from
    /// mean gradient strength, and EdgeDirectionality approximates straightness from how aligned the gradients are.
    /// A dark background raises DarkRatio no matter how thin the branch is, so these proxies depend heavily on the
    /// background and must be validated against real photos.
    /// </remarks>
    public static class PhotoAnalyzer
    {
        /// <summary>
        /// サンプリング格子の1辺の最大数。12MP を1画素ずつ走るのは端末では重すぎるので必ず間引く。
        /// Maximum samples per side of the sampling grid; walking a 12MP image per pixel is far too slow on device.
        /// </summary>
        public const int MaxSampleSide = 128;

        /// <summary>
        /// 勾配計算に必要な最小の格子サイズ (3x3 のソーベルを置ける幅)。
        /// Minimum grid size needed for the gradient pass (enough room for a 3x3 Sobel).
        /// </summary>
        public const int MinGridSideForEdges = 3;

        /// <summary>
        /// これ未満の彩度は色相が信用できない (灰色)。RGBToHSV は無彩色に h=0 を返し、それは茶色の帯に入ってしまう。
        /// Below this saturation the hue is meaningless (grey). RGBToHSV returns h=0 for a neutral colour, which
        /// would land inside the brown band, so low-saturation pixels are routed to BlueGray instead of brown.
        /// </summary>
        public const float GreyMaxSaturation = 0.16f;

        /// <summary>緑と判定する色相の下限 (0〜1 表記。約72°の黄緑)。 / Lower hue bound for green (hue is 0–1, ~72°).</summary>
        public const float GreenHueMin = 0.20f;

        /// <summary>緑と判定する色相の上限 (約169°の青緑手前)。 / Upper hue bound for green (~169°, just short of cyan).</summary>
        public const float GreenHueMax = 0.47f;

        /// <summary>青と判定する色相の下限 (約169°)。 / Lower hue bound for blue (~169°).</summary>
        public const float BlueHueMin = 0.47f;

        /// <summary>青と判定する色相の上限 (約270°の青紫)。 / Upper hue bound for blue (~270°, blue-violet).</summary>
        public const float BlueHueMax = 0.75f;

        /// <summary>茶と判定する色相の下限 (約7°。純赤は花や人工物なので除く)。 / Lower hue bound for brown (~7°; pure red is a flower or man-made, excluded).</summary>
        public const float BrownHueMin = 0.02f;

        /// <summary>茶と判定する色相の上限 (約54°。枯草の黄土色まで含める)。 / Upper hue bound for brown (~54°, including the ochre of dry grass).</summary>
        public const float BrownHueMax = 0.15f;

        /// <summary>
        /// 茶と判定する明度の上限。茶色と鮮やかなオレンジは色相が重なるため、明るく高彩度なものは茶から外す。
        /// Upper value bound for brown. Brown and vivid orange share a hue range, so bright pixels are excluded.
        /// </summary>
        public const float BrownMaxValue = 0.86f;

        /// <summary>
        /// 茶と判定する彩度の上限。これを超える鮮やかさは土や樹皮ではなく花・実・人工物と見なす。
        /// Upper saturation bound for brown; anything more vivid is a flower, a berry or a man-made object.
        /// </summary>
        public const float BrownMaxSaturation = 0.88f;

        /// <summary>暗い画素と見なす明度のしきい値。影・樹皮・湿った土がここに入る想定。 / Value below which a pixel counts as dark (shadow, bark, wet soil).</summary>
        public const float DarkValueThreshold = 0.28f;

        /// <summary>
        /// コントラストの正規化定数。0〜1 一様分布の標準偏差が約 0.289 なので、実写で滅多に超えない値として 0.30 を置く。
        /// これは当て推量で、実写のヒストグラムを見て調整する前提。
        /// Contrast normalizer. A uniform 0–1 distribution has a standard deviation of about 0.289, so 0.30 is a
        /// value real photos rarely exceed. This is a guess, to be tuned against real photo histograms.
        /// </summary>
        public const float ContrastNormalizer = 0.30f;

        /// <summary>
        /// 3x3 ソーベルの理論上の最大出力 (0〜1 入力で完全な段差のとき 4)。正規化に使う。
        /// Theoretical maximum of a 3x3 Sobel response: 4 for a perfect step edge on 0–1 input. Used to normalize.
        /// </summary>
        public const float SobelMaxResponse = 4f;

        /// <summary>
        /// エッジ量の正規化定数。正規化済み勾配の平均がこの値に達したら「最も込み入っている」と見なす。
        /// 間引きサンプリングは最近傍なので細部が強調されやすい。当て推量なので実写で調整すること。
        /// Edge-density normalizer: a mean normalized gradient of this much is treated as maximally busy.
        /// Nearest-neighbour sampling exaggerates fine detail. A guess; tune with real photos.
        /// </summary>
        public const float EdgeDensityNormalizer = 0.25f;

        /// <summary>ゼロ除算を避けるための下限。 / Floor used to avoid dividing by zero.</summary>
        private const float Epsilon = 1e-6f;

        /// <summary>
        /// 解析に失敗したときに返す中立の特徴量。全項目を中央付近に置き、木属性寄りで決着させる。
        /// Neutral features returned when analysis fails: every value near the middle, tipping to Wood.
        /// </summary>
        public static PhotoFeatures Neutral()
        {
            return new PhotoFeatures
            {
                GreenRatio = 0.34f,
                BrownRatio = 0.33f,
                BlueGrayRatio = 0.33f,
                MeanSaturation = 0.5f,
                MeanBrightness = 0.5f,
                Contrast = 0.5f,
                EdgeDensity = 0.5f,
                EdgeDirectionality = 0.5f,
                DarkRatio = 0.5f,
            };
        }

        /// <summary>
        /// テクスチャ1枚を解析して特徴量を返す。例外は投げず、失敗時は <see cref="Neutral"/> を返して警告を出す。
        /// Analyzes one texture and returns its features. Never throws: on failure it warns and returns <see cref="Neutral"/>.
        /// </summary>
        /// <param name="texture">解析対象。読み取り可能である必要がある。 / The texture to analyze; it must be readable.</param>
        /// <returns>すべて 0〜1 に収めた特徴量。 / Features, every field clamped to 0–1.</returns>
        public static PhotoFeatures Analyze(Texture2D texture)
        {
            if (texture == null)
            {
                Debug.LogWarning("[PhotoAnalyzer] テクスチャが null でした / Texture was null; returning neutral features.");
                return Neutral();
            }

            var width = texture.width;
            var height = texture.height;
            if (width <= 0 || height <= 0)
            {
                Debug.LogWarning(
                    "[PhotoAnalyzer] テクスチャのサイズが不正でした / Invalid texture size: " + width + "x" + height);
                return Neutral();
            }

            Color32[] pixels;
            try
            {
                // GetPixels32 は1回だけ呼ぶ。GetPixel を画素ごとに呼ぶ実装より桁違いに速い。
                // One bulk GetPixels32 call. Calling GetPixel per pixel would be orders of magnitude slower.
                pixels = texture.GetPixels32();
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[PhotoAnalyzer] テクスチャを読み取れませんでした / Texture was not readable: " + e.Message);
                return Neutral();
            }

            if (pixels == null || pixels.Length < width * height)
            {
                Debug.LogWarning("[PhotoAnalyzer] 画素配列が不足していました / Pixel array was shorter than expected.");
                return Neutral();
            }

            // 実解像度ではなく固定格子で間引く。最大 128x128 = 16384 サンプルなので端末でも一瞬で終わる。
            // Index-step over a fixed grid instead of the real resolution: at most 128x128 = 16384 samples.
            var gridWidth = Mathf.Min(MaxSampleSide, width);
            var gridHeight = Mathf.Min(MaxSampleSide, height);
            var sampleCount = gridWidth * gridHeight;
            var brightnessGrid = new float[sampleCount];

            var greenCount = 0;
            var brownCount = 0;
            var blueGrayCount = 0;
            var darkCount = 0;
            var saturationSum = 0f;
            var brightnessSum = 0f;
            var brightnessSquaredSum = 0f;

            for (var gy = 0; gy < gridHeight; gy++)
            {
                // 格子座標を元画像の座標へ写す。等間隔になるよう整数演算で割り当てる。
                // Map grid coordinates back onto the source image, evenly spaced, in integer arithmetic.
                var srcY = (gy * height) / gridHeight;
                var rowOffset = srcY * width;
                var gridRowOffset = gy * gridWidth;

                for (var gx = 0; gx < gridWidth; gx++)
                {
                    var srcX = (gx * width) / gridWidth;
                    var pixel = pixels[rowOffset + srcX];

                    // Color32 から Color への暗黙変換で 0〜1 に正規化される。
                    // The implicit Color32-to-Color conversion normalizes the channels to 0–1.
                    Color color = pixel;

                    float hue;
                    float saturation;
                    float value;

                    // 色相は 0〜1 (度ではない)。 / Hue comes back as 0–1, not degrees.
                    Color.RGBToHSV(color, out hue, out saturation, out value);

                    brightnessGrid[gridRowOffset + gx] = value;
                    saturationSum += saturation;
                    brightnessSum += value;
                    brightnessSquaredSum += value * value;

                    if (value < DarkValueThreshold)
                    {
                        darkCount++;
                    }

                    if (saturation < GreyMaxSaturation)
                    {
                        // 無彩色。石・曇り空・濡れた地面・影はここに来る。水属性の根拠として扱う。
                        // Achromatic: stone, overcast sky, wet ground, shadow. Counted as evidence for Water.
                        blueGrayCount++;
                        continue;
                    }

                    if (hue >= GreenHueMin && hue < GreenHueMax)
                    {
                        greenCount++;
                        continue;
                    }

                    if (hue >= BlueHueMin && hue <= BlueHueMax)
                    {
                        blueGrayCount++;
                        continue;
                    }

                    if (hue >= BrownHueMin
                        && hue <= BrownHueMax
                        && value <= BrownMaxValue
                        && saturation <= BrownMaxSaturation)
                    {
                        // 茶色と暗いオレンジは色相が重なるので、彩度と明度も判定に参加させている。
                        // Brown overlaps dark orange in hue, so saturation and value participate in the decision.
                        brownCount++;
                    }

                    // どの帯にも入らない画素 (純赤・紫・鮮やかなオレンジなど) は未分類のまま。
                    // 3つの割合は合計1にならない。属性判定は「最大のもの」だけを見るので問題ない。
                    // Pixels in no band (pure red, violet, vivid orange) stay unclassified, so the three ratios
                    // do not sum to 1. Element derivation only compares which is largest, so that is fine.
                }
            }

            var inverseCount = 1f / sampleCount;
            var meanBrightness = brightnessSum * inverseCount;
            var meanSaturation = saturationSum * inverseCount;

            // 分散 = E[v^2] - E[v]^2。浮動小数の誤差で僅かに負になることがあるので下限を切る。
            // Variance = E[v^2] - E[v]^2; floating point error can make it slightly negative, so clamp at zero.
            var variance = (brightnessSquaredSum * inverseCount) - (meanBrightness * meanBrightness);
            if (variance < 0f)
            {
                variance = 0f;
            }

            var contrast = Mathf.Sqrt(variance) / ContrastNormalizer;

            var edgeDensity = 0f;
            var edgeDirectionality = 0f;
            if (gridWidth >= MinGridSideForEdges && gridHeight >= MinGridSideForEdges)
            {
                ComputeEdges(brightnessGrid, gridWidth, gridHeight, out edgeDensity, out edgeDirectionality);
            }

            return new PhotoFeatures
            {
                GreenRatio = Mathf.Clamp01(greenCount * inverseCount),
                BrownRatio = Mathf.Clamp01(brownCount * inverseCount),
                BlueGrayRatio = Mathf.Clamp01(blueGrayCount * inverseCount),
                MeanSaturation = Mathf.Clamp01(meanSaturation),
                MeanBrightness = Mathf.Clamp01(meanBrightness),
                Contrast = Mathf.Clamp01(contrast),
                EdgeDensity = Mathf.Clamp01(edgeDensity),
                EdgeDirectionality = Mathf.Clamp01(edgeDirectionality),
                DarkRatio = Mathf.Clamp01(darkCount * inverseCount),
            };
        }

        /// <summary>
        /// 間引き済みの明度格子に 3x3 ソーベルをかけ、エッジ量と方向の揃い方を求める。
        /// Runs a 3x3 Sobel over the downscaled brightness grid to get edge quantity and alignment.
        /// </summary>
        /// <param name="grid">明度の格子 (行優先)。 / Brightness grid, row-major.</param>
        /// <param name="gridWidth">格子の幅。 / Grid width.</param>
        /// <param name="gridHeight">格子の高さ。 / Grid height.</param>
        /// <param name="edgeDensity">エッジ量 (0〜1)。 / Edge quantity, 0–1.</param>
        /// <param name="edgeDirectionality">方向の揃い方 (0〜1)。 / Edge alignment, 0–1.</param>
        private static void ComputeEdges(
            float[] grid,
            int gridWidth,
            int gridHeight,
            out float edgeDensity,
            out float edgeDirectionality)
        {
            var magnitudeSum = 0f;
            var interiorCount = 0;

            // 方向の集計は角度を2倍にした空間 (2θ) で行う。理由: 勾配は境界の両側で符号が反転するため、
            // 生のベクトルをそのまま足すと真っ直ぐな棒でも打ち消し合って合計がほぼ 0 になり、
            // 「揃っている」はずのものが「散らばっている」と出てしまう。2θ では反対向きの勾配が同じ向きになり、
            // 同じ直線に属するエッジが互いに強め合う (構造テンソルの主軸を取るのと等価)。
            // Direction is accumulated in doubled-angle space (2θ). A gradient flips sign across the two sides of
            // an edge, so a naive vector sum cancels out and returns ~0 even for a perfectly straight stick —
            // exactly the opposite of the intended reading. Doubling the angle maps opposite gradients onto the
            // same direction, so edges on one line reinforce each other. This is the structure-tensor orientation.
            var doubledX = 0f;
            var doubledY = 0f;
            var energySum = 0f;

            for (var y = 1; y < gridHeight - 1; y++)
            {
                var rowAbove = (y - 1) * gridWidth;
                var row = y * gridWidth;
                var rowBelow = (y + 1) * gridWidth;

                for (var x = 1; x < gridWidth - 1; x++)
                {
                    var topLeft = grid[rowAbove + x - 1];
                    var top = grid[rowAbove + x];
                    var topRight = grid[rowAbove + x + 1];
                    var left = grid[row + x - 1];
                    var right = grid[row + x + 1];
                    var bottomLeft = grid[rowBelow + x - 1];
                    var bottom = grid[rowBelow + x];
                    var bottomRight = grid[rowBelow + x + 1];

                    var gx = (topRight + (2f * right) + bottomRight)
                        - (topLeft + (2f * left) + bottomLeft);
                    var gy = (bottomLeft + (2f * bottom) + bottomRight)
                        - (topLeft + (2f * top) + topRight);

                    // 段差1の完全なエッジで 1 になるよう正規化する。 / Normalize so a full step edge reads 1.
                    gx /= SobelMaxResponse;
                    gy /= SobelMaxResponse;

                    magnitudeSum += Mathf.Sqrt((gx * gx) + (gy * gy));
                    interiorCount++;

                    doubledX += (gx * gx) - (gy * gy);
                    doubledY += 2f * gx * gy;
                    energySum += (gx * gx) + (gy * gy);
                }
            }

            if (interiorCount <= 0)
            {
                edgeDensity = 0f;
                edgeDirectionality = 0f;
                return;
            }

            edgeDensity = Mathf.Clamp01((magnitudeSum / interiorCount) / EdgeDensityNormalizer);

            if (energySum <= Epsilon)
            {
                // 完全な無地。方向は定義できないので「揃っていない」側に倒す。
                // A perfectly flat image: orientation is undefined, so report no alignment.
                edgeDirectionality = 0f;
                return;
            }

            var doubledLength = Mathf.Sqrt((doubledX * doubledX) + (doubledY * doubledY));
            edgeDirectionality = Mathf.Clamp01(doubledLength / energySum);
        }
    }
}
