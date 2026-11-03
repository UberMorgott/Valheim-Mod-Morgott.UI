using Morgott.Core;
using System;
using System.Collections.Generic;
using Morgott.UI.Classify;
using Morgott.UI.Ownership;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using UnityEngine;
using ITheme = Morgott.Core.ITheme;
using CoreTheme = Morgott.Core.Theme;

namespace Morgott.UI.Api
{
    // Roots RuneSkin restyles. Each root keeps the factory of its ScreenContext, so theme reloads re-run the same classification.
    public static class ScreenRegistry
    {
        private static readonly Dictionary<Transform, Func<ScreenContext>> Roots = new Dictionary<Transform, Func<ScreenContext>>();

        private static readonly List<Transform> Dead = new List<Transform>();

        // Bumped on every new root or removal: the fallback restyler (ModScreens\ForeignUi) re-derives what is foreign.
        public static int Version { get; private set; }

        public static void Register(Transform root, Func<ScreenContext> context)
        {
            // Destroyed roots (tooltip instances die on every hide) leave the registry here.
            foreach (var key in Roots.Keys)
                if (!key) Dead.Add(key);
            foreach (var key in Dead) Roots.Remove(key);
            Dead.Clear();
            if (!Roots.ContainsKey(root)) Version++;
            Roots[root] = context;
        }

        public static void Unregister(Transform root)
        {
            if (Roots.Remove(root)) Version++;
        }

        public static bool IsRoot(Transform t) => t && Roots.ContainsKey(t);

        public static IEnumerable<Transform> All => Roots.Keys;

        public static bool Restyle(Transform root)
        {
            if (!root || !Roots.TryGetValue(root, out var make)) return false;
            Restyler.RestyleRoot(root, make());
            return true;
        }

        public static void RestyleAll()
        {
            var dead = new List<Transform>();
            foreach (var pair in new List<KeyValuePair<Transform, Func<ScreenContext>>>(Roots))
            {
                if (!pair.Key) { dead.Add(pair.Key); continue; }
                Restyler.RestyleRoot(pair.Key, pair.Value());
            }
            foreach (var d in dead) Roots.Remove(d);
        }

        // Nearest registered ancestor (or self).
        public static Transform RootOf(Transform t)
        {
            for (var p = t; p; p = p.parent)
                if (Roots.ContainsKey(p)) return p;
            return null;
        }

        public static ScreenContext ContextOf(Transform root) => root && Roots.TryGetValue(root, out var make) ? make() : null;
    }

    // Morgott.Core ITheme implementation, registered through Core Services in Plugin.Awake.
    public sealed class ThemeProvider : ITheme
    {
        // Capability -> family that must be owned for it ("panel.dock": the AdventureBackpacks fork asks it).
        private static readonly Dictionary<string, string> Capabilities = new Dictionary<string, string>
        {
            ["effect.render"] = OwnershipRegistry.Hud,
            ["resource.render"] = OwnershipRegistry.Hud,
            ["capacity.render"] = OwnershipRegistry.Hud,
            ["ability.render"] = OwnershipRegistry.Hud,
            ["panel.dock"] = OwnershipRegistry.Inventory,
            ["slot.render"] = OwnershipRegistry.Inventory,
            ["panel.action"] = OwnershipRegistry.Inventory,
            [CoreTheme.CapSlotOverlay] = OwnershipRegistry.Inventory,
            [CoreTheme.CapHoldRender] = OwnershipRegistry.ActionHud, // drawn by WorldHud\HoldPromptSkin
        };

        public bool IsOwned(string family) => family != null && OwnershipRegistry.IsOwned(family);

        public bool Supports(string capability) =>
            capability != null && Capabilities.TryGetValue(capability, out var family) && OwnershipRegistry.IsOwned(family);

        public IDisposable RegisterResourceProvider(string owner, string pool, Func<Player, Morgott.Core.ResourceState> provider) =>
            new Handle(Providers.AddResource(Need(owner, nameof(owner)), Need(pool, nameof(pool)), Need(provider, nameof(provider))));

        public IDisposable RegisterCapacityProvider(string owner, string list, Func<Player, Morgott.Core.CapacityState> provider) =>
            new Handle(Providers.AddCapacity(Need(owner, nameof(owner)), Need(list, nameof(list)), Need(provider, nameof(provider))));

        public IDisposable RegisterPanelAction(string owner, Morgott.Core.PanelActionDescriptor action)
        {
            Need(action.Id, nameof(action.Id));
            return new Handle(InventoryScreen.InventoryRegistry.AddAction(Need(owner, nameof(owner)), action));
        }

        public IDisposable SetSlotOverlay(string owner, Inventory inventory, int x, int y, string glyphToken) =>
            new Handle(InventoryScreen.InventoryRegistry.SetOverlay(Need(owner, nameof(owner)), Need(inventory, nameof(inventory)), x, y, Need(glyphToken, nameof(glyphToken))));

        public IDisposable RegisterHoldPrompt(string owner, string id, Func<Player, Morgott.Core.HoldPromptState> provider) =>
            new Handle(Providers.AddHold(Need(owner, nameof(owner)), Need(id, nameof(id)), Need(provider, nameof(provider))));

        public bool TryGetSprite(string token, out Sprite sprite)
        {
            sprite = null;
            return token != null && ThemeTokens.TryGetSprite(token, out sprite);
        }

        public bool TryGetFloat(string token, out float value)
        {
            value = 0f;
            return token != null && ThemeTokens.TryGetFloat(token, out value);
        }

        private static T Need<T>(T value, string name) where T : class =>
            value == null || value is string s && s.Length == 0 ? throw new ArgumentNullException(name) : value;

        private sealed class Handle : IDisposable
        {
            private Action _dispose;
            public Handle(Action dispose) => _dispose = dispose;

            public void Dispose()
            {
                var d = _dispose;
                _dispose = null;
                d?.Invoke();
            }
        }
    }
}
