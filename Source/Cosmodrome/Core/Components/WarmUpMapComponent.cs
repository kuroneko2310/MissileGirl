using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace MissileGirl
{
    public class WarmUpMapComponent : MapComponent
    {
        private const int WarmupTimeSeconds = 4;

        private bool finished;
        private bool started;
        private bool settingsStashed;
        private int startingTicksGame = -1;
        private int ticksPassed;
        private bool showUI = true;
        private int integrityGameTick = -1;

        public bool SettingsStashed
        {
            get => settingsStashed;
            set => settingsStashed = value;
        }

        public float Progress
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Mathf.Clamp01(ticksPassed.TicksToSeconds() / WarmupTimeSeconds);
        }

        public bool Finished => finished;
        public bool Started => started;
        public bool Expired => Progress >= 1f;

        public static WarmUpMapComponent current;
        public static int warmUpsCount;
        public static bool settingsBeingStashed;

        private readonly Dictionary<FieldInfo, object> stashedValues = new Dictionary<FieldInfo, object>();
        private readonly Dictionary<int, IntVec3> positionStash = new Dictionary<int, IntVec3>();

        public WarmUpMapComponent(Map map) : base(map)
        {
            current?.AbortWarmUp();
            showUI = RocketPrefs.Enabled;
            Initialize();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Initialize();
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (finished || !started || !settingsBeingStashed || !showUI || !RocketPrefs.ShowWarmUpPopup)
                return;

            const int height = 65;
            const int width = 450;
            Rect rect = new Rect(UI.screenWidth / 2f - width / 2f, UI.screenHeight / 5f, width, height);
            GUIUtility.ExecuteSafeGUIAction(() => DoPopupContent(rect));
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (finished && GenTicks.TicksGame == integrityGameTick)
            {
                Logger.Message("MissileGirl: Position verification started!");
                PopPawnsPosition();
                if (RocketPrefs.PauseAfterWarmup && !Find.TickManager.Paused)
                    Find.TickManager.Pause();
            }

            if (finished || !started)
                return;

            int tick = GenTicks.TicksGame;
            if ((tick + 1 - startingTicksGame).TicksToSeconds() >= WarmupTimeSeconds)
            {
                integrityGameTick = tick + 3;
                StashPawnsPosition();
            }

            if ((tick - startingTicksGame).TicksToSeconds() < WarmupTimeSeconds)
            {
                ticksPassed++;
                return;
            }

            finished = true;
            current = null;
            if (SettingsStashed)
                PopSettings();
            Logger.Message("MissileGirl: <color=red>Warm up</color> finished for new map!");
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            if (SettingsStashed)
                PopSettings();
        }

        public void AbortWarmUp()
        {
            if (finished)
                return;

            current = null;
            finished = true;
            if (SettingsStashed)
                PopSettings();
            else
                settingsBeingStashed = false;
            Logger.Message("MissileGirl: <color=red>Warm up ABORTED!</color> for new map!");
        }

        private void Initialize()
        {
            if (started || settingsBeingStashed)
                return;

            StashSettings();
            if (!SettingsStashed)
                return;

            warmUpsCount++;
            current = this;
            started = true;
            startingTicksGame = GenTicks.TicksGame;
            Logger.Message("MissileGirl: <color=red>Warm up</color> started for new map!");
        }

        private void DoPopupContent(Rect inRect)
        {
            try
            {
                Widgets.DrawWindowBackground(inRect.ExpandedBy(20));
                Rect textRect = inRect.TopHalf();
                Rect progressRect = inRect.BottomHalf();
                progressRect.xMin += 25;
                progressRect.xMax -= 25;

                GUIFont.Font = GUIFontSize.Small;
                GUIFont.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(textRect.TopPart(0.6f), (Find.TickManager?.Paused ?? false)
                    ? KeyedResources.MissileGirl_Unpause
                    : "<color=orange>" + KeyedResources.MissileGirl_MissileGirl + "</color> " +
                      KeyedResources.MissileGirl_Warming);
                GUIFont.Font = GUIFontSize.Tiny;
                Widgets.Label(textRect.BottomPart(0.4f), KeyedResources.MissileGirl_HideProgressBar);
                DoProgressBar(progressRect);
            }
            catch (Exception exception)
            {
                Log.Warning($"MissileGirl: Warmup popup error! {exception}");
            }
        }

        private void DoProgressBar(Rect rect)
        {
            rect = rect.ContractedBy(7);
            Widgets.DrawBoxSolid(rect, Color.grey);
            rect = rect.ContractedBy(1);
            Rect progressRect = rect.LeftPart(Progress);
            Widgets.DrawBoxSolid(rect, Color.black);
            Widgets.DrawBoxSolid(progressRect, (Find.TickManager?.Paused ?? false) ? Color.yellow : Color.cyan);
        }

        private void StashSettings()
        {
            try
            {
                StashSettingsInternal();
                SettingsStashed = true;
                settingsBeingStashed = true;
            }
            catch (Exception exception)
            {
                Log.Error($"MissileGirl: Stashing settings failed! {exception}");
                stashedValues.Clear();
                SettingsStashed = false;
                settingsBeingStashed = false;
                current = null;
            }
        }

        private void PopSettings()
        {
            try
            {
                PopSettingsInternal();
            }
            catch (Exception exception)
            {
                Log.Error($"MissileGirl: Popping settings failed! {exception}");
                current = null;
            }
            finally
            {
                stashedValues.Clear();
                SettingsStashed = false;
                settingsBeingStashed = false;
            }
        }

        private void PopPawnsPosition()
        {
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!pawn.Spawned || pawn.Dead || pawn.Suspended || pawn.InContainerEnclosed || pawn.Destroyed)
                    continue;

                if (!positionStash.TryGetValue(pawn.thingIDNumber, out IntVec3 stashedPosition))
                    continue;

                bool movedTooFar = pawn.positionInt.DistanceTo(stashedPosition) >= 7.5f;
                bool invalidPosition = !pawn.positionInt.InBounds(map)
                                       || !pawn.positionInt.Standable(map);
                if (!movedTooFar && !invalidPosition)
                    continue;

                pawn.jobs?.StopAll(true);
                pawn.pather?.StopDead();
                pawn.Position = stashedPosition;
            }
            positionStash.Clear();
        }

        private void StashPawnsPosition()
        {
            positionStash.Clear();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!pawn.Spawned || pawn.Dead || pawn.Suspended || pawn.InContainerEnclosed || pawn.Destroyed)
                    continue;
                positionStash[pawn.thingIDNumber] = pawn.positionInt;
            }
        }

        private void StashSettingsInternal()
        {
            stashedValues.Clear();
            foreach (FieldInfo field in RocketPrefs.SettingsFields)
            {
                if (!field.TryGetAttribute(out Main.SettingsField config))
                    continue;
                stashedValues[field] = field.GetValue(null);
                field.SetValue(null, config.warmUpValue);
            }
        }

        private void PopSettingsInternal()
        {
            foreach (KeyValuePair<FieldInfo, object> pair in stashedValues)
                pair.Key.SetValue(null, pair.Value);
        }
    }
}
