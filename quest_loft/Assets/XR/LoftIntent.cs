// VIRTUAL LOFT — LoftIntent.cs: how a /voice answer is allowed to run.
// The backend suggests (needsConfirm, risky); the client NEVER trusts it to be
// the only safety: anything that changes work or removes things is forced onto
// the card, and risky ones can only run from a deliberate press.

using Newtonsoft.Json.Linq;

namespace VirtualLoft.XR
{
    public enum IntentRoute { Immediate, Confirm, Risky, Invalid }

    public static class LoftIntent
    {
        public static readonly string[] Actions =
            { "goal", "task", "spawn", "remove", "scene", "approve", "reject", "stop", "party", "project", "autoApprove", "menu", "note", "selectNext", "chat", "board", "browse", "preview", "watch", "none" };

        // Safe to run with no confirmation at all.
        static readonly string[] Harmless = { "scene", "party", "selectNext", "chat", "board", "browse", "preview", "menu", "note", "watch", "none" };

        public static string Action(JObject intent) => (string)intent?["action"];

        // approve / remove / reject / stop-all / auto-approve on. A single-agent stop still goes through the card.
        public static bool IsRisky(JObject intent)
        {
            switch (Action(intent))
            {
                case "approve": case "remove": case "reject": return true;
                case "stop": return string.IsNullOrEmpty((string)intent["agentId"]);
                case "autoApprove": return (bool?)intent["on"] ?? true;
                default: return false;
            }
        }

        public static IntentRoute Route(JObject response)
        {
            var intent = response?["intent"] as JObject;
            var action = Action(intent);
            if (action == null || System.Array.IndexOf(Actions, action) < 0) return IntentRoute.Invalid;
            if (action == "none") return IntentRoute.Immediate; // just a reply to show
            if (IsRisky(intent) || (bool?)response["risky"] == true) return IntentRoute.Risky;
            bool serverSaysAsk = (bool?)response["needsConfirm"] ?? true;
            if (!serverSaysAsk && System.Array.IndexOf(Harmless, action) >= 0) return IntentRoute.Immediate;
            return IntentRoute.Confirm;
        }
    }
}
