/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Achievements;
using System.Collections.Generic;
using DNA.CastleMinerZ.UI;
using DNA.CastleMinerZ;
using System;
using DNA;

using static ModLoader.LogSystem;

namespace MoreAchievements
{
    /// <summary>
    /// Drop-in replacement for <see cref="CastleMinerZAchievementManager"/> that:
    /// - Builds all vanilla achievements exactly as the base game does.
    /// - Then appends additional custom achievements to the internal list.
    /// </summary>
    internal sealed class ExtendedAchievementManager : CastleMinerZAchievementManager
    {
        #region Fields / Properties / Constructors

        // We track custom achievements separately so commands can distinguish them if desired.
        private readonly List<AchievementManager<CastleMinerZPlayerStats>.Achievement> _customAchievements
            = new List<AchievementManager<CastleMinerZPlayerStats>.Achievement>();

        public IReadOnlyList<AchievementManager<CastleMinerZPlayerStats>.Achievement> CustomAchievements
            => _customAchievements;

        /// <summary>
        /// Difficulty buckets used for coloring and optional grouping
        /// in the achievement browser.
        /// </summary>
        internal enum AchievementDifficulty
        {
            Vanilla = 0,
            Easy,
            Normal,
            Hard,
            Brutal,
            Insane
        }

        /// <summary>
        /// Difficulty assigned to each achievement instance.
        /// Anything not present is treated as <see cref="AchievementDifficulty.Vanilla"/>.
        /// </summary>
        private readonly Dictionary<Achievement, AchievementDifficulty> _difficultyByAchievement
            = new Dictionary<Achievement, AchievementDifficulty>();

        public ExtendedAchievementManager(CastleMinerZGame game)
            : base(game)
        {
        }
        #endregion

        #region Achievement Creation

