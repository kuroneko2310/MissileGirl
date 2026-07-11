using System;
using MissileGirl;
using MissileGirl.Tabs;
using UnityEngine;
using Verse;

namespace Gagarin
{
    public class TabContent_Gagarin : ITabContent
    {
        private string cacheRetentionTimeBuffer;
        private readonly Listing_Collapsible collapsible = new Listing_Collapsible(expanded: true);

        public override Texture2D Icon => TexTab.Gagarin;
        public override bool ShouldShow => true;
        public override string Label => KeyedResources.Gagarin_Tab;

        public override void DoContent(Rect rect)
        {
            collapsible.Begin(rect, KeyedResources.MissileGirl_Settings);
            collapsible.Label(KeyedResources.MissileGirl_EnableGagarin_Tip);

            if (collapsible.CheckboxLabeled(KeyedResources.MissileGirl_EnableGagarin,
                    ref GagarinPrefs.Enabled) && !GagarinPrefs.Enabled)
            {
                Context.IsUsingCache = false;
                GagarinCacheManager.InvalidateXmlCache("XML caching disabled by user");
            }

            if (GagarinPrefs.Enabled)
            {
                collapsible.Line(1);
                if (collapsible.CheckboxLabeled("Gagarin.CacheExpires".Translate(),
                        ref GagarinPrefs.CacheExpires, "Gagarin.CacheExpires.Desc".Translate()))
                    GagarinSettings.WriteSettings();

                if (GagarinPrefs.CacheExpires)
                {
                    int ageDays = GagarinPrefs.CacheCreationTime == default
                        ? GagarinPrefs.CacheRetentionTime
                        : (int)Math.Floor(DateTime.Now.Subtract(GagarinPrefs.CacheCreationTime).TotalDays);
                    int daysLeft = Math.Max(0, GagarinPrefs.CacheRetentionTime - Math.Max(0, ageDays));
                    collapsible.Label("Gagarin.Expiry".Translate(daysLeft));
                    collapsible.Gap(4);

                    collapsible.Lambda(30, fieldRect =>
                    {
                        cacheRetentionTimeBuffer ??= GagarinPrefs.CacheRetentionTime.ToString();
                        Rect labelRect = fieldRect.LeftPartPixels(fieldRect.width - 80f);
                        Rect numericRect = fieldRect.RightPartPixels(80f);

                        TextAnchor oldAnchor = Text.Anchor;
                        Text.Anchor = TextAnchor.MiddleLeft;
                        Widgets.Label(labelRect, "Gagarin.CacheRetentionTime".Translate());
                        Text.Anchor = oldAnchor;

                        int oldValue = GagarinPrefs.CacheRetentionTime;
                        Widgets.TextFieldNumeric(numericRect, ref GagarinPrefs.CacheRetentionTime,
                            ref cacheRetentionTimeBuffer, 1, 365);
                        if (GagarinPrefs.CacheRetentionTime != oldValue)
                            GagarinSettings.WriteSettings();
                    }, useMargins: true);
                }

                collapsible.Gap(4);
                collapsible.Label(KeyedResources.Gagarin_Tip);
                collapsible.Label(KeyedResources.Gagarin_Tip, invert: true);
                collapsible.Line(1);
                collapsible.Label(KeyedResources.Gagarin_ClearCache_Description);

                collapsible.Lambda(25, buttonRect =>
                {
                    if (Widgets.ButtonText(buttonRect, "Rebuild XML cache"))
                    {
                        Context.IsUsingCache = false;
                        GagarinCacheManager.InvalidateXmlCache("manual XML rebuild");
                        GagarinPrefs.CacheCreationTime = default;
                        GagarinSettings.WriteSettings();
                    }
                }, useMargins: true);

                collapsible.Lambda(25, buttonRect =>
                {
                    if (Widgets.ButtonText(buttonRect, "Clear texture cache only"))
                        GagarinCacheManager.ClearTextureCache("manual texture cache reset");
                }, useMargins: true);

                collapsible.Lambda(25, buttonRect =>
                {
                    if (Widgets.ButtonText(buttonRect, "Clear all MissileGirl caches"))
                    {
                        Context.IsUsingCache = false;
                        GagarinCacheManager.ClearAllCaches("manual full cache reset");
                        GagarinSettings.WriteSettings();
                    }
                }, useMargins: true);
            }

            collapsible.End(ref rect);
            if (GUI.changed)
                GagarinSettings.WriteSettings();
        }

        public override void OnSelect()
        {
            base.OnSelect();
            GagarinSettings.WriteSettings();
        }

        public override void OnDeselect()
        {
            base.OnDeselect();
            GagarinSettings.WriteSettings();
        }

        [Main.YieldTabContent]
        [Main.YieldModMenuTab]
        public static ITabContent YieldTab() => new TabContent_Gagarin();
    }
}
