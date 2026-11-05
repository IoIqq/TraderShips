using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 给派系挂"能派遣哪些贸易飞船"的标签。
    ///
    /// 为什么需要它：原版 Core 里没有任何 FactionDef 声明 orbitalTraderKinds，而四个
    /// Orbital_* 船型在装了 Odyssey 后会全部绑定到 TradersGuild 派系（见
    /// TraderKinds_Orbital_Misc.xml 的 MayRequire）。结果就是除了 TradersGuild，
    /// 其他派系一个可派遣船型都拿不到。
    ///
    /// 用法（其他模组也可以照这个写 XML 补丁）：
    ///
    ///   &lt;Operation Class="PatchOperationAddModExtension"&gt;
    ///     &lt;xpath&gt;Defs/FactionDef[defName="Empire"]&lt;/xpath&gt;
    ///     &lt;value Class="TraderShips.FactionShipExtension"&gt;
    ///       &lt;shipKinds&gt;
    ///         &lt;li&gt;Orbital_Empire&lt;/li&gt;
    ///       &lt;/shipKinds&gt;
    ///     &lt;/value&gt;
    ///   &lt;/Operation&gt;
    ///
    /// 不写这个扩展的派系会走科技等级兜底（Industrial 及以上给通用轨道商）。
    /// </summary>
    public class FactionShipExtension : DefModExtension
    {
        /// <summary>该派系可以派遣的船型。留空表示走兜底规则。</summary>
        public List<TraderKindDef> shipKinds;

        /// <summary>明确禁止该派系派遣贸易飞船（例如部落）。</summary>
        public bool noTradeShips;
    }
}
