using System.Collections.Generic;
using System.Linq;
using Morgott.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.ItemBrowser
{
    // STORAGE tab (approved mockup D:\tmp\cx-storage\network-storage-v2.png): the grid of this panel in storage mode, fed by
    // Core INetworkStorage (owner Morgott.Patcher) - network line, "All" + categories, cells with the network count, take
    // bar. The tab exists only while the current station is in a network (StoreTick).
    public sealed partial class BrowserPanel
    {
        public const string StoreTabName = "MorgottStorageTab";
        private const float StoreAllW = 52f, BarH = 104f, BarBtnH = 40f;

        private Button _storeTab;
        private bool _storage, _busy;
        private List<StorageEntry> _stock = new List<StorageEntry>(), _tmp = new List<StorageEntry>();
        private List<StorageEntry> _rows = new List<StorageEntry>(); // grid cells in storage mode, parallel to _results
        private int _chests, _netStations, _amount = -1, _barStack = 1, _barMax;
        private float _stockNext;
        private CraftingStation _stockStation;
        private StorageEntry _sel;

        private TMP_Text _netLine, _barName, _barFree, _amountText, _stackLabel, _takeLabel, _allLabel;
        private Image _selHalo, _selRim, _barRule, _barIcon, _amountBack, _takeHalo;
        private Button _bStack, _bMinus, _bPlus, _bTake, _bAll;
        private TMP_Text _minusLabel, _plusLabel;

        private void BuildStore(Transform g)
        {
            _netLine = Txt(g, "NetworkLine", "text.body", TextAlignmentOptions.Left);
            _selHalo = Img(g, "SelectedHalo", "ib.halo"); // built before the cells: drawn behind them
            _selRim = Img(g, "SelectedRim", "ib.rim"); // raised over the cells when placed (LayoutGrid)
            _barRule = Img(g, "TakeRule", "ib.rule");
            _barIcon = Img(g, "TakeIcon", "content");
            _barName = Txt(g, "TakeName", "text.body", TextAlignmentOptions.Left);
            _barFree = Txt(g, "FreeSlots", "text.muted", TextAlignmentOptions.Right);
            _bStack = Btn(g, "TakeStack", "ib.square", out _stackLabel, () => { _amount = _barStack; _dirty = true; });
            _bMinus = Btn(g, "TakeMinus", "ib.square", out _minusLabel, () => { _amount--; _dirty = true; });
            _amountBack = Img(g, "TakeAmount", "ib.square");
            _amountText = Txt(_amountBack.transform, "Text", "text.body", TextAlignmentOptions.Center);
            _bPlus = Btn(g, "TakePlus", "ib.square", out _plusLabel, () => { _amount++; _dirty = true; });
            _takeHalo = Img(g, "TakeHalo", "ib.halo");
            _bTake = Btn(g, "Take", "ib.square", out _takeLabel, () => Take(_amount), "text.ember");
            _bAll = Btn(g, "TakeAll", "ib.square", out _allLabel, () => Take(_barMax));
            foreach (var b in new[] { _bStack, _bMinus, _bPlus, _bTake, _bAll }) b.gameObject.AddComponent<CanvasGroup>();
        }

        // Every 2 s while the inventory is open (at once on a station change or tab open): stock, tab shown only while the
        // station is in a network.
        private void StoreTick()
        {
            var station = Player.m_localPlayer ? Player.m_localPlayer.GetCurrentCraftingStation() : null;
            if (Time.unscaledTime < _stockNext && station == _stockStation) return;
            _stockNext = Time.unscaledTime + 2f;
            _stockStation = station;
            _tmp.Clear();
            var on = station && Services.TryGet<INetworkStorage>(out var net) && net.Stock(station, _tmp, out _chests, out _netStations);
            if (_storeTab.gameObject.activeSelf != on) _storeTab.gameObject.SetActive(on);
            if (!on)
            {
                if (_up && _storage) Close();
                return;
            }
            // Changed stock only (keys + counts + chests): new entries, rows rebuilt, selection re-found by Key. Unchanged =
            // the grid stays, only the texts (network line, free slots) refresh.
            if (!StorageMath.Same(_stock, _tmp, s => s.Key, s => StorageMath.Sig(s.Count, s.Chests)))
            {
                (_stock, _tmp) = (_tmp, _stock);
                _resultsGen = -1;
            }
            if (_up && _storage) _dirty = true;
        }

        private static string PrefabOf(StorageEntry s) => s.Item.m_dropPrefab ? s.Item.m_dropPrefab.name : null;

        // Storage rows straight from the stock: every stored item is shown, also ones the item index hides or lacks; search
        // and category read the item itself (localized name, prefab, mod), a cell's Entry is built from it.
        private List<Entry> StoreResults(int cat)
        {
            var words = BrowserIndex.Words(_query);
            _rows = StorageMath.Rows(_stock, s => Shown(s, words, cat), s => L(s.Item.m_shared.m_name), s => s.Item.m_quality);
            var list = new List<Entry>(_rows.Count);
            foreach (var s in _rows) list.Add(new Entry { Id = PrefabOf(s) ?? s.Item.m_shared.m_name, Name = s.Item.m_shared.m_name, Type = EntryType.Item });
            return list;
        }

        private bool _nullWarned;

        private bool Shown(StorageEntry s, string[] words, int cat)
        {
            if (s.Item == null)
            {
                if (!_nullWarned) Plugin.Log.LogWarning("[Morgott.UI] STORAGE: network stock entry without an item skipped");
                _nullWarned = true;
                return false;
            }
            var sh = s.Item.m_shared;
            if (cat >= 0 && (int)PageModel.Categorize(sh.m_itemType, sh.m_skillType, sh.m_buildPieces, sh.m_food, sh.m_foodStamina, sh.m_foodEitr, sh.m_consumeStatusEffect, sh.m_ammoType) != cat) return false;
            var id = PrefabOf(s) ?? "";
            var mod = Browser.Index.Entries.TryGetValue(id, out var e) ? e.Mod : null;
            return BrowserIndex.Matches(words, BrowserIndex.NoTags(L(sh.m_name)).ToLowerInvariant() + GemWords(e), id.ToLowerInvariant(), (mod ?? "valheim").ToLowerInvariant() + " " + (Browser.ModName(mod) ?? "").ToLowerInvariant());
        }

        private void Select(StorageEntry entry)
        {
            _sel = entry;
            _amount = -1; // a full stack
            _dirty = true;
        }

        private void Take(int amount)
        {
            var station = _stockStation;
            if (_busy || _sel == null || amount <= 0 || !station || !Services.TryGet<INetworkStorage>(out var net)) return;
            _busy = true;
            _dirty = true;
            net.Withdraw(station, _sel, amount, _ =>
            {
                _busy = false;
                _stockNext = 0f; // re-Stock next frame
                _dirty = true;
            });
        }

        private static string Lang => Localization.instance?.GetSelectedLanguage();

        private void NetLine()
        {
            Put(_netLine, 0f, 0f, UnitW, 28f);
            Fit(_netLine, string.Format(L("$ib_store_net"), _chests, L("$ib_store_chest_" + StorageMath.Form(_chests, Lang)), _netStations, L("$ib_store_station_" + StorageMath.Form(_netStations, Lang))), 20f);
        }

        // Inventory room for the item in units: free slots + partial stacks it would really join (quality included, which
        // vanilla FindFreeStackSpace ignores).
        private static int Room(ItemDrop.ItemData item, out int stack, out int free)
        {
            stack = 1;
            free = 0;
            var inv = Player.m_localPlayer ? Player.m_localPlayer.GetInventory() : null;
            if (inv == null) return 0;
            free = inv.GetEmptySlots();
            if (item == null) return 0;
            stack = Mathf.Max(1, item.m_shared.m_maxStackSize);
            return StorageMath.Room(free, stack, item.m_shared.m_name, item.m_quality, item.m_worldLevel,
                inv.GetAllItems().Select(i => (i.m_shared.m_name, i.m_quality, i.m_worldLevel, i.m_stack)));
        }

        // Take bar under the grid; returns the grid's bottom (mockup units).
        private float StoreBar(float h)
        {
            // Selection keeps its identity across re-Stock (new entry objects): found again by Key, else the first row.
            var keep = StorageMath.Pick(_stock, _sel?.Key, s => s.Key);
            if (keep != null) _sel = keep;
            else Select(_rows.Count > 0 ? _rows[0] : null);
            var stock = _sel?.Count ?? 0;
            var room = Room(_sel?.Item, out _barStack, out var free);
            if (_amount < 0) _amount = _barStack;
            _amount = StorageMath.Clamp(_amount, stock, room);
            _barMax = StorageMath.Max(stock, room);
            var live = !_busy && _barMax > 0;

            var top = h - BarH;
            Put(_barRule, 0f, top, UnitW, 2f);
            if (_sel != null)
            {
                Put(_barIcon, 4f, top + 10f, 28f, 28f);
                SetSprite(_barIcon, _sel.Item.GetIcon());
                Put(_barName, 40f, top + 10f, 300f, 28f);
                Fit(_barName, $"{L(_sel.Item.m_shared.m_name)} — {StorageMath.Count(stock)} · {StorageMath.InChests(_sel.Chests, Lang, L)}", 20f);
            }
            else { Off(_barIcon); Off(_barName); }
            Put(_barFree, 330f, top + 10f, UnitW - 330f, 28f);
            Fit(_barFree, string.Format(L("$ib_store_free"), free, L("$ib_store_slot_" + StorageMath.Form(free, Lang))), 18f);

            var y = h - BarBtnH - 2f;
            Put(_takeHalo, 336f, y - 8f, 118f, BarBtnH + 16f);
            StoreBtn(_bTake, _takeLabel, L("$ib_store_take"), live, 344f, y, 102f);
            if (_sel != null && StorageMath.IsSingleItem(_sel.Count))
            {
                // One item: just "Take" (amount 1), the take button keeps its mockup place.
                foreach (var c in new Component[] { _bStack, _bMinus, _amountBack, _bPlus, _bAll }) Off(c);
                return top - 4f;
            }
            StoreBtn(_bStack, _stackLabel, string.Format(L("$ib_store_stack"), _barStack), live, 10f, y, 113f);
            StoreBtn(_bMinus, _minusLabel, "−", live && _amount > 1, 144f, y, 36f);
            Put(_amountBack, 191f, y, 85f, BarBtnH);
            PlateFit(_amountBack, 1.6f);
            Put(_amountText, 191f, y, 85f, BarBtnH);
            Text(_amountText, _amount.ToString(), 20f);
            StoreBtn(_bPlus, _plusLabel, "+", live && _amount < _barMax, 288f, y, 36f);
            StoreBtn(_bAll, _allLabel, L("$ib_store_all"), live, 466f, y, 81f);
            return top - 4f;
        }

        // Dimmed and click-dead when off: a not-interactable ib.square draws the selected plate, so the state is alpha.
        private void StoreBtn(Button b, TMP_Text label, string text, bool live, float x, float y, float w)
        {
            Square(b, false, x, y, w, BarBtnH);
            Put(label, x + 3f, y, w - 6f, BarBtnH);
            Fit(label, text, 20f);
            var group = b.GetComponent<CanvasGroup>();
            var alpha = live ? 1f : 0.45f;
            if (!Mathf.Approximately(group.alpha, alpha)) group.alpha = alpha;
            if (group.blocksRaycasts != live) group.blocksRaycasts = live;
        }

        // Storage widgets off in the items mode / on the item page.
        private void StoreOff()
        {
            foreach (var c in new Component[] { _netLine, _selHalo, _selRim, _barRule, _barIcon, _barName, _barFree, _bStack, _bMinus, _amountBack, _bPlus, _takeHalo, _bTake, _bAll }) Off(c);
        }
    }
}
