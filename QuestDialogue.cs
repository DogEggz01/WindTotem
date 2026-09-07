using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WindTotem
{
    // Uses the same vanilla TavernRumorsDude speech UI as Postal Expansion.
    // All scale/layout changes apply to our clone, never the source NPC or Postal UI.
    internal sealed class QuestDialogue
    {
        private static GameObject template;
        private static string textPath;
        private static string buttonPath;
        private readonly GameObject panel;
        private readonly Transform bubble;
        private QuestDialogue(GameObject panel, Transform bubble) { this.panel = panel; this.bubble = bubble; }

        internal static void Capture(TavernRumorsDude source)
        {
            if (template || !source || !source.speechUI || !source.text || !source.drinkButton) return;
            textPath = Path(source.speechUI.transform, source.text.transform);
            buttonPath = Path(source.speechUI.transform, source.drinkButton.transform);
            template = Object.Instantiate(source.speechUI);
            template.name = "Wind Totem dialogue template";
            template.SetActive(false);
            template.transform.SetParent(null, false);
            template.transform.localScale = Vector3.one;
            // Remove copied GPButtonInterface and its pointer outline. Its source
            // reference still points at the tavern NPC and must never be activated.
            foreach (var script in template.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
            if (Application.isPlaying) Object.DontDestroyOnLoad(template);
        }

        internal static QuestDialogue Create(Transform parent, string content, Action accept)
        {
            if (!template)
                foreach (var source in Resources.FindObjectsOfTypeAll<TavernRumorsDude>())
                    if (source.gameObject.scene.IsValid()) { Capture(source); if (template) break; }
            if (!template) return null;
            var panel = Object.Instantiate(template, parent, false);
            panel.name = "Wind Totem quest dialogue";
            panel.transform.localPosition = Vector3.zero;
            panel.transform.localRotation = Quaternion.identity;
            panel.transform.localScale = Vector3.one;
            var text = panel.transform.Find(textPath).GetComponent<TextMesh>();
            var bubble = text.transform.parent;
            var graphics = bubble.Find("gfx");
            var button = panel.transform.Find(buttonPath);
            panel.SetActive(true);
            button.gameObject.SetActive(false);
            // Measure rendered text instead of scaling the font. Existing bubble
            // padding is preserved while height grows to fit the entire paragraph.
            var textRenderer = text.GetComponent<Renderer>();
            Vector3 oldTextSize = textRenderer.bounds.size;
            Vector3 oldBubbleSize = graphics.GetComponent<Renderer>().bounds.size;
            string wrapped = Wrap(content, 40);
            text.font.RequestCharactersInTexture(wrapped, text.fontSize, text.fontStyle);
            text.text = wrapped;
            Vector3 newTextSize = textRenderer.bounds.size;
            Vector3 scale = graphics.localScale;
            scale.x *= Mathf.Max(0.35f, (oldBubbleSize.x + newTextSize.x - oldTextSize.x) / Mathf.Max(0.001f, oldBubbleSize.x));
            scale.y *= Mathf.Max(1f, (oldBubbleSize.y + newTextSize.y - oldTextSize.y) / Mathf.Max(0.001f, oldBubbleSize.y));
            graphics.localScale = scale;
            foreach (var collider in panel.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            if (accept != null)
            {
                button.gameObject.SetActive(true);
                button.GetComponentInChildren<TextMesh>(true).text = "Accept";
                var clickTarget = button.GetComponentInChildren<Collider>(true);
                clickTarget.enabled = true;
                clickTarget.gameObject.layer = 0;
                clickTarget.gameObject.AddComponent<QuestAcceptButton>().Accepted = accept;
                var buttonPosition = button.localPosition;
                buttonPosition.y = bubble.localPosition.y - graphics.GetComponent<Renderer>().bounds.size.y * 0.5f -
                    clickTarget.GetComponent<Renderer>().bounds.size.y * 0.5f - 0.08f;
                button.localPosition = buttonPosition;
            }
            panel.SetActive(false);
            return new QuestDialogue(panel, bubble);
        }

        internal void Show(Transform anchor, bool giver)
        {
            if (!panel || !Refs.observerMirror) return;
            var observer = Refs.observerMirror.transform;
            Vector3 toward = Vector3.ProjectOnPlane(observer.position - anchor.position, Vector3.up).normalized;
            Vector3 position = anchor.position + toward * 0.75f;
            position.y = (giver ? observer.position.y + 1.2f : Mathf.Max(anchor.position.y + 0.8f, observer.position.y + 0.5f) + 0.3f) - bubble.localPosition.y;
            panel.transform.SetPositionAndRotation(position, observer.rotation);
            panel.SetActive(true);
        }
        internal void Hide() { if (panel) panel.SetActive(false); }
        internal void PlaceServiceButton(Transform button, Quaternion faceRotation, Renderer[] renderers)
        {
            var bounds = bubble.Find("gfx").GetComponent<Renderer>().bounds;
            // Native transport graphics and text face 180 degrees away from
            // their button group. Preserve that authored facing and scale.
            button.rotation = panel.transform.rotation * Quaternion.Inverse(faceRotation);
            if (renderers.Length == 0) return;
            var buttonBounds = renderers[0].bounds;
            foreach (var renderer in renderers) buttonBounds.Encapsulate(renderer.bounds);
            button.position += new Vector3(bounds.center.x - buttonBounds.center.x,
                bounds.min.y - 0.08f - buttonBounds.max.y, bounds.center.z - buttonBounds.center.z);
        }
        internal void Dispose() { if (panel) Object.Destroy(panel); }

        private static string Path(Transform root, Transform child)
        {
            var names = new List<string>();
            while (child && child != root) { names.Add(child.name); child = child.parent; }
            if (child != root) throw new InvalidOperationException("Dialogue element is outside speech UI.");
            names.Reverse();
            return string.Join("/", names);
        }
        private static string Wrap(string value, int width)
        {
            value = value.TrimStart();
            if (value.Length <= width) return value;
            int cut = value.LastIndexOf(' ', width);
            if (cut < 1) cut = width;
            return value.Substring(0, cut) + "\n" + Wrap(value.Substring(cut), width);
        }
    }

    public sealed class QuestAcceptButton : GoPointerButton
    {
        public Action Accepted;
        public override void OnActivate() { Accepted?.Invoke(); }
    }

    [HarmonyPatch(typeof(TavernRumorsDude), "Awake")]
    internal static class QuestDialogueCapture
    {
        private static void Postfix(TavernRumorsDude __instance) { QuestDialogue.Capture(__instance); }
    }
}
