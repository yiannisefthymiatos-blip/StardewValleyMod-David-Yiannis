using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;

namespace StardewValleyModDavidYiannis
{
    internal sealed class MTodEntry : Mod
    {
        private IModHelper _helper = null!;
        //need a battery pack - vanilla item 787 - to make the reactor
        private const string ArcReactorItemId = "(O)787"; // battery pack
        private const string ArcReactorRecipeId = "ArcReactor";
        private bool reactorInstalled = false; // consumed on first suit-up
        
        // ── Suit state ────────────────────────────────────────────────────────
        private bool suitActive = false;
        private bool flightActive = false;

        // ── Arc Reactor energy (0–100) ────────────────────────────────────────
        private float arcEnergy = 100f;
        private const float MaxEnergy = 100f;
        private const float FlightDrain = 0.08f;    // per tick while flying
        private const float RepulsorCost = 25f;
        private const float EnergyRegen = 0.04f;    // per tick while grounded

        // ── Repulsor cooldown (ticks; 60/s) ──────────────────────────────────
        private int repulsorCooldown = 0;
        private const int RepulsorCooldownMax = 90; // 1.5 s

        // ── JARVIS idle quips ─────────────────────────────────────────────────
        private static readonly string[] IdleQuips =
        {
            "All systems nominal, sir.",
            "Arc reactor output stable.",
            "No threats detected in the vicinity.",
            "Structural integrity at 100%.",
            "Shall I compile a status report?",
        };
        private int quipTimer = 0;
        private readonly Random rng = new();

        // ─────────────────────────────────────────────────────────────────────

