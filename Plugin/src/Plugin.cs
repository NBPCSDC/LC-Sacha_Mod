﻿using System.Reflection;
using UnityEngine;
using BepInEx;
using LethalLib.Modules;
using BepInEx.Logging;
using System.IO;
using Sacha_Mod.Configuration;
using System.Collections.Generic;


namespace Sacha_Mod {
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)] 
    public class Plugin : BaseUnityPlugin {
        internal static new ManualLogSource Logger = null!;
        internal static PluginConfig BoundConfig { get; private set; } = null!;
        public static AssetBundle? ModAssets;

        private void Awake() {
            Logger = base.Logger;

            // If you don't want your mod to use a configuration file, you can remove this line, Configuration.cs, and other references.
            //BoundConfig = new PluginConfig(base.Config);

            // This should be ran before Network Prefabs are registered.
            InitializeNetworkBehaviours();

            // We load the asset bundle that should be next to our DLL file, with the specified name.
            // You may want to rename your asset bundle from the AssetBundle Browser in order to avoid an issue with
            // asset bundle identifiers being the same between multiple bundles, allowing the loading of only one bundle from one mod.
            // In that case also remember to change the asset bundle copying code in the csproj.user file.
            var bundleName = "sachamodassets";
            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), bundleName));
            if (ModAssets == null) {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }

            // We load our assets from our asset bundle. Remember to rename them both here and in our Unity project.
            var Sacha_Mod = ModAssets.LoadAsset<EnemyType>("Sacha");    
            var Sacha_ModTN = ModAssets.LoadAsset<TerminalNode>("SachaTN");
            var Sacha_ModTK = ModAssets.LoadAsset<TerminalKeyword>("SachaTK");

            // Optionally, we can list which levels we want to add our enemy to, while also specifying the spawn weight for each.

            var Sacha_ModLevelRarities = new Dictionary<Levels.LevelTypes, int> {
                {Levels.LevelTypes.ExperimentationLevel, 50},
                {Levels.LevelTypes.AssuranceLevel, 20},
                {Levels.LevelTypes.VowLevel, 15},
                {Levels.LevelTypes.OffenseLevel, 15},
                {Levels.LevelTypes.MarchLevel, 60},
                {Levels.LevelTypes.RendLevel, 10},
                {Levels.LevelTypes.DineLevel, 35},
                {Levels.LevelTypes.TitanLevel, 63},
                {Levels.LevelTypes.All, 20},     // Affects unset values, with lowest priority (gets overridden by Levels.LevelTypes.Modded)
                {Levels.LevelTypes.Modded, 20},     // Affects values for modded moons that weren't specified
            };
            // We can also specify custom level rarities
            var Sacha_ModCustomLevelRarities = new Dictionary<string, int> {
                {"EGyptLevel", 50},
                {"46 Infernis", 69},    // Either LLL or LE(C) name can be used, LethalLib will handle both
            };


            // Network Prefabs need to be registered. See https://docs-multiplayer.unity3d.com/netcode/current/basics/object-spawning/
            // LethalLib registers prefabs on GameNetworkManager.Start.
            NetworkPrefabs.RegisterNetworkPrefab(Sacha_Mod.enemyPrefab);

            // For different ways of registering your enemy, see https://github.com/EvaisaDev/LethalLib/blob/main/LethalLib/Modules/Enemies.cs
            //Enemies.RegisterEnemy(Sacha_Mod, BoundConfig.SpawnWeight.Value, Levels.LevelTypes.All, Sacha_ModTN, Sacha_ModTK);
            // For using our rarity tables, we can use the following:
            Enemies.RegisterEnemy(Sacha_Mod, Sacha_ModLevelRarities, Sacha_ModCustomLevelRarities, Sacha_ModTN, Sacha_ModTK);
            
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        private static void InitializeNetworkBehaviours() {
            // See https://github.com/EvaisaDev/UnityNetcodePatcher?tab=readme-ov-file#preparing-mods-for-patching
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        } 
    }
}