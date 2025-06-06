﻿
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
        
        //used to trouble position in game



        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();

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
            if (__instance.waitingForServerResponse || !__instance.geodeSpot.containsPoint(x, y))
                return true;

            Item held = __instance.heldItem;
            if (held == null || !Utility.IsGeode(held) || Game1.player.Money < 25 || __instance.geodeAnimationTimer > 0)
                return true;

            int heldStack = held.Stack;
            int maxAffordable = Game1.player.Money / 25;
            int maxBreakable = Math.Min(heldStack, maxAffordable);

            if (maxBreakable <= 0)
            {
                __instance.descriptionText = Game1.content.LoadString(@"Strings\UI:GeodeMenu_InventoryFull");
                __instance.wiggleWordsTimer = 500;
                __instance.alertTimer = 1500;
                return false;
            }

            int freeSlots = Game1.player.freeSpotsInInventory();

            int targetAmount = Config.GeodesToBreak switch
            {
                GeodeBreakMode.AllIfInventoryFits =>
                    heldStack <= freeSlots
                        ? Math.Min(heldStack, maxBreakable) // all rewards can fit
                        : Math.Max(0, Math.Min(maxBreakable, freeSlots - 1)), // reserve slot for held item if some will remain

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

            for (int i = 0; i < targetAmount - 1; i++)
            {
                Item tempGeode = held.getOne();

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

                Random freshRandom = Utility.CreateRandom(
                    Game1.uniqueIDForThisGame,
                    Game1.stats.DaysPlayed,
                    Game1.timeOfDay + Game1.random.Next()
                );

                Game1.random = freshRandom;
                rewards.Add(Utility.getTreasureFromGeode(tempGeode));
            }

            Game1.random = backupRandom;

            __instance.geodeSpot.item = held.getOne();
            held.Stack -= targetAmount;
            if (held.Stack <= 0)
                __instance.heldItem = null;

            Game1.player.Money -= 25 * targetAmount;
            Game1.playSound("stoneStep");
            
            __instance.clint.setCurrentAnimation(new List<FarmerSprite.AnimationFrame>
            {
                new(8, (int)(300*Config.AnimationSpeedMultiplier)),
                new(9, (int)(200*Config.AnimationSpeedMultiplier)),
                new(10, (int)(80*Config.AnimationSpeedMultiplier)),
                new(11, (int)(200*Config.AnimationSpeedMultiplier)),
                new(12, (int)(100*Config.AnimationSpeedMultiplier)),
                new(8, (int)(300*Config.AnimationSpeedMultiplier))
            });
            __instance.clint.loop = false;

            Game1.delayedActions.Add(new DelayedAction((int)(2700 * Config.AnimationSpeedMultiplier), () =>
            {
                foreach (Item reward in rewards)
                {
                    if (!Game1.player.addItemToInventoryBool(reward))
                    {
                        Game1.createItemDebris(reward, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
                    }
                }
            }));

            __instance.geodeAnimationTimer = (int)(2700* Config.AnimationSpeedMultiplier);
            return false;
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

                // using config.offset to test in game
                Vector2 drawPosition = new Vector2(
                    __instance.geodeSpot.bounds.X + 360 + Config.OverlayOffsetX,
                    __instance.geodeSpot.bounds.Y + 160 + Config.OverlayOffsetY
                );

                drawPosition.X -= textSize.X / 2;


                b.DrawString(font, text, drawPosition + new Vector2(2, 2), Color.Black * 0.75f, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);

                b.DrawString(font, text, drawPosition, Color.Yellow, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
            }
        }






    }
}
