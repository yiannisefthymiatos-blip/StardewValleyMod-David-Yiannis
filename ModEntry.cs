using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewValleyModDavidYiannis
{
    internal sealed class ModEntry : Mod
    {
        private IModHelper _helper = null!;

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
            "JARVIS: All systems nominal, sir.",
            "JARVIS: Arc reactor output stable.",
            "JARVIS: No threats detected in the vicinity.",
            "JARVIS: Structural integrity at 100%.",
            "JARVIS: Shall I compile a status report?",
        };
        private int quipTimer = 0;
        private readonly Random rng = new();

        // ─────────────────────────────────────────────────────────────────────

        public override void Entry(IModHelper helper)
        {
            _helper = helper;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.Player.Warped += OnWarped;

            Monitor.Log("Iron Man mod loaded. Press [F] to suit up.", LogLevel.Info);
        }

        // ── Input ─────────────────────────────────────────────────────────────

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            switch (e.Button)
            {
                case SButton.F:
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
            suitActive = !suitActive;

            if (suitActive)
            {
                arcEnergy = MaxEnergy;
                Game1.player.temporarySpeedBuff = 2f;
                Jarvis("Welcome back, sir. Suit online. All systems go.", HUDMessage.newQuest_type);
                Game1.playSound("cowboy_monsterkilled");
            }
            else
            {
                if (flightActive) DeactivateFlight();
                Game1.player.temporarySpeedBuff = 0f;
                Jarvis("Powering down. Have a good evening, sir.", HUDMessage.stamina_type);
                Game1.playSound("coin");
            }
        }

        private void ToggleFlight()
        {
            if (arcEnergy < 15f)
            {
                Jarvis("Insufficient arc reactor energy for flight.", HUDMessage.health_type);
                return;
            }

            flightActive = !flightActive;

            if (flightActive)
            {
                Game1.player.temporarySpeedBuff = 2f;
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

        // Setting Position each tick after the game's own movement code runs bypasses collision.
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

            Vector2 blastTile = FacingTile(2);

            // Explosion damages monsters, breaks rocks/trees/objects
            Game1.currentLocation.explode(blastTile, 2, Game1.player);

            // Visual flash at blast site
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
                0 => new Vector2(t.X, t.Y - tiles),  // up
                1 => new Vector2(t.X + tiles, t.Y),  // right
                2 => new Vector2(t.X, t.Y + tiles),  // down
                3 => new Vector2(t.X - tiles, t.Y),  // left
                _ => t
            };
        }

        // ── Per-tick update ───────────────────────────────────────────────────

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !suitActive) return;

            // Flight movement — overrides game collision by setting position after game logic runs
            if (flightActive) HandleFlightMovement();

            // Repulsor cooldown
            if (repulsorCooldown > 0) repulsorCooldown--;

            // Arc reactor energy
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

            // Suit handles exertion — regen stamina
            if (e.IsMultipleOf(2))
                Game1.player.Stamina = Math.Min(Game1.player.MaxStamina, Game1.player.Stamina + 1f);

            // Nanite repair — passive health regen once per second
            if (e.IsMultipleOf(60) && Game1.player.health < Game1.player.maxHealth)
                Game1.player.health = Math.Min(Game1.player.maxHealth, Game1.player.health + 1);

            // Occasional JARVIS idle quip (~every 30 s)
            quipTimer++;
            if (quipTimer >= 1800)
            {
                quipTimer = 0;
                if (rng.NextDouble() < 0.4)
                    Jarvis(IdleQuips[rng.Next(IdleQuips.Length)], HUDMessage.newQuest_type);
            }
        }

        private void OnWarped(object? sender, WarpedEventArgs e) { }

        // ── HUD ───────────────────────────────────────────────────────────────

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (!Context.IsWorldReady || !suitActive) return;
            DrawHud(e.SpriteBatch);
        }

        private void DrawHud(SpriteBatch b)
        {
            const int barW  = 200;
            const int barH  = 18;
            const int rowH  = 26;
            const int padX  = 12;
            const int padY  = 10;

            int panelX = 16;
            int panelY = Game1.uiViewport.Height - 205;
            int panelW = barW + padX * 2 + 20;
            int panelH = 190;

            // Background panel
            DrawRect(b, new Rectangle(panelX, panelY, panelW, panelH), new Color(0, 10, 30, 190));
            DrawBorder(b, new Rectangle(panelX, panelY, panelW, panelH), new Color(0, 160, 255, 220), 2);

            int x = panelX + padX;
            int y = panelY + padY;

            // Header
            DrawString(b, "[ STARK INDUSTRIES - MARK VI ]", new Vector2(x, y), new Color(0, 200, 255));
            y += rowH;

            // Arc Reactor bar
            DrawString(b, "ARC REACTOR", new Vector2(x, y), new Color(0, 160, 255));
            y += 18;
            DrawBar(b, new Rectangle(x, y, barW, barH), arcEnergy / MaxEnergy,
                new Color(0, 40, 120), new Color(0, 150, 255));
            DrawString(b, $"{(int)arcEnergy}%", new Vector2(x + barW + 6, y - 1), Color.White);
            y += rowH;

            // Suit Integrity bar
            float hp = (float)Game1.player.health / Game1.player.maxHealth;
            DrawString(b, "SUIT INTEGRITY", new Vector2(x, y), new Color(0, 160, 255));
            y += 18;
            DrawBar(b, new Rectangle(x, y, barW, barH), hp,
                new Color(80, 0, 0), new Color(220, 40, 40));
            DrawString(b, $"{Game1.player.health}/{Game1.player.maxHealth}", new Vector2(x + barW + 6, y - 1), Color.White);
            y += rowH;

            // Flight status
            string flightLabel = flightActive ? "FLIGHT   [ ACTIVE  ]" : "FLIGHT   [ OFFLINE ]";
            Color flightCol    = flightActive ? new Color(0, 230, 120) : new Color(200, 80, 0);
            DrawString(b, flightLabel, new Vector2(x, y), flightCol);
            y += rowH - 4;

            // Repulsor status
            string repLabel = repulsorCooldown > 0
                ? $"REPULSOR [ CHARGING {(RepulsorCooldownMax - repulsorCooldown) * 100 / RepulsorCooldownMax}% ]"
                : "REPULSOR [ READY   ]";
            Color repCol = repulsorCooldown > 0 ? Color.Orange : new Color(0, 230, 120);
            DrawString(b, repLabel, new Vector2(x, y), repCol);
            y += rowH - 4;

            // Key hint footer
            DrawString(b, "[F] Suit    [G] Flight    [R] Repulsor", new Vector2(x, y), new Color(100, 100, 100));
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
            b.Draw(Game1.staminaRect, new Rectangle(r.X,          r.Y,           r.Width, t),        c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X,          r.Bottom - t,  r.Width, t),        c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X,          r.Y,           t,       r.Height), c);
            b.Draw(Game1.staminaRect, new Rectangle(r.Right - t,  r.Y,           t,       r.Height), c);
        }

        private static void DrawString(SpriteBatch b, string text, Vector2 pos, Color c)
        {
            b.DrawString(Game1.smallFont, text, pos + new Vector2(1, 1), Color.Black * 0.6f);
            b.DrawString(Game1.smallFont, text, pos, c);
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private static void Jarvis(string message, int type) =>
            Game1.addHUDMessage(new HUDMessage($"JARVIS: {message}", type));
    }
}
