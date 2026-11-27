using System;
using System.Collections.Generic;
using HarmonyLib;
using Morgott.UI.Ownership;

namespace Morgott.UI.PlayerHud
{
    // Localization.GetBoundKeyString (every "$KEY_<button>" in texts, KeyHints rows, tooltips): mouse / modifier keys in
    // Latin at any language (KeyLabels). Prefix replaces the vanilla lookup so ZInput.GetBoundKeyString runs once (not
    // twice) and caches resolved strings per (bindingName, raw), invalidated on ZInput.OnInputLayoutChanged / language change.
    [HarmonyPatch(typeof(Localization), nameof(Localization.GetBoundKeyString))]
    internal static class KeyLabelsPatch
    {
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly string Binding;
            public readonly string Raw;

            public CacheKey(string binding, string raw)
            {
                Binding = binding;
                Raw = raw;
            }

            public bool Equals(CacheKey other) =>
                string.Equals(Binding, other.Binding, StringComparison.Ordinal) &&
                string.Equals(Raw, other.Raw, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode() =>
                unchecked(((Binding != null ? StringComparer.Ordinal.GetHashCode(Binding) : 0) * 397) ^
                          (Raw != null ? StringComparer.Ordinal.GetHashCode(Raw) : 0));
        }

        private static readonly Dictionary<CacheKey, string> Cache = new Dictionary<CacheKey, string>(64);
        private static string _lang;
        private static bool _subscribed;

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            _subscribed = true;
            ZInput.OnInputLayoutChanged += Invalidate;
            Localization.OnLanguageChange += Invalidate;
        }

        private static void Invalidate() => Cache.Clear();

        private static bool Prefix(Localization __instance, string bindingName, bool emptyStringOnMissing, ref string __result)
        {
            if (!OwnershipRegistry.IsOwned(OwnershipRegistry.KeyHints) || ZInput.instance == null) return true;
            EnsureSubscribed();
            var lang = __instance.GetSelectedLanguage();
            if (!string.Equals(lang, _lang, StringComparison.Ordinal))
            {
                Cache.Clear();
                _lang = lang;
            }
            var raw = ZInput.instance.GetBoundKeyString(bindingName, emptyStringOnMissing);
            var key = new CacheKey(bindingName ?? "", raw ?? "");
            if (Cache.TryGetValue(key, out var cached))
            {
                __result = cached;
                return false;
            }
            var resolved = KeyLabels.For(raw, lang) ?? (KeyLabels.IsLayoutGlyph(raw) ? GuardianAbility.KeyLabel(bindingName) : null);
            if (resolved == null)
            {
                if (raw != null && raw.Length > 0 && raw[0] == '$' && __instance.m_translations.TryGetValue(raw.Substring(1), out var value))
                    resolved = value;
                else
                    resolved = raw;
            }
            if (Cache.Count >= 512) Cache.Clear();
            Cache[key] = resolved;
            __result = resolved;
            return false;
        }
    }
}
