using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// Bakes a ShipSprite's part list into a single texture, so a ship costs one draw call
    /// instead of 1-2 per part (ShipSprite.Draw used to issue a Graphics.DrawMesh per part).
    ///
    /// Design constraints that shaped this:
    ///  - Baking happens in LOCAL space, at 1:1 art resolution, so no resampling blur.
    ///  - The ship's rotation is applied by the caller (ShipSprite.Draw(additionalAngle)), which
    ///    the landing animation relies on. Baking must therefore NOT bake rotation in.
    ///  - Parts are composited in layer order (ShipSprite.FinishCreating sorts them), which
    ///    reproduces the painter's-algorithm ordering of the old per-part draw loop.
    ///  - Anything unexpected (unreadable texture, missing Material, null parts) returns null and
    ///    the caller keeps using the old path. Baking is an optimisation, never a requirement.
    ///
    /// RimWorld 1.7 runs on Waylith with UnityStubs, but runtime texturing is genuinely supported:
    /// Verse.TextureAtlasHelper.MakeReadableTextureInstance does Blit -> RenderTexture -> ReadPixels,
    /// and Verse.StaticTextureAtlas blits with materials, so the API surface is real, not a stub.
    /// </summary>
    public static class ShipSpriteBaker
    {
        /// <summary>Filled with the baked result; null when baking is not possible.</summary>
        public class Baked
        {
            public Texture2D texture;
            /// <summary>Size in local (art) units — multiply by ShipSprite.scale for world size.</summary>
            public Vector2 size;
            /// <summary>Centre of the baked area in local units, relative to ShipSprite.offset.</summary>
            public Vector2 center;
        }

        public static Baked TryBake(ShipSprite sprite)
        {
            if (sprite == null || sprite.parts == null || sprite.parts.Count == 0) return null;

            try
            {
                return Bake(sprite);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[TraderShips] Ship texture baking failed, falling back to per-part "
                    + "drawing: " + e.Message, 771234001);
                return null;
            }
        }

        private static Baked Bake(ShipSprite sprite)
        {
            float dx = ShipSprite.dxMultiplier;
            float dz = ShipSprite.dzMultiplier;

            // ---- Pass 1: bounds in local units -------------------------------------------
            float minU = float.MaxValue, maxU = float.MinValue;
            float minV = float.MaxValue, maxV = float.MinValue;

            foreach (ShipSpritePart part in sprite.parts)
            {
                if (part == null || part.def == null || part.def.graphicData == null) continue;

                Vector2 drawSize = part.def.graphicData.drawSize;
                float halfW = drawSize.x * 0.5f;
                float halfH = drawSize.y * 0.5f;

                Accumulate(part, sprite, dx, dz, 1f, halfW, halfH, ref minU, ref maxU, ref minV, ref maxV);
                if (part.distance != 0f)
                {
                    // The old draw loop mirrored parts that have a non-zero distance (fake glow /
                    // reflection). Mirroring is a position swap, not a flip.
                    Accumulate(part, sprite, dx, dz, -1f, halfW, halfH, ref minU, ref maxU, ref minV, ref maxV);
                }
            }

            if (minU > maxU || minV > maxV) return null;

            // 关键：把画布的中心对准「绘制原点」，而不是对准内容包围盒的中心。
            //
            // 原来 ShipSprite.Draw 的映射是
            //     world.x = drawPos.x + (offset.x + u) * scale
            //     world.z = drawPos.z - (offset.y + v) * scale
            // 而 offset.y 恒为 -40、offset.x 是水平居中修正。若直接把包围盒中心当作原点，
            // 整艘船会偏移 40*0.015 = 0.6 格。
            //
            // Graphic 不支持偏移（quad 恒以 DrawPos 为中心），所以这里反过来：
            // 让画布以 (u=-offset.x, v=-offset.y) 为中心，这样"内容中心"就落在 DrawPos 上。
            float originU = -sprite.offset.x;
            float originV = -sprite.offset.y;

            float halfCanvasW = Mathf.Max(Mathf.Max(originU - minU, maxU - originU), 0.5f);
            float halfCanvasH = Mathf.Max(Mathf.Max(originV - minV, maxV - originV), 0.5f);

            minU = originU - halfCanvasW;
            maxU = originU + halfCanvasW;
            minV = originV - halfCanvasH;
            maxV = originV + halfCanvasH;

            int width = Mathf.CeilToInt(maxU - minU);
            int height = Mathf.CeilToInt(maxV - minV);
            if (width <= 0 || height <= 0) return null;

            // Guard against absurd sizes from broken part defs; 4096 is plenty for a ship.
            const int MaxDim = 4096;
            if (width > MaxDim || height > MaxDim)
            {
                Log.Warning("[TraderShips] Ship sprite too large to bake (" + width + "x" + height + ").");
                return null;
            }

            // ---- Pass 2: composite -------------------------------------------------------
            Color32[] dst = new Color32[width * height];
            Color32 clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < dst.Length; i++) dst[i] = clear;

            Color32 tint = sprite.color;

            foreach (ShipSpritePart part in sprite.parts)
            {
                if (part == null || part.def == null || part.def.graphicData == null) continue;

                bool tempSrc;
                Texture2D src = SourceTexture(part, out tempSrc);
                if (src == null) continue;

                Color32[] srcPixels = src.GetPixels32();
                int sw = src.width;
                int sh = src.height;

                // 临时副本立刻释放：Texture2D 不参与 GC，每船每部件一张会跨读档累积。
                if (tempSrc) UnityEngine.Object.Destroy(src);
                src = null;

                if (srcPixels == null || sw <= 0 || sh <= 0) continue;

                // 不烘焙镜像副本。
                //
                // 原版的逐部件绘制会为 distance != 0 的部件再画一份镜像，并把它的 y 压低
                // 0.0001*layer（CompShip 那条路径里是 -layer*0.0001f），靠深度缓冲藏在主体后面，
                // 制造"厚度/反光"的错觉。
                //
                // 烘焙是平铺合成，没有深度缓冲：镜像只能靠绘制顺序盖住，而它是一整份船体拷贝，
                // 盖不住就会变成"两艘船叠在一起"。所以这里干脆不画镜像，
                // 需要这个效果的话可以在设置里关掉烘焙，回退到逐部件绘制。
                Blit(dst, width, height, srcPixels, sw, sh, part, sprite, dx, dz, 1f, minU, minV, tint);
            }

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(dst);
            tex.Apply(false, false);

            Baked result = new Baked();
            result.texture = tex;
            result.size = new Vector2(width, height);
            // 画布已以绘制原点为中心，因此不需要额外偏移。
            result.center = Vector2.zero;
            return result;
        }

        private static void Accumulate(ShipSpritePart part, ShipSprite sprite, float dx, float dz,
            float side, float halfW, float halfH, ref float minU, ref float maxU, ref float minV, ref float maxV)
        {
            float u = sprite.offset.x + part.offset.x + side * part.distance * dx;
            float v = sprite.offset.y + part.offset.y + side * part.distance * dz;

            minU = Mathf.Min(minU, u - halfW);
            maxU = Mathf.Max(maxU, u + halfW);
            minV = Mathf.Min(minV, v - halfH);
            maxV = Mathf.Max(maxV, v + halfH);
        }

        /// <summary>
        /// 取部件的可读贴图。
        ///
        /// temporary = true 表示这是一份**新建的临时副本**，调用方用完必须 Destroy。
        /// Graphic 缓存里的原贴图是共享资源，绝不能销毁。
        /// </summary>
        private static Texture2D SourceTexture(ShipSpritePart part, out bool temporary)
        {
            temporary = false;

            Graphic g = part.def.graphicData.Graphic;
            if (g == null || g.MatSingle == null) return null;

            Texture2D tex = g.MatSingle.mainTexture as Texture2D;
            if (tex == null) return null;

            // Textures loaded from disk are frequently not CPU-readable; round-trip them through
            // a RenderTexture the way Verse.TextureAtlasHelper does.
            if (!tex.isReadable)
            {
                tex = MakeReadable(tex);
                temporary = true;
            }

            return tex;
        }

        /// <summary>
        /// 非可读贴图 → 可读副本。
        ///
        /// 这个副本是**临时**的：每船每个部件都会生成一张，
        /// Texture2D 不参与 GC，用完必须显式 Destroy，否则跨读档一直累积。
        /// 调用方负责销毁（见 Bake 里的 try/finally）。
        /// </summary>
        private static Texture2D MakeReadable(Texture2D source)
        {
            RenderTexture temp = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, temp);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = temp;
            Texture2D readable = new Texture2D(source.width, source.height);
            readable.ReadPixels(new Rect(0f, 0f, temp.width, temp.height), 0, 0);
            readable.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temp);

            return readable;
        }

        /// <summary>
        /// Copies one part into the destination buffer, tinted, with straight-alpha over-compositing.
        /// Parts are drawn at their native pixel size (1:1 with the art), so no scaling is needed.
        /// </summary>
        private static void Blit(Color32[] dst, int dstW, int dstH,
            Color32[] src, int sw, int sh,
            ShipSpritePart part, ShipSprite sprite, float dx, float dz,
            float side, float minU, float minV, Color32 tint)
        {
            float u = sprite.offset.x + part.offset.x + side * part.distance * dx;
            float v = sprite.offset.y + part.offset.y + side * part.distance * dz;

            // Destination origin of this part's rect, in texels relative to the bounds origin.
            // Row 0 corresponds to v == minV; ShipSprite.Draw maps v to world -z, so the resulting
            // quad is upright once drawn (if it ever looks vertically mirrored, flip this line).
            int originX = Mathf.RoundToInt(u - sw * 0.5f - minU);
            int originY = Mathf.RoundToInt(v - sh * 0.5f - minV);

            for (int y = 0; y < sh; y++)
            {
                int dy = originY + y;
                if (dy < 0 || dy >= dstH) continue;
                int dstRow = dy * dstW;
                int srcRow = y * sw;

                for (int x = 0; x < sw; x++)
                {
                    int destX = originX + x;
                    if (destX < 0 || destX >= dstW) continue;

                    Color32 s = src[srcRow + x];
                    if (s.a == 0) continue;

                    // Tint RGB, keep alpha.
                    s.r = (byte)(s.r * tint.r / 255);
                    s.g = (byte)(s.g * tint.g / 255);
                    s.b = (byte)(s.b * tint.b / 255);
                    s.a = (byte)(s.a * tint.a / 255);

                    dst[dstRow + destX] = Over(s, dst[dstRow + destX]);
                }
            }
        }

        private static Color32 Over(Color32 top, Color32 bottom)
        {
            if (top.a == 255) return top;
            if (bottom.a == 0) return top;

            float sa = top.a / 255f;
            float ba = bottom.a / 255f;
            float outA = sa + ba * (1f - sa);
            if (outA <= 0f) return new Color32(0, 0, 0, 0);

            float inv = 1f / outA;
            return new Color32(
                (byte)((top.r * sa + bottom.r * ba * (1f - sa)) * inv),
                (byte)((top.g * sa + bottom.g * ba * (1f - sa)) * inv),
                (byte)((top.b * sa + bottom.b * ba * (1f - sa)) * inv),
                (byte)(outA * 255f));
        }
    }
}