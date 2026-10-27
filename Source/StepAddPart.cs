using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace TraderShips
{
    public class StepAddPart : ShipgenStep
    {
        public string category;
        public Vector2 offset;
        public FloatRange angle;
        public int layer;

        public override void Apply(ShipgenState state, ShipSprite sprite)
        {
            ShipSpritePartDef def = DefDatabase<ShipSpritePartDef>.AllDefs.Where(x => x.category == category).RandomElement();

            // Defensive: RandomElement returns null (with a warning) when no part def matches the
            // category. Adding a part with a null def would later NRE inside
            // ShipSprite.FinishCreating when it reads part.def.graphicData.drawSize.
            if (def == null)
            {
                Log.WarningOnce("[TraderShips] No ShipSpritePartDef found for category '" + category + "'; skipping ship part.", category.GetHashCode());
                return;
            }

            sprite.parts.Add(new ShipSpritePart()
            {
                def = def,
                angle = angle.RandomInRange,
                distance = state.distance,
                offset = state.offset + offset,
                layer = layer,
            });
        }
    }
}
