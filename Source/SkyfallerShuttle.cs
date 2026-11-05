using RimWorld;
using UnityEngine;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 穿梭机框架专用的起飞动画。
    ///
    /// 为什么不能直接用模组原有的 TraderShipsShipLeaving：
    /// ShipJob_FlyAway.cs 里是
    ///     FlyShipLeaving flyShipLeaving = (FlyShipLeaving)SkyfallerMaker.MakeSkyfaller(...);
    /// 强制转换。而 TraderShipsShipLeaving 的 thingClass 是 SkyfallerTradeShip，
    /// 它继承 Skyfaller 而不是 FlyShipLeaving，所以一发射就抛 InvalidCastException。
    ///
    /// 另外穿梭机的坠落物里装的是 ActiveTransporter（货物），不是船体，
    /// 因此 SkyfallerTradeShip 那种"取 innerContainer[0] 的 CompShip"的写法也拿不到船。
    /// 船体本身通过 Contents.SetShuttle() 存着（ShipJob_FlyAway.cs:76），从那里取。
    ///
    /// 好处：起飞动画里画的仍然是 CompShip 随机拼装出来的那艘船，
    /// 不需要为穿梭机单独准备一张整船贴图。
    /// </summary>
    public class SkyfallerTradeShipShuttleLeaving : FlyShipLeaving
    {
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            GetDrawPositionAndRotation(ref drawLoc, out float extraRotation);

            if (ShipSpriteDraw.TryDrawShip(Contents, drawLoc, extraRotation)) return;

            // 拿不到船体（老存档 / 异常状态）就退回原版绘制，绝不吞掉动画。
            base.DrawAt(drawLoc, flip);
        }
    }

    /// <summary>
    /// 抵达动画。原版抵达时不会调用 SetShuttle（只有起飞会调用，见 ShipJob_FlyAway.cs），
    /// 所以这里通常拿不到船体，会退回原版绘制。保留子类是为了让
    /// TransportShipDef.arrivingSkyfaller 与 ShuttleIncoming 体系类型一致。
    /// </summary>
    public class SkyfallerTradeShipShuttleIncoming : ShuttleIncoming
    {
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            GetDrawPositionAndRotation(ref drawLoc, out float extraRotation);

            ActiveTransporterInfo contents = SafeContents();
            if (contents != null && ShipSpriteDraw.TryDrawShip(contents, drawLoc, extraRotation)) return;

            base.DrawAt(drawLoc, flip);
        }

        private ActiveTransporterInfo SafeContents()
        {
            // ShuttleIncoming.Contents 直接访问 innerContainer[0]，空容器会越界。
            if (!innerContainer.Any) return null;
            return (innerContainer[0] as ActiveTransporter)?.Contents;
        }
    }

    /// <summary>两个坠落物共用的绘制逻辑。旋转与落点由调用方（子类）自己算好。</summary>
    internal static class ShipSpriteDraw
    {
        internal static bool TryDrawShip(ActiveTransporterInfo contents, Vector3 drawLoc, float extraRotation)
        {
            if (contents == null) return false;

            Thing ship = contents.GetShuttle();
            if (ship == null || ship.Destroyed) return false;

            CompShip comp = ship.TryGetComp<CompShip>();
            if (comp == null) return false;

            ShipSprite sprite = comp.Sprite;
            if (sprite == null) return false;

            sprite.Draw(drawLoc, extraRotation);
            return true;
        }
    }
}
