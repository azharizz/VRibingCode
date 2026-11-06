// VIRTUAL LOFT — LoftUi.cs: tiny world-space UI kit shared by the confirm card
// and the wrist menu. Flat unlit panels + buttons with a collider, crisp TMP
// text (LoftUtil.Label). Parents must be uniformly scaled.

using System;
using TMPro;
using UnityEngine;

namespace VirtualLoft.XR
{
    public static class LoftUi
    {
        public static readonly Color Panel = new Color(0.10f, 0.12f, 0.17f);
        public static readonly Color Accent = new Color(0.40f, 0.85f, 1f);
        public static readonly Color Go = new Color(0.16f, 0.58f, 0.30f);
        public static readonly Color Neutral = new Color(0.30f, 0.34f, 0.44f);
        public static readonly Color Danger = new Color(0.85f, 0.30f, 0.28f);
        public static readonly Color Soft = new Color(0.85f, 0.90f, 1f);

        static Shader unlit;
        // Resources/LoftUiKeep.mat (made by LoftBootstrap) keeps Unlit/Color in player builds.
        public static Material NewMat(Color c)
        {
            var keep = Resources.Load<Material>("LoftUiKeep");
            if (keep != null) return new Material(keep) { color = c };
            if (unlit == null) unlit = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            return new Material(unlit) { color = c };
        }

        public static GameObject Quad(Transform parent, string name, Vector3 localPos, Vector2 size, Color c, float depth = 0.004f, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, depth);
            go.GetComponent<Renderer>().sharedMaterial = NewMat(c);
            if (!collider) LoftUtil.Kill(go.GetComponent<Collider>());
            return go;
        }

        public static TMP_Text Text(Transform parent, string name, Vector3 localPos, float heightM, Color color, float width = 0f, TextAlignmentOptions align = TextAlignmentOptions.Center) =>
            LoftUtil.Label(parent, name, localPos, heightM, color, width, align);

        public static void DestroyChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i);
                c.SetParent(null, false); // Destroy waits for frame end: don't let Find-by-name reuse it
                c.gameObject.SetActive(false);
                LoftUtil.Kill(c.gameObject);
            }
        }
    }

    // A pointable button. Press-and-release is not needed: the pointer calls
    // Press() on trigger-down, or after `holdSeconds` of holding the trigger.
    public class LoftButton : MonoBehaviour
    {
        public string id;
        public Action onClick;
        public float holdSeconds;          // > 0: needs a deliberate hold (Exit, Stop all)
        public bool selectedLook;          // e.g. the current scene
        public float HoldProgress { get; set; }
        public bool Enabled = true;

        Color baseColor;
        Renderer face;
        TMP_Text label;
        Transform bar, holder;
        bool hovered;
        Vector3 baseScale;

        public string Text => label != null ? label.text : "";

        public static LoftButton Make(Transform parent, string id, Vector3 localPos, Vector2 size, string text, Color color,
                                      float textHeight, Action onClick, float hold = 0f)
        {
            var go = LoftUi.Quad(parent, "Btn_" + id, localPos, size, color, 0.012f, collider: true);
            var b = go.AddComponent<LoftButton>();
            b.id = id; b.onClick = onClick; b.holdSeconds = hold; b.baseColor = color;
            b.face = go.GetComponent<Renderer>();
            b.baseScale = go.transform.localScale;
            // Text sits proud of the face; unscaled child so TMP keeps its size.
            b.holder = new GameObject("Content_" + id).transform;
            var holder = b.holder;
            holder.SetParent(parent, false);
            holder.localPosition = localPos;
            b.label = LoftUi.Text(holder, "Label", new Vector3(0f, 0f, 0.009f), textHeight, color.grayscale > 0.55f ? Color.black : Color.white, size.x - 0.01f);
            b.label.text = text;
            if (hold > 0f)
            {
                var bar = LoftUi.Quad(holder, "Hold", new Vector3(-size.x / 2f, -size.y / 2f + 0.003f, 0.008f), new Vector2(0.001f, 0.006f), Color.white, 0.002f);
                b.bar = bar.transform;
            }
            b.Apply();
            return b;
        }

        // Face and label move/show together.
        public void Show(bool on) { gameObject.SetActive(on); if (holder != null) holder.gameObject.SetActive(on); }
        public void MoveTo(Vector3 localPos) { transform.localPosition = localPos; if (holder != null) holder.localPosition = localPos; }

        public void SetText(string text) { if (label != null) label.text = text; }
        public void SetBase(Color c) { baseColor = c; Apply(); }
        public void SetHover(bool on) { if (hovered != on) { hovered = on; Apply(); } }

        public void Press()
        {
            if (!Enabled) return;
            try { onClick?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        void Update()
        {
            if (bar == null) return;
            float w = Mathf.Max(0.001f, baseScale.x * HoldProgress);
            bar.localScale = new Vector3(w, 0.006f, 0.002f);
            bar.localPosition = new Vector3(baseScale.x / 2f - w / 2f, -baseScale.y / 2f + 0.004f, 0.008f); // UI +X is the viewer's left
        }

        void Apply()
        {
            if (face == null) return;
            var c = Enabled ? baseColor : baseColor * 0.5f;
            if (selectedLook) c = Color.Lerp(c, Color.white, 0.25f);
            if (hovered && Enabled) c = Color.Lerp(c, Color.white, 0.4f);
            face.sharedMaterial.color = c;
            transform.localScale = baseScale * (hovered && Enabled ? 1.07f : 1f);
        }

        void OnDestroy()
        {
            if (holder != null) LoftUtil.Kill(holder.gameObject);
            if (face != null && face.sharedMaterial != null) LoftUtil.Kill(face.sharedMaterial);
        }
    }
}
