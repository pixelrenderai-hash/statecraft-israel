#nullable enable
using System;
using System.Text.RegularExpressions;
using UnityEngine.UIElements;

namespace Statecraft.Game.UI
{
    /// <summary>Local style comparisons for the main-map HUD, its panels and the Cabinet. Absent or invalid flags leave the
    /// shipped UI alone. No state is stored and no simulation command is generated. Web: ?ui-preview=archive|clear|table.
    /// Desktop: -ui-preview archive|clear|table. The normal style returns on the next launch without the flag.</summary>
    public static class UiPreview
    {
        static readonly string[] Directions = { "archive", "clear", "table" };

        /// <summary>The map table's view-only captions: element name, the panel it heads, the class of the block inside it
        /// that holds the caption (none: the panel itself), and its strings key. Made only for the table direction, so the
        /// default tree never holds them. The event's caption sits in the heading beside the portrait, as in the mockup, so
        /// it costs the card no height (UiLayoutTests.EveryEventCardStaysAboveTheMinimapUnderTheMapTable at 1568x752).</summary>
        static readonly (string name, string panel, string? inside, string key)[] Eyebrows =
        {
            ("preview-event-eyebrow", "event-card", "event-heading", "preview.event_eyebrow"),
            ("preview-info-eyebrow", "info-panel", null, "preview.info_eyebrow"),
        };

        public static string? Requested(string? url, string[] args, bool webgl)
        {
            string? value = null;
            if (webgl)
            {
                // Only top-level query parameters count, never a fragment or a nested URL's question mark.
                var address = (url ?? "").Split('#')[0];
                var start = address.IndexOf('?');
                var query = start < 0 ? "" : address.Substring(start + 1);
                var match = Regex.Match(query, @"(?:^|&)ui-preview=([a-z]+)(?=&|$)", RegexOptions.CultureInvariant);
                if (match.Success) value = match.Groups[1].Value;
            }
            else
            {
                var at = Array.IndexOf(args, "-ui-preview");
                if (at >= 0 && at + 1 < args.Length) value = args[at + 1];
            }
            return Array.IndexOf(Directions, value) >= 0 ? value : null;
        }

        /// <summary>Sets the direction's classes on #root; the table direction also heads the event card and the info panel
        /// with a caption from <paramref name="text"/> (none without it). Safe to repeat (GameRoot applies it on every
        /// Begin): another direction, or none, removes the captions an earlier call made.</summary>
        public static void Apply(VisualElement document, string? direction, UiText? text = null)
        {
            var root = Root(document);
            bool enabled = Array.IndexOf(Directions, direction) >= 0;
            root.EnableInClassList("ui-preview", enabled);
            foreach (var name in Directions)
                root.EnableInClassList("preview-" + name, enabled && direction == name);
            foreach (var (name, panel, inside, key) in Eyebrows)
            {
                var box = document.Q<VisualElement>(panel);
                var parent = inside == null ? box : box?.Q<VisualElement>(className: inside);
                var caption = parent?.Q<Label>(name);
                if (parent == null || text == null || direction != "table") { caption?.RemoveFromHierarchy(); continue; }
                if (caption == null)
                {
                    caption = new Label { name = name, pickingMode = PickingMode.Ignore };   // decoration: clicks reach the panel
                    caption.AddToClassList("preview-eyebrow");
                    parent.Insert(0, caption);   // first in its block, so never before the event's options (the smoke presses the first)
                }
                caption.text = text[key];
            }
        }

        /// <summary>Whether <paramref name="direction"/> is the active preview on this document. Presenters read it at each
        /// render, so switching takes effect on the next one.</summary>
        public static bool IsActive(VisualElement document, string direction) => Root(document).ClassListContains("preview-" + direction);

        static VisualElement Root(VisualElement document) => document.Q<VisualElement>("root") ?? document;
    }
}
