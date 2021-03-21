﻿// <copyright file="SettingsWindow.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Settings
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using ClickableTransparentOverlay;
    using Coroutine;
    using GameHelper.CoroutineEvents;
    using GameHelper.Plugin;
    using GameHelper.Utils;
    using ImGuiNET;

    /// <summary>
    /// Creates the MainMenu on the UI.
    /// </summary>
    internal static class SettingsWindow
    {
        private static bool isSettingsWindowVisible = true;
        private static string currentlySelectedPlugin = "Core";

        /// <summary>
        /// Initializes the Main Menu.
        /// </summary>
        internal static void InitializeCoroutines()
        {
            CoroutineHandler.Start(SaveGameHelperSettings());
            CoroutineHandler.Start(DrawSettingsWindow());
        }

        /// <summary>
        /// Draws the (core/plugins) names as ImGui buttons in a single group.
        /// </summary>
        private static void DrawNames()
        {
            var totalWidthAvailable = ImGui.GetContentRegionAvail().X * 0.2f;
            var buttonSize = new Vector2(totalWidthAvailable, 0);
            ImGui.PushItemWidth(totalWidthAvailable);
            ImGui.BeginGroup();
            bool tmp = true;
            ImGui.Checkbox("##CoreEnableCheckBox", ref tmp);
            ImGui.SameLine();
            if (ImGui.Button("Core##ShowSettingsButton", buttonSize))
            {
                currentlySelectedPlugin = "Core";
            }

            foreach (var pKeyValue in PManager.AllPlugins.ToList())
            {
                var pluginContainer = pKeyValue.Value;
                tmp = pluginContainer.Enable;
                if (ImGui.Checkbox($"##{pKeyValue.Key}EnableCheckbox", ref tmp))
                {
                    pluginContainer.Enable = !pluginContainer.Enable;
                    if (pluginContainer.Enable)
                    {
                        pluginContainer.Plugin.OnEnable();
                    }
                    else
                    {
                        pluginContainer.Plugin.OnDisable();
                    }

                    PManager.AllPlugins[pKeyValue.Key] = pluginContainer;
                }

                ImGui.SameLine();
                if (ImGui.Button($"{pKeyValue.Key}##ShowSettingsButton", buttonSize))
                {
                    currentlySelectedPlugin = pKeyValue.Key;
                }
            }

            ImGui.PopItemWidth();
            ImGui.EndGroup();
        }

        /// <summary>
        /// Draws the currently selected settings on ImGui.
        /// </summary>
        private static void DrawCurrentlySelectedSettings()
        {
            switch (currentlySelectedPlugin)
            {
                case "Core":
                    ImGui.BeginGroup();
                    ImGui.TextWrapped("NOTE: (Plugins/Core) Settings are saved automatically " +
                        "when you close the overlay or hide it via F12 button.");
                    ImGui.NewLine();
                    ImGui.Text($"Current Game State: {Core.States.GameCurrentState}");
                    ImGui.NewLine();
                    ImGui.TextWrapped($"Set the window scale value (e.g. 100 -> 1, 125 -> 1.25" +
                        $", 150 -> 1.50 etc). Restart the overlay after setting the value.");
                    ImGui.DragFloat("##WinScale", ref Core.GHSettings.WindowScale, 0.25f, 1f, 5f);
                    ImGui.NewLine();
                    ImGui.Checkbox("Performance Stats", ref Core.GHSettings.ShowPerfStats);
                    ImGui.Checkbox("Game UiExplorer", ref Core.GHSettings.ShowGameUiExplorer);
                    ImGui.Checkbox("Data Visualization", ref Core.GHSettings.ShowDataVisualization);
                    ImGui.EndGroup();
                    break;
                default:
                    if (PManager.AllPlugins.TryGetValue(currentlySelectedPlugin, out var pContainer))
                    {
                        ImGui.BeginGroup();
                        pContainer.Plugin.DrawSettings();
                        ImGui.EndGroup();
                    }

                    break;
            }
        }

        /// <summary>
        /// Draws the Settings Window.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private static IEnumerator<Wait> DrawSettingsWindow()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (NativeMethods.IsKeyPressedAndNotTimeout(Core.GHSettings.MainMenuHotKey))
                {
                    isSettingsWindowVisible = !isSettingsWindowVisible;
                    if (!isSettingsWindowVisible)
                    {
                        CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
                    }
                }

                if (!isSettingsWindowVisible)
                {
                    continue;
                }

                var isMainMenuExpanded = ImGui.Begin(
                    "Game Overlay Settings Menu",
                    ref Core.GHSettings.IsOverlayRunning);
                if (!Core.GHSettings.IsOverlayRunning)
                {
                    CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
                }

                if (!isMainMenuExpanded)
                {
                    ImGui.End();
                    continue;
                }

                DrawNames();
                ImGui.SameLine();
                DrawCurrentlySelectedSettings();
                ImGui.End();
            }
        }

        /// <summary>
        /// Saves the GameHelper settings to disk.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private static IEnumerator<Wait> SaveGameHelperSettings()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.TimeToSaveAllSettings);
                JsonHelper.SafeToFile(Core.GHSettings, State.CoreSettingFile);
            }
        }
    }
}
