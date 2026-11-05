using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 按具体 ThingDef 供货的生成器。
    ///
    /// 原版 StockGenerator_SingleDef 的 thingDef 是 private，运行时无法赋值，
    /// 所以这里做一个公开字段的等价版本。
    ///
    /// 三个关键点：
    ///   TradeabilityFor —— 交易界面靠它判断"这件东西卖不卖"，
    ///                      不覆写会出现"货单有货、交易窗却是空的"。
    ///   singleStack     —— 同一件物品只出一个堆叠，避免占满货位。
    ///   固定材质        —— 原版 TryMakeForStock 会给每件随机挑材质，
    ///                      一件扎甲会炸成"钢铁/陶瓷/哥布林合金扎甲"十几条商品。
    ///                      这里改成固定用 DefaultStuffFor，一种商品只有一条。
    /// </summary>
    public class StockGenerator_ThingDefs : StockGenerator
    {
        public ThingDef thingDef;

        /// <summary>固定材质：同一种商品只产出一条，不随机材质。</summary>
        public bool fixedStuff = true;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            if (thingDef == null) yield break;

            int count = countRange.RandomInRange;
            if (count <= 0) yield break;

            if (fixedStuff)
            {
                Thing made = MakeOne(count);
                if (made != null) yield return made;
                yield break;
            }

            foreach (Thing item in StockGeneratorUtility.TryMakeForStock(thingDef, count, faction))
            {
                yield return item;
            }
        }

        private Thing MakeOne(int count)
        {
            try
            {
                ThingDef stuff = null;
                if (thingDef.MadeFromStuff)
                {
                    // 固定材质：同一件物品每次都产同一种，不会变成十几个变体。
                    stuff = GenStuff.DefaultStuffFor(thingDef);
                    if (stuff == null)
                    {
                        GenStuff.TryRandomStuffFor(thingDef, out stuff, TechLevel.Archotech, null);
                    }
                }

                Thing made = ThingMaker.MakeThing(thingDef, stuff);
                if (made == null) return null;

                // 不设品质：品质同样会制造"优秀/极佳/大师"这类重复条目。
                made.stackCount = count;
                return made;
            }
            catch (System.Exception e)
            {
                Log.WarningOnce("[TraderShips] Could not stock " + thingDef.defName + ": " + e.Message,
                    thingDef.shortHash);
                return null;
            }
        }

        public override bool HandlesThingDef(ThingDef thingDef)
        {
            return this.thingDef != null && thingDef == this.thingDef;
        }

        public override Tradeability TradeabilityFor(ThingDef def)
        {
            if (def == null) return Tradeability.None;
            return HandlesThingDef(def) ? Tradeability.All : Tradeability.None;
        }
    }
}
