using System;
using System.Collections.Generic;
using VividWorld.Core.Channels;

namespace VividWorld.Core.Rumors
{
    /// <summary>累計每位聽的人的狀況：好感、意願、落在哪一層、沒講的原因。只在記憶體裡，不存檔。</summary>
    public sealed class TellTierTally
    {
        public const int RelationBinCount = 6;
        public const int WillingnessBinCount = 5;

        /// <summary>四類的順序：自家人、熟人（不含自家人）、不熟、交惡。</summary>
        public const int TierClassCount = 4;
        public const int TierFamily = 0;
        public const int TierFamiliar = 1;
        public const int TierUnfamiliar = 2;
        public const int TierHostile = 3;

        private readonly int[] _status = new int[Enum.GetValues(typeof(ContactStatus)).Length];
        private readonly int[] _relationInPerson = new int[RelationBinCount];
        private readonly int[] _relationRemote = new int[RelationBinCount];
        private readonly int[] _willingness = new int[WillingnessBinCount];
        private readonly int[] _ownInPerson = new int[RelationBinCount];
        private readonly int[] _ownRemote = new int[RelationBinCount];
        private readonly int[] _tierInPerson = new int[TierClassCount];
        private readonly int[] _tierRemote = new int[TierClassCount];
        // 沒講的筆數：[0] 交惡、[1] 不熟而且不是大事、[2] 醜事；各分見面與寫信。
        private readonly int[] _heldInPerson = new int[3];
        private readonly int[] _heldRemote = new int[3];

        public int Total { get; private set; }
        public int InPerson { get; private set; }
        public int Remote { get; private set; }
        public int ToldBig { get; private set; }
        public int ToldSmall { get; private set; }

        /// <summary>通道讀到的好感與自己的好感一正一負的筆數（0 不算）。</summary>
        public int SignDiffers { get; private set; }

        public int ToldTotal => ToldBig + ToldSmall;

        public int StatusCount(ContactStatus status) => _status[(int)status];

        /// <summary>好感分六段：&lt;= -20、-19..-1、0、1..9、10..29、&gt;= 30。見面的通道。</summary>
        public IReadOnlyList<int> RelationInPerson => _relationInPerson;

        /// <summary>同上，寫信的通道。</summary>
        public IReadOnlyList<int> RelationRemote => _relationRemote;

        /// <summary>意願分五段：&lt; 0、0..4.99、5..9.99、10..29.99、&gt;= 30。全部聯絡人。</summary>
        public IReadOnlyList<int> WillingnessBins => _willingness;

        /// <summary>自己的好感分六段，分段同 <see cref="RelationInPerson"/>。見面的通道。</summary>
        public IReadOnlyList<int> OwnRelationInPerson => _ownInPerson;

        /// <summary>同上，寫信的通道。</summary>
        public IReadOnlyList<int> OwnRelationRemote => _ownRemote;

        /// <summary>四類各幾筆（順序見 <see cref="TierFamily"/> 等常數）。見面的通道。自家人不算進熟人。</summary>
        public IReadOnlyList<int> TierInPerson => _tierInPerson;

        /// <summary>同上，寫信的通道。</summary>
        public IReadOnlyList<int> TierRemote => _tierRemote;

        public int HeldBackHostileInPerson => _heldInPerson[0];
        public int HeldBackHostileRemote => _heldRemote[0];
        public int HeldBackSmallNewsInPerson => _heldInPerson[1];
        public int HeldBackSmallNewsRemote => _heldRemote[1];
        public int HeldBackShamefulInPerson => _heldInPerson[2];
        public int HeldBackShamefulRemote => _heldRemote[2];

