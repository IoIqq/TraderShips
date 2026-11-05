using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace TraderShips
{
    /// <summary>
    /// 「偷渡客请求收留」的信件式抉择。
    ///
    /// 用 ChoiceLetter 而不是 QuestScriptDef：后者要新建 QuestScriptDef + QuestNode 图，
    /// 还要处理派发、过期、失败分支，成本远高于收益；ChoiceLetter 本身就是原版给
    /// "访客求收留"这类事件的载体（参见 ChoiceLetter_AcceptVisitors）。
    ///
    /// 交互契约：
    ///   收留 → 编入殖民地
    ///   拒绝 → 立刻离场
    ///   超时未处理 → 同样离场（走 LetterWithTimeout.Timeout）
    /// </summary>
    public class ChoiceLetter_Stowaway : ChoiceLetter
    {
        public Pawn pawn;

        public override bool CanDismissWithRightClick => false;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                DiaOption accept = new DiaOption("TraderShipsStowawayAccept".Translate());
                accept.action = delegate
                {
                    if (pawn != null && !pawn.Dead && pawn.Spawned)
                    {
                        // 先脱离访客队伍，再编入殖民地，否则会残留一个访客 Lord。
                        Lord lord = pawn.GetLord();
                        if (lord != null) lord.RemovePawn(pawn);

                        // 无阵营的人直接编入殖民地。
                        pawn.SetFaction(Faction.OfPlayer);
                        if (pawn.guest != null) pawn.guest.SetGuestStatus(null, GuestStatus.Guest);
                    }

                    Find.LetterStack.RemoveLetter(this);
                };
                accept.resolveTree = true;
                yield return accept;

                DiaOption refuse = new DiaOption("TraderShipsStowawayRefuse".Translate());
                refuse.action = delegate
                {
                    SendAway();
                    Find.LetterStack.RemoveLetter(this);
                };
                refuse.resolveTree = true;
                yield return refuse;
            }
        }

        private void SendAway()
        {
            if (pawn == null || pawn.Dead) return;
            if (!pawn.Spawned) return;

            Lord lord = pawn.GetLord();
            if (lord != null) lord.RemovePawn(pawn);

            pawn.mindState.exitMapAfterTick = Find.TickManager.TicksGame + 300;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref pawn, "stowawayPawn");
        }
    }
}