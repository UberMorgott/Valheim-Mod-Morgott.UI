using Morgott.Core;

namespace Morgott.UI.ItemBrowser
{
    // Core IItemBrowser (docs\mod-contracts.md "Interfaces"): other mods open an entry's ITEMS page, e.g. the Patcher
    // missing-materials rows. Same path as the hover keys: inventory shown if needed, then BrowserPanel.Show.
    internal sealed class BrowserService : IItemBrowser
    {
        internal static readonly BrowserService Instance = new BrowserService();

        public bool OpenPage(string prefab, ItemPageSection section)
        {
            var panel = BrowserPanel.Instance;
            if (!panel || !Player.m_localPlayer || !InventoryGui.instance) return false;
            if (!InventoryGui.IsVisible()) InventoryGui.instance.Show(null);
            return panel.Show(prefab, PageModel.SectionOf(section));
        }
    }
}
