using RimWorld;
using UnityEngine;
using Verse;

namespace TraderShips
{
    class SkyfallerTradeShip : Skyfaller
    {
        CompShip Ship => innerContainer.Any ? innerContainer[0].TryGetComp<CompShip>() : null;

        // 1.7 (Waylith): the base Skyfaller now owns a cached ShadowMaterial, and exposes the
        // draw-position/rotation maths through the virtual GetDrawPositionAndRotation hook.
        // Both replace the old hand-rolled copy + Harmony Traverse on the private
        // "TimeInAnimation" property, which no longer exists under that name/visibility contract.
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            CompShip comp = Ship;
            if (comp == null) return;

            GetDrawPositionAndRotation(ref drawLoc, out float extraRotation);

            comp.Sprite.Draw(drawLoc, extraRotation);

            if (ShadowMaterial != null)
            {
                drawLoc.z = GenThing.TrueCenter(this).z;
                DrawDropSpotShadow(drawLoc, Rotation, ShadowMaterial, def.skyfaller.shadowSize, ticksToImpact);
            }
        }

        protected override void Impact()
        {
            CompShip comp = Ship;

            base.Impact();

            if (Map == null || comp == null) return;
            if (comp.mustCrash) comp.Crash();
        }
    }
}
