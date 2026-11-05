using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Verse;
using Verse.AI;

namespace TraderShips
{
    public class LandedShip : TradeShip, ITrader
    {
        Map map;

        public LandedShip()
        {

        }
        public LandedShip(Map map, TraderKindDef def, Faction faction = null) : base(def, faction)
        {
            this.map = map;
            passingShipManager = map.passingShipManager;

            // PassingShip defaults ticksUntilDeparture to 40000 (PassingShip.cs:16) — about 16.7
            // hours. That is fine for an orbital trader you reach through the comms console, but
            // this ship sits on the ground and a pawn has to walk out to it, so the window is
            // uncomfortably short. Use the mod setting instead.
            TraderShipsSettings settings = TraderShips.settings;
            if (settings != null)
            {
                float days = settings.shipStayDays;
                if (days < 0.1f) days = 0.1f;
                ticksUntilDeparture = Mathf.RoundToInt(days * 60000f);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_References.Look(ref map, "map");
        }

        /// <summary>
        /// 「贸易伙伴」折扣。TradeShip 的同名属性不是 virtual，所以这里用 new ——
        /// 之所以能生效，是因为 LandedShip 重新列了 ITrader 接口，接口映射会指向本类成员。
        /// 正偏移对玩家有利（买价更低、卖价更高）。
        /// </summary>
        public new float TradePriceImprovementOffsetForPlayer
        {
            get
            {
                TraderShipsSettings settings = TraderShips.settings;
                return settings == null ? 0f : settings.PartnershipDiscount(Faction);
            }
        }

        /// <summary>一艘船只记一次，避免按出售件数刷贸易伙伴等级。</summary>
        private bool partnershipRecorded;

        private void NoteTrade()
        {
            if (partnershipRecorded) return;
            partnershipRecorded = true;

            TraderShipsSettings settings = TraderShips.settings;
            if (settings != null) settings.RegisterPartnershipVisit(Faction);
        }

        public override void PassingShipTick()
        {
            base.PassingShipTick();

            if (passingShipManager == null)
            {
                passingShipManager = map.passingShipManager;
            }
        }

        public override void Depart()
        {

        }

        public new void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
        {
            NoteTrade();

            Thing thing = toGive.SplitOff(countToGive);
            thing.PreTraded(TradeAction.PlayerBuys, playerNegotiator, this);

            if (!GenPlace.TryPlaceThing(thing, playerNegotiator.Position, Map, ThingPlaceMode.Near))
            {
                Log.Error(string.Concat(new object[]
                {
                        "Could not place bought thing ",
                        thing,
                        " at ",
                        playerNegotiator.Position
                }));
                thing.Destroy(DestroyMode.Vanish);
            }
        }


        public new IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator)
        {
            HashSet<Thing> alreadyListed = new HashSet<Thing>();

            // technically, this is pawn selling things to itself, but the code doesn't seem to care, and
            // using this method instead of just listing all things allows for compatibility with storage
            // mods that patch Pawn_TraderTracker.ColonyThingsWillingToBuy.
            foreach (Thing thing in new Pawn_TraderTracker(playerNegotiator).ColonyThingsWillingToBuy(playerNegotiator))
            {
                alreadyListed.Add(thing);

                yield return thing;
            }

            foreach (Pawn pawn in TradeUtility.AllSellableColonyPawns(playerNegotiator.Map).Where(x => !x.Downed && ReachableForTrade(playerNegotiator, x)))
            {
                if(!alreadyListed.Contains(pawn))
                    yield return pawn;
            }

            yield break;
        }

        private bool ReachableForTrade(Pawn pawn, Thing thing)
        {
            return pawn.Map == thing.Map && pawn.Map.reachability.CanReach(pawn.Position, thing, PathEndMode.Touch, TraverseMode.PassDoors, Danger.Some);
        }

    }
}