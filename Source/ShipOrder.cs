using Verse;

namespace TraderShips
{
    /// <summary>
    /// 一笔"预订货物"订单。
    ///
    /// 只记 defName 而不是 Def 引用：订单要跨很多次读档活着，这中间玩家的模组
    /// 列表可能变过（把卖这件货的模组拆了）。存字符串，读档后能判断"这件货还在不在"，
    /// 找不到就退款收尾；存 Def 引用则会变成一条指向空 def 的死账。
    /// </summary>
    public class ShipOrder : IExposable
    {
        public string factionDefName;
        public string thingDefName;
        public int count;
        public int dueTick;
        public int depositPaid;

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionDefName, "factionDefName");
            Scribe_Values.Look(ref thingDefName, "thingDefName");
            Scribe_Values.Look(ref count, "count", 0);
            Scribe_Values.Look(ref dueTick, "dueTick", 0);
            Scribe_Values.Look(ref depositPaid, "depositPaid", 0);
        }
    }
}
