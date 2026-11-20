using System;
using System.Collections.Generic;
using HarmonyLib;
using Morgott.Core;
using Morgott.UI.Classify;
using Morgott.UI.InfoScreens;
using Morgott.UI.InventoryScreen;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Morgott.UI.StationQueue
{
    // Station processing-queue window (mockups docs\mockups\station-queue: kiln.png = multi, eitr-refinery.png = single,
    // entry-editor.png = editor; DESIGN.md "User decisions 2026-11-17"). Data and edits through Core IStationQueue (owner
    // Morgott.Patcher [StationQueue]): Read every RefreshEvery s while open, Write on every change (false = no ward access:
    // vanilla "$msg_privatezone" message, the window shows the Read state again). Opened by the service's OpenRequested
    // (Patcher hotkey on the hovered station); closed by Esc, the close cap, out of reach of the station (StationQueueMath.KeepsOpen) or the station
    // gone. Built once under the HUD root (as HudCluster) on the modal band (ModalLayer); panels = RuneSkin panel stack +
    // window header (drag band, WindowMove). Player input off and cursor shown while open (StationQueueInput).
    public sealed class StationQueueWindow : MonoBehaviour
    {
        public const string RootName = "MorgottStationQueue";
        private const float RefreshEvery = 0.5f;
        private const int InputRows = 5, CardRows = 4, PickerRows = 6;

        private static StationQueueWindow _instance;
        private static int _closedFrame = -1;

        // ---------------------------------------------------------------- harness entry points (reflection)

        public static string LastScreen { get; private set; } = "";
        public static bool IsOpen => _instance && _instance._station && _instance._root && _instance._root.gameObject.activeSelf;
        public static void Open(Component station) { if (_instance) _instance.Show(station); }
        public static void Close() { if (_instance) _instance.Hide("Close()"); }

        /// <summary>Harness: why the window last closed or failed to open ("" = never).</summary>
        public static string LastCloseReason { get; private set; } = "";
        public static void OpenEditor(int index) { if (_instance && IsOpen) _instance.EditorOpen(index, null); }

        // Harness: the multi window's title badge text ("" when not shown), the shown header / status line, the card being
        // dragged (-1 = none).
        public static string BadgeText => IsOpen && _instance._badge && _instance._badge.gameObject.activeInHierarchy ? _instance._badgeLabel.text : "";
        public static string StatusLine => !IsOpen ? "" : _instance._multi.gameObject.activeSelf ? _instance._mStatus.text : _instance._sStatus.text;
        public static int DraggingCard => _instance ? _instance._dragCard : -1;

        /// <summary>Harness: "" when the open window is on screen, else why not (state checks alone passed while nothing
        /// was drawn, run 20261117-184423): every canvas above enabled, no CanvasGroup at alpha 0, the shown panel's
        /// rect inside the screen, under the GUI the HUD is drawn in.</summary>
        public static string VisibleWhy
        {
            get
            {
                if (!IsOpen) return "closed (" + LastCloseReason + ")";
                var root = _instance._root;
                if (!root.gameObject.activeInHierarchy) return "inactive in hierarchy";
                foreach (var c in root.GetComponentsInParent<Canvas>(true))
                    if (!c.enabled) return "canvas off: " + c.name;
                foreach (var g in root.GetComponentsInParent<CanvasGroup>(true))
                    if (g.alpha <= 0.01f) return "alpha 0: " + g.name;
                var hudHost = Hud.instance && Hud.instance.m_rootObject ? Hud.instance.m_rootObject.transform : null;
                if (hudHost && !root.IsChildOf(hudHost)) return "not under the HUD root (" + hudHost.name + ")";
                if (Hud.instance && !Hud.instance.IsVisible()) return "HUD hidden";
                var panel = _instance._editing ? _instance._editor : _instance._multi.gameObject.activeSelf ? _instance._multi : _instance._single;
                var canvas = root.GetComponentInParent<Canvas>();
                var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                var corners = new Vector3[4];
                panel.GetWorldCorners(corners);
                var a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                var b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                if (b.x - a.x < 50f || b.y - a.y < 50f) return $"panel {b.x - a.x:0}x{b.y - a.y:0} px";
                if (a.x < -1f || a.y < -1f || b.x > Screen.width + 1f || b.y > Screen.height + 1f) return $"panel off screen ({a}..{b})";
                return "";
            }
        }

        // Menu.Update skips the frame Esc closed (or would close) this window, so Esc never opens the pause menu too.
        internal static bool EatsEscape() =>
            (IsOpen || _closedFrame == Time.frameCount) && ZInput.GetKeyDown(KeyCode.Escape, false);

        public static void Attach(GameObject host)
        {
            if (!_instance) _instance = host.AddComponent<StationQueueWindow>();
        }

        // ---------------------------------------------------------------- state

        private IStationQueue _queue;
        private float _nextPoll, _nextRead;
        private Component _station;
        private readonly StationQueueState _state = new StationQueueState();
        private readonly Dictionary<string, ItemDrop> _items = new Dictionary<string, ItemDrop>(StringComparer.Ordinal);
        private int _inScroll, _cardScroll;
        private int _dragCard = -1, _hoverCard = -1;
        private string _dragItem;
        private Image _ghost;

        // editor: index of the edited entry (-1 = new), draft; open flag
        private bool _editing;
        private int _editIndex;
        private StationQueueEntry _draft;

        // ---------------------------------------------------------------- widgets

        private RectTransform _root, _multi, _single, _editorLayer, _editor;
        private TMP_Text _mStatus, _mHidden, _mPause, _sStatus, _sPause;
        private readonly List<InputRow> _rows = new List<InputRow>();
        private readonly List<Card> _cards = new List<Card>();
        private RectTransform _rowsRt, _cardsRt, _addHint, _badge;
        private TMP_Text _badgeLabel;

        private sealed class InputRow
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public Image Icon;
            public TMP_Text Badge, Name, Count;
            public string Item;
        }

        private sealed class Card
        {
            public RectTransform Rt;
            public Image[] Rim;
            public Image Icon, Fill;
            public RectTransform Trough;
            public TMP_Text Num, Text, Pct, Fed, Chip;
            public Image[] ChipRim;
            public Image ChipIcon;
            public int Index;
        }

        // single-input window
        private Image _sIn, _sOut, _sFuel, _sCond;
        private TMP_Text _sInCount, _sOutCount, _sFuelCount, _sInName, _sOutName, _sFuelName, _sFuelLabel, _sCondName, _sCompare, _sNow, _sFed;
        private RectTransform _sFuelBox, _sCountBox, _sInfBox, _sUntilBox, _sNowTrough;
        private Image _sNowFill;
        private readonly Button[] _sModes = new Button[3];
        private readonly Image[][] _sModeRims = new Image[3][];
        private readonly TMP_Text[] _sModeLabels = new TMP_Text[3];
        private Stepper _sCount, _sThreshold;

        // editor
        private Image _eIcon, _eCondIcon;
        private TMP_Text _eBadge, _eName, _eNet, _eCondName, _eSummary;
        private readonly Button[] _eModes = new Button[3];
        private readonly Image[][] _eModeRims = new Image[3][];
        private readonly TMP_Text[] _eModeLabels = new TMP_Text[3];
        private Image[] _eLessRim, _eAtLeastRim, _eUntilRim;
        private CanvasGroup _eCountGroup, _eUntilGroup;
        private Stepper _eCount, _eThreshold;
        private Button _eDelete;

        // item picker (until-condition dropdown)
        private RectTransform _picker;
        private TMP_InputField _pSearch;
        private readonly List<(Button b, Image icon, TMP_Text label)> _pRows = new List<(Button, Image, TMP_Text)>();
        private readonly List<string> _pAll = new List<string>(), _pShown = new List<string>();
        private int _pScroll;
        private Action<string> _pPick;

        private sealed class Stepper
        {
            public TMP_InputField Field;
            public Func<int> Get;
            public Action<int> Set;
        }

        // LMB drag (Begin false = not draggable now).
        private sealed class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public Func<PointerEventData, bool> Begin;
            public Action<PointerEventData> Move, End;
            private bool _on;

            public void OnBeginDrag(PointerEventData e) => _on = e.button == PointerEventData.InputButton.Left && Begin != null && Begin(e);
            public void OnDrag(PointerEventData e) { if (_on) Move?.Invoke(e); }

            public void OnEndDrag(PointerEventData e)
            {
                if (!_on) return;
                _on = false;
                End?.Invoke(e);
            }
        }

        private sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action<bool> Set;
            public void OnPointerEnter(PointerEventData e) => Set?.Invoke(true);
            public void OnPointerExit(PointerEventData e) => Set?.Invoke(false);
        }

        // Mouse wheel over a list.
        private sealed class Wheel : MonoBehaviour, IScrollHandler
        {
            public Action<float> Scroll;
            public void OnScroll(PointerEventData e) => Scroll?.Invoke(e.scrollDelta.y);
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        private static Color Gold => ThemeTokens.GetColor("color.text.active", Hex("#E8B64A"));
        private static Color Cyan => ThemeTokens.GetColor("color.text.accent", Hex("#4FE0E6"));
        private static Color Muted => ThemeTokens.GetColor("color.state.disabled", Hex("#8A9096"));
        private static Color Dim => new Color(0.24f, 0.51f, 0.56f, 0.55f);
        private static string W(string key) => Words.Get("$ui_sq_" + key);

        // ---------------------------------------------------------------- lifecycle

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (_queue != null) _queue.OpenRequested -= Show;
            if (_instance == this) _instance = null;
            if (_root) Destroy(_root.gameObject);
        }

        private void Update()
        {
            // The service registers when Morgott.Patcher starts (maybe after us): looked up once a second until found.
            if (_queue == null && Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 1f;
                if (Services.TryGet<IStationQueue>(out var q))
                {
                    _queue = q;
                    q.OpenRequested += Show;
                }
            }
            if (!IsOpen) return;
            var p = Player.m_localPlayer;
            var why = !_station ? "station gone" : !p ? "no player" : p.IsDead() ? "player dead" :
                !StationQueueMath.KeepsOpen(Surface(p, _station), p.m_maxInteractDistance) ? $"out of reach: surface {Surface(p, _station):0.0} m > {p.m_maxInteractDistance + StationQueueMath.ReachMargin:0.0} m" :
                InventoryGui.IsVisible() ? "inventory open" : Minimap.IsOpen() ? "map open" : Menu.IsVisible() ? "menu open" : null;
            if (why != null)
            {
                Hide(why);
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.Escape, false))
            {
                if (_picker.gameObject.activeSelf) _picker.gameObject.SetActive(false);
                else if (_editing) EditorClose();
                else Hide("Esc");
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.Return, false) || ZInput.GetKeyDown(KeyCode.KeypadEnter, false))
            {
                if (_picker.gameObject.activeSelf) PickerChoose(0); // Enter in the search = first match
                else if (_editing) EditorSave();
                return;
            }
            if (!_editing && _hoverCard >= 0 && ZInput.GetKeyDown(KeyCode.Delete, false))
            {
                RemoveCard(_hoverCard);
                return;
            }
            if (Time.unscaledTime >= _nextRead && _dragCard < 0 && _dragItem == null) Reload();
        }

        // Eye to the nearest point of the station's rendered bounds (its collider hull, near enough): vanilla hovers a station by its collider hit within m_maxInteractDistance
        // of the eye (Player.cs:4296), and a big station's centre lies metres behind that point.
        private static float Surface(Player p, Component station)
        {
            var eye = p.m_eye ? p.m_eye.position : p.transform.position;
            var best = float.MaxValue;
            foreach (var r in station.GetComponentsInChildren<Renderer>())
                if (r && r.enabled) best = Mathf.Min(best, Vector3.Distance(eye, r.bounds.ClosestPoint(eye)));
            return best < float.MaxValue ? best : Vector3.Distance(eye, station.transform.position);
        }

        private void RemoveCard(int index)
        {
            var list = Entries();
            if (index < 0 || index >= list.Count) return;
            list.RemoveAt(index);
            _hoverCard = -1;
            Commit(list, _state.Paused);
        }

        private void Show(Component station)
        {
            var fail = !station ? "no station" : _queue == null ? "no IStationQueue" : !EnsureBuilt() ? "not built: " + _buildFail :
                !_queue.Read(station, _state) ? "Read refused" : null;
            if (fail != null)
            {
                LastCloseReason = "open failed: " + fail;
                Plugin.Log.LogWarning("[StationQueue] window " + LastCloseReason);
                return;
            }
            _station = station;
            _inScroll = _cardScroll = 0;
            _editing = false;
            _picker.gameObject.SetActive(false);
            _root.gameObject.SetActive(true);
            _nextRead = Time.unscaledTime + RefreshEvery;
            RefreshAll();
        }

        private void Hide(string why)
        {
            if (IsOpen)
            {
                LastCloseReason = why;
                Plugin.Log.LogInfo($"[StationQueue] window closed: {why}");
            }
            if (_root && _root.gameObject.activeSelf) _closedFrame = Time.frameCount;
            _station = null;
            _editing = false;
            _dragCard = -1;
            _dragItem = null;
            _hoverCard = -1;
            if (_ghost) _ghost.gameObject.SetActive(false);
            if (_root) _root.gameObject.SetActive(false);
            LastScreen = "";
        }

        private void Reload()
        {
            _nextRead = Time.unscaledTime + RefreshEvery;
            if (_queue == null || !_station || !_queue.Read(_station, _state))
            {
                Hide("refresh: Read refused or station gone");
                return;
            }
            RefreshAll();
        }

        // Write, then show what the station holds now (a refused write reverts the window to it).
        private void Commit(List<StationQueueEntry> entries, bool paused)
        {
            if (_queue == null || !_station) return;
            if (!_queue.Write(_station, entries, paused) && Player.m_localPlayer)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_privatezone");
            Reload();
        }

        private List<StationQueueEntry> Entries() => StationQueueMath.Clone(_state.Entries);

        // ---------------------------------------------------------------- items

        private ItemDrop Item(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            if (_items.TryGetValue(prefab, out var d) && d) return d;
            var go = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            d = go ? go.GetComponent<ItemDrop>() : null;
            if (d) _items[prefab] = d;
            return d;
        }

        private static string Name(ItemDrop d) => d ? Words.Get(d.m_itemData.m_shared.m_name) : "";
        private string Name(string prefab) => Item(prefab) is ItemDrop d ? Name(d) : prefab ?? "";
        private static Sprite Icon(ItemDrop d) => d ? d.m_itemData.GetIcon() : null;
        private int Net(string prefab) => prefab != null && _state.Network.TryGetValue(prefab, out var n) ? n : 0;

        private string OutputOf(string input)
        {
            foreach (var i in _state.Inputs) if (i.Item && i.Item.name == input) return i.Output ? i.Output.name : null;
            return null;
        }

        private string StatusWord()
        {
            switch (_state.Status)
            {
                case StationQueueStatus.Working: return W("status_working");
                case StationQueueStatus.Waiting: return W("status_waiting");
                case StationQueueStatus.Paused: return W("status_paused");
                default: return W("status_idle");
            }
        }

        private string NetworkPart() => _state.InNetwork
            ? StationQueueMath.Format(W("network"), _state.Chests, StationQueueMath.Plural(_state.Chests, W("chest_one"), W("chest_few"), W("chest_many")))
            : W("no_network");

        // ---------------------------------------------------------------- refresh

        private void RefreshAll()
        {
            var single = StationQueueMath.IsSingle(_state.Inputs.Count);
            if (single) _editing = false; // the single window edits its one entry in place
            SetActive(_multi, !single);
            SetActive(_single, single);
            SetActive(_editorLayer, _editing);
            SetActive(_multi.Find(HintsName), !_editing); // entry-editor.png: only the dialog's hints under it
            Stamp();
            if (!_editing && _picker.gameObject.activeSelf && !single) _picker.gameObject.SetActive(false);
            LastScreen = _editing ? "editor" : single ? "single" : "multi";
            if (single) RefreshSingle();
            else RefreshMulti();
            if (_editing) RefreshEditor();
        }

        private void RefreshMulti()
        {
            Header(_multi).Text(Title(_state.Name));
            var first = _state.Inputs.Count > 0 && _state.Inputs[0].Output ? _state.Inputs[0].Output : null;
            _mStatus.text = StationQueueMath.Join(
                StationQueueMath.Loaded(W(StationQueueMath.LoadedKey(_state.Name)), _state.Loaded, _state.Capacity, null),
                first ? StationQueueMath.Format(W("output_net"), Name(first), StationQueueMath.Group(Net(first.name))) : null,
                NetworkPart());
            _mPause.text = _state.Paused ? W("resume") : W("pause");

            // inputs: known only (0 in the network = dimmed), unknown hidden with a footer note
            var known = new List<StationQueueInput>();
            foreach (var i in _state.Inputs) if (i.Known && i.Item) known.Add(i);
            _inScroll = Mathf.Clamp(_inScroll, 0, Mathf.Max(0, known.Count - InputRows));
            for (var r = 0; r < InputRows; r++)
            {
                var row = _rows[r];
                var k = r + _inScroll;
                SetActive(row.Rt, k < known.Count);
                if (k >= known.Count) continue;
                var input = known[k];
                row.Item = input.Item.name;
                row.Icon.sprite = Icon(input.Item);
                row.Icon.enabled = row.Icon.sprite;
                row.Badge.text = row.Count.text = StationQueueMath.Group(input.Network);
                row.Name.text = Name(input.Item);
                row.Group.alpha = input.Network > 0 ? 1f : 0.5f;
            }
            SetActive(_mHidden, known.Count < _state.Inputs.Count);

            // queue cards
            var entries = _state.Entries;
            _cardScroll = Mathf.Clamp(_cardScroll, 0, Mathf.Max(0, entries.Count - CardRows));
            var shown = 0;
            for (var c = 0; c < CardRows; c++)
            {
                var card = _cards[c];
                var k = c + _cardScroll;
                SetActive(card.Rt, k < entries.Count);
                if (k >= entries.Count) continue;
                shown++;
                Put(card.Rt, 0f, c * CardStep, CardW, CardH); // back in its slot after a drag
                FillCard(card, k, entries[k]);
            }
            Put(_addHint, 0f, shown * CardStep, CardW, AddH);
        }

        // Heights fill the queue column down to the footer rule (kiln.png: no empty band under the add zone).
        private const float CardW = 604f, CardH = 80f, CardStep = 88f, AddH = 66f, ChipH = 28f;

        private void FillCard(Card card, int index, StationQueueEntry e)
        {
            card.Index = index;
            var item = Item(e.Item);
            card.Num.text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            card.Icon.sprite = Icon(item);
            card.Icon.enabled = card.Icon.sprite;
            card.Text.text = StationQueueMath.CardText(e, item ? Name(item) : e.Item, Name(e.Condition), W("card_until"));
            var active = StationQueueMath.IsActive(index, _state.Status);
            SetRim(card.Rim, active ? Cyan : Dim);
            SetActive(card.Trough, active);
            SetActive(card.Fed, active && e.Mode == StationQueueMode.Count);
            var textRt = card.Text.rectTransform;
            Put(textRt, 126f, active ? 6f : 0f, 230f, active ? 44f : CardH);
            if (active)
            {
                var share = StationQueueMath.Share(e, _state.Progress);
                FillBar(card.Fill, card.Trough, share);
                card.Pct.text = Mathf.RoundToInt(share * 100f) + "%";
                card.Fed.text = StationQueueMath.Group(e.Fed) + " / " + StationQueueMath.Group(e.Target);
            }
            card.Chip.text = e.Mode == StationQueueMode.Count ? W("mode_count") : e.Mode == StationQueueMode.Infinite ? "∞" : W("mode_until");
            var chip = e.Mode == StationQueueMode.Count ? Cyan : Gold;
            card.Chip.color = chip;
            SetRim(card.ChipRim, new Color(chip.r, chip.g, chip.b, 0.7f));
            Put(card.Chip.transform.parent, 372f, active ? 14f : (CardH - ChipH) / 2f, 84f, ChipH); // kiln.png: centred, on top of the running card
            // until chip: "Пока" + the condition item's icon (mockup coal cloud)
            var until = e.Mode == StationQueueMode.Until;
            card.ChipIcon.sprite = until ? Icon(Item(e.Condition)) : null;
            card.ChipIcon.enabled = card.ChipIcon.sprite;
            Stretch(card.Chip.rectTransform, 2f, 0f, card.ChipIcon.enabled ? 26f : 2f, 0f);
        }

        private void RefreshSingle()
        {
            Header(_single).Text(Title(_state.Name));
            var input = _state.Inputs[0];
            var id = input.Item ? input.Item.name : null;
            var e = StationQueueMath.SingleEntry(_state.Entries, id);
            Cell(_sIn, _sInCount, _sInName, input.Item, input.Network);
            Cell(_sOut, _sOutCount, _sOutName, input.Output, input.Output ? Net(input.Output.name) : 0);
            SetActive(_sFuelBox, _state.Fuel);
            if (_state.Fuel) Cell(_sFuel, _sFuelCount, _sFuelName, _state.Fuel, Net(_state.Fuel.name));
            _sFuelLabel.text = W("fuel");
            var status = "<color=#" + ColorUtility.ToHtmlStringRGB(Cyan) + ">" + StatusWord() + "</color>";
            var unit = id != null ? Words.Get("$ui_sq_unit_" + id) : null;
            if (unit != null && unit.StartsWith("[", StringComparison.Ordinal)) unit = null; // no unit word for this input
            _sStatus.text = StationQueueMath.Join(
                StationQueueMath.Loaded(W(StationQueueMath.LoadedKey(_state.Name)), _state.Loaded, _state.Capacity, unit),
                _state.Fuel ? Name(_state.Fuel) + " " + Mathf.FloorToInt(_state.FuelLevel) + "/" + _state.FuelMax : null,
                _state.InNetwork ? null : W("no_network"),
                status);
            _sPause.text = _state.Paused ? W("resume") : W("pause");

            // modes: 0 = infinite, 1 = count, 2 = until (mockup order); none = empty queue
            var mode = e == null ? -1 : e.Mode == StationQueueMode.Infinite ? 0 : e.Mode == StationQueueMode.Count ? 1 : 2;
            for (var i = 0; i < 3; i++)
            {
                var on = i == mode;
                SetRim(_sModeRims[i], on ? Cyan : Dim);
                ModeLabel(_sModeLabels[i], on, i == 0 ? W("mode_infinite") : i == 1 ? W("mode_count") : W("mode_until"));
            }
            SetActive(_sCountBox, mode == 1);
            SetActive(_sInfBox, mode == 0 || mode < 0);
            SetActive(_sUntilBox, mode == 2);
            if (e == null) return;
            if (mode == 1)
            {
                SetField(_sCount, e.Target);
                _sFed.text = StationQueueMath.Format(W("fed"), StationQueueMath.Group(e.Fed), StationQueueMath.Group(e.Target));
            }
            if (mode == 2)
            {
                var cond = Item(e.Condition);
                _sCond.sprite = Icon(cond);
                _sCond.enabled = _sCond.sprite;
                _sCondName.text = cond ? Name(cond) : e.Condition;
                _sCompare.text = StationQueueMath.CompareText(e.Compare);
                SetField(_sThreshold, e.Threshold);
                var now = Net(e.Condition);
                _sNow.text = StationQueueMath.Format(W("now"), StationQueueMath.Group(now), StationQueueMath.Group(e.Threshold));
                FillBar(_sNowFill, _sNowTrough, StationQueueMath.Share(now, e.Threshold));
            }
        }

        private static void Cell(Image icon, TMP_Text count, TMP_Text name, ItemDrop item, int n)
        {
            icon.sprite = Icon(item);
            icon.enabled = icon.sprite;
            count.text = StationQueueMath.Group(n);
            name.text = Name(item);
        }

        // The single window's one entry, edited and written (none yet = a new one in that mode).
        private void SingleEdit(Action<StationQueueEntry> edit, StationQueueMode? mode = null)
        {
            var input = _state.Inputs.Count > 0 ? _state.Inputs[0] : null;
            if (input == null || !input.Item) return;
            var cur = StationQueueMath.SingleEntry(_state.Entries, input.Item.name);
            if (cur == null && mode == null) return;
            var e = cur != null ? cur.Clone() : StationQueueMath.NewEntry(input.Item.name, mode.Value, input.Output ? input.Output.name : null);
            if (mode != null && e.Mode != mode.Value)
            {
                e.Mode = mode.Value;
                e.Fed = 0;
                if (e.Condition == null) e.Condition = input.Output ? input.Output.name : null;
            }
            edit?.Invoke(e);
            Commit(StationQueueMath.Single(e), _state.Paused);
        }

        // ---------------------------------------------------------------- editor

        private void EditorOpen(int index, string newItem)
        {
            if (StationQueueMath.IsSingle(_state.Inputs.Count)) return;
            if (index >= 0 && index < _state.Entries.Count)
            {
                _editIndex = index;
                _draft = _state.Entries[index].Clone();
            }
            else
            {
                if (newItem == null)
                    foreach (var i in _state.Inputs)
                        if (i.Known && i.Item) { newItem = i.Item.name; break; }
                if (newItem == null) return;
                _editIndex = -1;
                _draft = StationQueueMath.NewEntry(newItem, StationQueueMode.Count, OutputOf(newItem));
            }
            _editing = true;
            RefreshAll();
        }

        private void EditorClose()
        {
            _editing = false;
            _picker.gameObject.SetActive(false);
            RefreshAll();
        }

        private void EditorSave()
        {
            if (!_editing || _draft == null) return;
            _editing = false;
            _picker.gameObject.SetActive(false);
            Commit(StationQueueMath.Save(_state.Entries, _editIndex, _draft), _state.Paused);
        }

        private void EditorDelete()
        {
            var list = Entries();
            if (_editIndex >= 0 && _editIndex < list.Count && list[_editIndex].Item == _draft.Item) list.RemoveAt(_editIndex);
            _editing = false;
            _picker.gameObject.SetActive(false);
            Commit(list, _state.Paused);
        }

        private void EditorMode(StationQueueMode m)
        {
            _draft.Mode = m;
            if (m == StationQueueMode.Count) _draft.Target = StationQueueMath.ShownTarget(_draft.Target);
            if (m == StationQueueMode.Until && _draft.Condition == null) _draft.Condition = OutputOf(_draft.Item) ?? _draft.Item;
            RefreshEditor();
        }

        private void RefreshEditor()
        {
            if (_draft == null) return;
            var item = Item(_draft.Item);
            _eIcon.sprite = Icon(item);
            _eIcon.enabled = _eIcon.sprite;
            var net = Net(_draft.Item);
            _eBadge.text = StationQueueMath.Group(net);
            _eName.text = item ? Name(item) : _draft.Item;
            _eNet.text = StationQueueMath.Format(W("net_count"), StationQueueMath.Group(net));
            var mode = (int)_draft.Mode; // Count, Infinite, Until = mockup order
            for (var i = 0; i < 3; i++)
            {
                var on = i == mode;
                SetRim(_eModeRims[i], on ? Cyan : Dim);
                ModeLabel(_eModeLabels[i], on, i == 0 ? W("mode_count") : i == 1 ? W("mode_infinite") : W("mode_until"));
            }
            Group(_eCountGroup, _draft.Mode == StationQueueMode.Count);
            Group(_eUntilGroup, _draft.Mode == StationQueueMode.Until);
            SetRim(_eUntilRim, _draft.Mode == StationQueueMode.Until ? Cyan : Dim);
            SetField(_eCount, StationQueueMath.ShownTarget(_draft.Target));
            SetField(_eThreshold, _draft.Threshold);
            var cond = Item(_draft.Condition);
            _eCondIcon.sprite = Icon(cond);
            _eCondIcon.enabled = _eCondIcon.sprite;
            _eCondName.text = cond ? Name(cond) : _draft.Condition ?? "";
            SetRim(_eLessRim, _draft.Compare == StationQueueCompare.Less ? Cyan : Dim);
            SetRim(_eAtLeastRim, _draft.Compare == StationQueueCompare.AtLeast ? Cyan : Dim);
            _eSummary.text = StationQueueMath.Summary(_draft, _eName.text, _eCondName.text, Net(_draft.Condition),
                W("sum_count"), W("sum_infinite"), W("sum_until"));
            SetActive(_eDelete, _editIndex >= 0);
        }

        // Mode button text (mockups): the selected one gold with a cyan radio mark.
        private static void ModeLabel(TMP_Text label, bool on, string word)
        {
            label.richText = true;
            label.color = on ? Gold : ThemeTokens.GetColor("color.text.body", Color.white);
            label.text = StationQueueMath.ModeText(on, word, ColorUtility.ToHtmlStringRGB(Cyan));
        }

        private static void Group(CanvasGroup g, bool on)
        {
            g.alpha = on ? 1f : 0.45f;
            g.interactable = on;
        }

        // ---------------------------------------------------------------- build

        private string _buildFail;

        private static string Path(Transform t) => t ? (t.parent ? Path(t.parent) + "/" : "") + t.name : "";

        private bool EnsureBuilt()
        {
            if (_root) return true;
            // Same host as HudCluster (Hud/HudCluster.cs:58, our HUD drawn in every in-world shot): Hud.m_rootObject, the
            // HUD's own drawn root (vanilla hides the HUD by moving it off screen, Hud.cs:448). Guessed hosts failed in game:
            // the root canvas (run 20261117-184423, never drawn), InventoryGui (185937, never opened), IngameGui (192310,
            // no canvas above it).
            var host = Hud.instance && Hud.instance.m_rootObject ? Hud.instance.m_rootObject.transform : null;
            _buildFail = !Hud.instance ? "no Hud" : !host ? "Hud has no root object" : !host.GetComponentInParent<Canvas>(true) ? "no canvas above " + Path(host) : null;
            if (_buildFail != null) return false;
            _root = NewRect(RootName, host);
            Plugin.Log.LogInfo($"[StationQueue] window built under {Path(host)} (layer {host.gameObject.layer})");
            _root.SetAsLastSibling();
            Stretch(_root, 0f, 0f, 0f, 0f);
            _root.gameObject.SetActive(false);
            ModalLayer.Ensure(_root);
            BuildMulti();
            BuildSingle();
            BuildEditor();
            BuildPicker();
            return true;
        }

        private readonly Dictionary<RectTransform, RuneWindowHeader> _headers = new Dictionary<RectTransform, RuneWindowHeader>();

        private RuneWindowHeader Header(RectTransform panel) => _headers[panel];

        // Centred panel: panel stack, header band (title, drag, close cap), key hints under it.
        private RectTransform Panel(Transform parent, string name, float w, float h, string moveId, Action close, string hints)
        {
            var p = NewRect(name, parent);
            p.anchorMin = p.anchorMax = p.pivot = new Vector2(0.5f, 0.5f);
            p.sizeDelta = new Vector2(w, h);
            var block = p.gameObject.AddComponent<Image>(); // takes the clicks on the panel's empty parts
            block.color = Color.clear;
            block.raycastTarget = true;
            DollCell.Mark(block, "content", "station queue panel click blocker");
            var decor = PanelDecor.Ensure(p, "size.panel.glass.inset", glass: true, plaque: false);
            // Title band = the panel top minus the close cap's column on BOTH sides: plaque and title centred on the panel,
            // the cap right of the plaque (mockups).
            var band = NewRect("TitleBand", p);
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = Vector2.one;
            band.pivot = new Vector2(0.5f, 1f);
            band.offsetMin = new Vector2(CloseColumn, -80f);
            band.offsetMax = new Vector2(-CloseColumn, 0f);
            var header = RuneWindowHeader.Ensure(band, moveId, p);
            header.Frame = decor;
            // mockups: wide serif caps title on a plaque 1.5 standard headers tall
            header.HeightScale = 1.5f;
            header.FaceToken = Serif;
            header.TitleClearsKnots = true; // long station names step down instead of running over the knotwork
            header.Stretch();
            var x = Btn(p, "Close", "button.icon", out var xl, close);
            xl.text = "×";
            xl.fontSize = 26f;
            Put(x, w - CloseColumn + 4f, 10f, 40f, 40f);
            MovableWindow.Ensure(p, moveId);
            _headers[p] = header;
            var hint = Txt(p, HintsName, "text.body", TextAlignmentOptions.Center, 21f);
            hint.richText = true;
            hint.text = hints;
            Put(hint, 0f, h + 14f, w, 30f);
            return p;
        }

        private const string HintsName = "Hints", Serif = "font.display"; // 3rd face (Averia) is map-list only (biome-overlay-design.md)
        private const float CloseColumn = 58f, BadgeW = 150f, MultiH = 680f;
        private readonly List<TMP_Text> _serif = new List<TMP_Text>();

        // Section heads + titles in the serif face (ChangelogPanel.Stamp: the face is late-bound, set once it exists).
        private void Stamp()
        {
            var face = ThemeTokens.GetFont(Serif);
            if (face) foreach (var t in _serif) if (t && t.font != face) t.font = face;
        }

        private static string Title(string key) => Words.Get(key).ToUpperInvariant();
        private float _badgeShare;

        // Multi window: the "Очередь" badge in the header's aux slot (right end of the plaque, kiln.png).
        private void LateUpdate()
        {
            if (!IsOpen || !_multi.gameObject.activeSelf || !_badge) return;
            var h = Header(_multi);
            var rt = (RectTransform)h.transform;
            var hh = rt.rect.height;
            if (!(hh > 0f)) return;
            var share = BadgeW / hh;
            if (Mathf.Abs(share - _badgeShare) > 0.001f)
            {
                _badgeShare = share;
                h.Stretch(share);
            }
            var aux = h.AuxLocal;
            SetActive(_badge, aux.width > 0f);
            if (aux.width <= 0f) return;
            _badge.anchorMin = _badge.anchorMax = _badge.pivot = new Vector2(0.5f, 0.5f);
            var pos = aux.center - rt.rect.center;
            if ((_badge.anchoredPosition - pos).sqrMagnitude > 0.01f) _badge.anchoredPosition = pos;
            if ((_badge.sizeDelta - aux.size).sqrMagnitude > 0.01f) _badge.sizeDelta = aux.size;
        }

        private static string Hint(string key, string word) => StationQueueMath.KeyHint(key, word, ColorUtility.ToHtmlStringRGB(Gold));

        private void BuildMulti()
        {
            const float W0 = 1100f, H0 = MultiH;
            _multi = Panel(_root, "Multi", W0, H0, "station.queue", () => Hide("close button"),
                Hint("Esc", W("close")) + "      " + Hint("LMB", W("drag")) + "      " + Hint("Del", W("delete")));
            var badge = Img(Header(_multi).transform, "QueueBadge", "button.primary");
            badge.preserveAspect = false;
            _badge = badge.rectTransform;
            SetRim(Rim(_badge), Gold);
            _badgeLabel = Txt(_badge, "Label", "text.accent", TextAlignmentOptions.Center, 22f);
            _badgeLabel.text = W("badge");
            _badgeLabel.color = Gold;
            Stretch(_badgeLabel.rectTransform, 4f, 0f, 4f, 0f);
            _mStatus = Txt(_multi, "Status", "text.body", TextAlignmentOptions.Center, 22f);
            Put(_mStatus, 40f, 70f, W0 - 80f, 32f);
            Put(Rule(_multi, "TopRule", Dim), 28f, 108f, W0 - 56f, 1f);

            // inputs column
            Section(_multi, "InputsLabel", W("inputs"), 28f, 116f);
            _rowsRt = NewRect("Inputs", _multi);
            Put(_rowsRt, 28f, 158f, 400f, InputRows * 84f);
            WheelOn(_rowsRt, d => { _inScroll -= Math.Sign(d); RefreshAll(); });
            for (var i = 0; i < InputRows; i++) _rows.Add(NewInputRow(_rowsRt, i));
            _mHidden = Txt(_multi, "UnknownHidden", "text.muted", TextAlignmentOptions.Center, 18f);
            _mHidden.text = W("unknown_hidden");
            _mHidden.color = Muted;
            Put(_mHidden, 28f, 158f + InputRows * 84f + 4f, 400f, 26f);
            Put(Rule(_multi, "SideRule", Dim), 446f, 116f, 1f, 480f);

            // queue column
            Section(_multi, "QueueLabel", W("queue"), 468f, 116f);
            _cardsRt = NewRect("Queue", _multi);
            Put(_cardsRt, 468f, 158f, CardW, 446f);
            WheelOn(_cardsRt, d => { _cardScroll -= Math.Sign(d); RefreshAll(); });
            for (var i = 0; i < CardRows; i++) _cards.Add(NewCard(_cardsRt, i));
            _addHint = NewRect("AddHint", _cardsRt);
            Dashes(_addHint, CardW, AddH, new Color(Dim.r, Dim.g, Dim.b, 0.9f));
            var addText = Txt(_addHint, "Text", "text.muted", TextAlignmentOptions.Center, 20f);
            addText.text = W("add_hint");
            addText.color = Muted;
            Stretch(addText.rectTransform, 0f, 0f, 0f, 0f);

            // drag ghost of an input row (dropped on the queue = appended)
            _ghost = IconIn(_root, 0f);
            _ghost.rectTransform.anchorMin = _ghost.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _ghost.rectTransform.sizeDelta = new Vector2(64f, 64f);
            _ghost.gameObject.SetActive(false);

            // footer
            Put(Rule(_multi, "BottomRule", Dim), 28f, 612f, W0 - 56f, 1f);
            var pause = PlainBtn(_multi, "Pause", out _mPause, () => Commit(Entries(), !_state.Paused), Light);
            _mPause.fontSize = 22f;
            Put(pause, 60f, 624f, 150f, 42f);
            var clear = PlainBtn(_multi, "Clear", out var clearLabel, () => Commit(new List<StationQueueEntry>(), _state.Paused), Light);
            clearLabel.text = W("clear");
            clearLabel.fontSize = 22f;
            Put(clear, 226f, 624f, 250f, 42f);
            var order = Txt(_multi, "Order", "text.muted", TextAlignmentOptions.Right, 19f);
            order.text = W("order");
            order.color = Muted;
            Put(order, W0 - 460f, 630f, 400f, 30f);
        }

        private void Section(Transform parent, string name, string text, float x, float y)
        {
            var t = Txt(parent, name, "text.header", TextAlignmentOptions.Left, 28f);
            t.text = text;
            t.color = ThemeTokens.GetColor("color.text.header", Hex("#D9DCE0"));
            _serif.Add(t);
            Put(t, x, y, 300f, 36f);
        }

        private InputRow NewInputRow(RectTransform parent, int i)
        {
            var row = new InputRow { Rt = NewRect("Row" + i, parent) };
            Put(row.Rt, 0f, i * 84f, 400f, 80f);
            var hit = row.Rt.gameObject.AddComponent<Image>(); // drag source (LMB onto the queue appends)
            hit.color = Color.clear;
            hit.raycastTarget = true;
            DollCell.Mark(hit, "content", "station queue input row drag area");
            var drag = row.Rt.gameObject.AddComponent<DragHandle>();
            drag.Begin = e =>
            {
                if (row.Item == null) return false;
                _dragItem = row.Item;
                _ghost.sprite = row.Icon.sprite;
                _ghost.transform.SetAsLastSibling();
                _ghost.gameObject.SetActive(_ghost.sprite);
                return true;
            };
            drag.Move = e =>
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, e.position, e.pressEventCamera, out var lp))
                    _ghost.rectTransform.anchoredPosition = lp - _root.rect.center;
            };
            drag.End = e =>
            {
                var item = _dragItem;
                _dragItem = null;
                _ghost.gameObject.SetActive(false);
                if (item == null || !RectTransformUtility.RectangleContainsScreenPoint(_cardsRt, e.position, e.pressEventCamera)) return;
                var list = Entries();
                list.Add(StationQueueMath.NewEntry(item, StationQueueMode.Count, OutputOf(item)));
                Commit(list, _state.Paused);
            };
            row.Group = row.Rt.gameObject.AddComponent<CanvasGroup>();
            row.Icon = SlotIcon(row.Rt, 0f, 6f, 68f, out row.Badge);
            row.Name = Txt(row.Rt, "Name", "text.body", TextAlignmentOptions.Left, 21f);
            row.Name.textWrappingMode = TextWrappingModes.Normal;
            Put(row.Name, 84f, 0f, 200f, 80f);
            row.Count = Txt(row.Rt, "Count", "text.body", TextAlignmentOptions.Right, 21f);
            Put(row.Count, 284f, 0f, 60f, 80f);
            var plus = Btn(row.Rt, "Add", "button.icon", out var pl, () =>
            {
                if (row.Item == null) return;
                var list = Entries();
                list.Add(StationQueueMath.NewEntry(row.Item, StationQueueMode.Count, OutputOf(row.Item)));
                Commit(list, _state.Paused);
            });
            pl.text = "+";
            pl.fontSize = 26f;
            Put(plus, 354f, 20f, 40f, 40f);
            Put(Rule(row.Rt, "Rule", new Color(Dim.r, Dim.g, Dim.b, 0.3f)), 84f, 81f, 316f, 1f);
            return row;
        }

        private Card NewCard(RectTransform parent, int i)
        {
            var card = new Card();
            var plate = Btn(parent, "Card" + i, "field", out var unused, () => EditorOpen(card.Index, null));
            Destroy(unused.gameObject);
            card.Rt = (RectTransform)plate.transform;
            Put(card.Rt, 0f, i * CardStep, CardW, CardH);
            card.Rim = Rim(card.Rt);
            // grip: 2 x 3 dots, LMB drag reorders
            var grip = Img(card.Rt, "Grip", "ib.rule", true);
            grip.color = Color.clear;
            Put(grip, 4f, 0f, 26f, CardH);
            for (var d = 0; d < 6; d++) Put(Rule(grip.transform, "Dot" + d, Muted), 7f + d % 2 * 8f, CardH / 2f - 11f + d / 2 * 8f, 4f, 4f);
            var gd = grip.gameObject.AddComponent<DragHandle>();
            gd.Begin = e =>
            {
                _dragCard = card.Index;
                card.Rt.SetAsLastSibling();
                return true;
            };
            gd.Move = e =>
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_cardsRt, e.position, e.pressEventCamera, out var lp)) return;
                var y = _cardsRt.rect.yMax - lp.y - CardH / 2f;
                Put(card.Rt, 0f, Mathf.Clamp(y, 0f, (CardRows - 1) * CardStep), CardW, CardH);
            };
            gd.End = e =>
            {
                var from = _dragCard;
                _dragCard = -1;
                var list = Entries();
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_cardsRt, e.position, e.pressEventCamera, out var lp) &&
                    StationQueueMath.MoveTo(list, from, StationQueueMath.SlotAt(_cardsRt.rect.yMax - lp.y, CardStep, _cardScroll)))
                    Commit(list, _state.Paused);
                else RefreshAll();
            };
            card.Rt.gameObject.AddComponent<Hover>().Set = on =>
            {
                if (on) _hoverCard = card.Index;
                else if (_hoverCard == card.Index) _hoverCard = -1;
            };
            card.Num = Txt(card.Rt, "Num", "text.body", TextAlignmentOptions.Center, 22f);
            Put(card.Num, 28f, 0f, 30f, CardH);
            card.Icon = SlotIcon(card.Rt, 62f, (CardH - 58f) / 2f, 58f, out var badge);
            Destroy(badge.gameObject);
            card.Text = Txt(card.Rt, "Text", "text.body", TextAlignmentOptions.Left, 19f);
            card.Text.textWrappingMode = TextWrappingModes.Normal;
            card.Trough = Bar(card.Rt, out card.Fill);
            Put(card.Trough, 126f, 54f, 230f, 16f);
            card.Pct = Txt(card.Trough, "Pct", "text.body", TextAlignmentOptions.Center, 15f);
            Stretch(card.Pct.rectTransform, 0f, -2f, 0f, -2f);
            card.Fed = Txt(card.Rt, "Fed", "text.accent", TextAlignmentOptions.Left, 18f);
            card.Fed.color = Cyan;
            Put(card.Fed, 372f, 48f, 92f, 24f);
            var chipRt = NewRect("Chip", card.Rt);
            Put(chipRt, 372f, 14f, 84f, ChipH);
            card.ChipRim = Rim(chipRt);
            card.ChipIcon = IconIn(chipRt, 0f);
            Put(card.ChipIcon, 60f, 3f, 22f, 22f);
            card.Chip = Txt(chipRt, "Label", "text.body", TextAlignmentOptions.Center, 17f);
            Stretch(card.Chip.rectTransform, 2f, 0f, 2f, 0f);
            Arrow(card, "Up", "▲", 466f, () => Reorder(card.Index, -1));
            Arrow(card, "Down", "▼", 512f, () => Reorder(card.Index, 1));
            Arrow(card, "Remove", "×", 558f, () =>
            {
                var list = Entries();
                if (card.Index < list.Count) list.RemoveAt(card.Index);
                Commit(list, _state.Paused);
            });
            return card;
        }

        private void Arrow(Card card, string name, string glyph, float x, Action click)
        {
            var b = Btn(card.Rt, name, "button.icon", out var l, click);
            l.text = glyph;
            l.fontSize = name == "Remove" ? 24f : 16f;
            Put(b, x, (CardH - 38f) / 2f, 38f, 38f);
        }

        private void Reorder(int index, int delta)
        {
            var list = Entries();
            if (StationQueueMath.Move(list, index, delta)) Commit(list, _state.Paused);
        }

        private void BuildSingle()
        {
            const float W0 = 720f, H0 = 636f;
            _single = Panel(_root, "Single", W0, H0, "station.queue.single", () => Hide("close button"), Hint("Esc", W("close")));
            // recipe row: input -> output | fuel (counts = network)
            _sIn = SlotIcon(_single, 80f, 84f, 100f, out _sInCount);
            _sInName = CellName(_single, "InName", 50f);
            ArrowGlyph(_single, 206f, 134f, 60f);
            _sOut = SlotIcon(_single, 292f, 84f, 100f, out _sOutCount);
            _sOutName = CellName(_single, "OutName", 262f);
            _sFuelBox = NewRect("Fuel", _single);
            Put(_sFuelBox, 440f, 50f, 260f, 190f);
            Put(Rule(_sFuelBox, "Rule", Dim), 0f, 30f, 1f, 110f);
            // eitr-refinery.png: label clear of the title ribbon, its cell a little below the recipe cells
            _sFuelLabel = Txt(_sFuelBox, "Label", "text.body", TextAlignmentOptions.Center, 20f);
            Put(_sFuelLabel, 60f, 20f, 160f, 26f);
            _sFuel = SlotIcon(_sFuelBox, 90f, 46f, 100f, out _sFuelCount);
            _sFuelName = CellName(_sFuelBox, "FuelName", 60f, 140f);
            _sStatus = Txt(_single, "Status", "text.body", TextAlignmentOptions.Center, 20f);
            _sStatus.richText = true;
            Put(_sStatus, 28f, 242f, W0 - 56f, 30f);
            Put(Rule(_single, "Rule1", Dim), 28f, 278f, W0 - 56f, 1f);
            Section(_single, "ModeLabel", W("mode"), 28f, 284f);
            var modes = new[] { StationQueueMode.Infinite, StationQueueMode.Count, StationQueueMode.Until };
            for (var i = 0; i < 3; i++)
            {
                var m = modes[i];
                _sModes[i] = Btn(_single, "Mode" + i, "field", out _sModeLabels[i], () => SingleEdit(null, m));
                _sModeLabels[i].fontSize = 22f;
                _sModeRims[i] = Rim(_sModes[i].transform);
                Put(_sModes[i], 28f + i * 226f, 324f, 212f, 46f);
            }

            // detail boxes of the selected mode
            var box = Img(_single, "Detail", "well");
            box.preserveAspect = false;
            Put(box, 28f, 384f, W0 - 56f, 170f);
            _sCountBox = NewRect("Count", box.transform);
            Stretch(_sCountBox, 0f, 0f, 0f, 0f);
            Label(_sCountBox, W("count_label"), 20f, 10f);
            _sCount = NewStepper(_sCountBox, 20f, 50f, () => CurrentSingle()?.Target ?? 0, v => SingleEdit(e => e.Target = v));
            Chips(_sCountBox, 330f, 50f, v => SingleEdit(e => e.Target = v), () => _state.Inputs.Count > 0 ? _state.Inputs[0].Network : 0);
            _sFed = Txt(_sCountBox, "Fed", "text.body", TextAlignmentOptions.Left, 20f);
            Put(_sFed, 20f, 110f, 400f, 30f);
            _sInfBox = NewRect("Infinite", box.transform);
            Stretch(_sInfBox, 0f, 0f, 0f, 0f);
            var note = Label(_sInfBox, W("infinite_note"), 20f, 60f);
            note.color = Muted;
            _sUntilBox = NewRect("Until", box.transform);
            Stretch(_sUntilBox, 0f, 0f, 0f, 0f);
            Label(_sUntilBox, W("process_while"), 20f, 8f);
            var condBtn = Btn(_sUntilBox, "Condition", "slot.cell", out var cl, null);
            Destroy(cl.gameObject);
            Put(condBtn, 20f, 42f, 56f, 56f);
            _sCond = IconIn(condBtn.transform, 6f);
            condBtn.onClick.AddListener(() => OpenPicker((RectTransform)condBtn.transform, id => SingleEdit(e => e.Condition = id)));
            _sCondName = Txt(_sUntilBox, "CondName", "text.body", TextAlignmentOptions.Left, 19f);
            _sCondName.textWrappingMode = TextWrappingModes.Normal;
            Put(_sCondName, 84f, 42f, 150f, 56f);
            var cmp = Btn(_sUntilBox, "Compare", "button.icon", out _sCompare,
                () => SingleEdit(e => e.Compare = e.Compare == StationQueueCompare.Less ? StationQueueCompare.AtLeast : StationQueueCompare.Less));
            _sCompare.fontSize = 22f;
            Put(cmp, 242f, 50f, 44f, 40f);
            _sThreshold = NewStepper(_sUntilBox, 300f, 50f, () => CurrentSingle()?.Threshold ?? 0, v => SingleEdit(e => e.Threshold = v));
            var inNet = Txt(_sUntilBox, "InNet", "text.body", TextAlignmentOptions.Left, 20f);
            inNet.text = W("in_network");
            Put(inNet, 504f, 50f, 140f, 40f);
            _sNow = Txt(_sUntilBox, "Now", "text.body", TextAlignmentOptions.Left, 20f);
            Put(_sNow, 20f, 104f, 400f, 28f);
            _sNowTrough = Bar(_sUntilBox, out _sNowFill);
            Put(_sNowTrough, 20f, 134f, W0 - 56f - 40f, 22f);

            Put(Rule(_single, "Rule2", Dim), 28f, 570f, W0 - 56f, 1f);
            var pause = PlainBtn(_single, "Pause", out _sPause, () => Commit(Entries(), !_state.Paused), Light);
            _sPause.fontSize = 22f;
            Put(pause, 40f, 582f, 150f, 42f);
            var reset = PlainBtn(_single, "Reset", out var rl, () => Commit(new List<StationQueueEntry>(), _state.Paused), Light);
            rl.text = W("reset");
            rl.fontSize = 22f;
            Put(reset, 206f, 582f, 170f, 42f);
            // two lines inside the frame (eitr-refinery.png), clear of its right border
            var from = Txt(_single, "FromNetwork", "text.muted", TextAlignmentOptions.TopLeft, 20f);
            from.text = W("from_network");
            from.color = Muted;
            from.textWrappingMode = TextWrappingModes.Normal;
            from.overflowMode = TextOverflowModes.Overflow;
            Put(from, 450f, 578f, 230f, 52f);
        }

        private StationQueueEntry CurrentSingle() =>
            _state.Inputs.Count > 0 && _state.Inputs[0].Item ? StationQueueMath.SingleEntry(_state.Entries, _state.Inputs[0].Item.name) : null;

        // Own name per cell: Txt reuses a same-named child (InfoArt.RowText), so one shared "Name" left the input unnamed.
        private TMP_Text CellName(Transform parent, string name, float x, float w = 160f)
        {
            var t = Txt(parent, name, "text.body", TextAlignmentOptions.Top, 19f);
            t.textWrappingMode = TextWrappingModes.Normal;
            Put(t, x, parent == _single ? 190f : 150f, w, 50f);
            return t;
        }

        // Thin cyan "→" between input and output (eitr-refinery.png), drawn from rules: no font glyph to go missing.
        private static void ArrowGlyph(Transform parent, float x, float yMid, float w)
        {
            const float T = 3f, Head = 16f;
            var a = NewRect("Arrow", parent);
            Put(a, x, yMid - Head / 2f, w, Head);
            Put(Rule(a, "Shaft", Cyan), 0f, Head / 2f - T / 2f, w - 1f, T);
            foreach (var deg in new[] { 40f, -40f })
            {
                var rt = Rule(a, "Head", Cyan).rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(Head, T);
                rt.anchoredPosition = Vector2.zero;
                rt.localRotation = Quaternion.Euler(0f, 0f, deg);
            }
        }

        private TMP_Text Label(Transform parent, string text, float x, float y)
        {
            var t = Txt(parent, "Label", "text.body", TextAlignmentOptions.Left, 20f);
            t.text = text;
            Put(t, x, y, 600f, 30f);
            return t;
        }

        private void Chips(Transform parent, float x, float y, Action<int> set, Func<int> all)
        {
            var values = new[] { 10, 50, 100, -1 };
            for (var i = 0; i < values.Length; i++)
            {
                var v = values[i];
                var b = PlainBtn(parent, "Chip" + i, out var l, () => set(StationQueueMath.Clamp(v > 0 ? v : all())), Light);
                l.text = v > 0 ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : W("all");
                l.fontSize = 20f;
                Put(b, x + i * 70f, y, 62f, 40f);
            }
        }

        private Stepper NewStepper(Transform parent, float x, float y, Func<int> get, Action<int> set)
        {
            var s = new Stepper { Get = get, Set = set };
            var minus = Btn(parent, "Minus", "craft.amount.step", out var ml, () => s.Set(StationQueueMath.Step(s.Get(), -1, Shift())));
            ml.text = "−";
            ml.fontSize = 22f;
            Put(minus, x, y, 40f, 40f);
            s.Field = InputBox(parent, "Value", null);
            s.Field.contentType = TMP_InputField.ContentType.IntegerNumber;
            s.Field.characterLimit = 5;
            s.Field.textComponent.alignment = TextAlignmentOptions.Center;
            s.Field.onEndEdit.AddListener(text =>
            {
                if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v)) s.Set(StationQueueMath.Clamp(v));
                else SetField(s, s.Get());
            });
            Put(s.Field, x + 46f, y, 100f, 40f);
            var plus = Btn(parent, "Plus", "craft.amount.step", out var pl, () => s.Set(StationQueueMath.Step(s.Get(), 1, Shift())));
            pl.text = "+";
            pl.fontSize = 22f;
            Put(plus, x + 152f, y, 40f, 40f);
            return s;
        }

        private static bool Shift() => ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false);

        private static void SetField(Stepper s, int v)
        {
            if (s.Field.isFocused) return; // the player is typing
            var text = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (s.Field.text != text) s.Field.SetTextWithoutNotify(text);
        }

        private void BuildEditor()
        {
            const float W0 = 620f, H0 = 576f;
            _editorLayer = NewRect("EditorLayer", _root);
            Stretch(_editorLayer, 0f, 0f, 0f, 0f);
            var dim = _editorLayer.gameObject.AddComponent<Image>();
            // The game blends in linear space (ChangelogPanel scrim note): 0.55 black kept ~70 % sRGB light and the queue
            // window read as undimmed (run 20261117-192839); 0.85 = the mockup's dimmed parent.
            dim.color = new Color(0f, 0f, 0f, 0.85f);
            dim.raycastTarget = true; // the queue window under the editor takes no clicks
            DollCell.Mark(dim, "content", "station queue editor backdrop");
            _editor = Panel(_editorLayer, "Editor", W0, H0, "station.queue.editor", EditorClose, Hint("Enter", W("save")) + "      " + Hint("Esc", W("cancel")));
            // entry-editor.png: the dialog hangs below the queue window's bottom edge, its hints right under it
            _editor.anchoredPosition = new Vector2(0f, -StationQueueMath.EditorDrop(MultiH, H0, 12f));
            Header(_editor).Text(Title("$ui_sq_editor_title"));

            _eIcon = SlotIcon(_editor, 28f, 72f, 76f, out _eBadge);
            _eName = Txt(_editor, "Name", "text.accent", TextAlignmentOptions.Left, 26f);
            _eName.color = Gold;
            Put(_eName, 120f, 74f, W0 - 150f, 34f);
            _eNet = Txt(_editor, "Net", "text.body", TextAlignmentOptions.Left, 20f);
            Put(_eNet, 120f, 110f, W0 - 150f, 28f);
            Put(Rule(_editor, "Rule0", Dim), 28f, 158f, W0 - 56f, 1f);
            Section(_editor, "ModeLabel", W("mode"), 28f, 162f);
            var modes = new[] { StationQueueMode.Count, StationQueueMode.Infinite, StationQueueMode.Until };
            for (var i = 0; i < 3; i++)
            {
                var m = modes[i];
                _eModes[i] = Btn(_editor, "Mode" + i, "field", out _eModeLabels[i], () => EditorMode(m));
                _eModeLabels[i].fontSize = 20f;
                _eModeRims[i] = Rim(_eModes[i].transform);
                Put(_eModes[i], 28f + i * 190f, 200f, 178f, 40f);
            }
            var countRt = NewRect("Count", _editor);
            Put(countRt, 28f, 252f, W0 - 56f, 44f);
            _eCountGroup = countRt.gameObject.AddComponent<CanvasGroup>();
            _eCount = NewStepper(countRt, 0f, 0f, () => StationQueueMath.ShownTarget(_draft.Target), v => { _draft.Target = v; RefreshEditor(); });
            Chips(countRt, 270f, 0f, v => { _draft.Target = v; RefreshEditor(); }, () => Net(_draft.Item));

            var until = NewRect("Until", _editor);
            Put(until, 28f, 308f, W0 - 56f, 108f);
            _eUntilGroup = until.gameObject.AddComponent<CanvasGroup>();
            _eUntilRim = Rim(until);
            var ut = Section2(until, W("until_net"));
            Put(ut, 16f, 8f, 300f, 32f);
            var drop = Btn(until, "Item", "dropdown.field", out _eCondName, null);
            _eCondName.alignment = TextAlignmentOptions.Left;
            Stretch(_eCondName.rectTransform, 50f, 0f, 34f, 0f);
            Put(drop, 16f, 50f, 230f, 42f);
            _eCondIcon = IconIn(drop.transform, 4f);
            Put(_eCondIcon, 6f, 4f, 34f, 34f);
            var arrow = Txt(drop.transform, "Arrow", "text.body", TextAlignmentOptions.Center, 16f);
            arrow.text = "▼";
            Put(arrow, 196f, 0f, 30f, 42f);
            drop.onClick.AddListener(() => OpenPicker((RectTransform)drop.transform, id => { _draft.Condition = id; RefreshEditor(); }));
            var less = Btn(until, "Less", "button.icon", out var ll, () => { _draft.Compare = StationQueueCompare.Less; RefreshEditor(); });
            ll.text = "<";
            ll.fontSize = 22f;
            _eLessRim = Rim(less.transform);
            Put(less, 256f, 51f, 40f, 40f);
            var atLeast = Btn(until, "AtLeast", "button.icon", out var al, () => { _draft.Compare = StationQueueCompare.AtLeast; RefreshEditor(); });
            al.text = "≥";
            al.fontSize = 22f;
            _eAtLeastRim = Rim(atLeast.transform);
            Put(atLeast, 300f, 51f, 40f, 40f);
            _eThreshold = NewStepper(until, 356f, 51f, () => _draft.Threshold, v => { _draft.Threshold = v; RefreshEditor(); });

            _eSummary = Txt(_editor, "Summary", "text.body", TextAlignmentOptions.TopLeft, 19f);
            _eSummary.textWrappingMode = TextWrappingModes.Normal;
            Put(_eSummary, 36f, 428f, W0 - 72f, 56f);
            Put(Rule(_editor, "Rule1", Dim), 28f, 492f, W0 - 56f, 1f);
            var red = ThemeTokens.GetColor("color.text.missing", Hex("#E0413A"));
            _eDelete = PlainBtn(_editor, "Delete", out var dl, EditorDelete, red);
            dl.text = W("delete");
            dl.color = red;
            dl.fontSize = 22f;
            Put(_eDelete, 40f, 506f, 160f, 44f);
            var cancel = PlainBtn(_editor, "Cancel", out var cl, EditorClose, Light);
            cl.text = W("cancel");
            cl.fontSize = 22f;
            Put(cancel, 230f, 506f, 160f, 44f);
            var save = PlainBtn(_editor, "Save", out var sl, EditorSave, Gold);
            sl.text = W("save");
            sl.fontSize = 22f;
            sl.color = Gold;
            Put(save, 420f, 506f, 160f, 44f);
        }

        // Plain rimmed plate (mockups: footer / dialog buttons are flat rectangles with a coloured rim, no knotwork).
        private static Button PlainBtn(Transform parent, string name, out TMP_Text label, Action click, Color rim)
        {
            var b = Btn(parent, name, "field", out label, click);
            SetRim(Rim(b.transform), rim);
            return b;
        }

        private static Color Light => new Color(0.62f, 0.68f, 0.72f, 0.75f);

        private TMP_Text Section2(Transform parent, string text)
        {
            var t = Txt(parent, "Title", "text.header", TextAlignmentOptions.Left, 24f);
            t.text = text;
            _serif.Add(t);
            return t;
        }

        // ---------------------------------------------------------------- picker

        private void BuildPicker()
        {
            _picker = (RectTransform)Img(_root, "Picker", "dropdown.list", true).transform;
            _picker.GetComponent<Image>().preserveAspect = false;
            _picker.anchorMin = _picker.anchorMax = new Vector2(0.5f, 0.5f);
            _picker.pivot = new Vector2(0f, 1f);
            _picker.sizeDelta = new Vector2(290f, 52f + PickerRows * 34f);
            _pSearch = InputBox(_picker, "Search", "$ui_sq_search");
            Put(_pSearch, 8f, 8f, 274f, 36f);
            var glass = Img(_pSearch.transform, "Glyph", "craft.glyph.search");
            glass.color = Muted;
            Put(glass, 10f, 8f, 20f, 20f);
            _pSearch.textViewport.offsetMin = new Vector2(38f, 0f);
            _pSearch.onValueChanged.AddListener(_ => { _pScroll = 0; PickerFill(); });
            for (var i = 0; i < PickerRows; i++)
            {
                var n = i;
                var b = Btn(_picker, "Row" + i, "dropdown.item", out var label, () => PickerChoose(n));
                label.alignment = TextAlignmentOptions.Left;
                Stretch(label.rectTransform, 44f, 0f, 6f, 0f);
                var icon = IconIn(b.transform, 0f);
                Put(icon, 6f, 2f, 30f, 30f);
                Put(b, 8f, 50f + i * 34f, 274f, 32f);
                _pRows.Add((b, icon, label));
            }
            WheelOn(_picker, d => { _pScroll -= Math.Sign(d); PickerFill(); });
            _picker.gameObject.SetActive(false);
        }

        // Candidates: every item in the station network + the station's inputs, outputs and fuel (names sorted).
        private void OpenPicker(RectTransform anchor, Action<string> pick)
        {
            if (_picker.gameObject.activeSelf) { _picker.gameObject.SetActive(false); return; }
            _pPick = pick;
            var set = new HashSet<string>(_state.Network.Keys, StringComparer.Ordinal);
            foreach (var i in _state.Inputs)
            {
                if (i.Item) set.Add(i.Item.name);
                if (i.Output) set.Add(i.Output.name);
            }
            if (_state.Fuel) set.Add(_state.Fuel.name);
            _pAll.Clear();
            foreach (var id in set) if (Item(id)) _pAll.Add(id);
            _pAll.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(Name(a), Name(b)));
            _pScroll = 0;
            _pSearch.SetTextWithoutNotify("");
            var c = new Vector3[4];
            anchor.GetWorldCorners(c);
            var local = (Vector2)_root.InverseTransformPoint(c[0]);
            _picker.anchoredPosition = local - _root.rect.center + new Vector2(0f, -4f);
            _picker.SetAsLastSibling();
            _picker.gameObject.SetActive(true);
            PickerFill();
            _pSearch.ActivateInputField();
        }

        private void PickerFill()
        {
            _pShown.Clear();
            foreach (var id in _pAll) if (StationQueueMath.Matches(Name(id), _pSearch.text)) _pShown.Add(id);
            _pScroll = Mathf.Clamp(_pScroll, 0, Mathf.Max(0, _pShown.Count - PickerRows));
            for (var i = 0; i < PickerRows; i++)
            {
                var k = i + _pScroll;
                var (b, icon, label) = _pRows[i];
                SetActive(b, k < _pShown.Count);
                if (k >= _pShown.Count) continue;
                var d = Item(_pShown[k]);
                icon.sprite = Icon(d);
                icon.enabled = icon.sprite;
                label.text = Name(d);
            }
        }

        private void PickerChoose(int row)
        {
            var k = row + _pScroll;
            if (k < 0 || k >= _pShown.Count) return;
            _picker.gameObject.SetActive(false);
            _pPick?.Invoke(_pShown[k]);
        }

        // ---------------------------------------------------------------- widget helpers (ChangelogPanel house style)

        private static void WheelOn(RectTransform host, Action<float> scroll)
        {
            var hit = new GameObject("WheelHit", typeof(RectTransform)).AddComponent<Image>();
            hit.rectTransform.SetParent(host, false);
            hit.rectTransform.SetAsFirstSibling();
            Stretch(hit.rectTransform, 0f, 0f, 0f, 0f);
            hit.color = Color.clear;
            hit.raycastTarget = true;
            DollCell.Mark(hit, "content", "station queue wheel area");
            host.gameObject.AddComponent<Wheel>().Scroll = scroll;
        }

        // Slot cell with the item icon and a big count over its bottom (mockups). The count is the cell's sibling, drawn
        // after it: as a child, CellFit's zone rule filed the cell-sized box in the top band and shrank it to the band
        // height (run 20261117-192839: tiny top-right numbers).
        private static Image SlotIcon(Transform parent, float x, float y, float size, out TMP_Text badge)
        {
            var cell = Img(parent, "Cell", "slot.cell");
            cell.preserveAspect = false;
            Put(cell, x, y, size, size);
            var icon = IconIn(cell.transform, size * 0.12f);
            badge = Txt(parent, "Badge" + cell.transform.GetSiblingIndex(), "text.body", TextAlignmentOptions.BottomRight, size * 0.3f);
            badge.fontStyle = FontStyles.Bold;
            badge.overflowMode = TextOverflowModes.Overflow;
            Put(badge, x + 2f, y + size * 0.5f, size - 8f, size * 0.5f - 2f);
            return icon;
        }

        private static Image IconIn(Transform parent, float inset)
        {
            var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
            icon.rectTransform.SetParent(parent, false);
            Stretch(icon.rectTransform, inset, inset, inset, inset);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            DollCell.Mark(icon, "content", "station queue item icon");
            return icon;
        }

        // 2 units: every edge covers the same whole pixels at the game's scale (1.5 drew 2 px on one side, 1 on the other).
        private static Image[] Rim(Transform host) => OutlineFrame.Build(host, 2f);

        // Dashed outline (drop zone): 10-unit dashes, 6-unit gaps.
        private static void Dashes(RectTransform host, float w, float h, Color c)
        {
            const float Dash = 10f, Gap = 6f, T = 1.5f;
            for (var x = 4f; x < w - 4f; x += Dash + Gap)
            {
                var len = Mathf.Min(Dash, w - 4f - x);
                Put(Rule(host, "DashT", c), x, 0f, len, T);
                Put(Rule(host, "DashB", c), x, h - T, len, T);
            }
            for (var y = 4f; y < h - 4f; y += Dash + Gap)
            {
                var len = Mathf.Min(Dash, h - 4f - y);
                Put(Rule(host, "DashL", c), 0f, y, T, len);
                Put(Rule(host, "DashR", c), w - T, y, T, len);
            }
        }

        private const float BarInset = 3f;

        // Progress bar (mockups): dark track with a visible rim even at 0, flat cyan fill inside it.
        private static RectTransform Bar(Transform parent, out Image fill)
        {
            var track = Rule(parent, "Trough", Hex("#0E191D"));
            fill = Rule(track.transform, "Fill", Hex("#00D6E8"));
            SetRim(Rim(track.transform), Hex("#4D6977"));
            return track.rectTransform;
        }

        private static void FillBar(Image fill, RectTransform track, float share)
        {
            var r = track.rect;
            Put(fill, BarInset, BarInset, Mathf.Max(0f, r.width - 2f * BarInset) * share, Mathf.Max(0f, r.height - 2f * BarInset));
        }

        private static void SetRim(Image[] rim, Color c)
        {
            foreach (var l in rim) if (l.color != c) l.color = c;
        }

        private static Image Rule(Transform parent, string name, Color color)
        {
            var img = Img(parent, name, "ib.rule");
            img.enabled = true;
            img.preserveAspect = false;
            img.color = color;
            return img;
        }

        private static Image Img(Transform parent, string name, string role, bool raycast = false)
        {
            var image = InfoArt.OwnImage(parent, name, role, "station queue " + name);
            image.raycastTarget = raycast;
            return image;
        }

        private static TMP_Text Txt(Transform parent, string name, string role, TextAlignmentOptions align, float size)
        {
            var t = InfoArt.RowText(parent, name, role, align);
            OwnFit.Claim(t);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.enableAutoSizing = false;
            t.fontSize = size;
            return t;
        }

        private static Button Btn(Transform parent, string name, string role, out TMP_Text label, Action click)
        {
            var image = InfoArt.OwnImage(parent, name, role, "station queue button " + name);
            image.preserveAspect = false;
            image.raycastTarget = true;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            if (click != null) b.onClick.AddListener(() => click());
            if (image.GetComponent<SkinMark>() is SkinMark m) m.Outcome = Restyler.Apply(image, m, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            label = Txt(image.transform, "Label", "text.body", TextAlignmentOptions.Center, 18f);
            Stretch(label.rectTransform, 4f, 0f, 4f, 0f);
            return b;
        }

        private static TMP_InputField InputBox(Transform parent, string name, string placeholderToken)
        {
            var back = Img(parent, name, "field", true);
            back.preserveAspect = false;
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            area.SetParent(back.rectTransform, false);
            Stretch(area, 10f, 0f, 10f, 0f);
            var text = Txt(area, "Text", "text.body", TextAlignmentOptions.Left, 20f);
            Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
            var f = back.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            if (placeholderToken != null)
            {
                var ph = Txt(area, "Placeholder", "text.muted", TextAlignmentOptions.Left, 19f);
                ph.text = Words.Get(placeholderToken);
                ph.fontStyle = FontStyles.Italic;
                ph.color = Muted;
                Stretch(ph.rectTransform, 0f, 0f, 0f, 0f);
                f.placeholder = ph;
            }
            f.targetGraphic = back;
            f.transition = Selectable.Transition.None;
            f.navigation = new Navigation { mode = Navigation.Mode.None };
            return f;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.gameObject.layer = parent.gameObject.layer; // UI layer, as the GUI camera culls by layer
            return rt;
        }

        private static void SetActive(Component c, bool on)
        {
            if (c && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        // Top-left box (y down) inside the parent.
        private static void Put(Component c, float x, float y, float w, float h)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }
    }

    // Player input off and cursor free while the window is open (the vanilla gates an open inventory takes: Player /
    // PlayerController TakeInput, GameCamera.UpdateMouseCapture); Esc closes the window, not the pause menu.
    [HarmonyPatch]
    internal static class StationQueueInputGate
    {
        [HarmonyPatch(typeof(Player), "TakeInput"), HarmonyPostfix]
        private static void Player_TakeInput(ref bool __result)
        {
            if (StationQueueWindow.IsOpen) __result = false;
        }

        [HarmonyPatch(typeof(PlayerController), "TakeInput"), HarmonyPostfix]
        private static void Controller_TakeInput(ref bool __result)
        {
            if (StationQueueWindow.IsOpen) __result = false;
        }

        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture)), HarmonyPostfix]
        private static void UpdateMouseCapture_Postfix()
        {
            if (!StationQueueWindow.IsOpen) return;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
        }

        [HarmonyPatch(typeof(Menu), "Update"), HarmonyPrefix]
        private static bool Menu_Update() => !StationQueueWindow.EatsEscape();
    }
}
