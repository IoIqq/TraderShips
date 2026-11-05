using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 处置"人船卖出去的人"：成交加入玩家之后，按船型决定身份。
    ///
    ///   人才船 → 普通殖民者（GuestStatus.Guest）
    ///   奴隶船 → 玩家的奴隶（GuestStatus.Slave）
    ///
    /// 为什么不在生成时直接设 GuestStatus：
    ///   Pawn_GuestTracker.SetGuestStatus 内部会访问 HostFaction。
    ///   生成阶段 pawn 还没有 host（host = null），调用直接抛
    ///   NullReferenceException，而异常发生在 StockGenerator 的迭代器里，
    ///   整个 ThingSetMaker 会失败 —— 表现就是"奴隶船/人才船里一件货都没有"。
    ///
    ///   所以这里改成"事后处理"：等 pawn 的阵营真的变成玩家，
    ///   再用 Faction.OfPlayer 作为 host 去设置，既安全也准确。
    ///
    /// RimWorld 会通过 GenTypes 自动实例化所有 GameComponent 子类，不需要 XML 注册。
    /// </summary>
    public class GameComponent_TalentPawns : GameComponent
    {
        /// <summary>人才船卖出的人：加入后转成普通殖民者。</summary>
        private List<int> talentPawnIds = new List<int>();

        /// <summary>奴隶船卖出的人：加入后转成奴隶。</summary>
        private List<int> slavePawnIds = new List<int>();

        /// <summary>
        /// id → 首次登记的 tick。
        ///
        /// 存在的意义是"清理死账"：pawn 可能在船上就被打死了、被别的模组清掉了，
        /// 或者玩家一直没买它。没有这个表的话，这些 id 会永远留在上面两个列表里，
        /// 每 60 tick 全图扫一遍找不存在的人，存档也跟着一直变大。
        /// </summary>
        private Dictionary<int, int> firstSeenTick = new Dictionary<int, int>();

        public GameComponent_TalentPawns(Game game)
        {
        }

        public static void MarkColonist(Pawn pawn)
        {
            Mark(pawn, false);
        }

        public static void MarkSlave(Pawn pawn)
        {
            Mark(pawn, true);
        }

        private static void Mark(Pawn pawn, bool slave)
        {
            if (pawn == null || Current.Game == null) return;

            GameComponent_TalentPawns comp = Current.Game.GetComponent<GameComponent_TalentPawns>();
            if (comp == null) return;

            List<int> list = slave ? comp.slavePawnIds : comp.talentPawnIds;
            if (list == null) return;

            int id = pawn.thingIDNumber;
            if (!list.Contains(id))
            {
                list.Add(id);
                if (comp.firstSeenTick != null && !comp.firstSeenTick.ContainsKey(id))
                {
                    comp.firstSeenTick[id] = Find.TickManager.TicksGame;
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref talentPawnIds, "talentPawnIds", LookMode.Value);
            Scribe_Collections.Look(ref slavePawnIds, "pawnShipSlaveIds", LookMode.Value);
            Scribe_Collections.Look(ref firstSeenTick, "pawnShipFirstSeen", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (talentPawnIds == null) talentPawnIds = new List<int>();
                if (slavePawnIds == null) slavePawnIds = new List<int>();
                if (firstSeenTick == null) firstSeenTick = new Dictionary<int, int>();
            }
        }

        public override void GameComponentTick()
        {
            if (talentPawnIds.Count == 0 && slavePawnIds.Count == 0) return;
            if (Find.TickManager.TicksGame % 60 != 0) return;

            Process(talentPawnIds, false);
            Process(slavePawnIds, true);
        }

        /// <summary>登记后多少 tick 还没成交就放弃（默认 5 天）。</summary>
        private const int GiveUpAfterTicks = 5 * 60000;

        private void Process(List<int> ids, bool asSlave)
        {
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                int id = ids[i];
                Pawn pawn = FindPawn(id);

                bool giveUp = false;

                if (pawn == null)
                {
                    // 找不到人：可能还在船的容器里（正常，继续等），
                    // 也可能已经死了/被别的模组清掉了（永远等不到）。
                    // 用登记时间兜底，避免列表无限增长。
                    giveUp = TimedOut(id);
                }
                else if (pawn.Dead || pawn.Destroyed)
                {
                    // 已经没了，不可能再成交。
                    giveUp = true;
                }
                else if (pawn.Faction != Faction.OfPlayer)
                {
                    // 还没成交 —— 继续等，但同样受超时保护。
                    giveUp = TimedOut(id);
                }
                else
                {
                    Apply(pawn, asSlave);
                    ids.RemoveAt(i);
                    firstSeenTick.Remove(id);
                    continue;
                }

                if (giveUp)
                {
                    ids.RemoveAt(i);
                    firstSeenTick.Remove(id);
                }
            }
        }

        private bool TimedOut(int id)
        {
            if (firstSeenTick == null) return true;

            int seen;
            if (!firstSeenTick.TryGetValue(id, out seen)) return false;

            return Find.TickManager.TicksGame - seen > GiveUpAfterTicks;
        }

        private static void Apply(Pawn pawn, bool asSlave)
        {
            if (pawn.guest == null) return;

            // 这里 host 一定有值（Faction.OfPlayer），不会再触发生成阶段那个空引用。
            pawn.guest.SetGuestStatus(Faction.OfPlayer, asSlave ? GuestStatus.Slave : GuestStatus.Guest);
        }

        private static Pawn FindPawn(int thingId)
        {
            foreach (Map map in Find.Maps)
            {
                IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn p = all[i];
                    if (p != null && p.thingIDNumber == thingId) return p;
                }
            }

            foreach (Pawn p in Find.WorldPawns.AllPawnsAliveOrDead)
            {
                if (p != null && p.thingIDNumber == thingId) return p;
            }

            return null;
        }
    }
}