#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using PerformanceProfiler.UI.Overlay.Components;

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

    /// <summary>
    /// Cached interface-layer instance reused every frame. v0.5 allocated a
    /// fresh <see cref="LegacyGameInterfaceLayer"/> per draw — at 60 FPS
    /// that's ~3,600 layer allocations per second on the draw thread. v0.6
    /// builds it once on first show and reuses thereafter (overlay §3 +
    /// cross-allocations §1.5).
    /// </summary>
    private LegacyGameInterfaceLayer? _cachedLayer;

    /// <summary>
    /// Cached <see cref="GameTime"/> instance. The overlay's
    /// <see cref="UserInterface.Draw"/> signature requires a non-null
    /// GameTime; we don't actually consume any of its fields, so a single
    /// shared sentinel suffices (overlay §3).
    /// </summary>
    private static readonly GameTime _cachedGameTime = new GameTime();

    private LegacyGameInterfaceLayer? _cachedToastLayer;

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        // Sit just beneath the cursor layer: above the gameplay HUD, below the mouse.
        int cursorLayer = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        int insertAt = cursorLayer >= 0 ? cursorLayer : layers.Count;

        // Toast layer is ALWAYS injected — retrospective cards must surface
        // even when the F9 overlay is closed, so the player sees "EoC fight
        // ended, here's the breakdown" without needing to open the panel.
        _cachedToastLayer ??= new LegacyGameInterfaceLayer(
            "PerformanceProfiler: Toasts",
            DrawToasts,
            InterfaceScaleType.UI);
        layers.Insert(insertAt, _cachedToastLayer);

        // Overlay layer is gated on _visible / userInterface.
        if (!_visible || _userInterface == null) return;

        _cachedLayer ??= new LegacyGameInterfaceLayer(
            "PerformanceProfiler: Overlay",
            DrawOverlay,
            InterfaceScaleType.UI);
        layers.Insert(insertAt, _cachedLayer);
    }

    /// <summary>Draw delegate for the interface layer; returns true so later layers still draw.</summary>
    private bool DrawOverlay()
    {
        _userInterface?.Draw(Main.spriteBatch, _cachedGameTime);
        return true;
    }

    /// <summary>
    /// Always-on layer combining the live Now-Playing widget (top-left) and
    /// the retrospective toasts (bottom-right). Both surfaces are visible
    /// regardless of whether the F9 overlay is open, but each is independently
    /// togglable via <see cref="ProfilerConfig"/>.
    /// </summary>
    private static bool DrawToasts()
    {
        NowPlayingPanel.DrawFloating(Main.spriteBatch);
        RetrospectiveToast.Pump();
        RetrospectiveToast.Draw(Main.spriteBatch);
        return true;
    }

    private static ProfilerOverlay CreateOverlay()
    {
        ProfilerOverlay overlay = new ProfilerOverlay();
        overlay.Activate();
        return overlay;
    }
}
