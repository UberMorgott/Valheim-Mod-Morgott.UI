using System;
using BepInEx.Configuration;
using Morgott.Core;
using Morgott.UI.PlayerHud;
using Morgott.UI.Theme;
using Morgott.UI.WorldHud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.MapScreen
{
    public static class SeasonHudConfig
    {
        public const string Section = "SeasonsHUD";

        public static ConfigEntry<bool> ShowMinimapSeasonOverlay { get; private set; }
        public static ConfigEntry<bool> ShowSeasonTimer { get; private set; }

        public static void Bind(ConfigFile config)
        {
            ShowMinimapSeasonOverlay = config.Bind(Section, "ShowMinimapSeasonOverlay", true,
                "Show dynamic seasonal wreath/snow ring overlays and blizzard frost rim around the minimap ring.");
            ShowSeasonTimer = config.Bind(Section, "ShowSeasonTimer", true,
                "Show a compact season name and remaining-time line above the minimap day/night arc.");
        }
    }

    // Seasonal ring overlays + optional compact season timer above the minimap top arc (never replaces RuneSkin_BiomePlaque).
    internal sealed class MinimapSeasonOverlay
    {
        private static readonly Sprite[] SeasonSprites = new Sprite[4];
        private static readonly bool[] SeasonTried = new bool[4];
        private static Sprite _blizzardSprite, _thawSprite;
        private static bool _blizzardTried, _thawTried;

        private readonly HudArt _art;
        private readonly Image _primary, _secondary, _blizzard, _thaw;
        private readonly TMP_Text _timer;
        private readonly float[] _weights = new float[4];
        private float _blizzardAlpha, _thawAlpha;
        private bool _initialized;

        private long _lastSeconds = -1L;
        private Season _lastSeason = (Season)(-1);
        private SeasonWeatherEvent _lastEvent = (SeasonWeatherEvent)(-1);
        private string _lastLanguage;
        private bool _lastServiceOn;

        public bool TimerVisible { get; private set; }

        public MinimapSeasonOverlay(HudArt art, RectTransform smallRoot, RectTransform ring)
        {
            _art = art;
            // Sits directly over RuneSkin_Ring and strictly under N/E/S/W runes, wind socket, day dial, and biome plaque.
            var insertAt = ring ? ring.GetSiblingIndex() + 1 : smallRoot.childCount;
            _primary = CreateOverlayImage("RuneSkin_SeasonPrimary", smallRoot, insertAt, "seasonal minimap ring overlay");
            _secondary = CreateOverlayImage("RuneSkin_SeasonSecondary", smallRoot, insertAt + 1, "seasonal minimap cross-fade overlay");
            _blizzard = CreateOverlayImage("RuneSkin_SeasonBlizzard", smallRoot, insertAt + 2, "blizzard frost rim minimap overlay");
            _thaw = CreateOverlayImage("RuneSkin_SeasonThaw", smallRoot, insertAt + 3, "thaw melt rim minimap overlay");
            _timer = art.Text("RuneSkin_SeasonTimer", smallRoot, "font.body", "color.text.body",
                ThemeTokens.GetFloat("size.minimap.font", 17f), TextAlignmentOptions.Center);
            TextLook.ApplyNameplate(_timer, "font.body", "season.timer", 0f);
            art.Note(_timer, "season.timer", true, "compact season and remaining time above top arc");
            _timer.raycastTarget = false;
            _timer.richText = false;
            _timer.textWrappingMode = TextWrappingModes.NoWrap;
            _timer.overflowMode = TextOverflowModes.Overflow;
            _timer.gameObject.SetActive(false);
        }

        private Image CreateOverlayImage(string name, Transform parent, int siblingIndex, string reason)
        {
            var rt = HudArt.Rect(name, parent);
            rt.SetSiblingIndex(siblingIndex);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.enabled = false;
            _art.Note(img, "season.overlay", true, reason);
            return img;
        }

        public float TopClearance(float timeDiameter, float k, float pxPerRef, float marginPx)
        {
            // Keep the timer line (placed above the noon/midnight top of RuneSkin_DayPhase) inside safe.yMax.
            var timerH = TimerHeight(k);
            var overhangRef = timeDiameter * 0.7f + 4f * k + timerH;
            var need = overhangRef * pxPerRef - marginPx * 0.5f;
            return need > 0f ? need : 0f;
        }

        public void Layout(float box, float k, float timeDiameter)
        {
            var size = new Vector2(box, box);
            Center(_primary.rectTransform, Vector2.zero, size);
            Center(_secondary.rectTransform, Vector2.zero, size);
            Center(_blizzard.rectTransform, Vector2.zero, size);
            Center(_thaw.rectTransform, Vector2.zero, size);

            if (_timer)
            {
                var rt = _timer.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f);
                var y = box * 0.5f + timeDiameter * 0.7f + 4f * k;
                rt.anchoredPosition = new Vector2(0f, y);
                rt.sizeDelta = new Vector2(box, TimerHeight(k));
                _timer.fontSize = MessageSkin.HudReadable(ThemeTokens.GetFloat("size.minimap.season.font", 15f)) * k;
            }
        }

        public void Update(float dt)
        {
            var hasService = Services.TryGet<ISeasonService>(out var service) && service != null;
            var wantTimer = hasService && (SeasonHudConfig.ShowSeasonTimer?.Value ?? true);
            TimerVisible = wantTimer;
            if (!hasService)
            {
                if (_lastServiceOn)
                {
                    _lastServiceOn = false;
                    _initialized = false;
                    for (var i = 0; i < 4; i++) _weights[i] = 0f;
                    _blizzardAlpha = 0f;
                    _thawAlpha = 0f;
                }
                if (_primary.enabled) _primary.enabled = false;
                if (_secondary.enabled) _secondary.enabled = false;
                if (_blizzard.enabled) _blizzard.enabled = false;
                if (_thaw.enabled) _thaw.enabled = false;
                if (_timer.gameObject.activeSelf) _timer.gameObject.SetActive(false);
                return;
            }
            _lastServiceOn = true;

            var snap = service.Current;
            var player = Player.m_localPlayer;
            var biome = player ? (int)player.GetCurrentBiome() : 0;
            var showOverlay = SeasonHudConfig.ShowMinimapSeasonOverlay?.Value ?? true;

            UpdateRings(snap, biome, showOverlay, dt);
            UpdateTimer(snap, wantTimer);
        }

        private void UpdateRings(SeasonSnapshot snap, int biome, bool showOverlay, float dt)
        {
            float t0 = 0f, t1 = 0f, t2 = 0f, t3 = 0f, tBliz = 0f, tThaw = 0f;
            if (showOverlay)
            {
                MinimapSeasonRules.ComputeCrossFadeWeights(
                    snap.Season, snap.Progress, MinimapSeasonRules.IsAshlands(biome),
                    out t0, out t1, out t2, out t3);
                tBliz = MinimapSeasonRules.ComputeBlizzardAlpha(snap, biome);
                tThaw = MinimapSeasonRules.ComputeThawAlpha(snap, biome);
            }

            if (!_initialized)
            {
                _initialized = true;
                _weights[0] = t0; _weights[1] = t1; _weights[2] = t2; _weights[3] = t3;
                _blizzardAlpha = tBliz;
                _thawAlpha = tThaw;
            }
            else
            {
                _weights[0] = MinimapSeasonRules.StepFade(_weights[0], t0, dt);
                _weights[1] = MinimapSeasonRules.StepFade(_weights[1], t1, dt);
                _weights[2] = MinimapSeasonRules.StepFade(_weights[2], t2, dt);
                _weights[3] = MinimapSeasonRules.StepFade(_weights[3], t3, dt);
                if (tBliz > 0f) _thawAlpha = 0f;
                if (tThaw > 0f) _blizzardAlpha = 0f;
                _blizzardAlpha = MinimapSeasonRules.StepFade(_blizzardAlpha, tBliz, dt);
                _thawAlpha = MinimapSeasonRules.StepFade(_thawAlpha, tThaw, dt);
            }

            MinimapSeasonRules.PickTopTwo(_weights[0], _weights[1], _weights[2], _weights[3],
                out var primarySeason, out var primaryAlpha, out var secondarySeason, out var secondaryAlpha);

            ApplyRing(_primary, primarySeason, primaryAlpha);
            ApplyRing(_secondary, secondarySeason, secondaryAlpha);

            ApplyRim(_blizzard, _blizzardAlpha > 0.002f ? BlizzardSprite() : null, new Color(0.90f, 0.97f, 1f, _blizzardAlpha), _blizzardAlpha);
            ApplyRim(_thaw, _thawAlpha > 0.002f ? ThawSprite() : null, new Color(1f, 1f, 1f, _thawAlpha), _thawAlpha);
        }

        private static void ApplyRim(Image img, Sprite sprite, Color tint, float alpha)
        {
            if (alpha <= 0.002f || !sprite)
            {
                if (img.enabled) img.enabled = false;
                return;
            }
            if (img.sprite != sprite) img.sprite = sprite;
            img.color = tint;
            if (!img.enabled) img.enabled = true;
        }

        private static void ApplyRing(Image img, Season season, float alpha)
        {
            if (alpha <= 0.002f)
            {
                if (img.enabled) img.enabled = false;
                return;
            }
            var sprite = SeasonSprite(season);
            if (!sprite)
            {
                if (img.enabled) img.enabled = false;
                return;
            }
            if (img.sprite != sprite) img.sprite = sprite;
            img.color = new Color(1f, 1f, 1f, alpha);
            if (!img.enabled) img.enabled = true;
        }

        private void UpdateTimer(SeasonSnapshot snap, bool wantTimer)
        {
            if (_timer.gameObject.activeSelf != wantTimer) _timer.gameObject.SetActive(wantTimer);
            if (!wantTimer) return;

            var wholeSec = snap.SecondsRemaining > 0.0 ? (long)Math.Ceiling(snap.SecondsRemaining) : 0L;
            var lang = Localization.instance != null ? Localization.instance.GetSelectedLanguage() : "";
            if (wholeSec == _lastSeconds && snap.Season == _lastSeason && snap.ActiveEvent == _lastEvent
                && string.Equals(lang, _lastLanguage, StringComparison.Ordinal))
                return;

            _lastSeconds = wholeSec;
            _lastSeason = snap.Season;
            _lastEvent = snap.ActiveEvent;
            _lastLanguage = lang;

            var seasonName = Words.Get(MinimapSeasonRules.SeasonKey(snap.Season));
            string eventLabel = null;
            if (snap.ActiveEvent == SeasonWeatherEvent.Blizzard) eventLabel = Words.Get("$ui_season_blizzard");
            else if (snap.ActiveEvent == SeasonWeatherEvent.Thaw) eventLabel = Words.Get("$ui_season_thaw");

            _timer.text = MinimapSeasonRules.FormatTimerLine(
                seasonName,
                snap.SecondsRemaining,
                Words.Get("$ui_season_unit_d"),
                Words.Get("$ui_season_unit_h"),
                Words.Get("$ui_season_unit_m"),
                Words.Get("$ui_season_unit_s"),
                eventLabel);
            _timer.color = snap.ActiveEvent == SeasonWeatherEvent.Blizzard
                ? ThemeTokens.GetColor("color.overlay.frost", new Color(0.84f, 0.95f, 1f, 1f))
                : ThemeTokens.GetColor("color.text.body", Color.white);
        }

        private static float TimerHeight(float k) =>
            ThemeTokens.GetFloat("size.minimap.season.h", 20f) * k;

        private static void Center(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static Sprite SeasonSprite(Season season)
        {
            var idx = ((int)season % 4 + 4) & 3;
            if (SeasonTried[idx]) return SeasonSprites[idx];
            SeasonTried[idx] = true;
            return SeasonSprites[idx] = LoadSprite(MinimapSeasonRules.ResourceName((Season)idx), "UI_MinimapSeason_" + (Season)idx);
        }

        private static Sprite BlizzardSprite() =>
            GetCachedRim(ref _blizzardSprite, ref _blizzardTried, MinimapSeasonRules.BlizzardResourceName, "UI_MinimapSeason_BlizzardRim");

        private static Sprite ThawSprite() =>
            GetCachedRim(ref _thawSprite, ref _thawTried, MinimapSeasonRules.ThawResourceName, "UI_MinimapSeason_ThawRim");

        private static Sprite GetCachedRim(ref Sprite cache, ref bool tried, string resource, string name)
        {
            if (tried) return cache;
            tried = true;
            return cache = LoadSprite(resource, name);
        }

        private static Sprite LoadSprite(string resource, string name)
        {
            var tex = Icons.ModStatusIcons.LoadEmbedded(resource, name);
            if (!tex) return null;
            if (tex.mipmapCount > 1) tex.mipMapBias = -0.5f;
            var s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            s.name = name;
            return s;
        }
    }
}
