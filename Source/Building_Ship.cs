using RimWorld;
using UnityEngine;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 用运行时烘焙出来的贴图做绘制的 Graphic。
    ///
    /// 为什么需要它：原版 Graphical 体系只能按 texPath 查图（Graphic.Init(GraphicRequest)），
    /// 无法直接塞入生成的 Texture2D。这个子类绕开那条路径，直接构造 Material，
    /// 从而让随机拼装的船走回原版渲染管线 —— 阴影、剔除、图集批处理全部由引擎负责，
    /// 取代原来 CompShip.PostDraw 里逐部件 Graphics.DrawMesh 的写法。
    /// </summary>
    public class Graphic_ShipBaked : Graphic_Single
    {
        /// <summary>由 Building_Ship 在拿到烘焙结果后调用。</summary>
        public void SetBaked(Texture2D texture, Vector2 worldSize)
        {
            if (texture == null) return;

            mat = MaterialPool.MatFrom(texture, ShaderTypeDefOf.CutoutComplex.Shader, Color.white, 0);
            drawSize = worldSize;

            // 船舱是固定的俯视布局，旋转会让贴图翻转成侧视，所以忽略 Rot4。
            ShouldDrawRotatedOverride = false;
        }

        private bool ShouldDrawRotatedOverride = true;

        public override bool ShouldDrawRotated => ShouldDrawRotatedOverride;
    }

    /// <summary>
    /// 会自己烘焙贴图的飞船建筑。
    ///
    /// Thing.Graphic 是 virtual（Thing.cs:478），所以每个实例可以返回自己的 Graphic：
    /// 同一份 ThingDef 下，每艘船的随机拼装结果不同，必须按实例区分。
    /// </summary>
    public class Building_Ship : Building
    {
        private Graphic_ShipBaked bakedGraphic;
        private bool bakeAttempted;

        /// <summary>烘焙 Graphic 是否已就绪（CompShip 据此决定要不要再手绘）。</summary>
        public bool HasBakedGraphic => bakedGraphic != null;

        public override Graphic Graphic
        {
            get
            {
                if (bakedGraphic != null) return bakedGraphic;

                // 只有一艘船被请求绘制时才会走到这里，失败后不再重试。
                if (bakeAttempted) return base.Graphic;
                bakeAttempted = true;

                CompShip comp = this.TryGetComp<CompShip>();
                if (comp == null) return base.Graphic;

                // Graphic 在绘制期每帧被访问，任何异常都会变成"每帧一条报错"。
                // 这里兜底一次，失败就永久退回标准绘制。
                try
                {
                    Graphic_ShipBaked result = comp.TryBuildBakedGraphic(def);
                    if (result == null) return base.Graphic;

                    bakedGraphic = result;
                    return bakedGraphic;
                }
                catch (System.Exception e)
                {
                    Log.WarningOnce("[TraderShips] Baked graphic failed, using the default one: " + e.Message, 771234055);
                    return base.Graphic;
                }
            }
        }
    }
}
