using System;

namespace CitaiTokens.AI
{
    /// <summary>
    /// 写真から抽出した視覚的特徴。すべて 0〜1 に正規化されており、ステータス導出はこの値だけを見る。
    /// Visual features extracted from a photo. Every value is normalized to 0–1, and stat derivation
    /// reads nothing but these.
    /// </summary>
    /// <remarks>
    /// 「枝の太さ」や「分岐の数」を直接測るには被写体の切り出しが必要で、実装が重い。ここでは
    /// 画像全体から頑健に取れる統計量を使い、それらを太さ・複雑さの代理指標として扱う。
    /// 将来切り出しを実装した場合も、この型に項目を足すだけで導出側は書き換えずに済む。
    ///
    /// Measuring actual branch thickness or fork count would require segmenting the subject, which is
    /// substantially more work. These are global statistics that are robust to compute, used as proxies
    /// for thickness and complexity. If segmentation lands later, fields can be added here without
    /// rewriting the derivation.
    /// </remarks>
    [Serializable]
    public struct PhotoFeatures
    {
        /// <summary>緑寄りの画素の割合。木属性の主な根拠。 / Fraction of green-ish pixels; the main evidence for Wood.</summary>
        public float GreenRatio;

        /// <summary>茶・土色寄りの画素の割合。土属性の主な根拠。 / Fraction of brown/earthy pixels; the main evidence for Earth.</summary>
        public float BrownRatio;

        /// <summary>青・灰寄りの画素の割合。水属性の主な根拠。 / Fraction of blue/grey pixels; the main evidence for Water.</summary>
        public float BlueGrayRatio;

        /// <summary>彩度の平均。鮮やかさ。レアリティの根拠のひとつ。 / Mean saturation; vividness, one input to rarity.</summary>
        public float MeanSaturation;

        /// <summary>明度の平均。 / Mean brightness.</summary>
        public float MeanBrightness;

        /// <summary>明度のばらつき。高いほどメリハリがある。 / Standard deviation of brightness; higher means more contrast.</summary>
        public float Contrast;

        /// <summary>
        /// エッジの量。高いほど込み入った被写体(分岐が多い、枝が細かい)。
        /// Edge quantity. Higher means a busier subject — more forks, finer twigs.
        /// </summary>
        public float EdgeDensity;

        /// <summary>
        /// エッジの向きの揃い方。1に近いと一方向(真っ直ぐな棒)、0に近いと四方八方(分岐や葉)。
        /// How aligned the edges are. Near 1 means one direction (a straight stick); near 0 means
        /// scattered directions (forks, foliage).
        /// </summary>
        public float EdgeDirectionality;

        /// <summary>
        /// 暗い画素の割合。明るい地面や草を背景にした被写体の占有面積の代理指標。
        /// Fraction of dark pixels; a proxy for how much of the frame the subject occupies against
        /// bright ground or grass.
        /// </summary>
        public float DarkRatio;

        /// <summary>
        /// デバッグ表示用の一行要約。Editor のプレビューとログで使う。
        /// One-line summary for debugging, used by the Editor preview and logs.
        /// </summary>
        public override string ToString()
        {
            return string.Format(
                "緑{0:0.00} 茶{1:0.00} 青{2:0.00} 彩度{3:0.00} 明度{4:0.00} "
                + "コントラスト{5:0.00} エッジ量{6:0.00} 方向{7:0.00} 暗さ{8:0.00}",
                GreenRatio,
                BrownRatio,
                BlueGrayRatio,
                MeanSaturation,
                MeanBrightness,
                Contrast,
                EdgeDensity,
                EdgeDirectionality,
                DarkRatio);
        }
    }
}
