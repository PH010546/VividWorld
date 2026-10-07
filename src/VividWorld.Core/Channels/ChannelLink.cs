namespace VividWorld.Core.Channels
{
    public sealed class ChannelLink
    {
        public string HeroId { get; }
        public ChannelKind Kind { get; }

        /// <summary>逐連結修正值，一律 1.0。通道種類的權重由 config.channelWeights 負責。
        /// 保留給日後做同一通道內的強弱分級（例如配偶 vs 堂表親）。</summary>
        public double Weight { get; }

        /// <summary>家族間的好感度。方向是「講的人怎麼看聽的人」。
        /// 僅供日誌與開發者工具做對照，傳話邏輯不再由它決定。
        /// Core 不認識 Hero——由 Module 在建立連結時填入。</summary>
        public int Relation { get; }

        /// <summary>兩個人自己的好感（不含任何第三方加成）。傳話分層、擲骰機率與名單排序一律看這個，
        /// 不是家族間的 <see cref="Relation"/>。預設 0。</summary>
        public int OwnRelation { get; }

        /// <summary>聽的人是不是講的人的自家人（同家族，或配偶、父母、子女、兄弟姊妹）。預設 false。</summary>
        public bool IsFamily { get; }

        public ChannelLink(string heroId, ChannelKind kind, double weight = 1.0, int relation = 0,
            int ownRelation = 0, bool isFamily = false)
        {
            HeroId = heroId;
            Kind = kind;
            Weight = weight;
            Relation = relation;
            OwnRelation = ownRelation;
            IsFamily = isFamily;
        }
    }
}
