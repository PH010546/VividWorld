using System.Collections.Generic;

namespace VividWorld.Core.Util
{
    /// <summary>無狀態：種子進、值出。同一個種子永遠得到同一個答案。
    /// 這是「防讀檔重骰」的地基——洩漏判定若用一般亂數，玩家讀檔重來就能洗掉重骰。</summary>
    public interface IDeterministicRng
    {
        double NextDouble(long seed);                                   // 均勻 [0,1)
        bool Chance(double p, long seed);
        int Pick(int count, long seed);                                 // 均勻 [0,count)
        int PickWeighted(IReadOnlyList<double> weights, long seed);     // 依權重比例挑索引
    }
}