        public void Record(ContactObservation obs, int weightTen, int bigNewsLine)
        {
            Total++;
            bool inPerson = ChannelClass.IsInPerson(obs.Kind);
            if (inPerson) InPerson++; else Remote++;

            _status[(int)obs.Status]++;

            var relationBins = inPerson ? _relationInPerson : _relationRemote;
            relationBins[RelationBin(obs.Relation)]++;
            (inPerson ? _ownInPerson : _ownRemote)[RelationBin(obs.OwnRelation)]++;
            _willingness[WillingnessBin(obs.Willingness)]++;

            int tierClass = obs.IsFamily ? TierFamily
                : obs.Tier == TellTier.Hostile ? TierHostile
                : obs.Tier == TellTier.Unfamiliar ? TierUnfamiliar
                : TierFamiliar;
            (inPerson ? _tierInPerson : _tierRemote)[tierClass]++;

            if (obs.Status == ContactStatus.HeldBackHostile) (inPerson ? _heldInPerson : _heldRemote)[0]++;
            else if (obs.Status == ContactStatus.HeldBackSmallNews) (inPerson ? _heldInPerson : _heldRemote)[1]++;
            else if (obs.Status == ContactStatus.HeldBackShameful) (inPerson ? _heldInPerson : _heldRemote)[2]++;

            if ((obs.Relation > 0 && obs.OwnRelation < 0) || (obs.Relation < 0 && obs.OwnRelation > 0)) SignDiffers++;

            // 大事與小事只算新知道的人：重聽的人本來就知道，不影響這件事傳到多遠。
            if (obs.Status != ContactStatus.Told) return;
            if (weightTen >= bigNewsLine) ToldBig++; else ToldSmall++;
        }

        /// <summary>把另一份累計加進這一份（一天的併進整段遊玩的合計）。</summary>
        public void Merge(TellTierTally other)
        {
            if (other == null) return;
            Total += other.Total;
            InPerson += other.InPerson;
            Remote += other.Remote;
            ToldBig += other.ToldBig;
            ToldSmall += other.ToldSmall;
            SignDiffers += other.SignDiffers;
            AddInto(_status, other._status);
            AddInto(_relationInPerson, other._relationInPerson);
            AddInto(_relationRemote, other._relationRemote);
            AddInto(_willingness, other._willingness);
            AddInto(_ownInPerson, other._ownInPerson);
            AddInto(_ownRemote, other._ownRemote);
            AddInto(_tierInPerson, other._tierInPerson);
            AddInto(_tierRemote, other._tierRemote);
            AddInto(_heldInPerson, other._heldInPerson);
            AddInto(_heldRemote, other._heldRemote);
        }

        public void Reset()
        {
            Total = 0;
            InPerson = 0;
            Remote = 0;
            ToldBig = 0;
            ToldSmall = 0;
            SignDiffers = 0;
            Array.Clear(_status, 0, _status.Length);
            Array.Clear(_relationInPerson, 0, _relationInPerson.Length);
            Array.Clear(_relationRemote, 0, _relationRemote.Length);
            Array.Clear(_willingness, 0, _willingness.Length);
            Array.Clear(_ownInPerson, 0, _ownInPerson.Length);
            Array.Clear(_ownRemote, 0, _ownRemote.Length);
            Array.Clear(_tierInPerson, 0, _tierInPerson.Length);
            Array.Clear(_tierRemote, 0, _tierRemote.Length);
            Array.Clear(_heldInPerson, 0, _heldInPerson.Length);
            Array.Clear(_heldRemote, 0, _heldRemote.Length);
        }

        public static int RelationBin(int relation)
        {
            if (relation <= -20) return 0;
            if (relation < 0) return 1;
            if (relation == 0) return 2;
            if (relation < 10) return 3;
            if (relation < 30) return 4;
            return 5;
        }

        public static int WillingnessBin(double willingness)
        {
            if (willingness < 0.0) return 0;
            if (willingness < 5.0) return 1;
            if (willingness < 10.0) return 2;
            if (willingness < 30.0) return 3;
            return 4;
        }

        private static void AddInto(int[] target, int[] source)
        {
            for (int i = 0; i < target.Length; i++) target[i] += source[i];
        }
    }
}