        /// <summary>
        /// Called by the base <see cref="AchievementManager{T}"/> ctor.
        /// We:
        /// 1) Let the original CMZ manager build all stock achievements.
        /// 2) Register our own custom achievements on top.
        /// </summary>
        public override void CreateAcheivements() // Notice: The game misspells 'Achievements' as 'Acheivements'.
        {
            // 1) Vanilla, unchanged.
            base.CreateAcheivements();
            Log($"Vanilla achievements created: Count={Count}.");

            // 2) Add custom achievements here.
            // Usage: apiName | manager | name | description | difficulty.

            #region Easy

            RegisterCustom(new CraftWoodBlockAchievement    ("ACH_CRAFT_WOODBLOCK",          this, "First Plank",               "Craft your first wood block."),                  AchievementDifficulty.Easy);
            RegisterCustom(new ZedKillerIAchievement        ("ACH_TOTAL_KILLS_50",           this, "Warmed Up",                 "Kill 50 enemies in total."),                     AchievementDifficulty.Easy);
            RegisterCustom(new BlockBreakerIAchievement     ("ACH_BLOCKS_DUG_100",           this, "Breaking Ground",           "Dig 100 blocks of any type."),                   AchievementDifficulty.Easy);
            RegisterCustom(new CraftIronPickaxeAchievement  ("ACH_CRAFT_IRON_PICKAXE",       this, "Iron Age",                  "Craft an Iron Pickaxe."),                        AchievementDifficulty.Easy);
            RegisterCustom(new CraftTorchesAchievement      ("ACH_CRAFT_TORCH_10",           this, "Let There Be Light",        "Craft 10 torches."),                             AchievementDifficulty.Easy);
            RegisterCustom(new TorchlighterIAchievement     ("ACH_TORCH_100",                this, "Torchbearer",               "Use 100 torches."),                              AchievementDifficulty.Easy);
            RegisterCustom(new PlaceTeleporterAchievement   ("ACH_PLACE_TELEPORTER",         this, "First Jump",                "Place your first Teleport Station."),            AchievementDifficulty.Easy);
            RegisterCustom(new PlaceSpawnPointAchievement   ("ACH_PLACE_SPAWN_BASIC",        this, "Home Base",                 "Place a basic spawn point block."),              AchievementDifficulty.Easy);
            RegisterCustom(new PlaceGlassWindowsAchievement ("ACH_PLACE_GLASS_10",           this, "Clear Outlook",             "Place 10 glass window blocks."),                 AchievementDifficulty.Easy);
            RegisterCustom(new PlaceCratesAchievement       ("ACH_PLACE_CRATES_10",          this, "Stash Basics",              "Place 10 crates or storage containers."),        AchievementDifficulty.Easy);
            RegisterCustom(new ArchitectIAchievement        ("ACH_STRUCT_BLOCKS_USED_100",   this, "Raising Walls",             "Place 100 structural building blocks."),         AchievementDifficulty.Easy);

            #endregion

            #region Normal

            RegisterCustom(new LaserSpecialistIAchievement  ("ACH_LASER_KILLS_10",           this, "Laser Tag",                "Kill 10 enemies with laser weapons."),            AchievementDifficulty.Normal);
            RegisterCustom(new ZedKillerIIAchievement       ("ACH_TOTAL_KILLS_250",          this, "Body Count Rising",        "Kill 250 enemies in total."),                     AchievementDifficulty.Normal);
            RegisterCustom(new CrafterIAchievement          ("ACH_CRAFTED_250",              this, "Workshop Regular",         "Craft 250 items in total."),                      AchievementDifficulty.Normal);
            RegisterCustom(new ExplorerIAchievement         ("ACH_DISTANCE_2000",            this, "Trailblazer",              "Reach distance 2,000 from spawn."),               AchievementDifficulty.Normal);
            RegisterCustom(new RockBottomAchievement        ("ACH_DEPTH_62",                 this, "Rock Bottom",              "Reach depth 62 below the surface."),              AchievementDifficulty.Normal);
            RegisterCustom(new DragonSlayerIAchievement     ("ACH_DRAGON_KILL_5",            this, "Scale Scratcher",          "Kill 5 undead dragons."),                         AchievementDifficulty.Normal);
            RegisterCustom(new TntSpecialistIAchievement    ("ACH_TNT_KILLS_25",             this, "Bomb Squad Rookie",        "Kill 25 enemies with TNT."),                      AchievementDifficulty.Normal);
            RegisterCustom(new GrenadeSpecialistIAchievement("ACH_GRENADE_KILLS_25",         this, "Frag Rookie",              "Kill 25 enemies with grenades."),                 AchievementDifficulty.Normal);
            RegisterCustom(new LaserSpecialistIIAchievement ("ACH_LASER_KILLS_50",           this, "Laser Specialist",         "Kill 50 enemies with laser weapons."),            AchievementDifficulty.Normal);
            RegisterCustom(new SpelunkerIAchievement        ("ACH_SPELUNKER_250_ROCK",       this, "Stone Miner",              "Mine 250 stone (rock) blocks."),                  AchievementDifficulty.Normal);
            RegisterCustom(new LumberjackIAchievement       ("ACH_LUMBERJACK_250_WOOD",      this, "Timber!",                  "Dig 250 wood or log blocks."),                    AchievementDifficulty.Normal);
            RegisterCustom(new ZombieSlayerIAchievement     ("ACH_ZOMBIES_100",              this, "Zombie Cleanup",           "Kill 100 zombies with any weapon."),              AchievementDifficulty.Normal);
            RegisterCustom(new SkeletonSlayerIAchievement   ("ACH_SKELETONS_50",             this, "Bone Collector I",         "Kill 50 skeletons with any weapon."),             AchievementDifficulty.Normal);
            RegisterCustom(new TriggerHappyIAchievement     ("ACH_TRIGGER_HAPPY_I",          this, "Spray and Pray",           "Fire an assault rifle 1,000 times."),             AchievementDifficulty.Normal);
            RegisterCustom(new SharpshooterIAchievement     ("ACH_SHARPSHOOTER_I",           this, "Deadeye",                  "Score 250 hits with a sniper rifle."),            AchievementDifficulty.Normal);
            RegisterCustom(new AlwaysArmedIAchievement      ("ACH_ASST_TIMEHELD_10_MIN",     this, "Locked and Loaded",        "Spend 10 minutes holding any assault rifle."),    AchievementDifficulty.Normal);
            RegisterCustom(new SurvivorIAchievement         ("ACH_DAYS_14_CUSTOM",           this, "Two Weeks Later",          "Survive a total of 14 in-game days."),            AchievementDifficulty.Normal);
            RegisterCustom(new CraftAmmoAchievement         ("ACH_CRAFT_AMMO_250",           this, "Ammo Smith",               "Craft 250 stacks of bullets by hand."),           AchievementDifficulty.Normal);
            RegisterCustom(new MasterPickaxeAchievement     ("ACH_CRAFT_ALL_PICKAXES",       this, "Pickaxe Collector",        "Craft every pickaxe variant at least once."),     AchievementDifficulty.Normal);
            RegisterCustom(new MasterSpadeAchievement       ("ACH_CRAFT_ALL_SPADES",         this, "Spade Collector",          "Craft every spade variant at least once."),       AchievementDifficulty.Normal);
            RegisterCustom(new MasterAxeAchievement         ("ACH_CRAFT_ALL_AXES",           this, "Axe Collector",            "Craft every axe variant at least once."),         AchievementDifficulty.Normal);
            RegisterCustom(new GuidedJusticeIAchievement    ("ACH_DRAGON_GUIDED_5",          this, "Guided Missile Diplomacy", "Kill 5 dragons using guided missiles."),          AchievementDifficulty.Normal);
            RegisterCustom(new ArchitectIIAchievement       ("ACH_STRUCT_BLOCKS_USED_1000",  this, "Fortress Rising",          "Place 1,000 structural blocks."),                 AchievementDifficulty.Normal);
            RegisterCustom(new TorchlighterIIAchievement    ("ACH_TORCH_1000",               this, "Torchmaster",              "Use 1,000 torches."),                             AchievementDifficulty.Normal);

            #endregion

            #region Hard

            RegisterCustom(new BlockBreakerIIAchievement    ("ACH_BLOCKS_DUG_5000",          this, "Heavy Excavator",          "Dig 5,000 blocks of any type."),                  AchievementDifficulty.Hard);
            RegisterCustom(new ZedKillerIIIAchievement      ("ACH_MASS_KILLER_2000",         this, "Reapers Due",              "Kill 2,000 enemies of any type."),                AchievementDifficulty.Hard);
            RegisterCustom(new AlienStalkerIAchievement     ("ACH_ALIEN_STALKER_10",         this, "Alien Magnet",             "Trigger 10 alien encounters."),                   AchievementDifficulty.Hard);
            RegisterCustom(new TntSpecialistIIAchievement   ("ACH_TNT_KILLS_100",            this, "Demolition Veteran",       "Kill 100 enemies with TNT."),                     AchievementDifficulty.Hard);
            RegisterCustom(new LaserSpecialistIIIAchievement("ACH_LASER_KILLS_250",          this, "Laser Surgeon",            "Kill 250 enemies with laser weapons."),           AchievementDifficulty.Hard);
            RegisterCustom(new CrafterIIAchievement         ("ACH_CRAFTED_2000",             this, "Production Manager",       "Craft a total of 2,000 items."),                  AchievementDifficulty.Hard);
            RegisterCustom(new ZombieSlayerIIAchievement    ("ACH_ZOMBIES_500",              this, "Zombie Eradicator",        "Kill 500 zombies with any weapons."),             AchievementDifficulty.Hard);
            RegisterCustom(new SkeletonSlayerIIAchievement  ("ACH_SKELETONS_250",            this, "Ossuary Keeper",           "Kill 250 skeletons with any weapons."),           AchievementDifficulty.Hard);
            RegisterCustom(new DragonSlayerIIAchievement    ("ACH_DRAGON_KILL_25",           this, "Dragon Hunter II",         "Kill 25 undead dragons."),                        AchievementDifficulty.Hard);
            RegisterCustom(new DragonSlayerIIIAchievement   ("ACH_DRAGON_KILL_50",           this, "Dragon Hunter III",        "Kill 50 undead dragons."),                        AchievementDifficulty.Hard);
            RegisterCustom(new ExplorerIIAchievement        ("ACH_MAXDIST_10000",            this, "Beyond the Horizon",       "Reach a distance of 10,000 units from spawn."),   AchievementDifficulty.Hard);
            RegisterCustom(new ExplorerIIIAchievement       ("ACH_MAXDIST_20000",            this, "Point of No Return",       "Reach a distance of 20,000 units from spawn."),   AchievementDifficulty.Hard);
            RegisterCustom(new AlwaysArmedIIAchievement     ("ACH_ASST_TIMEHELD_60_MIN",     this, "Married to the Rifle",     "Spend 60 minutes holding any assault rifle."),    AchievementDifficulty.Hard);
            RegisterCustom(new MasterToolsmithAchievement   ("ACH_MASTER_TOOLSMITH",         this, "Master Toolsmith",         "Craft every tool variant at least once."),        AchievementDifficulty.Hard);
            RegisterCustom(new ArchitectIIIAchievement      ("ACH_STRUCT_BLOCKS_USED_10000", this, "Master Architect",         "Place 10,000 structural blocks."),                AchievementDifficulty.Hard);
            RegisterCustom(new GuidedJusticeIIAchievement   ("ACH_DRAGON_GUIDED_50",         this, "Guided Justice",           "Kill 50 dragons using guided missiles."),         AchievementDifficulty.Hard);
            // RegisterCustom(new HellhoundSlayerAchievement("ACH_HELL_250",                 this, "Hells Janitor",            "Kill 250 enemies in Hell."),                      AchievementDifficulty.Hard);
            // RegisterCustom(new SessionVeteranAchievement ("ACH_GAMESPLAYED_100",          this, "Seasoned Regular",         "Play 100 games total."),                          AchievementDifficulty.Hard);

            #endregion

            #region Brutal

            RegisterCustom(new SurvivorIIAchievement        ("ACH_DAYS_30_CUSTOM",           this, "Thirty Days Strong",       "Survive a total of 30 in-game days."),            AchievementDifficulty.Brutal);
            RegisterCustom(new SurvivorIIIAchievement       ("ACH_DAYS_60_CUSTOM",           this, "Unbroken",                 "Survive a total of 60 in-game days."),            AchievementDifficulty.Brutal);
            RegisterCustom(new SurvivorIVAchievement        ("ACH_DAYS_120_CUSTOM",          this, "Too Stubborn to Die",      "Survive a total of 120 in-game days."),           AchievementDifficulty.Brutal);
            RegisterCustom(new ZedKillerIVAchievement       ("ACH_MASS_KILLER_10000",        this, "Massacre",                 "Kill 10,000 enemies of any type."),               AchievementDifficulty.Brutal);
            RegisterCustom(new ExplorerIVAchievement        ("ACH_MAXDIST_40000",            this, "Road Trip from Hell",      "Reach a distance of 40,000 units from spawn."),   AchievementDifficulty.Brutal);
            RegisterCustom(new ExplorerVAchievement         ("ACH_MAXDIST_46340",            this, "The Final Frontier",       "Reach the towering wall of bedrock & lanterns."), AchievementDifficulty.Brutal);
            RegisterCustom(new DragonSlayerIVAchievement    ("ACH_DRAGONS_100",              this, "Dragon Hunter IV",         "Kill 100 undead dragons."),                       AchievementDifficulty.Brutal);
            RegisterCustom(new DemolitionistIAchievement    ("ACH_EXPLOSIVE_KILLS_1000",     this, "One-Man Bomb Squad",       "Kill 1,000 enemies with explosives (TNT/Gr)."),   AchievementDifficulty.Brutal);
            RegisterCustom(new CrafterIIIAchievement        ("ACH_CRAFTED_10000",            this, "Factory Overlord",         "Craft a total of 10,000 items."),                 AchievementDifficulty.Brutal);
            RegisterCustom(new ZedKillerVAchievement        ("ACH_MASS_KILLER_50000",        this, "Apocalypse",               "Kill 50,000 enemies of any type."),               AchievementDifficulty.Brutal);
            RegisterCustom(new BlockBreakerIIIAchievement   ("ACH_BLOCKS_DUG_25000",         this, "Master Excavator",         "Dig 25,000 blocks of any type."),                 AchievementDifficulty.Brutal);

            #endregion

            #region Insane

            RegisterCustom(new MasterExplorerAchievement    ("ACH_MAXDIST_65536",            this, "Edge of the World",        "Cross a mysterious land made of lanterns."),      AchievementDifficulty.Insane);
            RegisterCustom(new SurvivorVAchievement         ("ACH_DAYS_730",                 this, "The Indomitable Survivor", "Survive for a total of 730 in-game days."),       AchievementDifficulty.Insane);
            RegisterCustom(new MasterProspectorAchievement  ("ACH_MINE_EVERY_BLOCK",         this, "Master Prospector",        "Mine at least one of every diggable block."),     AchievementDifficulty.Insane);
            RegisterCustom(new MasterCrafterAchievement     ("ACH_CRAFT_EVERY_ITEM",         this, "Master Crafter",           "Craft at least one of every craftable item."),    AchievementDifficulty.Insane);
            RegisterCustom(new CompletionistAchievement     ("ACH_COMPLETE_ALL",             this, "Completionist",            "Unlock every achievement in CastleMiner Z."),     AchievementDifficulty.Insane);
            RegisterCustom(new MasterOfAllThingsAchievement ("ACH_TIMEHELD_ALLITEMS_1_MIN",  this, "Hands-On Mastery",         "Hold every craftable item for 1 minute each."),   AchievementDifficulty.Insane);
            RegisterCustom(new BlockBreakerIVAchievement    ("ACH_BLOCKS_DUG_50000",         this, "Excavation Legend",        "Dig 50,000 blocks of any type."),                 AchievementDifficulty.Insane);

            #endregion

            Log($"Total achievements after extension: Count={Count} (vanilla + custom).");
        }
        #endregion

