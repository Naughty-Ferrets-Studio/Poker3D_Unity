using UnityEngine;

namespace HoldemTable
{
    /// <summary>Creates materials at runtime so the scene needs no material assets (Universal Render Pipeline first, safe fallbacks after).</summary>
    public static class TableMaterials
    {
        private static Shader Find(params string[] names)
        {
            foreach (string n in names)
            {
                Shader s = Shader.Find(n);
                if (s != null) return s;
            }
            return Shader.Find("Sprites/Default"); // always available
        }

        /// <summary>Plain lit colour (table, rim, floor, deck).</summary>
        public static Material Lit(Color color, float smoothness = 0.15f)
        {
            var m = new Material(Find("Universal Render Pipeline/Lit", "Standard"));
            m.color = color;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        /// <summary>Unlit textured material for card faces. Crops the texture and cuts out transparent pixels.</summary>
        public static Material Card(Texture texture, Vector2 cropScale, Vector2 cropOffset)
        {
            var m = new Material(Find("Universal Render Pipeline/Unlit", "Unlit/Texture", "Unlit/Transparent Cutout"));
            m.color = Color.white;
            if (texture != null)
            {
                m.mainTexture = texture;
                m.mainTextureScale = cropScale;
                m.mainTextureOffset = cropOffset;
            }
            if (m.HasProperty("_AlphaClip")) // URP Unlit: switch on alpha clipping for the rounded corners
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = 2450;
            }
            return m;
        }
    }
}
