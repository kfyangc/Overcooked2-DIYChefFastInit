using AssetBundles;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace OC2DIYChef
{
    // Borrow the same sprites as GameOptions/SettingsBody. Never clone its menu
    // behaviours, load a second copy of bundle24 or destroy the game's assets.
    internal static class ChefGridTheme
    {
        const string BackgroundName = "t_ui_dialog_large_01_d";
        const string HeaderName = "t_ui_dialog_header_01_d";
        static Sprite background, header;
        static bool reportedMissing;

        internal static readonly Color Ink = new Color(0.12f, 0.27f, 0.36f);
        internal static readonly Color Preview = new Color(0.61f, 0.79f, 0.89f);
        internal static readonly Color Cell = new Color(0.25f, 0.51f, 0.66f, 0.2f);
        internal static readonly Color Control = new Color(0.25f, 0.49f, 0.63f, 0.85f);

        internal static void Apply(Image panel, Image title)
        {
            if (background == null || header == null) Resolve();
            Apply(panel, background, Preview);
            Apply(title, header, new Color(0.21f, 0.66f, 0.77f));
            if ((background == null || header == null) && !reportedMissing)
            {
                reportedMissing = true;
                DIYChefPlugin.Warn("Native chef grid menu sprites are not loaded; using the blue palette.");
            }
        }

        static void Apply(Image image, Sprite sprite, Color fallback)
        {
            image.sprite = sprite;
            // These native sprites have irregular silhouettes and no nine-slice
            // borders. Simple matches the original Image's rendering mode.
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = sprite == null ? fallback : Color.white;
            image.raycastTarget = false;
            if (sprite != null) FitVisibleArtwork(image, sprite);
        }

        static void FitVisibleArtwork(Image image, Sprite sprite)
        {
            // The large dialog trims about 126 transparent pixels vertically.
            // Fit its visible artwork to the requested box, otherwise the white
            // edge shrinks inward and the footer falls outside the panel.
            var padding = DataUtility.GetPadding(sprite);
            var source = sprite.rect;
            float width = source.width - padding.x - padding.z;
            float height = source.height - padding.y - padding.w;
            if (width <= 0 || height <= 0) return;
            var rect = image.rectTransform;
            // Keep an unpadded axis stretched to its parent. The dialog has no
            // horizontal padding, so it continues to fill wider aspect ratios.
            if (padding.x != 0 || padding.z != 0)
            {
                float x = rect.rect.width / width;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, source.width * x);
                rect.anchoredPosition += new Vector2((padding.z - padding.x) * x * 0.5f, 0);
            }
            if (padding.y != 0 || padding.w != 0)
            {
                float y = rect.rect.height / height;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, source.height * y);
                rect.anchoredPosition += new Vector2(0, (padding.w - padding.y) * y * 0.5f);
            }
        }

        static void Resolve()
        {
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite.name == BackgroundName) background = sprite;
                else if (sprite.name == HeaderName) header = sprite;
            }
            if (background != null && header != null) return;
            // A lobby can have the shared UI bundle resident without its options
            // menu instantiated. Read only that existing bundle, preserving its
            // native reference counts and scene-loading lifecycle.
            string error;
            var loaded = AssetBundleManager.GetLoadedAssetBundle("bundle24", out error);
            if (loaded == null || loaded.m_AssetBundle == null) return;
            if (background == null)
                background = loaded.m_AssetBundle.LoadAsset<Sprite>("assets/gui/menu/" + BackgroundName + ".png");
            if (header == null)
                header = loaded.m_AssetBundle.LoadAsset<Sprite>("assets/gui/menu/" + HeaderName + ".png");
        }
    }
}
