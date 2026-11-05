using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 贸易台。
    ///
    /// 交互走原版 CompUsable + JobDriver_UseItem（见 CompUseEffect_TradeDispatch）：
    /// 殖民者必须走到建筑旁、站定、使用一段时间，才弹出界面。
    /// 这样和通讯台的使用体验一致，而不是右键凭空开窗。
    ///
    /// 继承 Building 而不是 Building_CommsConsole：后者会把其他模组挂在通讯台上的
    /// comp 与补丁一并继承过来（浮窗里曾混进「EES 拦截敌方据点通信」等选项）。
    /// </summary>
    public class Building_TradeDispatch : Building
    {
        private CompPowerTrader powerComp;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
        }

        /// <summary>与通讯台等价的可用性判断：没电或停电事件期间不能用。</summary>
        public bool CanUseTradeNetwork
        {
            get
            {
                if (!Spawned) return false;
                if (Map?.gameConditionManager != null && Map.gameConditionManager.ElectricityDisabled(Map))
                {
                    return false;
                }
                if (powerComp != null) return powerComp.PowerOn;
                return true;
            }
        }
    }
}
