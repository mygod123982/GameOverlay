﻿// <copyright file="Radar.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Radar
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;
    using System.Threading.Tasks;
    using Coroutine;
    using GameHelper;
    using GameHelper.CoroutineEvents;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.PixelFormats;
    using SixLabors.ImageSharp.Processing;

    /// <summary>
    /// <see cref="Radar"/> plugin.
    /// </summary>
    public sealed class Radar : PCore<RadarSettings>
    {
        /// <summary>
        /// If we don't do this, user will be asked to
        /// setup the culling window everytime they open the game.
        /// </summary>
        private bool skipOneSettingChange = false;

        private ActiveCoroutine onMove;
        private ActiveCoroutine onForegroundChange;
        private ActiveCoroutine onGameClose;
        private ActiveCoroutine onAreaChange;

        private string currentAreaName = string.Empty;
        private string tmpTileName = string.Empty;
        private string tmpDisplayName = string.Empty;
        private int tmpExpectedClusters = 1;

        private Vector2 miniMapCenterWithDefaultShift = Vector2.Zero;
        private double miniMapDiagonalLength = 0x00;

        private double largeMapDiagonalLength = 0x00;

        // Legion Cache.
        private Dictionary<uint, byte> frozenInTimeEntities = new Dictionary<uint, byte>();

        private string questChestStarting = "Metadata/Chests/QuestChests";
        private HashSet<uint> questChests = new HashSet<uint>();

        private string heistUsefullChestContains = "HeistChestSecondary";
        private string heistAllChestStarting = "Metadata/Chests/LeagueHeist";
        private Dictionary<uint, string> heistChestCache = new Dictionary<uint, string>();

        // Delirium Hidden Monster cache.
        private Dictionary<uint, string> deliriumHiddenMonster = new Dictionary<uint, string>();
        private string deliriumHiddenMonsterStarting = "Metadata/Monsters/LeagueAffliction/DoodadDaemons/DoodadDaemon";

        private string delveChestStarting = "Metadata/Chests/DelveChests/";
        private bool isAzuriteMine = false;
        private Dictionary<uint, string> delveChestCache = new Dictionary<uint, string>();

        private IntPtr walkableMapTexture = IntPtr.Zero;
        private Vector2 walkableMapDimension = Vector2.Zero;

        private Dictionary<string, TgtClusters> currentAreaImportantTiles = new Dictionary<string, TgtClusters>();

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

        /// <inheritdoc/>
        public override void DrawSettings()
        {
            ImGui.TextWrapped("Following slider is for fixing large map icons. " +
                "You have to use it if you feel that LargeMap Icons " +
                "are moving while your player is moving. You only have " +
                "to find a value that works for you per game window resolution. " +
                "Basically, you don't have to change it unless you change your " +
                "game window resolution. Also, please contribute back, let me know " +
                "what resolution you use and what value works best for you. " +
                "This slider has no impact on mini-map icons. For windowed-full-screen " +
                "default value should be good enough.");
            ImGui.DragFloat(
                "Large Map Fix",
                ref this.Settings.LargeMapScaleMultiplier,
                0.001f,
                0.01f,
                0.3f);
            ImGui.TextWrapped("If your mini/large map icon are not working/visible. Open this " +
                "Overlay setting window, click anywhere on it and then hide this Overlay " +
                "setting window. It will fix the issue.");

            ImGui.Checkbox("Draw Radar when game in foreground", ref this.Settings.DrawWhenForeground);
            ImGui.Checkbox("Modify Large Map Culling Window", ref this.Settings.ModifyCullWindow);
            ImGui.Separator();
            ImGui.NewLine();
            if (ImGui.Checkbox("Draw Area/Zone Map (maphack)", ref this.Settings.DrawWalkableMap))
            {
                if (this.Settings.DrawWalkableMap)
                {
                    if (this.walkableMapTexture == IntPtr.Zero)
                    {
                        this.GenerateMapTexture();
                    }
                }
                else
                {
                    this.RemoveMapTexture();
                }
            }

            if (ImGui.ColorEdit4("Drawn Map Color", ref this.Settings.WalkableMapColor))
            {
                if (this.walkableMapTexture != IntPtr.Zero)
                {
                    this.GenerateMapTexture();
                }
            }

            ImGui.Separator();
            ImGui.NewLine();
            if (ImGui.RadioButton("Show all tile names", this.Settings.ShowAllTgtNames))
            {
                this.Settings.ShowAllTgtNames = true;
                this.Settings.ShowImportantTgtNames = false;
            }

            ImGui.SameLine();
            if (ImGui.RadioButton("Show important tile names", this.Settings.ShowImportantTgtNames))
            {
                this.Settings.ShowAllTgtNames = false;
                this.Settings.ShowImportantTgtNames = true;
            }

            ImGui.SameLine();
            if (ImGui.RadioButton("Don't show tile name", !this.Settings.ShowAllTgtNames && !this.Settings.ShowImportantTgtNames))
            {
                this.Settings.ShowAllTgtNames = false;
                this.Settings.ShowImportantTgtNames = false;
            }

            ImGui.ColorEdit4("Tile text color", ref this.Settings.TgtNameColor);
            ImGui.Checkbox("Put black box around tile text, makes easier to read.", ref this.Settings.TgtNameBackground);
            if (ImGui.CollapsingHeader("Important Tile Setting"))
            {
                this.AddNewTileBox();
                this.DisplayAllImportantTile();
            }

            ImGui.Separator();
            ImGui.NewLine();
            ImGui.Checkbox("Hide Entities without Life/Chest component", ref this.Settings.HideUseless);
            ImGui.Checkbox("Show Player Names", ref this.Settings.ShowPlayersNames);
            if (ImGui.CollapsingHeader("Icons Setting"))
            {
                this.Settings.DrawIconsSettingToImGui(
                    "BaseGame Icons",
                    this.Settings.BaseIcons,
                    "Blockages icon can be set from Delve Icons category i.e. 'Blockage OR DelveWall'");

                this.Settings.DrawIconsSettingToImGui(
                    "Legion Icons",
                    this.Settings.LegionIcons,
                    "Legion bosses are same as BaseGame Icons -> Unique Monsters.");

                this.Settings.DrawIconsSettingToImGui(
                    "Delirium Icons",
                    this.Settings.DeliriumIcons,
                    string.Empty);

                this.Settings.DrawIconsSettingToImGui(
                    "Heist Icons",
                    this.Settings.HeistIcons,
                    string.Empty);

                this.Settings.DrawIconsSettingToImGui(
                    "Delve Icons",
                    this.Settings.DelveIcons,
                    string.Empty);
            }
        }

        /// <inheritdoc/>
        public override void DrawUI()
        {
            var largeMap = Core.States.InGameStateObject.GameUi.LargeMap;
            var miniMap = Core.States.InGameStateObject.GameUi.MiniMap;
            if (this.Settings.ModifyCullWindow)
            {
                ImGui.SetNextWindowPos(largeMap.Center, ImGuiCond.Appearing);
                ImGui.SetNextWindowSize(new Vector2(400f), ImGuiCond.Appearing);
                ImGui.Begin("Large Map Culling Window");
                ImGui.TextWrapped("This is a culling window for the large map icons. " +
                    "Any large map icons outside of this window will be hidden automatically. " +
                    "Feel free to change the position/size of this window. " +
                    "Once you are happy with the dimensions, double click this window. " +
                    "You can bring this window back from the settings menu.");
                this.Settings.CullWindowPos = ImGui.GetWindowPos();
                this.Settings.CullWindowSize = ImGui.GetWindowSize();
                if (ImGui.IsWindowHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    this.Settings.ModifyCullWindow = false;
                }

                ImGui.End();
            }

            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                return;
            }

            if (this.Settings.DrawWhenForeground && !Core.Process.Foreground)
            {
                return;
            }

            if (largeMap.IsVisible)
            {
                var largeMapRealCenter = largeMap.Center + largeMap.Shift + largeMap.DefaultShift;
                var largeMapModifiedZoom = largeMap.Zoom * this.Settings.LargeMapScaleMultiplier;
                Helper.DiagonalLength = this.largeMapDiagonalLength;
                Helper.Scale = largeMapModifiedZoom;
                ImGui.SetNextWindowPos(this.Settings.CullWindowPos);
                ImGui.SetNextWindowSize(this.Settings.CullWindowSize);
                ImGui.SetNextWindowBgAlpha(0f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
                ImGui.Begin("Large Map Culling Window", UiHelper.TransparentWindowFlags);
                ImGui.PopStyleVar();
                this.DrawLargeMap(largeMapRealCenter);
                this.DrawTgtFiles(largeMapRealCenter);
                this.DrawMapIcons(largeMapRealCenter, largeMapModifiedZoom * 5f);
                ImGui.End();
            }

            if (miniMap.IsVisible)
            {
                Helper.DiagonalLength = this.miniMapDiagonalLength;
                Helper.Scale = miniMap.Zoom;
                var miniMapRealCenter = this.miniMapCenterWithDefaultShift + miniMap.Shift;
                ImGui.SetNextWindowPos(miniMap.Postion);
                ImGui.SetNextWindowSize(miniMap.Size);
                ImGui.SetNextWindowBgAlpha(0f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
                ImGui.Begin("###minimapRadar", UiHelper.TransparentWindowFlags);
                ImGui.PopStyleVar();
                this.DrawMapIcons(miniMapRealCenter, miniMap.Zoom);
                ImGui.End();
            }
        }

        /// <inheritdoc/>
        public override void OnDisable()
        {
            this.onMove?.Cancel();
            this.onForegroundChange?.Cancel();
            this.onGameClose?.Cancel();
            this.onAreaChange?.Cancel();
            this.onMove = null;
            this.onForegroundChange = null;
            this.onGameClose = null;
            this.onAreaChange = null;
        }

        /// <inheritdoc/>
        public override void OnEnable(bool isGameOpened)
        {
            if (!isGameOpened)
            {
                this.skipOneSettingChange = true;
            }

            if (File.Exists(this.SettingPathname))
            {
                var content = File.ReadAllText(this.SettingPathname);
                this.Settings = JsonConvert.DeserializeObject<RadarSettings>(content);
            }

            this.Settings.AddDefaultIcons(this.DllDirectory);

            this.onMove = CoroutineHandler.Start(this.OnMove());
            this.onForegroundChange = CoroutineHandler.Start(this.OnForegroundChange());
            this.onGameClose = CoroutineHandler.Start(this.OnClose());
            this.onAreaChange = CoroutineHandler.Start(this.ClearCachesAndUpdateAreaInfo());
        }

        /// <inheritdoc/>
        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname));
            var settingsData = JsonConvert.SerializeObject(this.Settings, Formatting.Indented);
            File.WriteAllText(this.SettingPathname, settingsData);
        }

        private void DrawLargeMap(Vector2 mapCenter)
        {
            if (!this.Settings.DrawWalkableMap)
            {
                return;
            }

            if (this.walkableMapTexture == IntPtr.Zero)
            {
                return;
            }

            var player = Core.States.InGameStateObject.CurrentAreaInstance.Player;
            if (!player.TryGetComponent<Render>(out var pRender))
            {
                return;
            }

            var rectf = new RectangleF(
                -pRender.GridPosition.X,
                -pRender.GridPosition.Y,
                this.walkableMapDimension.X,
                this.walkableMapDimension.Y);

            var p1 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Left, rectf.Top), -pRender.TerrainHeight);
            var p2 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Right, rectf.Top), -pRender.TerrainHeight);
            var p3 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Right, rectf.Bottom), -pRender.TerrainHeight);
            var p4 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Left, rectf.Bottom), -pRender.TerrainHeight);
            p1 += mapCenter;
            p2 += mapCenter;
            p3 += mapCenter;
            p4 += mapCenter;
            ImGui.GetWindowDrawList().AddImageQuad(this.walkableMapTexture, p1, p2, p3, p4);
        }

        private void DrawTgtFiles(Vector2 mapCenter)
        {
            var col = UiHelper.Color(
                (uint)(this.Settings.TgtNameColor.X * 255),
                (uint)(this.Settings.TgtNameColor.Y * 255),
                (uint)(this.Settings.TgtNameColor.Z * 255),
                (uint)(this.Settings.TgtNameColor.W * 255));

            var fgDraw = ImGui.GetWindowDrawList();
            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            if (!currentAreaInstance.Player.TryGetComponent<Render>(out var playerRender))
            {
                return;
            }

            var pPos = new Vector2(playerRender.GridPosition.X, playerRender.GridPosition.Y);
            if (this.Settings.ShowAllTgtNames)
            {
                foreach (var tgtKV in currentAreaInstance.TgtTilesLocations)
                {
                    var pNameSizeH = ImGui.CalcTextSize(tgtKV.Key) / 2;
                    for (int i = 0; i < tgtKV.Value.Count; i++)
                    {
                        var val = tgtKV.Value[i];
                        var ePos = new Vector2(val.X, val.Y);
                        var fpos = Helper.DeltaInWorldToMapDelta(
                            ePos - pPos, -playerRender.TerrainHeight + currentAreaInstance.GridHeightData[val.Y][val.X]);
                        if (this.Settings.TgtNameBackground)
                        {
                            fgDraw.AddRectFilled(mapCenter + fpos - pNameSizeH, mapCenter + fpos + pNameSizeH, UiHelper.Color(0, 0, 0, 200));
                        }

                        fgDraw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), mapCenter + fpos - pNameSizeH, col, tgtKV.Key);
                    }
                }
            }
            else if (this.Settings.ShowImportantTgtNames)
            {
                foreach (var tile in this.currentAreaImportantTiles)
                {
                    if (!tile.Value.IsValid())
                    {
                        continue;
                    }

                    for (int i = 0; i < tile.Value.ClustersCount; i++)
                    {
                        var height = 0;
                        var loc = tile.Value.Clusters[i];
                        if (loc.X < currentAreaInstance.GridHeightData[0].Length && loc.Y < currentAreaInstance.GridHeightData.Length)
                        {
                            height = -currentAreaInstance.GridHeightData[(int)loc.Y][(int)loc.X];
                        }

                        var display = tile.Value.Display;
                        var pNameSizeH = ImGui.CalcTextSize(display) / 2;
                        var fpos = Helper.DeltaInWorldToMapDelta(
                            loc - pPos, -playerRender.TerrainHeight + height);
                        if (this.Settings.TgtNameBackground)
                        {
                            fgDraw.AddRectFilled(mapCenter + fpos - pNameSizeH, mapCenter + fpos + pNameSizeH, UiHelper.Color(0, 0, 0, 200));
                        }

                        fgDraw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), mapCenter + fpos - pNameSizeH, col, display);
                    }
                }
            }
        }

        private void DrawMapIcons(Vector2 mapCenter, float iconSizeMultiplier)
        {
            var fgDraw = ImGui.GetWindowDrawList();
            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            if (!currentAreaInstance.Player.TryGetComponent<Render>(out var playerRender))
            {
                return;
            }

            var pPos = new Vector2(playerRender.GridPosition.X, playerRender.GridPosition.Y);
            foreach (var entity in currentAreaInstance.AwakeEntities)
            {
                var hasVital = entity.Value.TryGetComponent<Life>(out var lifeComp);
                var hasBuffs = entity.Value.TryGetComponent<Buffs>(out var buffsComp);
                var isChest = entity.Value.TryGetComponent<Chest>(out var chestComp);
                var hasOMP = entity.Value.TryGetComponent<ObjectMagicProperties>(out var omp);
                var isShrine = entity.Value.TryGetComponent<Shrine>(out var shrineComp);
                var isBlockage = entity.Value.TryGetComponent<TriggerableBlockage>(out var blockageComp);
                var isPlayer = entity.Value.TryGetComponent<Player>(out var playerComp);

                if (this.Settings.HideUseless && !(hasVital || isChest || isPlayer))
                {
                    continue;
                }

                if (this.Settings.HideUseless && isChest && chestComp.IsOpened)
                {
                    continue;
                }

                if (this.Settings.HideUseless && hasVital)
                {
                    if (!lifeComp.IsAlive)
                    {
                        continue;
                    }

                    if (!hasOMP && !isBlockage && !isPlayer)
                    {
                        continue;
                    }
                }

                if (this.Settings.HideUseless && isBlockage && !blockageComp.IsBlocked)
                {
                    continue;
                }

                if (this.Settings.HideUseless && isPlayer && entity.Value.Address == Core.States.InGameStateObject.CurrentAreaInstance.Player.Address)
                {
                    continue;
                }

                if (!entity.Value.TryGetComponent<Positioned>(out var entityPos))
                {
                    continue;
                }

                if (!entity.Value.TryGetComponent<Render>(out var entityRender))
                {
                    continue;
                }

                var ePos = new Vector2(entityRender.GridPosition.X, entityRender.GridPosition.Y);
                var fpos = Helper.DeltaInWorldToMapDelta(
                    ePos - pPos, entityRender.TerrainHeight - playerRender.TerrainHeight);
                var iconSizeMultiplierVector = Vector2.One * iconSizeMultiplier;
                if (isPlayer)
                {
                    if (this.Settings.ShowPlayersNames)
                    {
                        var pNameSizeH = ImGui.CalcTextSize(playerComp.Name) / 2;
                        fgDraw.AddRectFilled(mapCenter + fpos - pNameSizeH, mapCenter + fpos + pNameSizeH, UiHelper.Color(0, 0, 0, 200));
                        fgDraw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), mapCenter + fpos - pNameSizeH, UiHelper.Color(255, 128, 128, 255), playerComp.Name);
                    }
                    else
                    {
                        var playerIcon = this.Settings.BaseIcons["Player"];
                        iconSizeMultiplierVector *= playerIcon.IconScale;
                        fgDraw.AddImage(
                            playerIcon.TexturePtr,
                            mapCenter + fpos - iconSizeMultiplierVector,
                            mapCenter + fpos + iconSizeMultiplierVector,
                            playerIcon.UV0,
                            playerIcon.UV1);
                    }
                }
                else if (isBlockage)
                {
                    var blockageIcon = this.Settings.DelveIcons["Blockage OR DelveWall"];
                    iconSizeMultiplierVector *= blockageIcon.IconScale;
                    fgDraw.AddImage(
                        blockageIcon.TexturePtr,
                        mapCenter + fpos - iconSizeMultiplierVector,
                        mapCenter + fpos + iconSizeMultiplierVector,
                        blockageIcon.UV0,
                        blockageIcon.UV1);
                }
                else if (isChest)
                {
                    if (this.isAzuriteMine)
                    {
                        if (this.delveChestCache.TryGetValue(entity.Key.id, out var iconFinder))
                        {
                            if (this.Settings.DelveIcons.TryGetValue(iconFinder, out var delveChestIcon))
                            {
                                // Have to force keep the Delve Chest since GGG changed
                                // network bubble radius for them.
                                entity.Value.ForceKeepEntity();
                                iconSizeMultiplierVector *= delveChestIcon.IconScale;
                                fgDraw.AddImage(
                                    delveChestIcon.TexturePtr,
                                    mapCenter + fpos - iconSizeMultiplierVector,
                                    mapCenter + fpos + iconSizeMultiplierVector,
                                    delveChestIcon.UV0,
                                    delveChestIcon.UV1);
                            }
                        }
                        else
                        {
                            this.delveChestCache[entity.Key.id] =
                                this.DelveChestPathToIcon(entity.Value.Path);
                        }

                        continue;
                    }

                    if (entity.Value.TryGetComponent<MinimapIcon>(out var _))
                    {
                        if (this.questChests.Contains(entity.Key.id))
                        {
                            continue;
                        }
                        else if (this.heistChestCache.TryGetValue(entity.Key.id, out var iconFinder))
                        {
                            if (this.Settings.HeistIcons.TryGetValue(iconFinder, out var heistChestIcon))
                            {
                                iconSizeMultiplierVector *= heistChestIcon.IconScale;
                                fgDraw.AddImage(
                                    heistChestIcon.TexturePtr,
                                    mapCenter + fpos - iconSizeMultiplierVector,
                                    mapCenter + fpos + iconSizeMultiplierVector,
                                    heistChestIcon.UV0,
                                    heistChestIcon.UV1);
                            }

                            continue;
                        }
                        else if (entity.Value.Path.StartsWith(
                            this.questChestStarting, StringComparison.Ordinal))
                        {
                            this.questChests.Add(entity.Key.id);
                            continue;
                        }
                        else if (entity.Value.Path.StartsWith(
                            this.heistAllChestStarting, StringComparison.Ordinal))
                        {
                            this.heistChestCache[entity.Key.id] =
                                this.HeistChestPathToIcon(entity.Value.Path);
                            continue;
                        }
                    }

                    var chestIcon = this.Settings.BaseIcons["Chest"];
                    iconSizeMultiplierVector *= chestIcon.IconScale;
                    fgDraw.AddImage(
                        chestIcon.TexturePtr,
                        mapCenter + fpos - iconSizeMultiplierVector,
                        mapCenter + fpos + iconSizeMultiplierVector,
                        chestIcon.UV0,
                        chestIcon.UV1);
                }
                else if (isShrine)
                {
                    if (!shrineComp.IsUsed)
                    {
                        var shrineIcon = this.Settings.BaseIcons["Shrine"];
                        iconSizeMultiplierVector *= shrineIcon.IconScale;
                        fgDraw.AddImage(
                            shrineIcon.TexturePtr,
                            mapCenter + fpos - iconSizeMultiplierVector,
                            mapCenter + fpos + iconSizeMultiplierVector,
                            shrineIcon.UV0,
                            shrineIcon.UV1);
                    }

                    continue;
                }
                else if (hasVital)
                {
                    if (hasBuffs && buffsComp.StatusEffects.ContainsKey("frozen_in_time"))
                    {
                        this.frozenInTimeEntities.TryAdd(entity.Key.id, 1);
                        if (buffsComp.StatusEffects.ContainsKey("legion_reward_display") ||
                            entity.Value.Path.Contains("Chest"))
                        {
                            var monsterChestIcon = this.Settings.LegionIcons["Legion Monster Chest"];
                            iconSizeMultiplierVector *= monsterChestIcon.IconScale;
                            fgDraw.AddImage(
                                monsterChestIcon.TexturePtr,
                                mapCenter + fpos - iconSizeMultiplierVector,
                                mapCenter + fpos + iconSizeMultiplierVector,
                                monsterChestIcon.UV0,
                                monsterChestIcon.UV1);
                            continue;
                        }
                    }

                    if (hasBuffs && buffsComp.StatusEffects.ContainsKey("hidden_monster"))
                    {
                        if (this.frozenInTimeEntities.ContainsKey(entity.Key.id))
                        {
                            continue;
                        }

                        if (this.deliriumHiddenMonster.TryGetValue(entity.Key.id, out var iconFinder))
                        {
                            if (this.Settings.DeliriumIcons.TryGetValue(iconFinder, out var dHiddenMIcon))
                            {
                                iconSizeMultiplierVector *= dHiddenMIcon.IconScale;
                                fgDraw.AddImage(
                                    dHiddenMIcon.TexturePtr,
                                    mapCenter + fpos - iconSizeMultiplierVector,
                                    mapCenter + fpos + iconSizeMultiplierVector,
                                    dHiddenMIcon.UV0,
                                    dHiddenMIcon.UV1);
                            }

                            continue;
                        }

                        if (entity.Value.Path.StartsWith(
                            this.deliriumHiddenMonsterStarting,
                            StringComparison.Ordinal))
                        {
                            this.deliriumHiddenMonster[entity.Key.id] =
                                this.DeliriumHiddenMonsterPathToIcon(entity.Value.Path);
                            continue;
                        }
                    }

                    var monsterIcon = entityPos.IsFriendly ?
                        this.Settings.BaseIcons["Friendly"] :
                        this.RarityToIconMapping(omp.Rarity);
                    iconSizeMultiplierVector *= monsterIcon.IconScale;
                    fgDraw.AddImage(
                        monsterIcon.TexturePtr,
                        mapCenter + fpos - iconSizeMultiplierVector,
                        mapCenter + fpos + iconSizeMultiplierVector,
                        monsterIcon.UV0,
                        monsterIcon.UV1);
                }
                else
                {
                    fgDraw.AddCircleFilled(mapCenter + fpos, 5f, UiHelper.Color(255, 0, 255, 255));
                }
            }
        }

        private IEnumerator<Wait> ClearCachesAndUpdateAreaInfo()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                this.frozenInTimeEntities.Clear();
                this.heistChestCache.Clear();
                this.deliriumHiddenMonster.Clear();
                this.delveChestCache.Clear();
                this.currentAreaName = Core.States.InGameStateObject.CurrentAreaInstance.AreaDetails.Id;
                this.isAzuriteMine = this.currentAreaName == "Delve_Main";
                this.GenerateMapTexture();
                this.ClusterImportantTgtName();
            }
        }

        private IEnumerator<Wait> OnMove()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnMoved);
                this.UpdateMiniMapDetails();
                this.UpdateLargeMapDetails();
                if (this.skipOneSettingChange)
                {
                    this.skipOneSettingChange = false;
                }
                else
                {
                    this.Settings.ModifyCullWindow = true;
                }
            }
        }

        private IEnumerator<Wait> OnClose()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnClose);
                this.skipOneSettingChange = true;
                this.currentAreaName = string.Empty;
            }
        }

        private IEnumerator<Wait> OnForegroundChange()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnForegroundChanged);
                this.UpdateMiniMapDetails();
                this.UpdateLargeMapDetails();
            }
        }

        private void UpdateMiniMapDetails()
        {
            var map = Core.States.InGameStateObject.GameUi.MiniMap;
            this.miniMapCenterWithDefaultShift = map.Postion + (map.Size / 2) + map.DefaultShift;

            var widthSq = map.Size.X * map.Size.X;
            var heightSq = map.Size.Y * map.Size.Y;
            this.miniMapDiagonalLength = Math.Sqrt(widthSq + heightSq);
        }

        private void UpdateLargeMapDetails()
        {
            var map = Core.States.InGameStateObject.GameUi.LargeMap;
            var widthSq = map.Size.X * map.Size.X;
            var heightSq = map.Size.Y * map.Size.Y;
            this.largeMapDiagonalLength = Math.Sqrt(widthSq + heightSq);
        }

        private void RemoveMapTexture()
        {
            this.walkableMapTexture = IntPtr.Zero;
            this.walkableMapDimension = Vector2.Zero;
            Core.Overlay.RemoveImage("walkable_map");
        }

        private void GenerateMapTexture()
        {
            this.RemoveMapTexture();
            if (Core.States.GameCurrentState != GameStateTypes.InGameState &&
                Core.States.GameCurrentState != GameStateTypes.EscapeState)
            {
                return;
            }

            var instance = Core.States.InGameStateObject.CurrentAreaInstance;
            var gridHeightData = instance.GridHeightData;
            var mapTextureData = instance.GridWalkableData;
            var bytesPerRow = instance.TerrainMetadata.BytesPerRow;
            var totalRows = mapTextureData.Length / bytesPerRow;
            using Image<Rgba32> image = new Image<Rgba32>(bytesPerRow * 2, totalRows);
            image.Mutate(c => c.ProcessPixelRowsAsVector4((row, i) =>
            {
                for (int x = 0; x < row.Length - 1; x += 2)
                {
                    var terrainHeight = gridHeightData[i.Y][x];
                    var yAxis = i.Y;
                    int yAxisChanges = yAxis - (terrainHeight / 21);
                    if (yAxisChanges >= 0 && yAxisChanges < totalRows)
                    {
                        yAxis = yAxisChanges;
                    }

                    var index = (yAxis * bytesPerRow) + (x / 2);
                    int xAxisChanges = index - (terrainHeight / 41);
                    if (xAxisChanges >= 0 && xAxisChanges < mapTextureData.Length)
                    {
                        index = xAxisChanges;
                    }

                    byte data = mapTextureData[index];

                    // each byte contains 2 data points of size 4 bit.
                    // I know this because if we don't do this, the whole map texture (final output)
                    // has to be multiplied (scaled) by 2 since (at that point) we are eating 1 data point.
                    // In the following loop, in iteration 0, we draw 1st data point and, in iteration 1,
                    // we draw the 2nd data point.
                    for (int k = 0; k < 2; k++)
                    {
                        switch ((data >> (0x04 * k)) & 0x0F)
                        {
                            case 2:
                            case 1: // walkable
                                row[x + k] = this.Settings.WalkableMapColor;
                                break;
                            case 5: // walkable
                            case 4: // walkable
                            case 3:
                            case 0: // non-walable
                                row[x + k] = Vector4.Zero;
                                break;
                            default:
                                throw new Exception($"New digit found {(data >> (0x04 * k)) & 0x0F}");
                        }
                    }
                }
            }));
#if DEBUG
            image.Save(this.DllDirectory + @$"/current_map_{Core.States.InGameStateObject.CurrentAreaInstance.AreaHash}.jpeg");
#endif
            Core.Overlay.AddOrGetImagePointer("walkable_map", image, false, false, out var t, out var w, out var h);
            this.walkableMapTexture = t;
            this.walkableMapDimension = new Vector2(w, h);
        }

        private void ClusterImportantTgtName()
        {
            if (!this.Settings.ImportantTgts.ContainsKey(this.currentAreaName))
            {
                this.currentAreaImportantTiles = new Dictionary<string, TgtClusters>();
                return;
            }

            var currentArea = Core.States.InGameStateObject.CurrentAreaInstance;
            this.currentAreaImportantTiles = this.Settings.ImportantTgts[this.currentAreaName];
            Parallel.ForEach(this.currentAreaImportantTiles, (kv) =>
            {
                if (!currentArea.TgtTilesLocations.ContainsKey(kv.Key))
                {
#if DEBUG
                    Console.WriteLine($"Couldn't find tile name {kv.Key} in area {this.currentAreaName}." +
                        " Please delete/fix Radar plugin config file.");
#endif
                    kv.Value.MakeInvalid();
                }
                else
                {
                    kv.Value.MakeValid();
                    if (kv.Value.ClustersCount == currentArea.TgtTilesLocations[kv.Key].Count)
                    {
                        for (int i = 0; i < kv.Value.ClustersCount; i++)
                        {
                            kv.Value.Clusters[i].X = currentArea.TgtTilesLocations[kv.Key][i].X;
                            kv.Value.Clusters[i].Y = currentArea.TgtTilesLocations[kv.Key][i].Y;
                        }
                    }
                    else
                    {
                        var tgttile = currentArea.TgtTilesLocations[kv.Key];
                        double[][] rawData = new double[tgttile.Count][];
                        double[][] result = new double[kv.Value.ClustersCount][];
                        for (int i = 0; i < kv.Value.ClustersCount; i++)
                        {
                            result[i] = new double[3] { 0, 0, 0 }; // x-sum, y-sum, total-count.
                        }

                        for (int i = 0; i < tgttile.Count; i++)
                        {
                            rawData[i] = new double[2];
                            rawData[i][0] = tgttile[i].X;
                            rawData[i][1] = tgttile[i].Y;
                        }

                        var cluster = KMean.Cluster(rawData, kv.Value.ClustersCount);
                        for (int i = 0; i < tgttile.Count; i++)
                        {
                            int result_index = cluster[i];
                            result[result_index][0] += rawData[i][0];
                            result[result_index][1] += rawData[i][1];
                            result[result_index][2] += 1;
                        }

                        for (int i = 0; i < result.Length; i++)
                        {
                            kv.Value.Clusters[i].X = (float)(result[i][0] / result[i][2]);
                            kv.Value.Clusters[i].Y = (float)(result[i][1] / result[i][2]);
                        }
                    }
                }
            });
        }

        private IconPicker RarityToIconMapping(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Normal:
                case Rarity.Magic:
                case Rarity.Rare:
                case Rarity.Unique:
                    return this.Settings.BaseIcons[$"{rarity} Monster"];
                default:
                    return this.Settings.BaseIcons[$"Normal Monster"];
            }
        }

        private string HeistChestPathToIcon(string path)
        {
            var strToReplace = string.Join('/', this.heistAllChestStarting, this.heistUsefullChestContains);
            var truncatedPath = path
                .Replace(strToReplace, null, StringComparison.Ordinal)
                .Replace("Military", null, StringComparison.Ordinal)
                .Replace("Thug", null, StringComparison.Ordinal)
                .Replace("Science", null, StringComparison.Ordinal)
                .Replace("Robot", null, StringComparison.Ordinal);
            return $"Heist {truncatedPath}";
        }

        private string DeliriumHiddenMonsterPathToIcon(string path)
        {
            if (path.Contains("BloodBag"))
            {
                return "Delirium Bomb";
            }
            else if (path.Contains("EggFodder"))
            {
                return "Delirium Spawner";
            }
            else if (path.Contains("GlobSpawn"))
            {
                return "Delirium Spawner";
            }
            else
            {
                return $"Delirium Ignore";
            }
        }

        private string DelveChestPathToIcon(string path)
        {
            string truncatedPath = path.Replace(
                this.delveChestStarting,
                null,
                StringComparison.Ordinal);

            if (truncatedPath.Length != path.Length)
            {
                return truncatedPath;
            }

            return "Delve Ignore";
        }

        private void AddNewTileBox()
        {
            var tgttilesInArea = Core.States.InGameStateObject.CurrentAreaInstance.TgtTilesLocations;
            ImGui.Text("Leave display name empty if you want to use tile name as display name.");
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 1.3f);
            ImGui.InputText("Area Name", ref this.currentAreaName, 200, ImGuiInputTextFlags.ReadOnly);
            UiHelper.IEnumerableComboBox("Tile Name", tgttilesInArea.Keys, ref this.tmpTileName);
            ImGui.InputText("Display Name", ref this.tmpDisplayName, 200);
            ImGui.Text("Set expected tile count to zero to show all tiles of that name.");
            ImGui.DragInt("Expected Tile Count", ref this.tmpExpectedClusters, 0.01f, 0, 10);
            ImGui.PopItemWidth();
            if (ImGui.Button("Add Tile Name"))
            {
                if (!string.IsNullOrEmpty(this.currentAreaName) &&
                    !string.IsNullOrEmpty(this.tmpTileName))
                {
                    if (this.tmpExpectedClusters == 0)
                    {
                        this.tmpExpectedClusters = tgttilesInArea[this.tmpTileName].Count;
                    }

                    if (string.IsNullOrEmpty(this.tmpDisplayName))
                    {
                        this.tmpDisplayName = this.tmpTileName;
                    }

                    if (!this.Settings.ImportantTgts.ContainsKey(this.currentAreaName))
                    {
                        this.Settings.ImportantTgts[this.currentAreaName] = new Dictionary<string, TgtClusters>();
                    }

                    this.Settings.ImportantTgts[this.currentAreaName][this.tmpTileName] = new TgtClusters()
                    {
                        Display = this.tmpDisplayName,
                        ClustersCount = this.tmpExpectedClusters,
                        Clusters = new Vector2[this.tmpExpectedClusters],
                    };

                    this.tmpTileName = string.Empty;
                    this.tmpDisplayName = string.Empty;
                    this.tmpExpectedClusters = 1;
                }
            }
        }

        private void DisplayAllImportantTile()
        {
            if (ImGui.TreeNode($"Important Tiles in Area: {this.currentAreaName}##import_time_in_area"))
            {
                if (this.Settings.ImportantTgts.ContainsKey(this.currentAreaName))
                {
                    foreach (var tgt in this.Settings.ImportantTgts[this.currentAreaName])
                    {
                        if (ImGui.SmallButton($"Delete##{tgt.Key}"))
                        {
                            this.Settings.ImportantTgts[this.currentAreaName].Remove(tgt.Key);
                        }

                        ImGui.SameLine();
                        ImGui.Text($"Tile Name: {tgt.Key}, Expected Clusters: {tgt.Value.ClustersCount}, Display: {tgt.Value.Display}");
                    }
                }

                ImGui.TreePop();
            }
        }
    }
}