        #region Achievement Lifecycle Overrides (OnAchieved)

        public override void OnAchieved(
            AchievementManager<CastleMinerZPlayerStats>.Achievement achievement)
        {
            if (achievement == null)
                return;

            // 1) Keep vanilla behavior (Steam integration, etc.)
            base.OnAchieved(achievement);

            // 2) Let achievements that implement IAchievementReward grant their own rewards.
            try
            {
                // Live stats backing this manager (same object passed into the ctor).
                var stats = AchievementResetHelper.GetManagerStats(this);
                if (stats != null && achievement is IAchievementReward reward)
                {
                    reward.GrantReward(stats);
                }
            }
            catch (Exception ex)
            {
                Log($"Reward grant failed for '{achievement.APIName ?? achievement.Name}': {ex}.");
            }

            // 3) Work out whether this is one of our custom achievements.
            bool isCustom = _customAchievements.Contains(achievement);

            // 4) Decide whether to show a HUD popup based on config.
            switch (AchievementUIConfig.ShowAchievementPopupMode)
            {
                case AchievementPopupMode.None:
                    return;     // Never show any HUD toast.

                case AchievementPopupMode.Custom:
                    if (!isCustom)
                        return; // Only pop for our custom achievements.
                    break;

                case AchievementPopupMode.Steam:
                    if (isCustom)
                        return; // Only pop for vanilla/Steam achievements.
                    break;

                case AchievementPopupMode.All:
                default:
                    break;      // Always allow.
            }

            // 5) Reuse the stock HUD popup (our Harmony patches then skin it).
            var hud = InGameHUD.Instance;
            hud?.DisplayAcheivement(achievement);
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Registers a custom achievement and records its difficulty tier.
        /// </summary>
        private void RegisterCustom(Achievement achievement, AchievementDifficulty difficulty)
        {
            if (achievement == null)
                return;

            base.AddAcheivement(achievement);     // Appends to internal list used by Count/Update().
            _customAchievements.Add(achievement); // Track separately for stats/commands.
            _difficultyByAchievement[achievement] = difficulty;

            // Log($"Registered custom achievement '{achievement.Name}' ({achievement.APIName}) [{difficulty}].");
        }

        /// <summary>
        /// Backwards-compatible helper that defaults to Normal difficulty.
        /// </summary>
        private void RegisterCustom(Achievement achievement)
        {
            RegisterCustom(achievement, AchievementDifficulty.Normal);
        }

        /// <summary>
        /// Returns the difficulty bucket for the given achievement.
        /// Vanilla or unknown achievements are treated as Vanilla.
        /// </summary>
        internal AchievementDifficulty GetDifficulty(Achievement achievement)
        {
            if (achievement == null)
                return AchievementDifficulty.Vanilla;

            if (_difficultyByAchievement.TryGetValue(achievement, out var diff))
                return diff;

            return AchievementDifficulty.Vanilla;
        }
        #endregion
    }
}