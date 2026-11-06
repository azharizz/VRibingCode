using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

[Serializable] public class Card { public string id; public string text; public string column; }
[Serializable] public class Board { public string id; public string type; public List<Card> cards = new List<Card>(); }

public interface IBoardStore { string Save(); void Load(string json); }

public class BoardManager : MonoBehaviour, IBoardStore
{
    public List<Board> boards = new List<Board>
    {
        new Board { id = "notes" }, new Board { id = "kanban" },
        new Board { id = "calendar" }, new Board { id = "log" }
    };

    Board Get(string boardId) => boards.Find(b => b.id == boardId);

    // CRUD (one method each, no over-abstraction)
    public string AddCard(string boardId, string text, string column = "ToDo")
    {
        var b = Get(boardId); if (b == null) return null;
        var c = new Card { id = Guid.NewGuid().ToString("N")[..8], text = text, column = column };
        b.cards.Add(c);
        return c.id;
    }
    public List<Card> ListCards(string boardId) => Get(boardId)?.cards ?? new List<Card>();
    public bool EditCard(string boardId, string cardId, string text)
    {
        var c = Get(boardId)?.cards.Find(x => x.id == cardId);
        if (c == null) return false;
        c.text = text; return true;
    }
    public bool DeleteCard(string boardId, string cardId)
    {
        var b = Get(boardId);
        return b != null && b.cards.RemoveAll(x => x.id == cardId) > 0;
    }

    public bool MoveCard(string boardId, string cardId, string toColumn)
    {
        var c = Get(boardId)?.cards.Find(x => x.id == cardId);
        if (c == null) return false;
        c.column = toColumn; return true;
    }

    // Voice hook: "move that to Done" -> MoveCard("kanban", lastSelected, "Done")
    public bool VoiceMove(string boardId, string cardId, string spokenColumn) =>
        MoveCard(boardId, cardId, NormalizeColumn(spokenColumn));

    static string NormalizeColumn(string s) =>
        s.ToLowerInvariant() switch { var x when x.Contains("done") => "Done", var x when x.Contains("doing") => "Doing", _ => "ToDo" };

    // Save/load: JSON is source of truth (local mock of Supabase cloud save).
    // Newtonsoft (Unity registry package) — no engine-module dependency.
    // Load is TOTAL: corrupt/foreign JSON never bricks the loft (A24).
    public string Save() => JsonConvert.SerializeObject(new BoardList { boards = boards });
    public void Load(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var loaded = JsonConvert.DeserializeObject<BoardList>(json)?.boards;
            if (loaded != null) boards = loaded;
            else Debug.LogWarning("[BoardManager] Load ignored: no boards in JSON.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BoardManager] Load ignored corrupt JSON ({e.GetType().Name}): boards unchanged.");
        }
    }
    [Serializable] public class BoardList { public List<Board> boards; }
}
