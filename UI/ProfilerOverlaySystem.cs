#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace PerformanceProfiler.UI;

/// <summary>
/// Hosts the F9 profiler overlay: owns the keybind, the <see cref="UserInterface"/>,
/// and the visibility toggle, and inserts the overlay's draw layer into the
/// in-game interface stack.
/// </summary>
public sealed class ProfilerOverlaySystem : ModSystem
{
    /// <summary>
    /// The F9 toggle keybind. Polled by <c>ProfilerPlayer.ProcessTriggers</c>;
    /// null before the mod loads and after it unloads.
    /// </summary>
    public static ModKeybind? ToggleKeybind { get; private set; }

    private UserInterface? _userInterface;
    private ProfilerOverlay? _overlay;
    private bool _visible;

    public override void OnModLoad()
    {
        ToggleKeybind = KeybindLoader.RegisterKeybind(Mod, "ToggleOverlay", "F9");

        // The UserInterface is client-only; a dedicated server has no UI.
        if (!Main.dedServ)
        {
            _userInterface = new UserInterface();
        }
    }

    public override void OnModUnload()
    {
        ToggleKeybind = null;
        _userInterface = null;
        _overlay = null;
    }

    /// <summary>Flips the overlay on or off. Called from the F9 keybind handler.</summary>
    public void ToggleVisibility()
    {
        if (_userInterface == null)
        {
            return; // Dedicated server, or the UI host was never constructed.
        }

        _visible = !_visible;

        if (_visible)
        {
            // Built lazily on first show, by which point the game's fonts and
            // textures are fully loaded.
            _overlay ??= CreateOverlay();
            _userInterface.SetState(_overlay);
        }
        else
        {
            _userInterface.SetState(null);
        }

        Mod.Logger.Info($"Profiler overlay toggled {(_visible ? "on" : "off")}.");
    }

    public override void UpdateUI(GameTime gameTime)
    {
        if (_visible)
        {
            _userInterface?.Update(gameTime);
        }
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        if (!_visible || _userInterface == null)
        {
            return;
        }

        // Sit just beneath the cursor layer: above the gameplay HUD, below the mouse.
        int cursorLayer = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        int insertAt = cursorLayer >= 0 ? cursorLayer : layers.Count;

        layers.Insert(insertAt, new LegacyGameInterfaceLayer(
            "PerformanceProfiler: Overlay",
            DrawOverlay,
            InterfaceScaleType.UI));
    }

    /// <summary>Draw delegate for the interface layer; returns true so later layers still draw.</summary>
    private bool DrawOverlay()
    {
        _userInterface?.Draw(Main.spriteBatch, new GameTime());
        return true;
    }

    private static ProfilerOverlay CreateOverlay()
    {
        ProfilerOverlay overlay = new ProfilerOverlay();
        overlay.Activate();
        return overlay;
    }
}
