using VividWorld.Core.Channels;

namespace VividWorld.Core.Rumors
{
    /// <summary>講的人開口時，某一位聽的人最後落在哪一種結果。只供診斷，不影響傳話。</summary>
    public enum ContactStatus
    {
        /// <summary>擲中了，而且這位聽的人原本不知道這件事：多了一個知道的人。</summary>
        Told,
        /// <summary>擲了骰，沒中。</summary>
        Missed,
        /// <summary>這位聽的人已經知道這件事（而且沒忘），被跳過、沒擲骰。</summary>
        AlreadyKnows,
        /// <summary>這位聽的人不在傳話網路裡（死了、被俘、不是領主也不是流浪者），被跳過。</summary>
        NotEligible,
        /// <summary>這一次新增的人數已達上限，名單後面的人沒輪到；沒擲骰、沒算機率。</summary>
        NotReached,
        /// <summary>擲中了，但這位聽的人早就聽過（還記得、或忘了又聽一次）：沒有多一個知道的人。
        /// 跟 <see cref="Told"/> 分開記，因為「少傳多少」要看的是新知道的人。</summary>
        Reheard,
        /// <summary>兩個人自己的好感在交惡線以下：不講，沒擲骰、沒算機率。</summary>
        HeldBackHostile,
        /// <summary>不熟的人，而這則消息的份量沒到大事線：不講，沒擲骰、沒算機率。</summary>
        HeldBackSmallNews,
        /// <summary>醜事關係表判定不講：不講，沒擲骰、沒算機率。</summary>
        HeldBackShameful
    }

    /// <summary>講的人開口時，對一位聽的人的觀察：通道、好感、意願、層、算出來的機率、結果。</summary>
    public sealed class ContactObservation
    {
        public string HeroId { get; }
        public ChannelKind Kind { get; }

        /// <summary>講的人對聽的人的好感（方向是講的人怎麼看聽的人）。</summary>
        public int Relation { get; }

        /// <summary>那一次算出來的傳話機率；沒算到（跳過、沒輪到）就是 0。</summary>
        public double Chance { get; }

        public ContactStatus Status { get; }

        /// <summary>兩個人自己的好感（分層看的是這個）。</summary>
        public int OwnRelation { get; }

        /// <summary>聽的人是不是講的人的自家人。</summary>
        public bool IsFamily { get; }

        /// <summary>講的人對聽的人的意願（自己的好感加三項個性）。引擎算好放進來。</summary>
        public double Willingness { get; }

        /// <summary>這位聽的人落在哪一層。</summary>
        public TellTier Tier { get; }

        /// <summary>被擋下時的原因說明（例如醜事規則誰對誰）。</summary>
        public string? HeldBackReason { get; }

        /// <summary>那一次算出來的好感乘數（由個人好感算出，供診斷觀察）。</summary>
        public double RelationFactor { get; }

        public ContactObservation(string heroId, ChannelKind kind, int relation, double chance, ContactStatus status,
            int ownRelation = 0, bool isFamily = false, double willingness = 0.0, TellTier tier = TellTier.Familiar,
            string? heldBackReason = null, double relationFactor = 1.0)
        {
            HeroId = heroId;
            Kind = kind;
            Relation = relation;
            Chance = chance;
            Status = status;
            OwnRelation = ownRelation;
            IsFamily = isFamily;
            Willingness = willingness;
            Tier = tier;
            HeldBackReason = heldBackReason;
            RelationFactor = relationFactor;
        }
    }
}
