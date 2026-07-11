// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

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
        private string maxGenerationsBuffer;
        private string textureCacheMaxMbBuffer;
        private readonly Listing_Collapsible collapsible = new Listing_Collapsible(expanded: true);

        public override Texture2D Icon => TexTab.Gagarin;
        public override bool ShouldShow => true;
        public override string Label => KeyedResources.Gagarin_Tab;

        public override void DoContent(Rect rect)
        {
            collapsible.Begin(rect, KeyedResources.MissileGirl_Settings);
            collapsible.Label(KeyedResources.MissileGirl_EnableGagarin_Tip);
            if (collapsible.CheckboxLabeled(KeyedResources.MissileGirl_EnableGagarin, ref GagarinPrefs.Enabled) && !GagarinPrefs.Enabled)
                AssetPipelineCoordinator.ClearXmlCache();

            if (GagarinPrefs.Enabled)
            {
                collapsible.Line(1);
                collapsible.CheckboxLabeled("Enable GPU-ready texture blob cache", ref GagarinPrefs.TextureCachingEnabled,
                    "GraphicsSetter can reuse validated GPU-ready texture payloads without decoding PNG/DDS again.");
                collapsible.CheckboxLabeled("Expire XML generations by age", ref GagarinPrefs.CacheExpires,
                    "Change detection remains active even when time-based expiry is disabled.");

                if (GagarinPrefs.CacheExpires)
                {
                    var daysLeft = Math.Max(0, GagarinPrefs.CacheRetentionTime - DateTime.Now.Subtract(GagarinPrefs.CacheCreationTime).Days);
                    collapsible.Label("Gagarin.Expiry".Translate(daysLeft));
                    DrawNumeric("Max XML generation age (days)", ref GagarinPrefs.CacheRetentionTime, ref cacheRetentionTimeBuffer, 1, 365);
                }

                DrawNumeric("READY generations to retain", ref GagarinPrefs.MaxCacheGenerations, ref maxGenerationsBuffer, 2, 20);
                DrawNumeric("Texture blob cache limit (MB)", ref GagarinPrefs.TextureCacheMaxMB, ref textureCacheMaxMbBuffer, 128, 65536);
                collapsible.Gap(4);
                collapsible.Label(AssetPipelineCoordinator.GetStatusText(), invert: true);
                collapsible.Line(1);
                collapsible.Label("Cache domains are independent. XML rebuilds do not delete textures; renderer-only changes do not rebuild XML.");
                DrawButtonRow("Revalidate current mods", AssetPipelineCoordinator.RevalidateNow,
                    "Rebuild XML only", () => AssetPipelineCoordinator.RequestXmlRebuild("Manual rebuild requested"));
                DrawButtonRow("Delete texture cache", AssetPipelineCoordinator.ClearTextureCache,
                    "Prune old data", AssetPipelineCoordinator.PruneOldData);
                DrawButtonRow("Delete all caches", AssetPipelineCoordinator.ClearAllCaches,
                    "Save settings", GagarinSettings.WriteSettings);
            }

            collapsible.End(ref rect);
            if (GUI.changed) GagarinSettings.WriteSettings();
        }

        private void DrawNumeric(string label, ref int value, ref string buffer, int minimum, int maximum)
        {
            collapsible.Lambda(30, rect =>
            {
                buffer ??= value.ToString();
                var labelRect = rect.LeftPartPixels(rect.width - 100f);
                var fieldRect = rect.RightPartPixels(95f);
                var oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, label);
                Text.Anchor = oldAnchor;
                Widgets.TextFieldNumeric(fieldRect, ref value, ref buffer, minimum, maximum);
            }, useMargins: true);
        }

        private void DrawButtonRow(string leftLabel, Action leftAction, string rightLabel, Action rightAction)
        {
            collapsible.Lambda(30, rect =>
            {
                var left = rect.LeftHalf().ContractedBy(2f);
                var right = rect.RightHalf().ContractedBy(2f);
                if (Widgets.ButtonText(left, leftLabel))
                {
                    leftAction();
                    GagarinSettings.WriteSettings();
                }
                if (Widgets.ButtonText(right, rightLabel))
                {
                    rightAction();
                    GagarinSettings.WriteSettings();
                }
            }, useMargins: true);
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
