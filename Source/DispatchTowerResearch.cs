using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 让贸易台和通讯台共用同一套研究前置。
    ///
    /// 为什么不在 XML 里写死 MicroelectronicsBasics：
    /// 其他模组可能把通讯台的前置换成别的科技，写死的值就会脱节，
    /// 出现"解锁了通讯台却没有贸易台"的情况。这里在启动时直接复制。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DispatchTowerResearch
    {
        static DispatchTowerResearch()
        {
            try
            {
                Sync();
            }
            catch (System.Exception e)
            {
                Log.Warning("[TraderShips] Could not sync dispatch tower research: " + e.Message);
            }
        }

        private static void Sync()
        {
            ThingDef tower = DefDatabase<ThingDef>.GetNamedSilentFail("TraderShipsDispatchTower");
            ThingDef console = DefDatabase<ThingDef>.GetNamedSilentFail("CommsConsole");
            if (tower == null || console == null) return;

            if (console.researchPrerequisites == null || console.researchPrerequisites.Count == 0)
            {
                // 通讯台没有前置（被模组去掉了），贸易台也不要。
                tower.researchPrerequisites = new List<ResearchProjectDef>();
                return;
            }

            tower.researchPrerequisites = new List<ResearchProjectDef>(console.researchPrerequisites);
        }
    }
}
