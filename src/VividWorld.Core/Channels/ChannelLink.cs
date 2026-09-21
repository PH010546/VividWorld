namespace VividWorld.Core.Channels
{
    public sealed class ChannelLink
    {
        public string HeroId { get; }
        public ChannelKind Kind { get; }

        /// <summary>逐連結修正值，一律 1.0。通道種類的權重由 config.channelWeights 負責。
        /// 保留給日後做同一通道內的強弱分級（例如配偶 vs 堂表親）。</summary>
        public double Weight { get; }

        /// <summary>講述者對這位接觸者的好感度。方向是「講的人怎麼看聽的人」。
        /// Core 不認識 Hero——由 Module 在建立連結時填入。</summary>
        public int Relation { get; }

        public ChannelLink(string heroId, ChannelKind kind, double weight = 1.0, int relation = 0)
        {
            HeroId = heroId;
            Kind = kind;
            Weight = weight;
            Relation = relation;
        }
    }
}
