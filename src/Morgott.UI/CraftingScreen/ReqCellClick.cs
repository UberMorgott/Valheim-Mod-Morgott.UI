using Morgott.UI.ItemBrowser;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Morgott.UI.CraftingScreen
{
    // Requirement cell left click -> the material's item browser page (recipe section), like the Patcher chain rows.
    public sealed class ReqCellClick : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            var id = CraftingSkin.Instance ? CraftingSkin.Instance.CellPrefab(gameObject) : null;
            if (id != null && BrowserPanel.Instance) BrowserPanel.Instance.Show(id, PageSection.Recipe);
        }
    }
}
