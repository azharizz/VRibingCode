// VIRTUAL LOFT — LoftBridge.cs: small seams between the interaction code and
// the world, agents and boards.

using UnityEngine;

namespace VirtualLoft.XR
{
    public static class LoftBridge
    {
        public static Rect WalkRect() => LoftWorld.WalkBounds;
        public static void Say(AgentSkin skin, string text, float seconds = 5f) { if (skin != null) skin.Say(text, seconds); }
        public const bool HasPanels = true;

        public static bool Panels(string method, string type = null)
        {
            switch (method)
            {
                case "Open": LoftPanels.Open(type); return true;
                case "Close": LoftPanels.Close(type); return true;
                case "CloseAll": LoftPanels.CloseAll(); return true;
                default: return false;
            }
        }

        public static readonly string[] BoardTypes = { "kanban", "review", "log", "preview", "notes", "files", "diff" };
    }
}