        public override void Entry(IModHelper helper)
        {
            _helper = helper;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.Content.AssetRequested += OnAssetRequested;
            
         
            Monitor.Log("Iron Man mod loaded. Craft an Arc Reactor (battery pack) and press [F] to suit up.", LogLevel.Info);
        }
        
        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, string>().Data;
                    // Key   = recipe display name shown in crafting menu
                    // Value = "ingredient1 qty ingredient2 qty .../Field/resultID/isBigCraft/conditions"
                    data["Arc Reactor"] = "334 5 336 2 338 1/Field/787/false/null";
                });
            }
        }
        //this makes the recipe a thing
        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            // Restore persisted state
            reactorInstalled = _helper.Data.ReadSaveData<SaveData>("reactorInstalled")?.Value ?? false;

            // Unlock the crafting recipe for the player if they don't have it yet
            if (!Game1.player.craftingRecipes.ContainsKey("Arc Reactor"))
            {
                Game1.player.craftingRecipes.Add("Arc Reactor", 0);
                Jarvis("New blueprint unlocked: Arc Reactor. Check your crafting menu.", HUDMessage.newQuest_type);
            }
        }
 // ── Input ─────────────────────────────────────────────────────────────

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            switch (e.Button)
            {
                case SButton.Z:
                    ToggleSuit();
                    break;
                case SButton.G when suitActive:
                    ToggleFlight();
                    break;
                case SButton.R when suitActive:
                    FireRepulsor();
                    break;
            }
        }

        // ── Suit ──────────────────────────────────────────────────────────────

        private void ToggleSuit()
        {
            if (!suitActive)
            {
                // Turning ON — need the Arc Reactor item if not yet installed
                if (!reactorInstalled)
                {
                    if (!PlayerHasItem(ArcReactorItemId))
                    {
                        Jarvis("Arc Reactor not found. Craft one first: 5 Iron Bar + 2 Gold Bar + 1 Refined Quartz.", HUDMessage.health_type);
                        return;
                    }

                    // Consume the Battery Pack (Arc Reactor) from inventory
                    RemoveItemFromInventory(ArcReactorItemId);
                    reactorInstalled = true;
                    _helper.Data.WriteSaveData("reactorInstalled", new SaveData { Value = true });
                    Jarvis("Arc Reactor installed. Initialising J.A.R.V.I.S. framework...", HUDMessage.newQuest_type);
                    Game1.playSound("cowboy_gunload");
                }

                suitActive = true;
                arcEnergy = MaxEnergy;
                Game1.player.temporarySpeedBuff = 2f;
                Jarvis("Welcome back, sir. Suit online. All systems go.", HUDMessage.newQuest_type);
                if (reactorInstalled)
                    Game1.playSound("cowboy_gunload");
            }
            else
            {
                // Turning OFF
                if (flightActive) DeactivateFlight();
                suitActive = false;
                Game1.player.temporarySpeedBuff = 0f;
                Jarvis("Powering down. Have a good evening, sir.", HUDMessage.stamina_type);
                Game1.playSound("coin");
            }
        }

        private void ToggleFlight()
        {
            if (!flightActive && arcEnergy < 15f)
            {
                Jarvis("Insufficient arc reactor energy for flight.", HUDMessage.health_type);
                return;
            }

            flightActive = !flightActive;

            if (flightActive)
            {
                Game1.player.temporarySpeedBuff = 5f; // faster than walking while airborne
                Jarvis("Repulsor thrusters engaged.", HUDMessage.newQuest_type);
                Game1.playSound("crystal");
            }
            else
            {
                DeactivateFlight();
            }
        }

        private void DeactivateFlight()
        {
            flightActive = false;
            Game1.player.temporarySpeedBuff = suitActive ? 2f : 0f;
            Jarvis("Thrusters offline. Landing sequence complete.", HUDMessage.stamina_type);
        }

        private void HandleFlightMovement()
        {
            const float speed = 8f;
            var pos = Game1.player.Position;

            if (_helper.Input.IsDown(SButton.W) || _helper.Input.IsDown(SButton.Up))    pos.Y -= speed;
            if (_helper.Input.IsDown(SButton.S) || _helper.Input.IsDown(SButton.Down))  pos.Y += speed;
            if (_helper.Input.IsDown(SButton.A) || _helper.Input.IsDown(SButton.Left))  pos.X -= speed;
            if (_helper.Input.IsDown(SButton.D) || _helper.Input.IsDown(SButton.Right)) pos.X += speed;

            Game1.player.Position = pos;
        }

        // ── Repulsor blast ────────────────────────────────────────────────────

        private void FireRepulsor()
        {
            if (repulsorCooldown > 0)
            {
                int pct = (RepulsorCooldownMax - repulsorCooldown) * 100 / RepulsorCooldownMax;
                Jarvis($"Repulsor recharging... {pct}%", HUDMessage.stamina_type);
                return;
            }
            if (arcEnergy < RepulsorCost)
            {
                Jarvis("Arc reactor energy critical. Cannot fire.", HUDMessage.health_type);
                return;
            }

            arcEnergy -= RepulsorCost;
            repulsorCooldown = RepulsorCooldownMax;

            Vector2 blastTile = FacingTile(4);
            Game1.currentLocation.explode(blastTile, 2, Game1.player);

            Game1.currentLocation.temporarySprites.Add(new TemporaryAnimatedSprite(
                "TileSheets\\animations",
                new Rectangle(0, 320, 64, 64),
                50f, 8, 0,
                blastTile * 64f,
                false, false));

            Game1.playSound("explosion");
            Jarvis("Repulsor discharged.", HUDMessage.newQuest_type);
        }

        private static Vector2 FacingTile(int tiles)
        {
            Vector2 t = Game1.player.Tile;
            return Game1.player.FacingDirection switch
            {
                0 => new Vector2(t.X, t.Y - tiles),
                1 => new Vector2(t.X + tiles, t.Y),
                2 => new Vector2(t.X, t.Y + tiles),
                3 => new Vector2(t.X - tiles, t.Y),
                _ => t
            };
        }

        // ── Per-tick update ───────────────────────────────────────────────────

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !suitActive) return;

            if (flightActive) HandleFlightMovement();

            if (repulsorCooldown > 0) repulsorCooldown--;

            if (flightActive)
            {
                arcEnergy = Math.Max(0f, arcEnergy - FlightDrain);
                if (arcEnergy <= 0f)
                {
                    DeactivateFlight();
                    Jarvis("Power failure! Emergency landing initiated!", HUDMessage.health_type);
                    Game1.playSound("debuffHit");
                }
            }
            else
            {
                arcEnergy = Math.Min(MaxEnergy, arcEnergy + EnergyRegen);
            }

            if (e.IsMultipleOf(2))
                Game1.player.Stamina = Math.Min(Game1.player.MaxStamina, Game1.player.Stamina + 1f);

            if (e.IsMultipleOf(60) && Game1.player.health < Game1.player.maxHealth)
                Game1.player.health = Math.Min(Game1.player.maxHealth, Game1.player.health + 1);

            quipTimer++;
            if (quipTimer >= 1800)
            {
                quipTimer = 0;
                if (rng.NextDouble() < 0.4)
                    Jarvis(IdleQuips[rng.Next(IdleQuips.Length)], HUDMessage.newQuest_type);
            }
        }

        // ── HUD ───────────────────────────────────────────────────────────────

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // Show "craft Arc Reactor" hint if reactor not yet installed
            if (!reactorInstalled && !suitActive)
            {
                DrawString(
                    e.SpriteBatch,
                    "[ IRON MAN ] Craft an Arc Reactor to suit up  (5 Iron Bar + 2 Gold Bar + 1 Refined Quartz)",
                    new Vector2(16, Game1.uiViewport.Height - 40),
                    new Color(0, 160, 255, 180)
                );
                return;
            }

            if (!suitActive) return;
            DrawHud(e.SpriteBatch);
        }

        private void DrawHud(SpriteBatch b)
        {
            const int barW = 200;
            const int barH = 18;
            const int rowH = 26;
            const int padX = 12;
            const int padY = 10;

            int panelX = 16;
            int panelY = Game1.uiViewport.Height - 205;
            int panelW = barW + padX * 2 + 20;
            int panelH = 190;

            DrawRect(b, new Rectangle(panelX, panelY, panelW, panelH), new Color(0, 10, 30, 190));
            DrawBorder(b, new Rectangle(panelX, panelY, panelW, panelH), new Color(0, 160, 255, 220), 2);

            int x = panelX + padX;
            int y = panelY + padY;

            DrawString(b, "[ STARK INDUSTRIES - MARK VI ]", new Vector2(x, y), new Color(0, 200, 255));
            y += rowH;

            DrawString(b, "ARC REACTOR", new Vector2(x, y), new Color(0, 160, 255));
            y += 18;
            DrawBar(b, new Rectangle(x, y, barW, barH), arcEnergy / MaxEnergy,
                new Color(0, 40, 120), new Color(0, 150, 255));
            DrawString(b, $"{(int)arcEnergy}%", new Vector2(x + barW + 6, y - 1), Color.White);
            y += rowH;

            float hp = (float)Game1.player.health / Game1.player.maxHealth;
            DrawString(b, "SUIT INTEGRITY", new Vector2(x, y), new Color(0, 160, 255));
            y += 18;
            DrawBar(b, new Rectangle(x, y, barW, barH), hp,
                new Color(80, 0, 0), new Color(220, 40, 40));
            DrawString(b, $"{Game1.player.health}/{Game1.player.maxHealth}", new Vector2(x + barW + 6, y - 1), Color.White);
            y += rowH;

            string flightLabel = flightActive ? "FLIGHT   [ ACTIVE  ]" : "FLIGHT   [ OFFLINE ]";
            Color flightCol    = flightActive ? new Color(0, 230, 120) : new Color(200, 80, 0);
            DrawString(b, flightLabel, new Vector2(x, y), flightCol);
            y += rowH - 4;

            string repLabel = repulsorCooldown > 0
                ? $"REPULSOR [ CHARGING {(RepulsorCooldownMax - repulsorCooldown) * 100 / RepulsorCooldownMax}% ]"
                : "REPULSOR [ READY   ]";
            Color repCol = repulsorCooldown > 0 ? Color.Orange : new Color(0, 230, 120);
            DrawString(b, repLabel, new Vector2(x, y), repCol);
            y += rowH - 4;

            DrawString(b, "[Z] Suit    [G] Flight    [R] Repulsor", new Vector2(x, y), new Color(100, 100, 100));
        }

        // ── Draw helpers ──────────────────────────────────────────────────────

        private static void DrawBar(SpriteBatch b, Rectangle r, float fill, Color bg, Color fg)
        {
            b.Draw(Game1.staminaRect, r, bg * 0.8f);
            if (fill > 0f)
                b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, (int)(r.Width * fill), r.Height), fg);
        }

        private static void DrawRect(SpriteBatch b, Rectangle r, Color c) =>
            b.Draw(Game1.staminaRect, r, c);

        private static void DrawBorder(SpriteBatch b, Rectangle r, Color c, int t)
        {
            b.Draw(Game1.staminaRect, new Rectangle(r.X,         r.Y,          r.Width, t),        c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X,         r.Bottom - t, r.Width, t),        c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X,         r.Y,          t,       r.Height), c);
            b.Draw(Game1.staminaRect, new Rectangle(r.Right - t, r.Y,          t,       r.Height), c);
        }

        private static void DrawString(SpriteBatch b, string text, Vector2 pos, Color c)
        {
            b.DrawString(Game1.smallFont, text, pos + new Vector2(1, 1), Color.Black * 0.6f);
            b.DrawString(Game1.smallFont, text, pos, c);
        }

        // ── Inventory helpers ─────────────────────────────────────────────────

        private static bool PlayerHasItem(string qualifiedItemId)
        {
            foreach (var item in Game1.player.Items)
                if (item?.QualifiedItemId == qualifiedItemId) return true;
            return false;
        }

        private static void RemoveItemFromInventory(string qualifiedItemId)
        {
            for (int i = 0; i < Game1.player.Items.Count; i++)
            {
                var item = Game1.player.Items[i];
                if (item?.QualifiedItemId != qualifiedItemId) continue;

                if (item.Stack > 1) { item.Stack--; }
                else                { Game1.player.Items[i] = null; }
                return;
            }
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private static void Jarvis(string message, int type) =>
            Game1.addHUDMessage(new HUDMessage($"JARVIS: {message}", type));
    }

    // Wrapper needed because WriteSaveData requires a serializable class, not a primitive
    internal class SaveData
    {
        public bool Value { get; set; }
    }
}
//presentation ready