using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace TraderShips
{
    public class TraderShipsSettings : ModSettings
    {
        public bool disableOrbital = true;
        public bool colors = true;
        public bool requireCommsConsole = true;
        public bool enableQuests = true;
        public bool enableShipRequest = true;
        public float shipRequestSilverCost = 1000f;
        /// <summary>付费呼叫飞船是否要求盟友。关掉后中立派系也能包船。</summary>
        public bool shipRequestRequireAlly = true;
        /// <summary>两次随机飞船之间的最小间隔（天）。0 = 使用原版随机节奏，不额外限制。</summary>
        public float shipArrivalIntervalDays = 0f;
        /// <summary>飞船停留天数。原版默认 40000 ticks(≈16.7 小时) 是轨道商船的时长，</summary>
        /// <summary>飞船停在地面上、小人要走过去，那个时长经常不够，所以单独给一个更长的默认值。</summary>
        public float shipStayDays = 1f;
        /// <summary>地图上同时停靠的飞船上限。0 = 不限制。</summary>
        public int maxLandedShips = 2;
        /// <summary>把飞船部件烘焙成单张贴图，减少 draw call。出问题时可以关掉回退到逐部件绘制。</summary>
        public bool bakeShipGraphics = true;
        /// <summary>是否为各派系自动生成专属贸易飞船（卖该派系模组的物品）。</summary>
        public bool autoFactionShips = true;
        /// <summary>两次呼叫飞船之间的冷却（天）。0 = 无冷却。</summary>
        public float shipRequestCooldownDays = 1f;
        /// <summary>偷渡客出现概率（百分比）。0 = 关闭。</summary>
        public int stowawayChancePercent = 25;
        /// <summary>偷渡客停留天数，到期后离队离开地图。</summary>
        public float stowawayStayDays = 3f;
        /// <summary>按派系记录成功交易的次数，用于「贸易伙伴」折扣。</summary>
        public Dictionary<string, int> partnershipVisits = new Dictionary<string, int>();
        public IntRange shipColorSaturation = new IntRange(-50, 100);
        public IntRange shipColorValue = new IntRange(50, 100);

        float[] colorHues = new float[100];
        float[] colorSaturations = new float[100];
        float[] colorValues = new float[100];

        void RedoColors()
        {
            for (int i = 0; i < colorHues.Length; i++) colorHues[i] = Rand.Value;
            for (int i = 0; i < colorSaturations.Length; i++) colorSaturations[i] = Rand.Value;
            for (int i = 0; i < colorValues.Length; i++) colorValues[i] = Rand.Value;
        }

        override public void ExposeData()
        {
            Scribe_Values.Look(ref disableOrbital, "disableOrbital");
            Scribe_Values.Look(ref colors, "colors");
            Scribe_Values.Look(ref requireCommsConsole, "requireCommsConsole");
            Scribe_Values.Look(ref enableQuests, "enableQuests");
            Scribe_Values.Look(ref enableShipRequest, "enableShipRequest", true);
            Scribe_Values.Look(ref shipRequestSilverCost, "shipRequestSilverCost", 1000f);
            Scribe_Values.Look(ref shipRequestRequireAlly, "shipRequestRequireAlly", true);
            Scribe_Values.Look(ref shipArrivalIntervalDays, "shipArrivalIntervalDays", 0f);
            Scribe_Values.Look(ref shipStayDays, "shipStayDays", 1f);
            Scribe_Values.Look(ref maxLandedShips, "maxLandedShips", 2);
            Scribe_Values.Look(ref bakeShipGraphics, "bakeShipGraphics", true);
            Scribe_Values.Look(ref autoFactionShips, "autoFactionShips", true);
            Scribe_Values.Look(ref shipRequestCooldownDays, "shipRequestCooldownDays", 1f);
            if (shipRequestCooldownDays < 0f) shipRequestCooldownDays = 0f;
            if (shipRequestCooldownDays > 30f) shipRequestCooldownDays = 30f;

            Scribe_Values.Look(ref stowawayChancePercent, "stowawayChancePercent", 25);
            Scribe_Values.Look(ref stowawayStayDays, "stowawayStayDays", 3f);
            Scribe_Collections.Look(ref partnershipVisits, "partnershipVisits", LookMode.Value, LookMode.Value);
            if (partnershipVisits == null) partnershipVisits = new Dictionary<string, int>();

            // 校验并修正非法值，避免手改配置导致滑条越界
            if (stowawayChancePercent < 0) stowawayChancePercent = 0;
            if (stowawayChancePercent > 100) stowawayChancePercent = 100;
            if (stowawayStayDays < 0.5f) stowawayStayDays = 0.5f;
            if (stowawayStayDays > 20f) stowawayStayDays = 20f;
            Scribe_Values.Look(ref shipColorSaturation, "shipColorSaturation");
            Scribe_Values.Look(ref shipColorValue, "shipColorValue");
        }

        /// <summary>某个派系已成功交易过多少次（按 defName 记录，跨存档保留）。</summary>
        public int PartnershipVisits(Faction faction)
        {
            if (faction == null || faction.def == null) return 0;
            int count;
            return partnershipVisits.TryGetValue(faction.def.defName, out count) ? count : 0;
        }

        /// <summary>
        /// 「贸易伙伴」折扣：每次成功交易 +1%，上限 +15%。
        /// 正值对玩家有利（原版语义：买价降低、卖价提高）。
        /// </summary>
        public float PartnershipDiscount(Faction faction)
        {
            int visits = PartnershipVisits(faction);
            if (visits <= 0) return 0f;
            return Mathf.Min(visits, 15) * 0.01f;
        }

        /// <summary>记录一次成功交易。同一艘船只记一次，避免按物品件数刷等级。</summary>
        public void RegisterPartnershipVisit(Faction faction)
        {
            if (faction == null || faction.def == null) return;
            string key = faction.def.defName;
            int count;
            partnershipVisits.TryGetValue(key, out count);
            partnershipVisits[key] = count + 1;
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing_Standard = new Listing_Standard();
            listing_Standard.Begin(inRect);
            listing_Standard.CheckboxLabeled("TraderShipsDisableOrbitalName".Translate(), ref disableOrbital, "TraderShipsDisableOrbitalDesc".Translate());
            listing_Standard.CheckboxLabeled("TraderShipsRequireCommssConsoleName".Translate(), ref requireCommsConsole, "TraderShipsRequireCommssConsoleDesc".Translate());
            listing_Standard.CheckboxLabeled("TraderShipsEnableQuestsName".Translate(), ref enableQuests, "TraderShipsEnableQuestsDesc".Translate());

            listing_Standard.GapLine();
            listing_Standard.CheckboxLabeled("TraderShipsEnableShipRequestName".Translate(), ref enableShipRequest, "TraderShipsEnableShipRequestDesc".Translate());
            if (enableShipRequest)
            {
                listing_Standard.Label("TraderShipsShipRequestCostName".Translate(shipRequestSilverCost.ToString("F0")), -1, "TraderShipsShipRequestCostDesc".Translate());
                shipRequestSilverCost = (float)Math.Round(listing_Standard.Slider(shipRequestSilverCost, 0f, 5000f) / 100f) * 100f;

                listing_Standard.CheckboxLabeled("TraderShipsShipRequestAllyName".Translate(), ref shipRequestRequireAlly, "TraderShipsShipRequestAllyDesc".Translate());
            }

            listing_Standard.GapLine();
            listing_Standard.Label("TraderShipsArrivalIntervalName".Translate(shipArrivalIntervalDays.ToString("F0")), -1, "TraderShipsArrivalIntervalDesc".Translate());
            shipArrivalIntervalDays = (float)Math.Round(listing_Standard.Slider(shipArrivalIntervalDays, 0f, 30f));

            listing_Standard.GapLine();
            listing_Standard.Label("TraderShipsStayDaysName".Translate(shipStayDays.ToString("F2")), -1, "TraderShipsStayDaysDesc".Translate());
            shipStayDays = (float)Math.Round(listing_Standard.Slider(shipStayDays, 0.25f, 5f) * 4f) / 4f;

            listing_Standard.Label("TraderShipsMaxShipsName".Translate(Mathf.RoundToInt(maxLandedShips).ToString()), -1, "TraderShipsMaxShipsDesc".Translate());
            maxLandedShips = Mathf.RoundToInt(listing_Standard.Slider(maxLandedShips, 1f, 5f));

            listing_Standard.CheckboxLabeled("TraderShipsBakeGraphicsName".Translate(), ref bakeShipGraphics, "TraderShipsBakeGraphicsDesc".Translate());

            listing_Standard.GapLine();
            listing_Standard.CheckboxLabeled("TraderShipsAutoFactionShipsName".Translate(), ref autoFactionShips, "TraderShipsAutoFactionShipsDesc".Translate());

            listing_Standard.Label("TraderShipsRequestCooldownName".Translate(shipRequestCooldownDays.ToString("F1")), -1, "TraderShipsRequestCooldownDesc".Translate());
            shipRequestCooldownDays = Mathf.Round(listing_Standard.Slider(shipRequestCooldownDays, 0f, 30f) * 2f) / 2f;

            listing_Standard.Label("TraderShipsStowawayChanceName".Translate(stowawayChancePercent.ToString()), -1, "TraderShipsStowawayChanceDesc".Translate());
            stowawayChancePercent = Mathf.RoundToInt(listing_Standard.Slider(stowawayChancePercent, 0f, 100f));

            if (stowawayChancePercent > 0)
            {
                listing_Standard.Label("TraderShipsStowawayStayName".Translate(stowawayStayDays.ToString("F1")), -1, "TraderShipsStowawayStayDesc".Translate());
                stowawayStayDays = Mathf.Round(listing_Standard.Slider(stowawayStayDays, 0.5f, 20f) * 2f) / 2f;
            }

            listing_Standard.GapLine();
            listing_Standard.CheckboxLabeled("TraderShipsColorsName".Translate(), ref colors, "TraderShipsColorsDesc".Translate());
            if (colors)
            {

                listing_Standard.Label("TraderShipsSaturationName".Translate(), -1, "TraderShipsSaturationDesc".Translate());
                listing_Standard.IntRange(ref shipColorSaturation, -100, 200);
                listing_Standard.Label("TraderShipsValueName".Translate(), -1, "TraderShipsValueDesc".Translate());
                listing_Standard.IntRange(ref shipColorValue, -100, 200);

                if (colorValues[0] == 0) RedoColors();

                listing_Standard.Gap(24);

                Rect row = listing_Standard.GetRect(64).TopHalf();

                for (int i = 0; i < colorSaturations.Length; i++)
                {
                    Rect rect = new Rect(row.x + i * (row.height + 11), row.y, row.height, row.height);
                    if (rect.x + rect.width >= row.width) break;

                    GUI.color = Color.white;
                    GUI.DrawTexture(rect, BaseContent.WhiteTex);
                    GUI.color = Color.HSVToRGB(colorHues[i], Mathf.Clamp(shipColorSaturation.Lerped(colorSaturations[i]) * 0.01f, 0f, 1f), Mathf.Clamp(shipColorValue.Lerped(colorValues[i]) * 0.01f, 0f, 1f));
                    GUI.DrawTexture(rect.ContractedBy(1), BaseContent.WhiteTex);
                }
                GUI.color = Color.white;


                Rect rowButton = listing_Standard.GetRect(48);
                rowButton.width = 180;
                if (Widgets.ButtonText(rowButton, "TraderShipsRerollColors".Translate()))
                {
                    RedoColors();
                }
            }

            listing_Standard.End();
        }

    }
}
