using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 交易船的货品主题。
    ///
    /// 每个派系会按自己的物品库能凑出的主题各生成一艘船 —— 医疗船卖药、武器船卖枪，
    /// 而不是像原来那样一个派系只有一艘"什么都卖"的杂货船。
    /// </summary>
    public enum ShipTheme
    {
        Medical,
        Weapon,
        Apparel,
        /// <summary>人才：卖技能被拉满的人（贵）。</summary>
        Talent,
        /// <summary>奴隶：卖普通奴工（多、便宜）。</summary>
        Slave,
        /// <summary>杂货 / 特产：不属于上面任何一类的物品都归这里。</summary>
        General,
        /// <summary>食品：成品餐与饮品（生食不进船）。</summary>
        Food,
        /// <summary>原材料：钢铁、布料、皮毛、石块。</summary>
        Resource,
        /// <summary>工业品：零件、燃料、弹药、化工。</summary>
        Industrial,
        /// <summary>家具与建材：能搬的建筑、家具、装饰。</summary>
        Furniture,
        /// <summary>奇异物品：神器、遗物、超科技、义体。</summary>
        Exotic,
    }

    /// <summary>
    /// 启动时给「有自己物品的派系」自动生成主题化的专属贸易飞船。
    ///
    /// 思路：Def.modContentPack 能直接告诉我们某个派系和哪些 ThingDef 来自同一个模组，
    /// 不需要扫描文件系统。于是：
    ///   派系 → 同模组的可交易物品 → 按主题切分 → 每个主题一个 TraderKindDef
    ///
    /// 生成的 defName 是确定性的（TraderShipsAuto_&lt;派系&gt;_&lt;主题&gt;），
    /// 所以每次启动生成的 defName 一致，存档里的引用能正常解析。
    ///
    /// 已经用 XML 打过标签的派系（帝国 / 海盗 / 部落）不会被覆盖。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class FactionShipGenerator
    {
        /// <summary>
        /// 每个主题的船上最多放多少种物品。
        ///
        /// 演进：8 → 14 → 24。
        ///
        /// 曾经给每个主题额外生成 2 条变体船（_Weapon1 / _Weapon2）来增加"可选船型"，
        /// 结果列表里一堆几乎同名的船，每条只有 14 种货 —— 玩家反馈"艘船上的内容会很少"。
        /// 现在改成：每个主题只留一条船，但货位翻倍到 24，
        /// 把原来三条变体的货合并进同一条。列表更清爽，单船货更丰富。
        /// </summary>
        private const int MaxItemsPerTheme = 40;

        /// <summary>
        /// 农业船（食品）和杂货船的货位翻倍。
        ///
        /// 这两类船本来就是"什么都带一点"的性质，24 格摊开之后
        /// 每样只有寥寥几种，看起来还是很空。翻倍后能真正卖出"集市"的感觉。
        /// </summary>
        private static int SlotsFor(ShipTheme theme)
        {
            switch (theme)
            {
                case ShipTheme.Food:
                case ShipTheme.General:
                    return MaxItemsPerTheme * 2;   // 80
                default:
                    return MaxItemsPerTheme;
            }
        }

        /// <summary>每种物品的供货量区间。</summary>
        private static readonly IntRange StockRange = new IntRange(8, 35);

        /// <summary>
        /// 全局"可交易物品池"，整次启动只算一遍。
        ///
        /// IsTradeableGood 只看 ThingDef 自身的静态数据，跟派系无关；以前每个派系
        /// 都重扫一遍 DefDatabase&lt;ThingDef&gt;，40+ 个派系就是 40+ 次全表过滤，
        /// 这是加载期最大的一笔开销。
        ///
        /// 缓存而不是每次现扫，还有第二个理由：Generate() 是 public 的，可能被调两次，
        /// 池子必须两次一模一样，生成的货单才能一模一样。
        /// def 加载完成后 ThingDef 集合不再变化，所以这份缓存不会过期。
        /// </summary>
        private static List<ThingDef> tradeableGoods;

        /// <summary>
        /// 物品 → 主题的记忆化表，key 用 ThingDef.shortHash。
        ///
        /// 同一件物品会被多个派系重复分类，杂货补位那一步更是每件物品都要再判一次；
        /// 而分类要沿 thingCategories 一路往上匹配关键字。缓存后热点只剩一次字典查询。
        /// shortHash 在 DefDatabase 里唯一且同一进程内稳定，正好当 key。
        /// </summary>
        private static readonly Dictionary<int, ShipTheme> themeCache = new Dictionary<int, ShipTheme>();

        // 分类关键字表。以前是 params string[]，每次调用都现分配一个数组 ——
        // 生成期这里是 每件物品 × 每派系 × 7 个判定，光 GC 就是可观的量。
        // 数组只读，挪到字段上之后一次分配到底。
        private static readonly string[] MedicalNeedles = new string[] { "Medicine", "Drug", "Medical" };
        private static readonly string[] FoodNeedles = new string[] { "FoodMeals", "Meals", "Beverage" };
        private static readonly string[] RawFoodNeedles = new string[] { "PlantFoodRaw", "FoodRaw", "MeatRaw", "AnimalProductRaw" };
        private static readonly string[] ResourceNeedles = new string[] { "ResourcesRaw", "Textiles", "Leathers", "StoneBlocks", "WoodLogs" };
        private static readonly string[] IndustrialNeedles = new string[] { "ResourcesIndustrial", "Manufactured", "Components",
            "Chemicals", "Fuel", "MortarShells" };
        private static readonly string[] FurnitureNeedles = new string[] { "BuildingsFurniture", "Furniture", "Buildings",
            "Structure", "Power", "Production", "Art" };
        private static readonly string[] ExoticNeedles = new string[] { "Artifacts", "Exotic", "Relic", "Ultra", "BodyPartsArchotech" };

        static FactionShipGenerator()
        {
            try
            {
                Generate();
            }
            catch (Exception e)
            {
                Log.Error("[TraderShips] Auto faction ship generation failed: " + e);
            }
        }

        public static void Generate()
        {
            TraderShipsSettings settings = TraderShips.settings;
            if (settings != null && !settings.autoFactionShips) return;

            // 全流程唯一需要全扫 DefDatabase<ThingDef> 的地方，先算完再进派系循环。
            EnsureTradeableGoods();

            int factions = 0;
            int kindsCreated = 0;
            List<string> usedFallback = new List<string>();
            List<string> skipped = new List<string>();

            foreach (FactionDef factionDef in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                // 物品池算一次给两个地方用：判"这套船齐没齐"要知道该派系应该有几个主题，
                // 那就要先知道它有哪些物品。算两遍等于白搭一次排序。
                List<ThingDef> items = factionDef == null ? null : CollectItems(factionDef);

                string reason = SkipReason(factionDef, items);
                if (reason != null)
                {
                    if (factionDef != null && factionDef.humanlikeFaction && !factionDef.isPlayer)
                    {
                        skipped.Add(factionDef.defName + ":" + reason);
                    }
                    continue;
                }

                if (items.Count == 0)
                {
                    // 该派系的模组里没有可交易物品（纯派系模组，物品来自别处）。
                    // 退回科技阶段的默认船型，至少保证"能叫到船"。
                    List<TraderKindDef> fallback = CommsShipRequest.DefaultKindsForTechLevel(factionDef.techLevel);
                    if (fallback.Count == 0)
                    {
                        skipped.Add(factionDef.defName + ":noItemsAndLowTech");
                        continue;
                    }

                    AttachTag(factionDef, fallback);
                    usedFallback.Add(factionDef.defName);
                    continue;
                }

                List<TraderKindDef> kinds = BuildThemedKinds(factionDef, items);
                if (kinds.Count == 0) continue;

                AttachTag(factionDef, kinds);
                factions++;
                kindsCreated += kinds.Count;
            }

            Log.Message("[TraderShips] Generated " + kindsCreated + " themed trade ships for "
                + factions + " factions.");

            if (usedFallback.Count > 0)
            {
                Log.Message("[TraderShips] Fallback (no same-mod items): " + string.Join(", ", usedFallback));
            }

            if (skipped.Count > 0)
            {
                Log.Message("[TraderShips] Skipped factions: " + string.Join(", ", skipped));
            }
        }

        /// <summary>返回 null 表示"应该自动生成"，否则返回放弃的原因（用于日志）。</summary>
        private static string SkipReason(FactionDef factionDef, List<ThingDef> items)
        {
            if (factionDef == null) return "null";
            if (factionDef.isPlayer) return "player";
            if (!factionDef.humanlikeFaction) return "nonHumanlike";
            if (factionDef.hidden) return "hidden";

            // 已经用 XML 明确配置过的派系，尊重配置，不自动覆盖。
            if (factionDef.GetModExtension<FactionShipExtension>() != null) return "alreadyTagged";

            // 只有真正的石器时代（techLevel 未定义 / 动物级）才不给船。
            if (factionDef.techLevel < TechLevel.Neolithic) return "techLevel=" + factionDef.techLevel;

            // 只有"该有的每一条船都在"才算生成过（防止重复生成）。
            //
            // 以前只查 Medical 一个主题，于是上次生成到一半抛异常留下的半套派系
            // 也有 Medical，下次启动被判成已生成，缺掉的主题永远补不回来。
            // 现在只要缺一条就返回 null，让生成流程把缺的补齐。
            if (AllExpectedKindsExist(factionDef, items))
            {
                return "alreadyGenerated";
            }

            return null;
        }

        public static string DefNameFor(FactionDef factionDef, ShipTheme theme)
        {
            return "TraderShipsAuto_" + factionDef.defName + "_" + theme;
        }

        /// <summary>
        /// 懒加载全局物品池，只算一次。
        ///
        /// 放在这里而不是静态字段初始化里，是因为静态构造器本身就调 Generate()，
        /// 那时 DefDatabase 里已经装好了全部 ThingDef；而 Generate() 若被外部再调一次，
        /// 这里也会直接复用同一份结果，不会因为 def 集合的细微差别算出不同的货单。
        /// </summary>
        private static void EnsureTradeableGoods()
        {
            if (tradeableGoods != null) return;

            List<ThingDef> pool = new List<ThingDef>();

            foreach (ThingDef thing in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!IsTradeableGood(thing)) continue;

                pool.Add(thing);
            }

            tradeableGoods = pool;
        }

        /// <summary>
        /// 收集"能在这艘船上当商品"的 ThingDef。
        ///
        /// 两级来源：
        ///   1) 与派系同一个模组的可交易成品（派系专属风味）
        ///   2) 原版 + 所有模组的通用商品（补足货位，避免"只有 2 条船、每条 8 件货"）
        ///
        /// 第 2 级是必须的：一个只有 37 件物品的中世纪模组，靠自己的物品池
        /// 连一条完整的船都凑不满，玩家看到的就是"货太少了"。
        ///
        /// 两级其实同一份池子：第 1 级只是把它按 modContentPack 排到前面。
        /// 池子本体由 EnsureTradeableGoods 算一次并缓存，这里不再重做过滤。
        /// </summary>
        private static List<ThingDef> CollectItems(FactionDef factionDef)
        {
            EnsureTradeableGoods();

            ModContentPack pack = factionDef.modContentPack;

            // 复制一份缓存池再按模组分组：池子的内容顺序和旧实现完全相同
            // （都是 AllDefsListForReading 的顺序），而 List.Sort 不稳定，
            // 只有输入顺序一致时排出来的相对次序才和以前一字不差。
            List<ThingDef> pool = new List<ThingDef>(tradeableGoods);

            // 该派系自己模组的物品排前面：稳定哈希排序后它们会优先占住预留位。
            // 这里不排序，只保证同一份池子每次结果一致（见 OrderBy(StableHash)）。
            if (pack != null)
            {
                pool.Sort(delegate(ThingDef a, ThingDef b)
                {
                    bool aOwn = a.modContentPack == pack;
                    bool bOwn = b.modContentPack == pack;
                    if (aOwn == bOwn) return 0;
                    return aOwn ? -1 : 1;
                });
            }

            return pool;
        }

        /// <summary>能不能当商品：搬运、定价、可交易、有图、不是原材料形态。</summary>
        private static bool IsTradeableGood(ThingDef thing)
        {
            if (thing == null) return false;

            if (thing.category != ThingCategory.Item
                && thing.category != ThingCategory.Building
                && thing.category != ThingCategory.Pawn)
            {
                return false;
            }

            if (thing.IsStuff) return false;
            if (thing.BaseMarketValue <= 0f) return false;
            if (thing.tradeability == Tradeability.None) return false;
            if (thing.graphicData == null || thing.graphicData.texPath.NullOrEmpty()) return false;
            if (thing.stackLimit <= 0 && !thing.Minifiable) return false;

            // 派系自己都不卖的东西（例如脚本生成的自定义占位）不放进货池。
            if (thing.modContentPack == null) return false;

            // 会自己 Tick 的建筑一律不卖。
            //
            // 教训：人船的随机杂货里抽到过 VFEMedieval 的蜂房（Minifiable 建筑），
            // 飞船被击毁后它落到地上开始 Tick，那个模组的
            // Building_Apiary.FlowerAround() 在"从贸易库存直接落地"这种情况下
            // 会空引用，玩家看到的是每 tick 刷满屏红字。
            //
            // 判断依据是"这个 thingClass 自己声明了 Tick 系列方法"——
            // 原版家具（床、桌子、雕塑）都不声明，只有带行为的建筑才会，
            // 所以普通家具仍然能卖，只有真正有副作用的被挡掉。
            if (thing.category == ThingCategory.Building && HasOwnTick(thing))
            {
                if (Prefs.DevMode)
                {
                    Log.Message("[TraderShips] skipped ticking building: " + thing.defName
                        + " (" + thing.thingClass?.FullName + ")");
                }

                return false;
            }

            return true;
        }

        /// <summary>临时缓存：一个 thingClass 是否自己声明了 Tick / TickRare / TickLong。</summary>
        private static readonly Dictionary<Type, bool> ownTickCache = new Dictionary<Type, bool>();

        /// <summary>
        /// 该类是否自己声明了无参的某个 Tick 方法。
        ///
        /// 用 GetMethod(name, flags, null, Type.EmptyTypes, null) 而不是
        /// GetMethod(name, flags)：后者在同名重载时抛 AmbiguousMatchException。
        /// </summary>
        private static bool DeclaresNoArgTick(Type type, string name, BindingFlags flags)
        {
            try
            {
                return type.GetMethod(name, flags, null, Type.EmptyTypes, null) != null;
            }
            catch (AmbiguousMatchException)
            {
                // 理论上不会走到（已指定参数类型），但兜一下总比让整个生成流程中止好。
                return true;
            }
        }

        private static bool HasOwnTick(ThingDef thing)
        {
            Type type = thing.thingClass;
            if (type == null) return false;

            bool cached;
            if (ownTickCache.TryGetValue(type, out cached)) return cached;

            const BindingFlags flags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;

            // DeclaredOnly 是关键：继承自 Building 的默认空实现不算，
            // 只有模组自己写了行为才会命中。
            //
            // 必须用带参数类型的重载解析：一个类同时有 Tick() 和 Tick(int) 时，
            // GetMethod("Tick", flags) 会抛 AmbiguousMatchException，
            // 而它会被 Generate 的 catch 吞掉 —— 结果整个主题船生成中止、
            // 所有派系退化成原版轨道船型。这种静默退化极难排查。
            cached = DeclaresNoArgTick(type, "Tick", flags)
                || DeclaresNoArgTick(type, "TickRare", flags)
                || DeclaresNoArgTick(type, "TickLong", flags);

            // 建筑自己没写 Tick，但挂的 ThingComp 可能写了：
            // ThingWithComps.Tick 会遍历 comps 调 CompTick，蜂房那类崩溃同样会复现。
            if (!cached && thing.comps != null)
            {
                foreach (CompProperties comp in thing.comps)
                {
                    if (comp == null || comp.compClass == null) continue;

                    if (DeclaresNoArgTick(comp.compClass, "CompTick", flags)
                        || DeclaresNoArgTick(comp.compClass, "CompTickRare", flags)
                        || DeclaresNoArgTick(comp.compClass, "CompTickLong", flags))
                    {
                        cached = true;
                        break;
                    }
                }
            }

            ownTickCache[type] = cached;
            return cached;
        }

        /// <summary>
        /// 物品归哪个主题（决定它属于哪条船的主货）。
        ///
        /// 顺序有意义：先判武器/服装（最具体的虚拟属性），再判大类分类，
        /// 最后落到杂货。每件物品只认一个主题。
        /// </summary>
        public static ShipTheme ThemeOf(ThingDef thing)
        {
            // 记忆化：同一件物品会被多个派系反复分类，杂货补位那一步还要再判一次。
            // 分类本身要沿 thingCategories 上溯匹配关键字，是整个生成流程的热点。
            ShipTheme cached;
            if (themeCache.TryGetValue(thing.shortHash, out cached)) return cached;

            ShipTheme theme = ComputeTheme(thing);
            themeCache[thing.shortHash] = theme;
            return theme;
        }

        /// <summary>真正做分类的那一层，每件物品只会被算一次，之后走 ThemeOf 的缓存。</summary>
        private static ShipTheme ComputeTheme(ThingDef thing)
        {
            // 生食/农产品不是任何主题的主货 —— 归到原材料，且不会成为 Food 船的核心。
            if (IsRawFood(thing)) return ShipTheme.Resource;

            if (thing.IsWeapon) return ShipTheme.Weapon;
            if (thing.IsApparel) return ShipTheme.Apparel;
            if (IsMedical(thing)) return ShipTheme.Medical;
            if (IsFood(thing)) return ShipTheme.Food;
            if (IsIndustrial(thing)) return ShipTheme.Industrial;
            if (IsExotic(thing)) return ShipTheme.Exotic;
            if (IsFurniture(thing)) return ShipTheme.Furniture;
            if (IsResource(thing)) return ShipTheme.Resource;

            return ShipTheme.General;
        }

        /// <summary>
        /// 药品 / 医疗物资。
        ///
        /// 顺序很重要：先排除服装。
        /// 有些模组的衣物带"医疗/防护"标签（甚至 IsDrug 为真），
        /// 会导致鞋子、护甲之类被归进医疗船，而医疗船根本不是卖衣服的。
        /// </summary>
        private static bool IsMedical(ThingDef thing)
        {
            if (thing.IsApparel) return false;
            if (thing.IsWeapon) return false;

            if (thing.IsMedicine) return true;
            if (thing.IsDrug) return true;

            return CatMatch(thing, MedicalNeedles);
        }

        /// <summary>成品餐 / 半成品食物（不含生食）。</summary>
        private static bool IsFood(ThingDef thing)
        {
            if (IsRawFood(thing)) return false;
            if (thing.IsNutritionGivingIngestible) return true;
            return CatMatch(thing, FoodNeedles);
        }

        /// <summary>生食 / 农产品。</summary>
        private static bool IsRawFood(ThingDef thing)
        {
            return CatMatch(thing, RawFoodNeedles);
        }

        /// <summary>原材料：金属、矿物、布料、皮毛、石块。</summary>
        private static bool IsResource(ThingDef thing)
        {
            return CatMatch(thing, ResourceNeedles);
        }

        /// <summary>工业品：零件、燃料、弹药、化工。</summary>
        private static bool IsIndustrial(ThingDef thing)
        {
            return CatMatch(thing, IndustrialNeedles);
        }

        /// <summary>能搬的家具与建材。</summary>
        private static bool IsFurniture(ThingDef thing)
        {
            if (thing.category != ThingCategory.Building) return false;
            return CatMatch(thing, FurnitureNeedles);
        }

        /// <summary>
        /// 奇异物品：神器、遗物、超科技、义体。
        ///
        /// 用市场价兜底比分类更可靠 —— 这些模组很少把它们放进统一分类。
        /// </summary>
        private static bool IsExotic(ThingDef thing)
        {
            if (CatMatch(thing, ExoticNeedles)) return true;

            // 单件市场价超过 800 且不是装备的，当奇异物品。
            return !thing.IsApparel && !thing.IsWeapon && thing.BaseMarketValue >= 800f;
        }

        /// <summary>
        /// 沿分类树向上匹配关键字。
        ///
        /// 原版分类（ResourcesRaw → Textiles → Leathers）是链式的，
        /// 只比对自身会漏掉大量子类，所以这里要一路走到根。
        /// </summary>
        private static bool CatMatch(ThingDef thing, string[] needles)
        {
            if (thing.thingCategories == null) return false;

            foreach (ThingCategoryDef cat in thing.thingCategories)
            {
                for (ThingCategoryDef c = cat; c != null; c = c.parent)
                {
                    string n = c.defName;
                    if (n == null) continue;
                    for (int i = 0; i < needles.Length; i++)
                    {
                        if (n.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 主题预留的货位比例。
        ///
        /// 不设成 1.0 是刻意的：整船清一色会让货单看着单调，
        /// 也浪费了这个派系其它特产。留一部分随机位既保主题辨识度，又有惊喜。
        /// </summary>
        private static float ReservedRatioFor(ShipTheme theme)
        {
            switch (theme)
            {
                case ShipTheme.General: return 1.0f;
                case ShipTheme.Resource: return 0.7f;
                case ShipTheme.Furniture: return 0.7f;
                case ShipTheme.Exotic: return 0.7f;
                case ShipTheme.Food: return 0.5f;
                default: return 0.6f;
            }
        }

        /// <summary>先按主题把物品分组，后面"该有哪些船"的判断全基于这份分组。</summary>
        private static Dictionary<ShipTheme, List<ThingDef>> Bucketize(List<ThingDef> items)
        {
            Dictionary<ShipTheme, List<ThingDef>> buckets = new Dictionary<ShipTheme, List<ThingDef>>();
            foreach (ShipTheme t in Enum.GetValues(typeof(ShipTheme)))
            {
                buckets[t] = new List<ThingDef>();
            }

            foreach (ThingDef thing in items)
            {
                buckets[ThemeOf(thing)].Add(thing);
            }

            return buckets;
        }

        /// <summary>
        /// 该派系"应该有"的每一条船是不是都已经在 DefDatabase 里。
        ///
        /// 门槛必须和 BuildThemedKinds 严格一致，只是不真的构造 def：
        /// 写宽了会"永远觉得缺一条"，每次启动都白跑一遍完整生成。
        /// </summary>
        private static bool AllExpectedKindsExist(FactionDef factionDef, List<ThingDef> items)
        {
            // 没有同模组物品的派系走科技等级兜底，本来就不生成主题船。
            if (items.Count == 0) return false;

            Dictionary<ShipTheme, List<ThingDef>> buckets = Bucketize(items);
            bool pawnShip = FactionPawnKind(factionDef) != null;
            bool any = false;

            foreach (KeyValuePair<ShipTheme, List<ThingDef>> pair in buckets)
            {
                if (!WouldBuildTheme(pair.Key, pair.Value, items, pawnShip)) continue;

                any = true;
                if (DefDatabase<TraderKindDef>.GetNamedSilentFail(DefNameFor(factionDef, pair.Key)) == null)
                {
                    return false;
                }
            }

            // 一条主题船都不该有的时候（物品太少，每个主题都不够格）没有东西可补。
            return any;
        }

        /// <summary>
        /// BuildThemedKinds 会不会为该主题真的产出一条船 —— 只看门槛，不构造 def。
        ///
        /// 人船看有没有可用的 PawnKind；其余主题看货位能不能凑够两种货 ——
        /// 只有一种货的船 BuildThemedKinds 会整条丢掉（chosen.Count &lt; 2）。
        /// </summary>
        private static bool WouldBuildTheme(ShipTheme theme, List<ThingDef> pool, List<ThingDef> items, bool pawnShip)
        {
            if (theme == ShipTheme.Slave || theme == ShipTheme.Talent)
            {
                return pawnShip;
            }

            if (pool.Count == 0) return false;

            int slots = SlotsFor(theme);
            int reserved = Math.Min(pool.Count, Mathf.CeilToInt(slots * ReservedRatioFor(theme)));
            int filler = Math.Min(slots - reserved, items.Count - pool.Count);

            return reserved + filler >= 2;
        }

        /// <summary>
        /// 把物品池按主题切开，每个主题生成一个 TraderKindDef。
        ///
        /// 每个主题预留固定比例货位给它自己的货，其余从该派系物品库随机补。
        ///
        /// 除了"每主题一条船"，还会额外生成变体船（如果该主题物品够多），
        /// 让同一个派系能叫到不同货色的同类型船 —— 玩家反馈"选项只有三四个"。
        /// 变体的 defName 是 _Medical1 / _Medical2 这种，同样确定性。
        /// </summary>
        private static List<TraderKindDef> BuildThemedKinds(FactionDef factionDef, List<ThingDef> items)
        {
            Dictionary<ShipTheme, List<ThingDef>> buckets = Bucketize(items);

            // 先只构建，登记统一放到方法末尾。
            List<TraderKindDef> built = new List<TraderKindDef>();

            foreach (KeyValuePair<ShipTheme, List<ThingDef>> pair in buckets)
            {
                ShipTheme theme = pair.Key;
                List<ThingDef> pool = pair.Value;

                // 人才船 / 奴隶船不看物品池，卖的是人，单独构建。
                if (theme == ShipTheme.Slave || theme == ShipTheme.Talent)
                {
                    TraderKindDef pawnKind = BuildPawnKind(factionDef, theme, items);
                    if (pawnKind == null) continue;

                    built.Add(pawnKind);
                    continue;
                }

                // 主题货一种都没有 → 这条船没有存在意义。
                if (pool.Count == 0) continue;

                // 排序规则：先本模组物品，再稳定哈希。
                //
                // 只按哈希排会打乱"本模组优先"，导致旧唐的武器船卖的是别家模组的枪。
                // 玩家要的是"这个派系的船卖这个派系的东西"，所以本模组必须排前面，
                // 哈希只用来在同类之间保持确定性（每次启动结果一致）。
                ModContentPack ownPack = factionDef.modContentPack;

                List<ThingDef> ordered = pool
                    .OrderBy(t => t.modContentPack == ownPack ? 0 : 1)
                    .ThenBy(t => StableHash(t.defName))
                    .ToList();

                int slots = SlotsFor(theme);
                int reservedSlots = Mathf.CeilToInt(slots * ReservedRatioFor(theme));
                List<ThingDef> reserved = ordered.Take(reservedSlots).ToList();

                // 剩下的位置从"除主题货以外"的物品里随机补，
                // 用稳定哈希保证每次启动抽到同一批。
                int fillerSlots = slots - reserved.Count;
                List<ThingDef> filler = items
                    .Where(t => ThemeOf(t) != theme)
                    .OrderBy(t => t.modContentPack == ownPack ? 0 : 1)
                    .ThenBy(t => StableHash(t.defName + theme))
                    .Take(fillerSlots)
                    .ToList();

                List<ThingDef> chosen = reserved.Concat(filler).Distinct().ToList();

                if (chosen.Count < 2) continue;

                TraderKindDef kind = BuildKind(factionDef, theme, chosen);
                if (kind == null) continue;

                // 诊断：主题和实际收录的 def 都打出来，
                // 便于核对"医疗船里为什么会出现护甲"这类归类错误。
                if (Prefs.DevMode)
                {
                    Log.Message("[TraderShips] " + factionDef.defName + " / " + theme + " ("
                        + chosen.Count + " defs): "
                        + string.Join(", ", chosen.Select(t => t.defName + "[" + t.thingCategories?.FirstOrDefault()?.defName + "]")));
                }

                built.Add(kind);
            }

            // 构建全部完成之后才登记。
            //
            // 边建边 Add 的话，中途抛的异常会被 Generate 的 try/catch 吞掉，
            // DefDatabase 里就留下半套船型：该派系只有前几个主题，玩家叫到的船不全，
            // 而"是否已生成"的判定又会把这份残局当成做完了。先攒后加，这一步就只剩全有或全无。
            List<TraderKindDef> result = new List<TraderKindDef>(built.Count);

            foreach (TraderKindDef kind in built)
            {
                TraderKindDef existing = DefDatabase<TraderKindDef>.GetNamedSilentFail(kind.defName);
                if (existing != null)
                {
                    // 同名 def 已在库里（Generate 被调了两次，或者补齐半套时那几条还在）。
                    // 沿用旧实例而不是覆盖：它可能已被 FactionShipExtension 或存档引用，
                    // 换成新对象会让那些引用指向孤儿 def。
                    result.Add(existing);
                    continue;
                }

                DefDatabase<TraderKindDef>.Add(kind);
                result.Add(kind);
            }

            return result;
        }

        /// <summary>不依赖 string.GetHashCode 的稳定哈希（后者在不同运行时可能变化）。</summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }

        private static TraderKindDef BuildKind(FactionDef factionDef, ShipTheme theme, List<ThingDef> items)
        {
            TraderKindDef kind = new TraderKindDef();
            kind.defName = DefNameFor(factionDef, theme);
            kind.label = LabelFor(factionDef, theme);
            kind.description = DescriptionFor(factionDef, theme);
            kind.faction = factionDef;
            kind.orbital = true;
            kind.requestable = true;
            kind.commonality = 1f;
            kind.category = factionDef.label;
            kind.tradeCurrency = TradeCurrency.Silver;

            // 现金，保证玩家能卖出东西。
            StockGenerator_ThingDefs silver = new StockGenerator_ThingDefs();
            silver.thingDef = ThingDefOf.Silver;
            silver.countRange = new IntRange(1500, 3500);
            kind.stockGenerators.Add(silver);

            foreach (ThingDef item in items)
            {
                StockGenerator_ThingDefs gen = new StockGenerator_ThingDefs();
                gen.thingDef = item;
                gen.countRange = StockRange;
                kind.stockGenerators.Add(gen);
            }

            kind.stockGenerators.Add(BuyPolicyFor(theme));

            kind.PostLoad();
            return kind;
        }

        /// <summary>
        /// 该主题的收购范围。
        ///
        /// 全部用原版 ThingCategoryDef 名和原版 tradeTag，不发明自定义分类 ——
        /// 这样模组物品只要挂了原版分类/tag 就会被收，覆盖面最广。
        /// 分类匹配会沿 parent 链上溯，所以写 "Apparel" 就覆盖了护甲、头盔、工具腰带等全部子类。
        /// </summary>
        public static StockGenerator_BuyCategories BuyPolicyFor(ShipTheme theme)
        {
            StockGenerator_BuyCategories buy = new StockGenerator_BuyCategories();
            buy.countRange = new IntRange(0, 0);

            switch (theme)
            {
                case ShipTheme.Weapon:
                    buy.categoryDefNames.AddRange(new[] { "Weapons", "WeaponsMelee", "WeaponsRanged", "WeaponsArtillery" });
                    buy.tradeTags.AddRange(new[] { "Weapons", "WeaponsMelee", "WeaponsRanged" });
                    break;

                case ShipTheme.Apparel:
                    buy.categoryDefNames.AddRange(new[] { "Apparel", "Headgear" });
                    buy.tradeTags.AddRange(new[] { "BasicClothing", "Clothing", "Armor" });
                    break;

                case ShipTheme.Medical:
                    buy.categoryDefNames.AddRange(new[] { "Medicine", "Drugs" });
                    buy.tradeTags.AddRange(new[] { "Medicine", "Drugs" });
                    break;

                case ShipTheme.Food:
                    buy.categoryDefNames.AddRange(new[] { "Foods", "FoodMeals", "FoodRaw", "PlantFoodRaw", "MeatRaw", "AnimalProductRaw", "EggsUnfertilized", "EggsFertilized", "Beverages" });
                    break;

                case ShipTheme.Resource:
                    buy.categoryDefNames.AddRange(new[] { "ResourcesRaw", "Textiles", "Leathers", "StoneBlocks", "WoodLogs", "PlantFoodRaw", "MeatRaw", "AnimalProductRaw" });
                    break;

                case ShipTheme.Industrial:
                    buy.categoryDefNames.AddRange(new[] { "ResourcesIndustrial", "Manufactured", "Components", "MortarShells", "Chemicals" });
                    break;

                case ShipTheme.Furniture:
                    buy.categoryDefNames.AddRange(new[] { "Buildings", "BuildingsFurniture", "BuildingsArt", "BuildingsProduction", "BuildingsMisc", "BuildingsSecurity", "Structure" });
                    break;

                case ShipTheme.Exotic:
                    buy.categoryDefNames.AddRange(new[] { "Artifacts", "BodyPartsArchotech", "InertRelics", "BodyParts" });
                    buy.tradeTags.AddRange(new[] { "Artifacts" });
                    break;

                case ShipTheme.Talent:
                case ShipTheme.Slave:
                    // 人船也收普通物资 —— 不然整条船只收人和白银，列表一样是空的。
                    buy.categoryDefNames.AddRange(new[] { "ResourcesRaw", "Textiles", "Leathers", "Foods", "FoodMeals",
                        "Medicine", "Drugs", "Manufactured", "Apparel", "Weapons" });
                    break;

                default:
                    // 杂货船 / 兜底：什么都收。
                    buy.buyEverything = true;
                    break;
            }

            return buy;
        }

        /// <summary>
        /// 人才船 / 奴隶船：卖人。
        ///
        /// 不用原版 StockGenerator_Slaves —— 它在构造 PawnGenerationRequest 时读
        /// this.trader.orbital，而我们凭空创建的 TraderKindDef 没有 trader，
        /// 会抛 NullReferenceException 导致整船空货（玩家看到的就是"人才船里什么都没有"）。
        /// 改为用自定义的 StockGenerator_Pawns。
        ///
        ///   人才船：1~3 人，随机 3 项技能拉到 12~20，价格高。
        ///   奴隶船：4~9 人，普通小人，价格低、量大。
        /// </summary>
        private static TraderKindDef BuildPawnKind(FactionDef factionDef, ShipTheme theme, List<ThingDef> items)
        {
            PawnKindDef kind = FactionPawnKind(factionDef);
            if (kind == null) return null;

            bool talent = theme == ShipTheme.Talent;

            TraderKindDef def = new TraderKindDef();
            def.defName = DefNameFor(factionDef, theme);
            def.label = LabelFor(factionDef, theme);
            def.description = DescriptionFor(factionDef, theme);
            def.faction = factionDef;
            def.orbital = true;
            def.requestable = true;
            def.commonality = 1f;
            def.category = factionDef.label;
            def.tradeCurrency = TradeCurrency.Silver;

            StockGenerator_ThingDefs silver = new StockGenerator_ThingDefs();
            silver.thingDef = ThingDefOf.Silver;
            silver.countRange = talent ? new IntRange(3000, 6000) : new IntRange(1500, 3000);
            def.stockGenerators.Add(silver);

            // 船里除了人，还带一批随机杂货 —— 只有人的船打开来太空。
            // 用该派系自己的物品优先，剩下从全池补。
            ModContentPack ownPack = factionDef.modContentPack;
            // 只挑"纯物品"，不要可搬运的建筑。
            //
            // 教训：人船的随机杂货里抽到了 VFEMedieval 的蜂房（Minifiable 建筑），
            // 击毁飞船后它被丢到地上开始 Tick，那个模组的
            // Building_Apiary.FlowerAround() 在"从贸易库存直接落地"这种情况下
            // 会空引用，玩家看到的就是每 tick 刷满屏红字。
            // 建筑类商品对飞船交易没什么价值，索性不卖。
            List<ThingDef> goods = items
                .Where(t => t.category == ThingCategory.Item)
                .Where(t => ThemeOf(t) != ShipTheme.Slave && ThemeOf(t) != ShipTheme.Talent)
                .OrderBy(t => t.modContentPack == ownPack ? 0 : 1)
                .ThenBy(t => StableHash(t.defName + theme))
                .Take(PawnShipGoods)
                .ToList();

            foreach (ThingDef item in goods)
            {
                StockGenerator_ThingDefs gen = new StockGenerator_ThingDefs();
                gen.thingDef = item;
                gen.countRange = StockRange;
                def.stockGenerators.Add(gen);
            }

            StockGenerator_Pawns pawns = new StockGenerator_Pawns();
            pawns.pawnKindDef = kind;
            pawns.boostSkills = talent;
            pawns.boostedSkillCount = 4;
            pawns.boostedSkillLevel = talent ? new IntRange(14, 20) : new IntRange(3, 8);
            pawns.countRange = talent ? new IntRange(4, 8) : new IntRange(10, 18);

            // 人才船买回来是殖民者，奴隶船买回来是奴隶。
            pawns.promoteToColonist = talent;
            pawns.asSlave = !talent;

            def.stockGenerators.Add(pawns);

            def.stockGenerators.Add(BuyPolicyFor(theme));

            def.PostLoad();
            return def;
        }

        /// <summary>人船上除了人还带多少种杂货。</summary>
        private const int PawnShipGoods = 8;

        /// <summary>
        /// 选"这个派系自己的人"。
        ///
        /// basicMemberKind 是最直接的答案，但部分模组派系这个字段为空或指向非人形，
        /// 那种情况下从 pawnGroupMakers 里挑出现权重最高的人形种类，
        /// 保证绮罗族的人才船里是绮罗族、鼠族的是鼠族，而不是万能的 Villager。
        /// </summary>
        private static PawnKindDef FactionPawnKind(FactionDef factionDef)
        {
            PawnKindDef basic = factionDef.basicMemberKind;
            if (basic != null && basic.race != null && basic.race.race != null && basic.race.race.Humanlike)
            {
                return basic;
            }

            PawnKindDef best = null;
            float bestWeight = -1f;

            if (factionDef.pawnGroupMakers != null)
            {
                foreach (PawnGroupMaker group in factionDef.pawnGroupMakers)
                {
                    if (group == null || group.options == null) continue;

                    foreach (PawnGenOption option in group.options)
                    {
                        if (option == null || option.kind == null) continue;
                        if (option.kind.race == null || option.kind.race.race == null) continue;
                        if (!option.kind.race.race.Humanlike) continue;

                        if (option.selectionWeight > bestWeight)
                        {
                            bestWeight = option.selectionWeight;
                            best = option.kind;
                        }
                    }
                }
            }

            return best;
        }

        private static string LabelFor(FactionDef factionDef, ShipTheme theme)
        {
            TaggedString translated = ("TraderShipsAutoKind_" + theme).Translate(factionDef.label);
            return translated.ToString();
        }

        /// <summary>
        /// 列表里显示的简短说明（只写货品类型）。
        /// </summary>
        private static string DescriptionFor(FactionDef factionDef, ShipTheme theme)
        {
            TaggedString translated = ("TraderShipsAutoKindShort_" + theme).Translate();
            return translated.ToString();
        }

        /// <summary>完整说明，供货台 tooltip 使用。</summary>
        public static string FullDescriptionFor(FactionDef factionDef, ShipTheme theme)
        {
            TaggedString translated = ("TraderShipsAutoKindDesc_" + theme).Translate(factionDef.label);
            return translated.ToString();
        }

        private static void AttachTag(FactionDef factionDef, TraderKindDef kind)
        {
            AttachTag(factionDef, new List<TraderKindDef> { kind });
        }

        private static void AttachTag(FactionDef factionDef, List<TraderKindDef> kinds)
        {
            if (factionDef.modExtensions == null)
            {
                factionDef.modExtensions = new List<DefModExtension>();
            }

            FactionShipExtension tag = new FactionShipExtension();
            tag.shipKinds = new List<TraderKindDef>(kinds);
            factionDef.modExtensions.Add(tag);
        }
    }
}