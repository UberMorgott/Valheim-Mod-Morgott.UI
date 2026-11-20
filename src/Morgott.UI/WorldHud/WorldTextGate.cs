using Morgott.UI.Restyle;
using UnityEngine;

namespace Morgott.UI.WorldHud
{
    // World-anchored texts (Chat NPC / raven / creature bubbles m_npcTexts, say / shout / ping m_worldTexts; both
    // instantiated under the Chat object, Chat.cs:383/692) are hidden while a modal is open (pause menu with its quit /
    // logout dialogs, UnifiedPopup; ModalLayer.AnyOpen). User 2026-10-23: "Я принес весть." drew inside the quit
    // dialog. Vanilla keeps updating and timing them out (Chat.LateUpdate); this only gates each live instance (RenderGate:
    // a CanvasGroup at alpha 0 was still batched, overdraw-menu alpha0 Chat/DialogBox(Clone), run 20261121-020139), so they
    // reappear in place when the modal closes. Lives on the Chat object.
    [DisallowMultipleComponent]
    public sealed class WorldTextGate : MonoBehaviour, IRevertible
    {
        private const string Owner = "worldtext";
        private Chat _chat;
        private bool _hidden;

        public static bool Hidden { get; private set; }

        public static void Ensure(Chat chat)
        {
            if (!chat || chat.GetComponent<WorldTextGate>()) return;
            var g = chat.gameObject.AddComponent<WorldTextGate>();
            g.hideFlags = HideFlags.HideInInspector;
            g._chat = chat;
        }

        private void LateUpdate()
        {
            if (!_chat) return;
            var hide = ModalLayer.AnyOpen();
            Hidden = hide;
            // Instances appear any frame (a raven line while the menu is open): applied every frame while hidden,
            // once on the change back.
            if (!hide && !_hidden) return;
            _hidden = hide;
            if (!hide)
            {
                RenderGate.ReleaseAll(Owner);
                return;
            }
            foreach (var n in _chat.m_npcTexts) if (n != null && n.m_gui && !RenderGate.Holds(n.m_gui.transform, Owner)) RenderGate.Set(n.m_gui, Owner, true);
            foreach (var w in _chat.m_worldTexts) if (w != null && w.m_gui && !RenderGate.Holds(w.m_gui.transform, Owner)) RenderGate.Set(w.m_gui, Owner, true);
        }

        public void Revert()
        {
            RenderGate.ReleaseAll(Owner);
            Hidden = false;
            Destroy(this);
        }
    }
}
