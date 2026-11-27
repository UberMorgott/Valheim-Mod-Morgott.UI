using System;
using System.Collections.Generic;
using System.Globalization;
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
                if (b.x - a.x < 50f || b.y - a.y < 50f) return $"panel {(b.x - a.x).ToString("0", CultureInfo.InvariantCulture)}x{(b.y - a.y).ToString("0", CultureInfo.InvariantCulture)} px";
                if (a.x < -1f || a.y < -1f || b.x > Screen.width + 1f || b.y > Screen.height + 1f) return $"panel off screen ({a.ToString()}..{b.ToString()})";
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

        // editor: index of the edited entry (-1 = new), draft; per-edit Keep memory for Until/Infinite; open flag
        private bool _editing, _untilKeep = true, _infiniteKeep = true;
        private int _editIndex;
        private StationQueueEntry _draft, _singleDraft;

        // ---------------------------------------------------------------- widgets

        private RectTransform _root, _multi, _single, _editorLayer, _editor;
        private TMP_Text _mStatus, _mHidden, _mPause, _sStatus, _sPause;
        private readonly List<InputRow> _rows = new List<InputRow>();
        private readonly List<Card> _cards = new List<Card>();
        private RectTransform _rowsRt, _cardsRt, _addHint;
        private DragScroll _rowsDrag, _cardsDrag;

        private sealed class InputRow
        {
            public StationQueueWindow Owner;
            public RectTransform Rt;
            public CanvasGroup Group;
            public Image Icon;
            public TMP_Text Badge, Name, Count;
            public string Item;

            public bool BeginDrag(PointerEventData e)
            {
                if (Item == null) return false;
                Owner._dragItem = Item;
                Owner._ghost.sprite = Icon.sprite;
                Owner._ghost.transform.SetAsLastSibling();
                Owner._ghost.gameObject.SetActive(Owner._ghost.sprite);
                return true;
            }

            public void MoveDrag(PointerEventData e)
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Owner._root, e.position, e.pressEventCamera, out var lp))
                    Owner._ghost.rectTransform.anchoredPosition = lp - Owner._root.rect.center;
            }

            public void EndDrag(PointerEventData e)
            {
                var item = Owner._dragItem;
                Owner._dragItem = null;
                Owner._ghost.gameObject.SetActive(false);
                if (item == null || !RectTransformUtility.RectangleContainsScreenPoint(Owner._cardsRt, e.position, e.pressEventCamera)) return;
                Owner.EditorOpen(-1, item);
            }

            public void OnAddClick()
            {
                if (Item != null) Owner.EditorOpen(-1, Item);
            }
        }

        private sealed class Card
        {
            public StationQueueWindow Owner;
            public RectTransform Rt;
            public Image[] Rim;
            public Image Icon, Fill;
            public RectTransform Trough;
            public TMP_Text Num, Text, Pct, Fed, Chip, Status;
            public Image[] ChipRim, StatusRim;
            public Image ChipIcon;
            public int Index;

            public void OnClick() => Owner.EditorOpen(Index, null);

            public bool BeginDrag(PointerEventData e)
            {
                Owner._dragCard = Index;
                Rt.SetAsLastSibling();
                return true;
            }

            public void MoveDrag(PointerEventData e)
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Owner._cardsRt, e.position, e.pressEventCamera, out var lp)) return;
                var y = Owner._cardsRt.rect.yMax - lp.y - CardH / 2f;
                Put(Rt, 0f, Mathf.Clamp(y, 0f, (CardRows - 1) * CardStep), CardW, CardH);
            }

            public void EndDrag(PointerEventData e)
            {
                var from = Owner._dragCard;
                Owner._dragCard = -1;
                var list = Owner.Entries();
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Owner._cardsRt, e.position, e.pressEventCamera, out var lp) &&
                    StationQueueMath.MoveTo(list, from, StationQueueMath.SlotAt(Owner._cardsRt.rect.yMax - lp.y, CardStep, Owner._cardScroll)))
                    Owner.Commit(list, Owner._state.Paused);
                else Owner.RefreshAll();
            }

            public void OnHover(bool on)
            {
                if (on) Owner._hoverCard = Index;
                else if (Owner._hoverCard == Index) Owner._hoverCard = -1;
            }

            public void OnUp() => Owner.Reorder(Index, -1);
            public void OnDown() => Owner.Reorder(Index, 1);

            public void OnRemove()
            {
                var list = Owner.Entries();
                if (Index < list.Count) list.RemoveAt(Index);
                Owner.Commit(list, Owner._state.Paused);
            }
        }

        private sealed class IntClick
        {
            private readonly Action<int> _fn;
            private readonly int _arg;
            public IntClick(Action<int> fn, int arg) { _fn = fn; _arg = arg; }
            public void Invoke() => _fn(_arg);
        }

        private sealed class BoolClick
        {
            private readonly Action<bool> _fn;
            private readonly bool _arg;
            public BoolClick(Action<bool> fn, bool arg) { _fn = fn; _arg = arg; }
            public void Invoke() => _fn(_arg);
        }

        private sealed class ChipClick
        {
            private readonly Action<int> _set;
            private readonly Func<int> _all;
            private readonly int _v;
            public ChipClick(Action<int> set, Func<int> all, int v) { _set = set; _all = all; _v = v; }
            public void Invoke() => _set(StationQueueMath.Clamp(_v > 0 ? _v : (_all != null ? _all() : 1)));
        }

        private sealed class TabBar
        {
            public readonly Button[] Buttons = new Button[StationQueueMath.TabCount];
            public readonly Image[][] Rims = new Image[StationQueueMath.TabCount][];
            public readonly TMP_Text[] Labels = new TMP_Text[StationQueueMath.TabCount];
        }

        private sealed class RepeatToggle
        {
            public Image[] RepeatRim, OneShotRim;
            public TMP_Text RepeatLabel, OneShotLabel;
        }

        // single-input window
        private Image _sIn, _sOut, _sFuel, _sCond;
        private TMP_Text _sInCount, _sOutCount, _sFuelCount, _sInName, _sOutName, _sFuelName, _sFuelLabel, _sCondName, _sNow, _sFed, _sKeepLabel, _sKeepNow, _sKeepStock, _sInfNote, _sFrom;
        private RectTransform _sFuelBox, _sMakeBox, _sKeepBox, _sInfBox, _sUntilBox, _sNowTrough;
        private Image _sNowFill;
        private Image[] _sLessRim, _sAtLeastRim;
        private TabBar _sTabs;
        private RepeatToggle _sRepeat;
        private Stepper _sCount, _sKeepTarget, _sThreshold;
        private Button _sStart;

        // editor
        private Image _eIcon, _eCondIcon;
        private TMP_Text _eBadge, _eName, _eNet, _eIngredients, _eCondName, _eSummary, _eMakeYield, _eKeepLabel, _eKeepNow, _eUntilNow, _eSaveLabel, _eHint;
        private RectTransform _eMakeBox, _eKeepBox, _eUntilBox, _eInfBox;
        private TabBar _eTabs;
        private RepeatToggle _eRepeat;
        private Image[] _eLessRim, _eAtLeastRim;
        private Stepper _eCount, _eKeepTarget, _eThreshold;
        private Button _eDelete;

        // item picker (until-condition dropdown)
        private RectTransform _picker;
        private TMP_InputField _pSearch;
        private readonly List<(Button b, Image icon, TMP_Text label)> _pRows = new List<(Button, Image, TMP_Text)>();
        private readonly List<string> _pAll = new List<string>(), _pShown = new List<string>();
        private int _pScroll;
        private Action<string> _pPick;

        private RectTransform _sCondBtn, _eCondBtn;
        private int _singleEditInt;
        private bool _singleEditBool;
        private string _singleEditStr;

        private sealed class Stepper
        {
            public TMP_InputField Field;
            public Func<int> Get;
            public Action<int> Set;

            public void StepMinus() => Set(StationQueueMath.Step(Get(), -1, Shift()));
            public void StepPlus() => Set(StationQueueMath.Step(Get(), 1, Shift()));

            public void OnEndEdit(string text)
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) Set(StationQueueMath.Clamp(v));
                else SetField(this, Get());
            }
        }

        // LMB drag (Begin false = not draggable now); RMB drags forward to the parent list's DragScroll.
        private sealed class DragHandle : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public Func<PointerEventData, bool> Begin;
            public Action<PointerEventData> Move, End;
            public DragScroll RightScroll;
            private bool _on, _right;

            public void OnInitializePotentialDrag(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Right) RightScroll?.OnInitializePotentialDrag(e);
            }

            public void OnBeginDrag(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Right)
                {
                    _right = RightScroll != null;
                    if (_right) RightScroll.OnBeginDrag(e);
                    return;
                }
                _on = e.button == PointerEventData.InputButton.Left && Begin != null && Begin(e);
            }

            public void OnDrag(PointerEventData e)
            {
                if (_right) { RightScroll?.OnDrag(e); return; }
                if (_on) Move?.Invoke(e);
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (_right)
                {
                    _right = false;
                    RightScroll?.OnEndDrag(e);
                    return;
                }
                if (!_on) return;
                _on = false;
                End?.Invoke(e);
            }

            private void OnDisable()
            {
                _on = false;
                _right = false;
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
        private static Color Ok => ThemeTokens.GetColor("color.text.ok", Hex("#9FD66B"));
        private static Color Ember => ThemeTokens.GetColor("color.text.ember", Hex("#F08A3A"));
        private static Color BodyText => ThemeTokens.GetColor("color.text.body", Hex("#E3E6E8"));
        private static Color Muted => ThemeTokens.GetColor("color.state.disabled", Hex("#8A9096"));
        private static Color Sub => ThemeTokens.GetColor("color.stats.sub", Hex("#A9B3B7"));
        private static Color Missing => ThemeTokens.GetColor("color.text.missing", Hex("#E0413A"));
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
                !StationQueueMath.KeepsOpen(Surface(p, _station), p.m_maxInteractDistance) ? $"out of reach: surface {Surface(p, _station).ToString("0.0", CultureInfo.InvariantCulture)} m > {(p.m_maxInteractDistance + StationQueueMath.ReachMargin).ToString("0.0", CultureInfo.InvariantCulture)} m" :
                InventoryGui.IsVisible() ? "inventory open" : Minimap.IsOpen() ? "map open" : Menu.IsVisible() ? "menu open" : null;
            if (why != null)
            {
                Hide(why);
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.Escape, false))
            {
                if (_picker.gameObject.activeSelf) ClosePicker();
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
            _singleDraft = null;
            _dragCard = _hoverCard = -1; // a Hud destroyed mid-drag never ran Hide
            _dragItem = null;
            ClosePicker();
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
            _singleDraft = null;
            _dragCard = -1;
            _dragItem = null;
            _hoverCard = -1;
            ClosePicker();
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
        private void Commit(List<StationQueueEntry> entries, bool paused, Action onAccepted = null)
        {
            if (_queue == null || !_station) return;
            var ok = _queue.Write(_station, entries, paused);
            if (!ok && Player.m_localPlayer)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_privatezone");
            if (ok) onAccepted?.Invoke();
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

        // Network + what the queued stations still turn out: what keep-in-stock and until conditions judge.
        private int Have(string prefab) => Net(prefab) + (prefab != null && _state.Pending.TryGetValue(prefab, out var n) ? n : 0);

        private StationQueueInput FindInputByKey(string key) => StationQueueMath.FindInput(_state.Inputs, key);
        private ItemDrop DisplayItem(StationQueueInput input, string key) =>
            input != null
                ? (StationQueueMath.PrefersOutput(input.Item, input.Ingredients?.Count ?? 0)
                    ? (input.Output ? input.Output : input.Item)
                    : (input.Item ? input.Item : input.Output))
                : Item(key);
        private int InputNet(string key) => FindInputByKey(key) is StationQueueInput i ? i.Network : Net(key);
        private string OutputOf(string key) => FindInputByKey(key) is StationQueueInput i && i.Output ? i.Output.name : null;

        private string LoadedWord()
        {
            var key = StationQueueMath.LoadedKey(_state.Name, _state.Kind);
            return key.StartsWith("ui_sq_", StringComparison.Ordinal) ? Words.Get("$" + key) : W(key);
        }

        private static string FormatIngredients(IReadOnlyList<StationQueueIngredient> ingredients, string okHex, string missingHex, string template = null)
        {
            var list = new List<(string Name, int Amount, int Network)>(ingredients.Count);
            for (var i = 0; i < ingredients.Count; i++)
            {
                var ing = ingredients[i];
                list.Add((ing.Item ? Name(ing.Item) : "?", ing.Amount, ing.Network));
            }
            return StationQueueMath.IngredientsText(list, okHex, missingHex, template);
        }

        // A fuel entry (its item is the station's fuel, Patcher Plan) keeps the fuel inside the station, not an output.
        private bool IsFuel(string item) => _state.Fuel && item == _state.Fuel.name;
        private int Stocked(string item) => IsFuel(item) ? Mathf.FloorToInt(_state.FuelLevel) : Have(OutputOf(item));
        private string OutputName(string item) => IsFuel(item) ? W("fuel") : Name(OutputOf(item));

        private string NetworkPart() => _state.InNetwork
            ? StationQueueMath.Format(W("network"), _state.Chests.ToString(CultureInfo.InvariantCulture), StationQueueMath.Plural(_state.Chests, W("chest_one"), W("chest_few"), W("chest_many")))
            : W("no_network");

        // ---------------------------------------------------------------- refresh

        private void RefreshAll()
        {
            // Keep _multi + _editor open while _editing is true even if a 1-input queue shrinks from 2 to 1.
            var single = StationQueueMath.IsSingle(_state.Inputs.Count, _state.MultiOnly, _state.Entries.Count, _editing);
            if (!single) _singleDraft = null;
            SetActive(_multi, !single);
            SetActive(_single, single);
            SetActive(_editorLayer, _editing);
            SetActive(_multi.Find(HintsName), !_editing); // entry-editor.png: only the dialog's hints under it
            Stamp();
            if (!_editing && !single) ClosePicker();
            LastScreen = _editing ? "editor" : single ? "single" : "multi";
            if (single) RefreshSingle();
            else RefreshMulti();
            if (_editing) RefreshEditor();
        }

        private void RefreshMulti()
        {
            Header(_multi).Text(Title(_state.Name));
            var first = !_state.MultiOnly && _state.Inputs.Count > 0 && _state.Inputs[0].Output ? _state.Inputs[0].Output : null;
            _mStatus.text = StationQueueMath.Join(
                StationQueueMath.Loaded(LoadedWord(), _state.Loaded, _state.Capacity, null),
                first ? StationQueueMath.Format(W("output_net"), Name(first), StationQueueMath.Group(Net(first.name))) : null,
                _state.Fuel ? Name(_state.Fuel) + " " + Mathf.FloorToInt(_state.FuelLevel) + "/" + _state.FuelMax : null, // as the single window's line
                NetworkPart());
            _mPause.text = _state.Paused ? W("resume") : W("pause");

            // inputs: known only (0 in the network = dimmed), unknown hidden with a footer note
            var known = new List<StationQueueInput>();
            foreach (var i in _state.Inputs) if (i.Known && (i.Item || i.Output) && !string.IsNullOrEmpty(i.Key)) known.Add(i);
            _inScroll = Mathf.Clamp(_inScroll, 0, Mathf.Max(0, known.Count - InputRows));
            var subHex = ColorUtility.ToHtmlStringRGB(Sub);
            var missingHex = ColorUtility.ToHtmlStringRGB(Missing);
            for (var r = 0; r < InputRows; r++)
            {
                var row = _rows[r];
                var k = r + _inScroll;
                SetActive(row.Rt, k < known.Count);
                if (k >= known.Count) continue;
                var input = known[k];
                var display = DisplayItem(input, input.Key);
                var title = StationQueueMath.DisplayName(Name(display), input.OutputAmount, input.ApproximateYield);
                var hasIng = input.Ingredients != null && input.Ingredients.Count > 0;
                row.Item = input.Key;
                row.Icon.sprite = Icon(display);
                row.Icon.enabled = row.Icon.sprite;
                row.Badge.text = row.Count.text = StationQueueMath.Group(input.Network);
                row.Name.richText = hasIng;
                row.Name.fontSize = hasIng ? 18.5f : 21f;
                row.Name.text = hasIng
                    ? title + "\n<size=14>" + FormatIngredients(input.Ingredients, subHex, missingHex) + "</size>"
                    : title;
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
            var input = FindInputByKey(e.Item);
            var item = DisplayItem(input, e.Item);
            var displayName = StationQueueMath.DisplayName(item ? Name(item) : e.Item, input != null ? input.OutputAmount : 1, input != null && input.ApproximateYield);
            var outName = OutputName(e.Item);
            if (string.IsNullOrEmpty(outName)) outName = displayName;
            card.Num.text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            card.Icon.sprite = Icon(item);
            card.Icon.enabled = card.Icon.sprite;
            card.Text.text = StationQueueMath.CardText(e, displayName, Name(e.Condition), W("card_until"), W("card_keep"), outName);

            var entryStatus = StationQueueMath.EntryStatus(e.Id, index, _state.Active, _state.Status, _state.EntryStatuses);
            var active = entryStatus == StationQueueEntryStatus.Running;
            SetRim(card.Rim, active ? Cyan : Dim);
            SetActive(card.Trough, active);
            var showFed = active && e.Mode == StationQueueMode.Count;
            SetActive(card.Fed, showFed);
            var textRt = card.Text.rectTransform;
            Put(textRt, 124f, active ? 6f : 0f, 216f, active ? 44f : CardH);
            if (active)
            {
                Put(card.Trough, 124f, 54f, showFed ? 148f : 216f, 16f);
                var share = StationQueueMath.Share(e, _state.Progress, Stocked(e.Item));
                FillBar(card.Fill, card.Trough, share);
                card.Pct.text = Mathf.RoundToInt(share * 100f) + "%";
                if (showFed) card.Fed.text = StationQueueMath.Group(e.Keep ? Stocked(e.Item) : e.Fed) + " / " + StationQueueMath.Group(e.Target);
            }

            // Mode chip (top) + live entry status chip (bottom)
            var tab = StationQueueMath.TabOf(e);
            card.Chip.text = tab == StationQueueMath.TabKeep ? W("chip_keep")
                : tab == StationQueueMath.TabMake ? W("tab_make")
                : tab == StationQueueMath.TabInfinite ? "∞"
                : W("mode_until");
            var chip = tab == StationQueueMath.TabMake ? Cyan : Gold;
            card.Chip.color = chip;
            SetRim(card.ChipRim, new Color(chip.r, chip.g, chip.b, 0.7f));
            var until = e.Mode == StationQueueMode.Until;
            card.ChipIcon.sprite = until ? Icon(Item(e.Condition)) : null;
            card.ChipIcon.enabled = card.ChipIcon.sprite;
            Stretch(card.Chip.rectTransform, 4f, 0f, card.ChipIcon.enabled ? 28f : 4f, 0f);

            var stColor = StatusColor(entryStatus);
            card.Status.text = W(StationQueueMath.StatusKey(entryStatus));
            card.Status.color = stColor;
            SetRim(card.StatusRim, new Color(stColor.r, stColor.g, stColor.b, 0.65f));
        }

        private static Color StatusColor(StationQueueEntryStatus status)
        {
            switch (status)
            {
                case StationQueueEntryStatus.Running: return Cyan;
                case StationQueueEntryStatus.MissingInput: return Missing;
                case StationQueueEntryStatus.Stocked: return Ok;
                case StationQueueEntryStatus.Pending: return Gold;
                case StationQueueEntryStatus.Blocked: return Ember;
                default: return Muted;
            }
        }

        private void RefreshSingle()
        {
            Header(_single).Text(Title(_state.Name));
            var input = _state.Inputs[0];
            var id = input.Key;
            if (string.IsNullOrEmpty(id) && input.Item) id = input.Item.name;
            var cur = StationQueueMath.SingleEntry(_state.Entries, id);
            if (cur != null || (_singleDraft != null && !string.Equals(_singleDraft.Item, id, StringComparison.Ordinal))) _singleDraft = null;
            var e = cur ?? _singleDraft;
            Cell(_sIn, _sInCount, _sInName, input.Item, input.Network);
            Cell(_sOut, _sOutCount, _sOutName, input.Output, input.Output ? Net(input.Output.name) : 0);
            SetActive(_sFuelBox, _state.Fuel);
            if (_state.Fuel) Cell(_sFuel, _sFuelCount, _sFuelName, _state.Fuel, Net(_state.Fuel.name));
            _sFuelLabel.text = W("fuel");
            var stKey = StationQueueMath.SingleStatusKey(cur, _state.Status, _state.EntryStatuses, out var liveSt);
            var stColor = liveSt.HasValue ? StatusColor(liveSt.Value) : Cyan;
            var status = "<color=#" + ColorUtility.ToHtmlStringRGB(stColor) + ">" + W(stKey) + "</color>";
            var unit = id != null ? Words.Get("$ui_sq_unit_" + id) : null;
            if (unit != null && unit.StartsWith("[", StringComparison.Ordinal)) unit = null; // no unit word for this input
            _sStatus.text = StationQueueMath.Join(
                StationQueueMath.Loaded(LoadedWord(), _state.Loaded, _state.Capacity, unit),
                _state.Fuel ? Name(_state.Fuel) + " " + Mathf.FloorToInt(_state.FuelLevel) + "/" + _state.FuelMax : null,
                _state.InNetwork ? null : W("no_network"),
                status);
            _sPause.text = _state.Paused ? W("resume") : W("pause");

            var tab = StationQueueMath.TabOf(e);
            if (tab != StationQueueMath.TabUntil) ClosePicker();
            StyleTabs(_sTabs, tab);
            SetActive(_sMakeBox, tab == StationQueueMath.TabMake);
            SetActive(_sKeepBox, tab == StationQueueMath.TabKeep);
            SetActive(_sUntilBox, tab == StationQueueMath.TabUntil);
            SetActive(_sInfBox, tab == StationQueueMath.TabInfinite || tab < 0);
            var showStart = cur == null && _singleDraft != null;
            SetActive(_sStart, showStart);
            SetActive(_sFrom, !showStart);
            if (tab < 0)
            {
                _sInfNote.text = W("choose_mode");
                return;
            }
            if (tab == StationQueueMath.TabInfinite)
            {
                _sInfNote.text = W("infinite_note");
                return;
            }
            if (tab == StationQueueMath.TabMake)
            {
                SetField(_sCount, e.Target);
                var fed = StationQueueMath.Format(W("fed"), StationQueueMath.Group(e.Fed), StationQueueMath.Group(e.Target));
                var yield = StationQueueMath.MakeYield(e.Target, input.OutputAmount, W("make_yield"), input.ApproximateYield);
                _sFed.text = string.IsNullOrEmpty(yield) ? fed : fed + StationQueueMath.Separator + yield;
            }
            else if (tab == StationQueueMath.TabKeep)
            {
                SetField(_sKeepTarget, e.Target);
                var fuel = IsFuel(id);
                _sKeepLabel.text = W(fuel ? "keep_fuel_label" : "keep_net_label");
                _sKeepNow.text = KeepReadout(id, input.Output ? input.Output.name : null);
                _sKeepStock.text = StationQueueMath.Format(W("in_stock"), StationQueueMath.Group(Stocked(id)), StationQueueMath.Group(e.Target));
            }
            else if (tab == StationQueueMath.TabUntil)
            {
                var cond = Item(e.Condition);
                _sCond.sprite = Icon(cond);
                _sCond.enabled = _sCond.sprite;
                _sCondName.text = cond ? Name(cond) : e.Condition;
                SetRim(_sLessRim, e.Compare == StationQueueCompare.Less ? Cyan : Dim);
                SetRim(_sAtLeastRim, e.Compare == StationQueueCompare.AtLeast ? Cyan : Dim);
                StyleRepeatToggle(_sRepeat, e.Keep);
                SetField(_sThreshold, e.Threshold);
                var now = Have(e.Condition);
                _sNow.text = StationQueueMath.Format(W("now"), StationQueueMath.Group(now), StationQueueMath.Group(e.Threshold));
                FillBar(_sNowFill, _sNowTrough, StationQueueMath.Share(now, e.Threshold));
            }
        }

        private string KeepReadout(string itemKey, string explicitOut = null)
        {
            if (IsFuel(itemKey))
                return StationQueueMath.Format(W("keep_now_fuel"), StationQueueMath.Group(Mathf.FloorToInt(_state.FuelLevel)), StationQueueMath.Group(_state.FuelMax));
            var outId = explicitOut ?? OutputOf(itemKey);
            var pend = outId != null && _state.Pending.TryGetValue(outId, out var p) ? p : 0;
            return StationQueueMath.KeepNowText(Net(outId), pend, W("keep_now"), W("keep_now_pending"));
        }

        private static void Cell(Image icon, TMP_Text count, TMP_Text name, ItemDrop item, int n)
        {
            icon.sprite = Icon(item);
            icon.enabled = icon.sprite;
            count.text = StationQueueMath.Group(n);
            name.text = Name(item);
        }

        private string SingleKey(StationQueueInput input) =>
            input == null ? null : !string.IsNullOrEmpty(input.Key) ? input.Key : (input.Item ? input.Item.name : null);

        private void SingleTab(int tab)
        {
            if (tab != StationQueueMath.TabUntil) ClosePicker();
            var input = _state.Inputs.Count > 0 ? _state.Inputs[0] : null;
            var id = SingleKey(input);
            if (string.IsNullOrEmpty(id)) return;
            var cur = StationQueueMath.SingleEntry(_state.Entries, id);
            var outId = input.Output ? input.Output.name : id;
            if (StationQueueMath.SingleTab(cur, ref _singleDraft, tab, id, outId, out var next))
                Commit(StationQueueMath.Single(next), _state.Paused);
            else if (next != null)
                RefreshSingle();
        }

        // The single window's active entry (committed immediately) or local draft on an empty station.
        private void SingleEdit(Action<StationQueueEntry> edit)
        {
            var input = _state.Inputs.Count > 0 ? _state.Inputs[0] : null;
            var id = SingleKey(input);
            if (string.IsNullOrEmpty(id)) return;
            var cur = StationQueueMath.SingleEntry(_state.Entries, id);
            if (StationQueueMath.SingleEdit(cur, _singleDraft, edit, out var next))
                Commit(StationQueueMath.Single(next), _state.Paused);
            else if (next != null)
                RefreshSingle();
        }

        private void SingleStart()
        {
            if (_singleDraft == null) return;
            ClosePicker();
            Commit(StationQueueMath.Single(_singleDraft), _state.Paused, () => _singleDraft = null);
        }

        private void SingleReset()
        {
            _singleDraft = null;
            ClosePicker();
            if (_state.Entries.Count > 0) Commit(new List<StationQueueEntry>(), _state.Paused);
            else RefreshSingle();
        }

        // ---------------------------------------------------------------- editor

        private void EditorOpen(int index, string newItem)
        {
            if (StationQueueMath.IsSingle(_state.Inputs.Count, _state.MultiOnly, _state.Entries.Count)) return;
            ClosePicker();
            if (index >= 0 && index < _state.Entries.Count)
            {
                _editIndex = index;
                _draft = _state.Entries[index].Clone();
                _untilKeep = _draft.Mode != StationQueueMode.Until || _draft.Keep;
                _infiniteKeep = _draft.Mode != StationQueueMode.Infinite || _draft.Keep;
            }
            else
            {
                if (newItem == null)
                    foreach (var i in _state.Inputs)
                        if (i.Known && (i.Item || i.Output) && !string.IsNullOrEmpty(i.Key)) { newItem = i.Key; break; }
                if (newItem == null) return;
                _editIndex = -1;
                _draft = StationQueueMath.NewEntry(newItem, StationQueueMode.Count, OutputOf(newItem), keep: false);
                _untilKeep = true;
                _infiniteKeep = true;
            }
            _editing = true;
            RefreshAll();
        }

        private void EditorClose()
        {
            _editing = false;
            ClosePicker();
            RefreshAll();
        }

        private void EditorSave()
        {
            if (!_editing || _draft == null) return;
            _editing = false;
            ClosePicker();
            if (_editIndex >= 0 && StationQueueMath.Find(_state.Entries, _editIndex, _draft) < 0)
            {
                Reload();
                return;
            }
            Commit(StationQueueMath.Save(_state.Entries, _editIndex, _draft), _state.Paused);
        }

        private void EditorDelete()
        {
            var list = Entries();
            var at = _editIndex >= 0 ? StationQueueMath.Find(list, _editIndex, _draft) : -1;
            if (at >= 0) list.RemoveAt(at);
            _editing = false;
            ClosePicker();
            Commit(list, _state.Paused);
        }

        private void EditorTab(int tab)
        {
            if (_draft == null) return;
            if (tab != StationQueueMath.TabUntil) ClosePicker();
            StationQueueMath.SwitchTab(_draft, tab, OutputOf(_draft.Item) ?? _draft.Item, _untilKeep, _infiniteKeep);
            RefreshEditor();
        }

        private void RefreshEditor()
        {
            if (_draft == null) return;
            var input = FindInputByKey(_draft.Item);
            var item = DisplayItem(input, _draft.Item);
            var outputAmount = input != null ? input.OutputAmount : 1;
            var approximate = input != null && input.ApproximateYield;
            _eIcon.sprite = Icon(item);
            _eIcon.enabled = _eIcon.sprite;
            var net = InputNet(_draft.Item);
            _eBadge.text = StationQueueMath.Group(net);
            _eName.text = StationQueueMath.DisplayName(item ? Name(item) : _draft.Item, outputAmount, approximate);
            _eNet.text = StationQueueMath.Format(W("net_count"), StationQueueMath.Group(net));
            var hasIng = input?.Ingredients != null && input.Ingredients.Count > 0;
            SetActive(_eIngredients, hasIng);
            if (hasIng)
            {
                Put(_eName, 120f, 68f, EditorW - 150f, 30f);
                _eName.fontSize = 24f;
                Put(_eNet, 120f, 98f, EditorW - 150f, 22f);
                _eNet.fontSize = 18f;
                _eIngredients.text = FormatIngredients(input.Ingredients, null, ColorUtility.ToHtmlStringRGB(Missing), W("ingredients"));
            }
            else
            {
                Put(_eName, 120f, 74f, EditorW - 150f, 34f);
                _eName.fontSize = 26f;
                Put(_eNet, 120f, 110f, EditorW - 150f, 28f);
                _eNet.fontSize = 20f;
            }

            var tab = StationQueueMath.TabOf(_draft);
            if (tab != StationQueueMath.TabUntil) ClosePicker();
            StyleTabs(_eTabs, tab);
            SetActive(_eMakeBox, tab == StationQueueMath.TabMake);
            SetActive(_eKeepBox, tab == StationQueueMath.TabKeep);
            SetActive(_eUntilBox, tab == StationQueueMath.TabUntil);
            SetActive(_eInfBox, tab == StationQueueMath.TabInfinite);

            var target = StationQueueMath.ShownTarget(_draft.Target);
            if (tab == StationQueueMath.TabMake)
            {
                SetField(_eCount, target);
                var yield = StationQueueMath.MakeYield(target, outputAmount, W("make_yield"), approximate);
                SetActive(_eMakeYield, !string.IsNullOrEmpty(yield));
                if (!string.IsNullOrEmpty(yield)) _eMakeYield.text = yield;
            }
            else if (tab == StationQueueMath.TabKeep)
            {
                SetField(_eKeepTarget, target);
                _eKeepLabel.text = W(IsFuel(_draft.Item) ? "keep_fuel_label" : "keep_net_label");
                _eKeepNow.text = KeepReadout(_draft.Item);
            }
            else if (tab == StationQueueMath.TabUntil)
            {
                SetField(_eThreshold, _draft.Threshold);
                var cond = Item(_draft.Condition);
                _eCondIcon.sprite = Icon(cond);
                _eCondIcon.enabled = _eCondIcon.sprite;
                _eCondName.text = cond ? Name(cond) : _draft.Condition ?? "";
                SetRim(_eLessRim, _draft.Compare == StationQueueCompare.Less ? Cyan : Dim);
                SetRim(_eAtLeastRim, _draft.Compare == StationQueueCompare.AtLeast ? Cyan : Dim);
                StyleRepeatToggle(_eRepeat, _draft.Keep);
                _eUntilNow.text = StationQueueMath.Format(W("now"), StationQueueMath.Group(Have(_draft.Condition)), StationQueueMath.Group(_draft.Threshold));
            }

            var keepCount = _draft.Keep && _draft.Mode == StationQueueMode.Count;
            var keepOut = OutputName(_draft.Item);
            if (string.IsNullOrEmpty(keepOut)) keepOut = _eName.text;
            _eSummary.text = StationQueueMath.Summary(_draft, _eName.text, keepCount ? keepOut : _eCondName.text, keepCount ? Stocked(_draft.Item) : Have(_draft.Condition),
                W("sum_count"), W("sum_infinite"), W("sum_until"), W(IsFuel(_draft.Item) ? "sum_keep_fuel" : "sum_keep"), W("keep_tail"),
                outputAmount, W("sum_count_yield"), approximate);
            SetActive(_eDelete, _editIndex >= 0);
            var actionWord = _editIndex < 0 ? W("add") : W("save");
            _eSaveLabel.text = actionWord;
            _eHint.text = Hint("Enter", actionWord) + "      " + Hint("Esc", W("cancel"));
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
            // A rebuild (logout destroys the Hud and our root with it; this component lives on) starts from empty widget
            // caches: the Build* calls append, and stale rows of the destroyed window threw NREs in RefreshMulti.
            _rows.Clear();
            _cards.Clear();
            _pRows.Clear();
            _headers.Clear();
            _serif.Clear();
            _root = NewRect(RootName, host);
            Plugin.Log.LogInfo($"[StationQueue] window built under {Path(host)} (layer {host.gameObject.layer.ToString(CultureInfo.InvariantCulture)})");
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
        private RectTransform Panel(Transform parent, string name, float w, float h, string moveId, UnityEngine.Events.UnityAction close, string hints)
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
        private const float CloseColumn = 58f, MultiH = 680f;
        private readonly List<TMP_Text> _serif = new List<TMP_Text>();

        // Section heads + titles in the serif face (ChangelogPanel.Stamp: the face is late-bound, set once it exists).
        private void Stamp()
        {
            var face = ThemeTokens.GetFont(Serif);
            if (face) foreach (var t in _serif) if (t && t.font != face) t.font = face;
        }

        private static string Title(string key) => Words.Get(key).ToUpperInvariant();

        private static string Hint(string key, string word) => StationQueueMath.KeyHint(key, word, ColorUtility.ToHtmlStringRGB(Gold));

        private void BuildMulti()
        {
            const float W0 = 1100f, H0 = MultiH;
            _multi = Panel(_root, "Multi", W0, H0, "station.queue", () => Hide("close button"),
                Hint("Esc", W("close")) + "      " + Hint("LMB", W("drag")) + "      " + Hint("Del", W("delete")));
            _mStatus = Txt(_multi, "Status", "text.body", TextAlignmentOptions.Center, 22f);
            Put(_mStatus, 40f, 70f, W0 - 80f, 32f);
            Put(Rule(_multi, "TopRule", Dim), 28f, 108f, W0 - 56f, 1f);

            // inputs column
            Section(_multi, "InputsLabel", W("inputs"), 28f, 116f);
            _rowsRt = NewRect("Inputs", _multi);
            Put(_rowsRt, 28f, 158f, 400f, InputRows * 84f);
            WheelOn(_rowsRt, d => { _inScroll -= Math.Sign(d); RefreshAll(); });
            _rowsDrag = _rowsRt.gameObject.AddComponent<DragScroll>();
            _rowsDrag.AllowLeft = false;
            _rowsDrag.StepSize = () => 84f;
            _rowsDrag.OnStep = (steps, _) => { _inScroll += steps; RefreshAll(); };
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
            _cardsDrag = _cardsRt.gameObject.AddComponent<DragScroll>();
            _cardsDrag.AllowLeft = false;
            _cardsDrag.StepSize = () => CardStep;
            _cardsDrag.OnStep = (steps, _) => { _cardScroll += steps; RefreshAll(); };
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
            var row = new InputRow { Owner = this, Rt = NewRect("Row" + i, parent) };
            Put(row.Rt, 0f, i * 84f, 400f, 80f);
            var hit = row.Rt.gameObject.AddComponent<Image>(); // drag source (LMB onto the queue appends)
            hit.color = Color.clear;
            hit.raycastTarget = true;
            DollCell.Mark(hit, "content", "station queue input row drag area");
            var drag = row.Rt.gameObject.AddComponent<DragHandle>();
            drag.RightScroll = _rowsDrag;
            drag.Begin = row.BeginDrag;
            drag.Move = row.MoveDrag;
            drag.End = row.EndDrag;
            row.Group = row.Rt.gameObject.AddComponent<CanvasGroup>();
            row.Icon = SlotIcon(row.Rt, 0f, 6f, 68f, out row.Badge);
            row.Name = Txt(row.Rt, "Name", "text.body", TextAlignmentOptions.Left, 21f);
            row.Name.textWrappingMode = TextWrappingModes.Normal;
            Put(row.Name, 84f, 0f, 200f, 80f);
            row.Count = Txt(row.Rt, "Count", "text.body", TextAlignmentOptions.Right, 21f);
            Put(row.Count, 284f, 0f, 60f, 80f);
            var plus = Btn(row.Rt, "Add", "button.icon", out var pl, row.OnAddClick);
            pl.text = "+";
            pl.fontSize = 26f;
            Put(plus, 354f, 20f, 40f, 40f);
            Put(Rule(row.Rt, "Rule", new Color(Dim.r, Dim.g, Dim.b, 0.3f)), 84f, 81f, 316f, 1f);
            return row;
        }

        private Card NewCard(RectTransform parent, int i)
        {
            var card = new Card { Owner = this };
            var plate = Btn(parent, "Card" + i, "field", out var unused, card.OnClick);
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
            gd.RightScroll = _cardsDrag;
            gd.Begin = card.BeginDrag;
            gd.Move = card.MoveDrag;
            gd.End = card.EndDrag;
            card.Rt.gameObject.AddComponent<Hover>().Set = card.OnHover;
            card.Num = Txt(card.Rt, "Num", "text.body", TextAlignmentOptions.Center, 22f);
            Put(card.Num, 28f, 0f, 28f, CardH);
            card.Icon = SlotIcon(card.Rt, 60f, (CardH - 58f) / 2f, 58f, out var badge);
            Destroy(badge.gameObject);
            card.Text = Txt(card.Rt, "Text", "text.body", TextAlignmentOptions.Left, 18.5f);
            card.Text.textWrappingMode = TextWrappingModes.Normal;
            card.Trough = Bar(card.Rt, out card.Fill);
            Put(card.Trough, 124f, 54f, 148f, 16f);
            card.Pct = Txt(card.Trough, "Pct", "text.body", TextAlignmentOptions.Center, 14f);
            Stretch(card.Pct.rectTransform, 0f, -2f, 0f, -2f);
            card.Fed = Txt(card.Rt, "Fed", "text.accent", TextAlignmentOptions.MidlineRight, 15.5f);
            card.Fed.color = Cyan;
            card.Fed.enableAutoSizing = true;
            card.Fed.fontSizeMin = 12f;
            card.Fed.fontSizeMax = 15.5f;
            Put(card.Fed, 276f, 50f, 64f, 22f);
            var chipRt = NewRect("Chip", card.Rt);
            Put(chipRt, 346f, 10f, 118f, 26f);
            card.ChipRim = Rim(chipRt);
            card.ChipIcon = IconIn(chipRt, 0f);
            Put(card.ChipIcon, 92f, 2f, 22f, 22f);
            card.Chip = Txt(chipRt, "Label", "text.body", TextAlignmentOptions.Center, 16f);
            card.Chip.enableAutoSizing = true;
            card.Chip.fontSizeMin = 12f;
            card.Chip.fontSizeMax = 16f;
            Stretch(card.Chip.rectTransform, 4f, 0f, 4f, 0f);
            var statusRt = NewRect("StatusChip", card.Rt);
            Put(statusRt, 346f, 44f, 118f, 26f);
            card.StatusRim = Rim(statusRt);
            card.Status = Txt(statusRt, "Label", "text.body", TextAlignmentOptions.Center, 15f);
            card.Status.enableAutoSizing = true;
            card.Status.fontSizeMin = 12f;
            card.Status.fontSizeMax = 15f;
            Stretch(card.Status.rectTransform, 4f, 0f, 4f, 0f);
            Arrow(card, "Up", "▲", 474f, card.OnUp);
            Arrow(card, "Down", "▼", 516f, card.OnDown);
            Arrow(card, "Remove", "×", 558f, card.OnRemove);
            return card;
        }

        private void Arrow(Card card, string name, string glyph, float x, UnityEngine.Events.UnityAction click)
        {
            var b = Btn(card.Rt, name, "button.icon", out var l, click);
            l.text = glyph;
            l.fontSize = name == "Remove" ? 24f : 16f;
            Put(b, x, (CardH - 36f) / 2f, 36f, 36f);
        }

        private void Reorder(int index, int delta)
        {
            var list = Entries();
            if (StationQueueMath.Move(list, index, delta)) Commit(list, _state.Paused);
        }

        private static readonly int[] MakeChips = { 10, 50, 100, -1 }, KeepChips = { 20, 50, 100, 200 };

        private TabBar BuildTabs(Transform parent, float x, float y, float w, float gap, float h, float font, Action<int> onSelect)
        {
            var bar = new TabBar();
            string[] keys = { "tab_make", "tab_keep", "tab_until", "tab_infinite" };
            for (var i = 0; i < StationQueueMath.TabCount; i++)
            {
                bar.Buttons[i] = Btn(parent, "Mode" + i, "field", out bar.Labels[i], new IntClick(onSelect, i).Invoke);
                bar.Labels[i].text = W(keys[i]);
                bar.Labels[i].fontSize = font;
                bar.Labels[i].enableAutoSizing = true;
                bar.Labels[i].fontSizeMin = 14f;
                bar.Labels[i].fontSizeMax = font;
                bar.Rims[i] = Rim(bar.Buttons[i].transform);
                Put(bar.Buttons[i], x + i * (w + gap), y, w, h);
            }
            return bar;
        }

        private static void StyleTabs(TabBar bar, int activeTab)
        {
            for (var i = 0; i < StationQueueMath.TabCount; i++)
            {
                var on = i == activeTab;
                SetRim(bar.Rims[i], on ? Cyan : Dim);
                bar.Labels[i].color = on ? Gold : BodyText;
            }
        }

        private RepeatToggle BuildRepeatToggle(Transform parent, float x, float y, float w, float h, Action<bool> onSelect)
        {
            var t = new RepeatToggle();
            var rep = Btn(parent, "Repeat", "field", out t.RepeatLabel, new BoolClick(onSelect, true).Invoke);
            t.RepeatLabel.text = W("repeat");
            t.RepeatLabel.fontSize = 17f;
            t.RepeatRim = Rim(rep.transform);
            Put(rep, x, y, w, h);
            var one = Btn(parent, "OneShot", "field", out t.OneShotLabel, new BoolClick(onSelect, false).Invoke);
            t.OneShotLabel.text = W("oneshot");
            t.OneShotLabel.fontSize = 17f;
            t.OneShotRim = Rim(one.transform);
            Put(one, x + w + 8f, y, w, h);
            return t;
        }

        private static void StyleRepeatToggle(RepeatToggle t, bool repeat)
        {
            SetRim(t.RepeatRim, repeat ? Cyan : Dim);
            SetRim(t.OneShotRim, !repeat ? Cyan : Dim);
            t.RepeatLabel.color = repeat ? Gold : Sub;
            t.OneShotLabel.color = !repeat ? Gold : Sub;
        }

        private void ApplyTarget(StationQueueEntry e) => e.Target = _singleEditInt;
        private void ApplyKeep(StationQueueEntry e) => e.Keep = _singleEditBool;
        private void ApplyThreshold(StationQueueEntry e) => e.Threshold = _singleEditInt;
        private void ApplyCondition(StationQueueEntry e) => e.Condition = _singleEditStr;
        private static void ApplyCompareLess(StationQueueEntry e) => e.Compare = StationQueueCompare.Less;
        private static void ApplyCompareAtLeast(StationQueueEntry e) => e.Compare = StationQueueCompare.AtLeast;

        private void SetSingleTarget(int v) { _singleEditInt = v; SingleEdit(ApplyTarget); }
        private void SetSingleKeep(bool keep) { _singleEditBool = keep; SingleEdit(ApplyKeep); }
        private void SetSingleThreshold(int v) { _singleEditInt = v; SingleEdit(ApplyThreshold); }
        private void SetSingleCondition(string id) { _singleEditStr = id; SingleEdit(ApplyCondition); }
        private void OnSingleCondClick() => OpenPicker(_sCondBtn, SetSingleCondition);
        private void OnSingleLess() => SingleEdit(ApplyCompareLess);
        private void OnSingleAtLeast() => SingleEdit(ApplyCompareAtLeast);

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
            _sTabs = BuildTabs(_single, 28f, 322f, 160f, 8f, 44f, 20f, SingleTab);

            // detail well: only the active tab's controls are shown
            var box = Img(_single, "Detail", "well");
            box.preserveAspect = false;
            Put(box, 28f, 378f, W0 - 56f, 180f);

            _sMakeBox = NewRect("Make", box.transform);
            Stretch(_sMakeBox, 0f, 0f, 0f, 0f);
            Label(_sMakeBox, W("count_label"), 20f, 12f);
            _sCount = NewStepper(_sMakeBox, 20f, 48f, () => CurrentSingle()?.Target ?? 0, SetSingleTarget);
            Chips(_sMakeBox, 330f, 48f, MakeChips, SetSingleTarget, () => _state.Inputs.Count > 0 ? _state.Inputs[0].Network : 0);
            _sFed = Txt(_sMakeBox, "Fed", "text.body", TextAlignmentOptions.Left, 20f);
            Put(_sFed, 20f, 110f, 624f, 30f);

            _sKeepBox = NewRect("Keep", box.transform);
            Stretch(_sKeepBox, 0f, 0f, 0f, 0f);
            _sKeepLabel = Label(_sKeepBox, W("keep_net_label"), 20f, 12f);
            _sKeepTarget = NewStepper(_sKeepBox, 20f, 48f, () => CurrentSingle()?.Target ?? 0, SetSingleTarget);
            Chips(_sKeepBox, 330f, 48f, KeepChips, SetSingleTarget);
            _sKeepNow = Txt(_sKeepBox, "KeepNow", "text.body", TextAlignmentOptions.Left, 20f);
            Put(_sKeepNow, 20f, 104f, 624f, 28f);
            _sKeepStock = Txt(_sKeepBox, "KeepStock", "text.body", TextAlignmentOptions.Left, 18f);
            _sKeepStock.color = Sub;
            Put(_sKeepStock, 20f, 134f, 624f, 26f);

            _sInfBox = NewRect("Infinite", box.transform);
            Stretch(_sInfBox, 0f, 0f, 0f, 0f);
            _sInfNote = Label(_sInfBox, W("infinite_note"), 20f, 64f);
            _sInfNote.color = Muted;
            _sInfNote.textWrappingMode = TextWrappingModes.Normal;
            Put(_sInfNote, 20f, 52f, 624f, 70f);

            _sUntilBox = NewRect("Until", box.transform);
            Stretch(_sUntilBox, 0f, 0f, 0f, 0f);
            Label(_sUntilBox, W("until_net"), 20f, 10f);
            _sRepeat = BuildRepeatToggle(_sUntilBox, 376f, 8f, 130f, 30f, SetSingleKeep);
            var condBtn = Btn(_sUntilBox, "Condition", "slot.cell", out var cl, null);
            Destroy(cl.gameObject);
            Put(condBtn, 20f, 46f, 56f, 56f);
            _sCond = IconIn(condBtn.transform, 6f);
            _sCondBtn = (RectTransform)condBtn.transform;
            condBtn.onClick.AddListener(OnSingleCondClick);
            _sCondName = Txt(_sUntilBox, "CondName", "text.body", TextAlignmentOptions.Left, 19f);
            _sCondName.textWrappingMode = TextWrappingModes.Normal;
            Put(_sCondName, 84f, 46f, 150f, 56f);
            var sLess = Btn(_sUntilBox, "Less", "button.icon", out var sll, OnSingleLess);
            sll.text = "<";
            sll.fontSize = 22f;
            _sLessRim = Rim(sLess.transform);
            Put(sLess, 242f, 54f, 38f, 40f);
            var sAtLeast = Btn(_sUntilBox, "AtLeast", "button.icon", out var sal, OnSingleAtLeast);
            sal.text = "≥";
            sal.fontSize = 22f;
            _sAtLeastRim = Rim(sAtLeast.transform);
            Put(sAtLeast, 284f, 54f, 38f, 40f);
            _sThreshold = NewStepper(_sUntilBox, 332f, 54f, () => CurrentSingle()?.Threshold ?? 0, SetSingleThreshold);
            var inNet = Txt(_sUntilBox, "InNet", "text.body", TextAlignmentOptions.Left, 19f);
            inNet.text = W("in_network");
            Put(inNet, 532f, 54f, 118f, 40f);
            _sNow = Txt(_sUntilBox, "Now", "text.body", TextAlignmentOptions.Left, 19f);
            Put(_sNow, 20f, 108f, 624f, 26f);
            _sNowTrough = Bar(_sUntilBox, out _sNowFill);
            Put(_sNowTrough, 20f, 138f, W0 - 56f - 40f, 20f);

            Put(Rule(_single, "Rule2", Dim), 28f, 570f, W0 - 56f, 1f);
            var pause = PlainBtn(_single, "Pause", out _sPause, () => Commit(Entries(), !_state.Paused), Light);
            _sPause.fontSize = 22f;
            Put(pause, 40f, 582f, 150f, 42f);
            var reset = PlainBtn(_single, "Reset", out var rl, SingleReset, Light);
            rl.text = W("reset");
            rl.fontSize = 22f;
            Put(reset, 206f, 582f, 170f, 42f);
            _sStart = PlainBtn(_single, "Start", out var sl, SingleStart, Gold);
            sl.text = W("start");
            sl.color = Gold;
            sl.fontSize = 22f;
            Put(_sStart, 500f, 582f, 180f, 42f);
            // two lines inside the frame (eitr-refinery.png), clear of its right border
            _sFrom = Txt(_single, "FromNetwork", "text.muted", TextAlignmentOptions.TopLeft, 20f);
            _sFrom.text = W("from_network");
            _sFrom.color = Muted;
            _sFrom.textWrappingMode = TextWrappingModes.Normal;
            _sFrom.overflowMode = TextOverflowModes.Overflow;
            Put(_sFrom, 450f, 578f, 230f, 52f);
        }

        private StationQueueEntry CurrentSingle()
        {
            var id = _state.Inputs.Count > 0 ? SingleKey(_state.Inputs[0]) : null;
            return (string.IsNullOrEmpty(id) ? null : StationQueueMath.SingleEntry(_state.Entries, id)) ?? _singleDraft;
        }

        // Own name per cell: Txt reuses a same-named child (InfoArt.RowText), so one shared "Name" left the input unnamed.
        private TMP_Text CellName(Transform parent, string name, float x, float w = 160f)
        {
            var t = Txt(parent, name, "text.body", TextAlignmentOptions.Top, 19f);
            t.textWrappingMode = TextWrappingModes.Normal;
            Put(t, x, parent == _single ? 190f : 150f, w, 50f);
            return t;
        }

        // Thin cyan "→" between input and output (eitr-refinery.png), drawn from rules: no font glyph to go missing.
        private static RectTransform ArrowGlyph(Transform parent, float x, float yMid, float w, float head = 16f, float t = 3f, bool gold = false)
        {
            var color = gold ? Gold : Cyan;
            var a = NewRect("Arrow", parent);
            Put(a, x, yMid - head / 2f, w, head);
            Put(Rule(a, "Shaft", color), 0f, head / 2f - t / 2f, w - 1f, t);
            foreach (var deg in new[] { 40f, -40f })
            {
                var rt = Rule(a, "Head", color).rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(head, t);
                rt.anchoredPosition = Vector2.zero;
                rt.localRotation = Quaternion.Euler(0f, 0f, deg);
            }
            return a;
        }

        private TMP_Text Label(Transform parent, string text, float x, float y)
        {
            var t = Txt(parent, "Label", "text.body", TextAlignmentOptions.Left, 20f);
            t.text = text;
            Put(t, x, y, 600f, 30f);
            return t;
        }

        private void Chips(Transform parent, float x, float y, int[] values, Action<int> set, Func<int> all = null)
        {
            for (var i = 0; i < values.Length; i++)
            {
                var v = values[i];
                var b = PlainBtn(parent, "Chip" + i, out var l, new ChipClick(set, all, v).Invoke, Light);
                l.text = v > 0 ? v.ToString(CultureInfo.InvariantCulture) : W("all");
                l.fontSize = 20f;
                Put(b, x + i * 70f, y, 62f, 40f);
            }
        }

        private Stepper NewStepper(Transform parent, float x, float y, Func<int> get, Action<int> set)
        {
            var s = new Stepper { Get = get, Set = set };
            var minus = Btn(parent, "Minus", "craft.amount.step", out var ml, s.StepMinus);
            ml.text = "−";
            ml.fontSize = 22f;
            Put(minus, x, y, 40f, 40f);
            s.Field = InputBox(parent, "Value", null);
            s.Field.contentType = TMP_InputField.ContentType.IntegerNumber;
            s.Field.characterLimit = 5;
            s.Field.textComponent.alignment = TextAlignmentOptions.Center;
            s.Field.onEndEdit.AddListener(s.OnEndEdit);
            Put(s.Field, x + 46f, y, 100f, 40f);
            var plus = Btn(parent, "Plus", "craft.amount.step", out var pl, s.StepPlus);
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

        private const float EditorW = 620f, EditorH = 526f;

        private void BuildEditor()
        {
            const float W0 = EditorW, H0 = EditorH;
            _editorLayer = NewRect("EditorLayer", _root);
            Stretch(_editorLayer, 0f, 0f, 0f, 0f);
            var dim = _editorLayer.gameObject.AddComponent<Image>();
            // The game blends in linear space (ChangelogPanel scrim note): 0.55 black kept ~70 % sRGB light and the queue
            // window read as undimmed (run 20261117-192839); 0.85 = the mockup's dimmed parent.
            dim.color = new Color(0f, 0f, 0f, 0.85f);
            dim.raycastTarget = true; // the queue window under the editor takes no clicks
            DollCell.Mark(dim, "content", "station queue editor backdrop");
            _editor = Panel(_editorLayer, "Editor", W0, H0, "station.queue.editor", EditorClose, Hint("Enter", W("save")) + "      " + Hint("Esc", W("cancel")));
            _eHint = _editor.Find(HintsName)?.GetComponent<TMP_Text>();
            // entry-editor.png: the dialog hangs below the queue window's bottom edge, its hints right under it
            _editor.anchoredPosition = new Vector2(0f, -StationQueueMath.EditorDrop(MultiH, H0, 12f));
            Header(_editor).Text(Title("$ui_sq_editor_title"));

            _eIcon = SlotIcon(_editor, 28f, 72f, 76f, out _eBadge);
            _eName = Txt(_editor, "Name", "text.accent", TextAlignmentOptions.Left, 26f);
            _eName.color = Gold;
            Put(_eName, 120f, 74f, W0 - 150f, 34f);
            _eNet = Txt(_editor, "Net", "text.body", TextAlignmentOptions.Left, 20f);
            Put(_eNet, 120f, 110f, W0 - 150f, 28f);
            _eIngredients = Txt(_editor, "Ingredients", "text.body", TextAlignmentOptions.TopLeft, 15f);
            _eIngredients.richText = true;
            _eIngredients.color = Sub;
            _eIngredients.textWrappingMode = TextWrappingModes.Normal;
            Put(_eIngredients, 120f, 122f, W0 - 150f, 34f);
            Put(Rule(_editor, "Rule0", Dim), 28f, 158f, W0 - 56f, 1f);

            _eTabs = BuildTabs(_editor, 28f, 166f, 135f, 8f, 42f, 18f, EditorTab);

            var detail = Img(_editor, "Detail", "well");
            detail.preserveAspect = false;
            Put(detail, 28f, 216f, W0 - 56f, 142f);

            _eMakeBox = NewRect("Make", detail.transform);
            Stretch(_eMakeBox, 0f, 0f, 0f, 0f);
            Label(_eMakeBox, W("count_label"), 16f, 12f);
            _eCount = NewStepper(_eMakeBox, 16f, 46f, () => StationQueueMath.ShownTarget(_draft.Target), v => { _draft.Target = v; RefreshEditor(); });
            Chips(_eMakeBox, 268f, 46f, MakeChips, v => { _draft.Target = v; RefreshEditor(); }, () => InputNet(_draft.Item));
            _eMakeYield = Txt(_eMakeBox, "Yield", "text.accent", TextAlignmentOptions.Left, 19f);
            _eMakeYield.color = Cyan;
            Put(_eMakeYield, 16f, 100f, 532f, 28f);

            _eKeepBox = NewRect("Keep", detail.transform);
            Stretch(_eKeepBox, 0f, 0f, 0f, 0f);
            _eKeepLabel = Label(_eKeepBox, W("keep_net_label"), 16f, 12f);
            _eKeepTarget = NewStepper(_eKeepBox, 16f, 46f, () => StationQueueMath.ShownTarget(_draft.Target), v => { _draft.Target = v; RefreshEditor(); });
            Chips(_eKeepBox, 268f, 46f, KeepChips, v => { _draft.Target = v; RefreshEditor(); });
            _eKeepNow = Txt(_eKeepBox, "KeepNow", "text.body", TextAlignmentOptions.Left, 19f);
            _eKeepNow.color = Sub;
            Put(_eKeepNow, 16f, 100f, 532f, 28f);

            _eUntilBox = NewRect("Until", detail.transform);
            Stretch(_eUntilBox, 0f, 0f, 0f, 0f);
            Label(_eUntilBox, W("until_net"), 16f, 10f);
            _eRepeat = BuildRepeatToggle(_eUntilBox, 276f, 8f, 132f, 30f, keep => { _draft.Keep = _untilKeep = keep; RefreshEditor(); });
            var drop = Btn(_eUntilBox, "Item", "dropdown.field", out _eCondName, null);
            _eCondName.alignment = TextAlignmentOptions.Left;
            Stretch(_eCondName.rectTransform, 50f, 0f, 34f, 0f);
            Put(drop, 16f, 46f, 214f, 42f);
            _eCondIcon = IconIn(drop.transform, 4f);
            Put(_eCondIcon, 6f, 4f, 34f, 34f);
            var arrow = Txt(drop.transform, "Arrow", "text.body", TextAlignmentOptions.Center, 16f);
            arrow.text = "▼";
            Put(arrow, 180f, 0f, 30f, 42f);
            _eCondBtn = (RectTransform)drop.transform;
            drop.onClick.AddListener(OnEditorCondClick);
            var less = Btn(_eUntilBox, "Less", "button.icon", out var ll, () => { _draft.Compare = StationQueueCompare.Less; RefreshEditor(); });
            ll.text = "<";
            ll.fontSize = 22f;
            _eLessRim = Rim(less.transform);
            Put(less, 238f, 47f, 38f, 40f);
            var atLeast = Btn(_eUntilBox, "AtLeast", "button.icon", out var al, () => { _draft.Compare = StationQueueCompare.AtLeast; RefreshEditor(); });
            al.text = "≥";
            al.fontSize = 22f;
            _eAtLeastRim = Rim(atLeast.transform);
            Put(atLeast, 280f, 47f, 38f, 40f);
            _eThreshold = NewStepper(_eUntilBox, 326f, 47f, () => _draft.Threshold, v => { _draft.Threshold = v; RefreshEditor(); });
            _eUntilNow = Txt(_eUntilBox, "UntilNow", "text.body", TextAlignmentOptions.Left, 19f);
            _eUntilNow.color = Sub;
            Put(_eUntilNow, 16f, 100f, 532f, 28f);

            _eInfBox = NewRect("Infinite", detail.transform);
            Stretch(_eInfBox, 0f, 0f, 0f, 0f);
            var infNote = Label(_eInfBox, W("infinite_note"), 16f, 40f);
            infNote.color = Muted;
            infNote.textWrappingMode = TextWrappingModes.Normal;
            Put(infNote, 16f, 40f, 532f, 60f);

            _eSummary = Txt(_editor, "Summary", "text.body", TextAlignmentOptions.TopLeft, 18.5f);
            _eSummary.textWrappingMode = TextWrappingModes.Normal;
            Put(_eSummary, 32f, 368f, W0 - 64f, 72f);
            Put(Rule(_editor, "Rule1", Dim), 28f, 448f, W0 - 56f, 1f);
            var red = ThemeTokens.GetColor("color.text.missing", Hex("#E0413A"));
            _eDelete = PlainBtn(_editor, "Delete", out var dl, EditorDelete, red);
            dl.text = W("delete");
            dl.color = red;
            dl.fontSize = 22f;
            Put(_eDelete, 40f, 462f, 160f, 44f);
            var cancel = PlainBtn(_editor, "Cancel", out var cl, EditorClose, Light);
            cl.text = W("cancel");
            cl.fontSize = 22f;
            Put(cancel, 230f, 462f, 160f, 44f);
            var save = PlainBtn(_editor, "Save", out _eSaveLabel, EditorSave, Gold);
            _eSaveLabel.text = W("save");
            _eSaveLabel.fontSize = 22f;
            _eSaveLabel.color = Gold;
            Put(save, 420f, 462f, 160f, 44f);
        }

        private void OnEditorCondClick() => OpenPicker(_eCondBtn, SetEditorCondition);
        private void SetEditorCondition(string id) { _draft.Condition = id; RefreshEditor(); }

        // Plain rimmed plate (mockups: footer / dialog buttons are flat rectangles with a coloured rim, no knotwork).
        private static Button PlainBtn(Transform parent, string name, out TMP_Text label, UnityEngine.Events.UnityAction click, Color rim)
        {
            var b = Btn(parent, name, "field", out label, click);
            SetRim(Rim(b.transform), rim);
            return b;
        }

        private static Color Light => new Color(0.62f, 0.68f, 0.72f, 0.75f);

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
                var b = Btn(_picker, "Row" + i, "dropdown.item", out var label, new IntClick(PickerChoose, i).Invoke);
                label.alignment = TextAlignmentOptions.Left;
                Stretch(label.rectTransform, 44f, 0f, 6f, 0f);
                var icon = IconIn(b.transform, 0f);
                Put(icon, 6f, 2f, 30f, 30f);
                Put(b, 8f, 50f + i * 34f, 274f, 32f);
                _pRows.Add((b, icon, label));
            }
            WheelOn(_picker, d => { _pScroll -= Math.Sign(d); PickerFill(); });
            var pickerDrag = _picker.gameObject.AddComponent<DragScroll>();
            pickerDrag.StepSize = () => 34f;
            pickerDrag.OnStep = (steps, _) => { _pScroll += steps; PickerFill(); };
            _picker.gameObject.SetActive(false);
        }

        // Candidates: every item in the station network + the station's inputs, outputs and fuel (names sorted).
        private void OpenPicker(RectTransform anchor, Action<string> pick)
        {
            if (_picker.gameObject.activeSelf) { ClosePicker(); return; }
            _pPick = pick;
            var set = new HashSet<string>(_state.Network.Keys, StringComparer.Ordinal);
            foreach (var i in _state.Inputs)
            {
                if (i.Item) set.Add(i.Item.name);
                if (i.Output) set.Add(i.Output.name);
                if (i.Ingredients != null)
                    for (var j = 0; j < i.Ingredients.Count; j++)
                        if (i.Ingredients[j].Item) set.Add(i.Ingredients[j].Item.name);
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

        private void ClosePicker()
        {
            _pPick = null;
            if (_picker && _picker.gameObject.activeSelf) _picker.gameObject.SetActive(false);
        }

        private void PickerChoose(int row)
        {
            var k = row + _pScroll;
            if (k < 0 || k >= _pShown.Count) return;
            var pick = _pPick;
            ClosePicker();
            pick?.Invoke(_pShown[k]);
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

        private static Button Btn(Transform parent, string name, string role, out TMP_Text label, UnityEngine.Events.UnityAction click)
        {
            var image = InfoArt.OwnImage(parent, name, role, "station queue button " + name);
            image.preserveAspect = false;
            image.raycastTarget = true;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            if (click != null) b.onClick.AddListener(click);
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
