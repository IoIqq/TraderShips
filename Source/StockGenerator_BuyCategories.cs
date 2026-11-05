using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 只负责"这艘船收什么货"的生成器 —— 不供货，只声明购买范围。
    ///
    /// 为什么需要它：
    ///   TraderKindDef.WillTrade(ThingDef) 的实现就是遍历 stockGenerators、
    ///   看谁的 HandlesThingDef 返回 true（IL 里直接 callvirt StockGenerator::HandlesThingDef）。
    ///   我们原来每条船只有一堆 StockGenerator_ThingDefs，每个只认自己那一个 def，
    ///   于是交易窗口的"此贸易者会购买的物品"里只剩白银和生物 —— 玩家看到的
    ///   就是"每条船的购买项都只有一两个"。
    ///
    ///   加上这个生成器后，船按主题（武器船收武器、服装船收衣物……）用原版
    ///   ThingCategoryDef / tradeTag 大范围收货，列表自然就长了。
    ///
    /// 它不产出任何物品：GenerateThings 直接空返回，纯粹是"收购声明"。
    /// </summary>
    public class StockGenerator_BuyCategories : StockGenerator
    {
        /// <summary>原版分类名。匹配时会沿 parent 链上溯，写父类即可覆盖全部子类。</summary>
        public List<string> categoryDefNames = new List<string>();

        /// <summary>原版 tradeTag。分类没覆盖到的模组物品常常只带 tag。</summary>
        public List<string> tradeTags = new List<string>();

        /// <summary>true = 什么都收（杂货船）。</summary>
        public bool buyEverything;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            yield break;
        }

        public override bool HandlesThingDef(ThingDef thingDef)
        {
            return Matches(thingDef);
        }

        public override Tradeability TradeabilityFor(ThingDef def)
        {
            return Matches(def) ? Tradeability.All : Tradeability.None;
        }

        private bool Matches(ThingDef def)
        {
            if (def == null) return false;
            if (def.tradeability == Tradeability.None) return false;
            if (def == ThingDefOf.Silver) return true;

            if (buyEverything)
            {
                // 杂货船：只要能被交易，全都收。
                // 唯一的例外是"不可搬运且不可最小化"的东西（矿脉、地图固定物），
                // 那些本来也进不了交易窗口。
                return def.stackLimit > 0 || def.Minifiable;
            }

            if (MatchesCategory(def)) return true;
            return MatchesTag(def);
        }

        private bool MatchesCategory(ThingDef def)
        {
            if (categoryDefNames == null || categoryDefNames.Count == 0) return false;
            if (def.thingCategories == null) return false;

            foreach (ThingCategoryDef cat in def.thingCategories)
            {
                for (ThingCategoryDef c = cat; c != null; c = c.parent)
                {
                    string n = c.defName;
                    if (n == null) continue;

                    for (int i = 0; i < categoryDefNames.Count; i++)
                    {
                        if (string.Equals(n, categoryDefNames[i], StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool MatchesTag(ThingDef def)
        {
            if (tradeTags == null || tradeTags.Count == 0) return false;
            if (def.tradeTags == null) return false;

            foreach (string tag in def.tradeTags)
            {
                if (tag == null) continue;

                for (int i = 0; i < tradeTags.Count; i++)
                {
                    if (string.Equals(tag, tradeTags[i], StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
