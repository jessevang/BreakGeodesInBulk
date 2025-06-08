
using GenericModConfigMenu;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace BreakGeodesInBulk
{
    public class ModConfig
    {
        public GeodeBreakMode GeodesToBreak { get; set; } = GeodeBreakMode.AllIfInventoryFits;
        public float AnimationSpeedMultiplier { get; set; } = 0.3f;
        public int OverlayOffsetX { get; set; } = 40;
        public int OverlayOffsetY { get; set; } = 60;
        public float OverlayScale { get; set; } = 1.0f;
        public bool UseMobileGeodeFix { get; set; } = false;
        public bool DebugMode { get; set; } = false; 
    }


    public enum GeodeBreakMode
    {
        AllIfInventoryFits,
        AllExtraFallsOnGround
    }


    public class ModEntry : Mod
    {
        internal static ModConfig Config;
        private static int showBreakAmountTimer = 0;
        private static int lastBreakAmount = 0;
        public static ModEntry Instance { get; private set; }


        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            Instance = this;

            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.Patch(
                original: AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.receiveLeftClick)),
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(ReceiveLeftClick_Prefix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(GeodeMenu), "draw", new[] { typeof(SpriteBatch) }),
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(DrawOverlay_Postfix))
            );

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        }

        private void OnGameLaunched(object sender, StardewModdingAPI.Events.GameLaunchedEventArgs e)
        {
            generateGMCM();
        }

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (!Config.DebugMode)
                return;

            Monitor.Log(message, level);
        }



        private void generateGMCM()
        {
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm == null)
                return;

            gmcm.Register(
                mod: ModManifest,
                reset: () => Config = new ModConfig(),
                save: () => Helper.WriteConfig(Config)
            );

            gmcm.AddSectionTitle(ModManifest, () => "Geode Breaking Options");

            gmcm.AddTextOption(
                mod: ModManifest,
                name: () => "Geode Break Mode",
                tooltip: () => "Choose how geodes are broken when inventory is full.",
                getValue: () => Config.GeodesToBreak.ToString(),
                setValue: value =>
                {
                    if (Enum.TryParse<GeodeBreakMode>(value, true, out var result))
                        Config.GeodesToBreak = result;
                },
                allowedValues: Enum.GetNames(typeof(GeodeBreakMode)),
                formatAllowedValue: value =>
                {
                    return value switch
                    {
                        nameof(GeodeBreakMode.AllIfInventoryFits) => "All (If Inventory Fits)",
                        nameof(GeodeBreakMode.AllExtraFallsOnGround) => "All (Extra Falls On Ground)",
                        _ => value
                    };
                }
            );


            gmcm.AddNumberOption(
                mod: ModManifest,
                name: () => "Animation Speed Multiplier",
                tooltip: () => "Adjust how fast Clint breaks geodes. Lower = faster. Example: 0.5 = 2x faster.",
                getValue: () => Config.AnimationSpeedMultiplier,
                setValue: value => Config.AnimationSpeedMultiplier = value,
                min: 0.1f,
                max: 1f,
                interval: 0.1f
            );

            gmcm.AddBoolOption(
                mod: ModManifest,
                name: () => "Mobile Compatibility Mode",
                tooltip: () => "Enable this if you're playing on Android/mobile to prevent game crashes.",
                getValue: () => Config.UseMobileGeodeFix,
                setValue: value => Config.UseMobileGeodeFix = value
            );

            gmcm.AddBoolOption(
                mod: ModManifest,
                name: () => "Enable Debug Mode",
                tooltip: () => "Enable verbose logging to help with debugging. Turn off for normal gameplay.",
                getValue: () => Config.DebugMode,
                setValue: value => Config.DebugMode = value
            );



            /* Used for troubleshooting the number value but not needed, commenting out.

            gmcm.AddNumberOption(
                mod: ModManifest,
                name: () => "Overlay Offset X",
                tooltip: () => "Horizontal offset of geode count text. Positive moves right, negative moves left.",
                getValue: () => Config.OverlayOffsetX,
                setValue: value => Config.OverlayOffsetX = value
            );

            gmcm.AddNumberOption(
                mod: ModManifest,
                name: () => "Overlay Offset Y",
                tooltip: () => "Vertical offset of geode count text. Positive moves down, negative moves up.",
                getValue: () => Config.OverlayOffsetY,
                setValue: value => Config.OverlayOffsetY = value
            );

            gmcm.AddNumberOption(
                mod: ModManifest,
                name: () => "Overlay Font Scale",
                tooltip: () => "Size of the overlay number. Example: 0.6 = 60% size, 1.0 = full size.",
                getValue: () => Config.OverlayScale,
                setValue: value => Config.OverlayScale = MathF.Max(0.1f, MathF.Min(2f, value)) // clamps between 0.1 and 2.0
            );

            */

        }


        
        private static bool ReceiveLeftClick_Prefix(GeodeMenu __instance, int x, int y, bool playSound)
        {

            if (Config.UseMobileGeodeFix)
            {
                return HandleMobileGeodeClick(__instance, x, y);
            }

            ModEntry.Instance.Log("Start ReceiveLeftClick_Prefix", LogLevel.Info);

            try
            {
                if (__instance.waitingForServerResponse)
                {
                    ModEntry.Instance.Log("Aborted: waitingForServerResponse is true", LogLevel.Info);
                    return true;
                }

                if (!__instance.geodeSpot.containsPoint(x, y))
                {
                    ModEntry.Instance.Log("Aborted: click not on geode spot", LogLevel.Info);
                    return true;
                }

                ModEntry.Instance.Log("Click is valid, checking held item...", LogLevel.Info);

                Item held = null;
                try
                {
                    held = __instance.heldItem;
                    if (held == null)
                        throw new Exception("held item is null");

                    ModEntry.Instance.Log($"Held item: {held.DisplayName} ({held.QualifiedItemId})", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Log($"[ERROR] Failed to get held item: {ex}", LogLevel.Error);
                    return true;
                }

                try
                {
                    if (!Utility.IsGeode(held))
                    {
                        ModEntry.Instance.Log("Aborted: held item is not a geode", LogLevel.Info);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Log($"[ERROR] IsGeode check failed: {ex}", LogLevel.Error);
                    return true;
                }

                if (Game1.player.Money < 25)
                {
                    ModEntry.Instance.Log("Aborted: not enough money", LogLevel.Info);
                    return true;
                }

                if (__instance.geodeAnimationTimer > 0)
                {
                    ModEntry.Instance.Log("Aborted: geodeAnimationTimer active", LogLevel.Info);
                    return true;
                }

                int heldStack = 0, maxAffordable = 0, maxBreakable = 0;
                try
                {
                    heldStack = held.Stack;
                    maxAffordable = Game1.player.Money / 25;
                    maxBreakable = Math.Min(heldStack, maxAffordable);
                    ModEntry.Instance.Log($"heldStack={heldStack}, maxAffordable={maxAffordable}, maxBreakable={maxBreakable}", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Log($"[ERROR] Stack or affordability calculation failed: {ex}", LogLevel.Error);
                    return true;
                }

                if (maxBreakable <= 0)
                {
                    __instance.descriptionText = Game1.content.LoadString(@"Strings\UI:GeodeMenu_InventoryFull");
                    __instance.wiggleWordsTimer = 500;
                    __instance.alertTimer = 1500;
                    ModEntry.Instance.Log("Aborted: maxBreakable <= 0", LogLevel.Info);
                    return false;
                }

                int targetAmount = 1;
                try
                {
                    int freeSlots = Game1.player.freeSpotsInInventory();
                    ModEntry.Instance.Log($"freeSlots={freeSlots}", LogLevel.Info);

                    targetAmount = Config.GeodesToBreak switch
                    {
                        GeodeBreakMode.AllIfInventoryFits =>
                            heldStack <= freeSlots
                                ? Math.Min(heldStack, maxBreakable)
                                : Math.Max(0, Math.Min(maxBreakable, freeSlots - 1)),

                        GeodeBreakMode.AllExtraFallsOnGround => maxBreakable,
                        _ => 1
                    };

                    ModEntry.Instance.Log($"targetAmount={targetAmount}", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Log($"[ERROR] Failed to calculate target amount: {ex}", LogLevel.Error);
                    return true;
                }

                if (targetAmount <= 0)
                {
                    Game1.showRedMessage("Not enough inventory space for geode rewards.");
                    return false;
                }

                showBreakAmountTimer = (int)(120 * Config.AnimationSpeedMultiplier);
                lastBreakAmount = targetAmount;

                List<Item> rewards = new();
                Random backupRandom = Game1.random;

                try
                {
                    ModEntry.Instance.Log("Generating rewards...", LogLevel.Info);

                    for (int i = 0; i < targetAmount - 1; i++)
                    {
                        Item tempGeode = held.getOne();
                        ModEntry.Instance.Log($"Processing geode #{i + 1}: {tempGeode?.QualifiedItemId ?? "null"}", LogLevel.Trace);

                        if (tempGeode.QualifiedItemId == "(O)791" && !Game1.netWorldState.Value.GoldenCoconutCracked)
                        {
                            rewards.Add(ItemRegistry.Create("(O)73"));
                            Game1.netWorldState.Value.GoldenCoconutCracked = true;
                            continue;
                        }

                        if (tempGeode.QualifiedItemId == "(O)MysteryBox" || tempGeode.QualifiedItemId == "(O)GoldenMysteryBox")
                        {
                            Game1.stats.Increment("MysteryBoxesOpened");
                        }
                        else
                        {
                            Game1.stats.GeodesCracked++;
                        }

                        Game1.random = Utility.CreateRandom(
                            Game1.uniqueIDForThisGame,
                            Game1.stats.DaysPlayed,
                            Game1.timeOfDay + Game1.random.Next()
                        );

                        Item reward = Utility.getTreasureFromGeode(tempGeode);
                        ModEntry.Instance.Log($"Generated reward: {reward?.QualifiedItemId ?? "null"}", LogLevel.Trace);
                        rewards.Add(reward);
                    }
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Log($"[ERROR] Failed during reward generation: {ex}", LogLevel.Error);
                    Game1.random = backupRandom;
                    return true;
                }

                Game1.random = backupRandom;

                try
                {
                    __instance.geodeSpot.item = held.getOne();
                    held.Stack -= targetAmount;
                    if (held.Stack <= 0)
                    {
                        __instance.heldItem = null;
                        ModEntry.Instance.Log("Held item stack is now 0; clearing", LogLevel.Info);
                    }

                    Game1.player.Money -= 25 * targetAmount;
                    ModEntry.Instance.Log("Deducted money", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Log($"[ERROR] Failed to update inventory or money: {ex}", LogLevel.Error);
                    return true;
                }

                try
                {
                    Game1.playSound("stoneStep");
                    __instance.clint.setCurrentAnimation(new List<FarmerSprite.AnimationFrame>
            {
                new(8, (int)(300 * Config.AnimationSpeedMultiplier)),
                new(9, (int)(200 * Config.AnimationSpeedMultiplier)),
                new(10, (int)(80 * Config.AnimationSpeedMultiplier)),
                new(11, (int)(200 * Config.AnimationSpeedMultiplier)),
                new(12, (int)(100 * Config.AnimationSpeedMultiplier)),
                new(8, (int)(300 * Config.AnimationSpeedMultiplier))
            });
                    __instance.clint.loop = false;
                    ModEntry.Instance.Log("Clint animation set", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Log($"[ERROR] Failed to set Clint animation or play sound: {ex}", LogLevel.Error);
                }

                try
                {
                    Game1.delayedActions.Add(new DelayedAction((int)(2700 * Config.AnimationSpeedMultiplier), () =>
                    {
                        try
                        {
                            ModEntry.Instance.Log("Reward delivery triggered", LogLevel.Info);

                            foreach (Item reward in rewards)
                            {
                                if (!Game1.player.addItemToInventoryBool(reward))
                                {
                                    ModEntry.Instance.Log($"Dropping reward on ground: {reward?.QualifiedItemId ?? "null"}", LogLevel.Trace);
                                    Game1.createItemDebris(reward, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
                                }
                                else
                                {
                                    ModEntry.Instance.Log($"Reward added to inventory: {reward?.QualifiedItemId ?? "null"}", LogLevel.Trace);
                                }
                            }
                        }
                        catch (Exception ex2)
                        {
                            ModEntry.Instance.Log($"[ERROR] Exception during reward delivery: {ex2}", LogLevel.Error);
                        }
                    }));

                    __instance.geodeAnimationTimer = (int)(2700 * Config.AnimationSpeedMultiplier);
                    ModEntry.Instance.Log("Set geodeAnimationTimer and delayed reward action", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Log($"[ERROR] Failed to schedule delayed action or set animation timer: {ex}", LogLevel.Error);
                }

                ModEntry.Instance.Log("Finished ReceiveLeftClick_Prefix", LogLevel.Info);
                return false;
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Log($"[CRITICAL ERROR] Exception in ReceiveLeftClick_Prefix: {ex}", LogLevel.Error);
                return true; 
            }
        }



        private static void DrawOverlay_Postfix(GeodeMenu __instance, SpriteBatch b)
        {
            if (showBreakAmountTimer > 0)
            {
                showBreakAmountTimer--;

                string text = $"x{lastBreakAmount}";
                SpriteFont font = Game1.smallFont;
                float scale = Config.OverlayScale;

                Vector2 textSize = font.MeasureString(text) * scale;

  
                Vector2 drawPosition = new Vector2(
                    __instance.geodeSpot.bounds.X + 360 + Config.OverlayOffsetX,
                    __instance.geodeSpot.bounds.Y + 160 + Config.OverlayOffsetY
                );

                drawPosition.X -= textSize.X / 2;


                b.DrawString(font, text, drawPosition + new Vector2(2, 2), Color.Black * 0.75f, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);

                b.DrawString(font, text, drawPosition, Color.Yellow, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
            }
        }



        private static bool HandleMobileGeodeClick(GeodeMenu __instance, int x, int y)
        {
            ModEntry.Instance.Log("Start HandleMobileGeodeClick", LogLevel.Info);

            if (__instance.waitingForServerResponse)
            {
                ModEntry.Instance.Log("Aborted: waitingForServerResponse is true", LogLevel.Info);
                return true;
            }

            int selectedIndex = -1;
            for (int i = 0; i < __instance.inventory.inventory.Count; i++)
            {
                if (__instance.inventory.inventory[i].containsPoint(x, y))
                {
                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex == -1)
            {
                ModEntry.Instance.Log("No inventory slot selected (mobile)", LogLevel.Info);
                return true;
            }

            Item held = __instance.inventory.actualInventory.ElementAtOrDefault(selectedIndex);
            if (held == null || !Utility.IsGeode(held))
            {
                ModEntry.Instance.Log("Selected item is not a valid geode", LogLevel.Info);
                return true;
            }

            int totalStack = held.Stack;
            int maxAffordable = Game1.player.Money / 25;
            int maxBreakable = Math.Min(totalStack, maxAffordable);

            if (maxBreakable <= 0)
            {
                __instance.descriptionText = Game1.content.LoadString("Strings\\UI:GeodeMenu_InventoryFull");
                __instance.wiggleWordsTimer = 500;
                __instance.alertTimer = 1500;
                return false;
            }

            int freeSlots = Game1.player.freeSpotsInInventory();
            int targetAmount = Config.GeodesToBreak switch
            {
                GeodeBreakMode.AllIfInventoryFits =>
                    totalStack <= freeSlots ? Math.Min(totalStack, maxBreakable) : Math.Max(0, Math.Min(maxBreakable, freeSlots - 1)),
                GeodeBreakMode.AllExtraFallsOnGround => maxBreakable,
                _ => 1
            };

            if (targetAmount <= 0)
            {
                Game1.showRedMessage("Not enough inventory space for geode rewards.");
                return false;
            }

            showBreakAmountTimer = (int)(120 * Config.AnimationSpeedMultiplier);
            lastBreakAmount = targetAmount;

            List<Item> rewards = new();
            Random backupRandom = Game1.random;

            try
            {
                for (int i = 0; i < targetAmount; i++)
                {
                    Item tempGeode = ItemRegistry.Create(held.QualifiedItemId);

                    if (tempGeode.QualifiedItemId == "(O)791" && !Game1.netWorldState.Value.GoldenCoconutCracked)
                    {
                        rewards.Add(ItemRegistry.Create("(O)73"));
                        Game1.netWorldState.Value.GoldenCoconutCracked = true;
                        continue;
                    }

                    if (tempGeode.QualifiedItemId is "(O)MysteryBox" or "(O)GoldenMysteryBox")
                        Game1.stats.Increment("MysteryBoxesOpened");
                    else
                        Game1.stats.GeodesCracked++;

                    Game1.random = Utility.CreateRandom(Game1.uniqueIDForThisGame, Game1.stats.DaysPlayed, Game1.timeOfDay + Game1.random.Next());

                    var reward = Utility.getTreasureFromGeode(tempGeode);
                    rewards.Add(reward);
                }
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Log($"[ERROR] Mobile reward generation: {ex}", LogLevel.Error);
                Game1.random = backupRandom;
                return true;
            }

            Game1.random = backupRandom;
            __instance.geodeSpot.item = ItemRegistry.Create(held.QualifiedItemId);

            held.Stack -= targetAmount;
            if (held.Stack <= 0)
            {
                Game1.player.Items[selectedIndex] = null;
                ModEntry.Instance.Log("Removed geode stack from selected slot.", LogLevel.Info);
            }

            Game1.player.Money -= 25 * targetAmount;

            __instance.clint.setCurrentAnimation(new()
            {
                new(8, (int)(300 * Config.AnimationSpeedMultiplier)),
                new(9, (int)(200 * Config.AnimationSpeedMultiplier)),
                new(10, (int)(80 * Config.AnimationSpeedMultiplier)),
                new(11, (int)(200 * Config.AnimationSpeedMultiplier)),
                new(12, (int)(100 * Config.AnimationSpeedMultiplier)),
                new(8, (int)(300 * Config.AnimationSpeedMultiplier))
            });
            __instance.clint.loop = false;

            Game1.delayedActions.Add(new DelayedAction((int)(2700 * Config.AnimationSpeedMultiplier), () =>
            {
                foreach (var reward in rewards)
                {
                    if (!Game1.player.addItemToInventoryBool(reward))
                        Game1.createItemDebris(reward, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
                }
            }));

            __instance.geodeAnimationTimer = (int)(2700 * Config.AnimationSpeedMultiplier);
            return false;
        }







    }
}
