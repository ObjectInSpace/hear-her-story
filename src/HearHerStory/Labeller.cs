// Hear Her Story - screen-reader accessibility mod for Her Story
// Copyright (C) 2026 amock
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace HearHerStory
{
    /// <summary>
    /// Turns a focused GameObject into something worth hearing: what it is, what
    /// it says, what state it is in, and where it sits in its group.
    ///
    /// The game's UI carries no accessible names — elements are sprites with
    /// layout positions — so labels are recovered from whatever the object
    /// actually holds: child Text, an InputField's contents, or, for a search
    /// result, the clip's own transcript via ClipLibrary.
    /// </summary>
    internal static class Labeller
    {
        /// <summary>How much transcript a focused result quotes. Enough to tell clips apart.</summary>
        private const int ResultPreviewChars = 90;

        /// <summary>
        /// The full announcement for an element gaining focus.
        /// </summary>
        internal static string Describe(GameObject go)
        {
            if (go == null)
            {
                return string.Empty;
            }

            // A search result is the case worth getting right: it is the element
            // with no visible text at all, and the one the player uses most.
            var clicky = go.GetComponent<ClickyBox>();
            if (clicky != null)
            {
                return DescribeResult(clicky, go);
            }

            // The fragment mosaic's own button. FragmentManager.RefreshContent
            // rebuilds three blocks of coloured glyph characters — the visual
            // grid of which clips have been seen — so the button is real, but
            // "Refresh, button" tells the player nothing about what it refreshes
            // or what the panel currently shows. Reading the grid back as counts
            // uses the game's own display rather than inventing a mod key for
            // the same information.
            if (go.GetComponentInParent<FragmentManager>() != null
                && go.GetComponent<Button>() != null)
            {
                return DescribeFragmentButton(go);
            }

            // A history entry carries the query it would re-run. That string is
            // the whole of its meaning, and the object's own name is a prefab
            // name that tells the player nothing.
            var history = go.GetComponent<HistoryItem>();
            if (history != null)
            {
                return DescribeHistoryItem(history, go);
            }

            // The search box gets its own phrasing: it is the control the player
            // spends most of the game in, and arriving at it needs to say what is
            // already typed there. Doing it here rather than speaking separately
            // keeps focus a single utterance, and re-baselines the echo snapshot
            // against whatever the field holds on arrival.
            var input = go.GetComponent<InputField>();
            if (input != null)
            {
                var database = ClipLibrary.Database;

                if (database != null && input == database.myTextField)
                {
                    return SearchBox.DescribeOnFocus(input);
                }

                // The clip's tag box. Without naming it, Name() would fall
                // through to using the field's contents as its label — so an
                // untagged clip announced as "BLANK, text field" (the game's own
                // placeholder) and a cleared one as ", text field" with no name
                // at all. Neither tells the player what they have landed on.
                if (database != null && input == database.userTags)
                {
                    string tags = input.text;

                    return string.IsNullOrEmpty(tags) || tags == "BLANK"
                        ? "Tags for this clip, text field, empty."
                        : "Tags for this clip, text field, containing " + tags + ".";
                }
            }

            var sb = new StringBuilder();

            string name = Name(go);
            if (!string.IsNullOrEmpty(name))
            {
                sb.Append(name);
            }

            string role = Role(go);
            if (!string.IsNullOrEmpty(role))
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(role);
            }

            string state = State(go);
            if (!string.IsNullOrEmpty(state))
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(state);
            }

            string position = Position(go);
            if (!string.IsNullOrEmpty(position))
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(position);
            }

            return sb.ToString();
        }

        /// <summary>
        /// The fragment panel's refresh button, described by what the panel is
        /// showing rather than by the button's own name.
        ///
        /// The three text blocks hold glyph characters, one per clip, coloured
        /// to encode state — speaking them literally would be a stream of
        /// noise. Their lengths are the information: how much of the archive
        /// each colour accounts for.
        /// </summary>
        private static string DescribeFragmentButton(GameObject go)
        {
            var manager = go.GetComponentInParent<FragmentManager>();

            int r = GlyphCount(manager.myText_R);
            int b = GlyphCount(manager.myText_B);
            int y = GlyphCount(manager.myText_Y);

            var sb = new StringBuilder();
            sb.Append("Refresh fragments, button. Showing ");
            sb.Append(r + b + y).Append(" fragments");

            if (r + b + y > 0)
            {
                sb.Append(": ").Append(r).Append(" red, ")
                  .Append(b).Append(" blue, ").Append(y).Append(" yellow");
            }

            sb.Append('.');

            return sb.ToString();
        }

        /// <summary>
        /// How many glyphs a fragment block holds, ignoring the whitespace the
        /// game pads them with.
        /// </summary>
        private static int GlyphCount(Text text)
        {
            if (text == null || string.IsNullOrEmpty(text.text))
            {
                return 0;
            }

            int n = 0;

            for (int i = 0; i < text.text.Length; i++)
            {
                if (!char.IsWhiteSpace(text.text[i]))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// A past query in the history list. The entry's whole purpose is the
        /// query text and the fact that activating it runs that search again,
        /// so the label is the query plus its position in the list.
        /// </summary>
        private static string DescribeHistoryItem(HistoryItem item, GameObject go)
        {
            string query = item.rawSearch;

            if (string.IsNullOrEmpty(query))
            {
                // Fall back to the visible label: rawSearch is assigned by the
                // game when the entry is built, and an empty one means we are
                // looking at a partially-initialised object.
                var text = go.GetComponentInChildren<Text>();
                query = text != null ? text.text : string.Empty;
            }

            if (string.IsNullOrEmpty(query))
            {
                return "Past search";
            }

            var sb = new StringBuilder();
            sb.Append(Narrator.Soften(query));

            string position = Position(go);
            if (!string.IsNullOrEmpty(position))
            {
                sb.Append(", ").Append(position);
            }

            return sb.ToString();
        }

        /// <summary>
        /// A search result thumbnail. Sighted players get a still frame; this is
        /// the replacement for it — position in the list, whether it has been
        /// seen, how long it runs, and the clip's opening words.
        /// </summary>
        private static string DescribeResult(ClickyBox clicky, GameObject go)
        {
            var sb = new StringBuilder();

            // Search results and favourites are both ClickyBox tiles, so without
            // naming the panel the two are indistinguishable — "result 1 of 1"
            // spoken in the favourites strip reads as a search that returned one
            // hit. The container is what tells them apart.
            string noun = IsFavourite(go) ? "favourite" : "result";

            string position = ResultPosition(go, noun);
            sb.Append(string.IsNullOrEmpty(position)
                ? char.ToUpperInvariant(noun[0]) + noun.Substring(1)
                : position);

            var clip = ClipLibrary.Get(clicky.myIndex);
            if (clip == null)
            {
                // Better to say something locating than to fall silent.
                return sb.ToString() + ", clip " + clicky.myIndex;
            }

            ClipLibrary.RefreshWatchedFlags();

            sb.Append(clip.Watched ? ", watched" : ", new");
            sb.Append(", ").Append(clip.SpokenDuration);

            if (!string.IsNullOrEmpty(clip.Tags))
            {
                sb.Append(", tagged ").Append(clip.Tags);
            }

            string preview = clip.Preview(ResultPreviewChars);
            if (!string.IsNullOrEmpty(preview))
            {
                sb.Append(". ").Append(preview);
            }

            return sb.ToString();
        }

        /// <summary>
        /// True when a tile belongs to the favourites strip rather than the
        /// search results. Both are ClickyBox tiles; only the container differs.
        /// </summary>
        private static bool IsFavourite(GameObject go)
        {
            var database = ClipLibrary.Database;

            return database != null
                   && database.panelParentSession != null
                   && go.transform.parent == database.panelParentSession.transform;
        }

        /// <summary>
        /// "result 2 of 5" — counted among sibling tiles, which is the grouping
        /// the player perceives. Tiles are laid out as children of one panel.
        /// </summary>
        private static string ResultPosition(GameObject go, string noun)
        {
            var parent = go.transform.parent;
            if (parent == null)
            {
                return string.Empty;
            }

            int index = 0;
            int total = 0;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.GetComponent<ClickyBox>() == null || !child.gameObject.activeInHierarchy)
                {
                    continue;
                }

                total++;

                if (child.gameObject == go)
                {
                    index = total;
                }
            }

            if (total == 0 || index == 0)
            {
                return string.Empty;
            }

            return noun + " " + index + " of " + total;
        }

        /// <summary>
        /// What the element is called. Child Text first — that is what a sighted
        /// player reads — then an input field's contents, then a cleaned-up
        /// object name as a last resort.
        /// </summary>
        internal static string Name(GameObject go)
        {
            if (go == null)
            {
                return string.Empty;
            }

            var input = go.GetComponent<InputField>();
            if (input != null)
            {
                return string.IsNullOrEmpty(input.text) ? "empty" : input.text;
            }

            var text = go.GetComponentInChildren<Text>();
            if (text != null && !string.IsNullOrEmpty(text.text))
            {
                string label = text.text.Trim();
                if (label.Length > 0)
                {
                    return label;
                }
            }

            return Humanise(go.name);
        }

        /// <summary>
        /// The control's kind, in screen-reader vocabulary.
        /// </summary>
        internal static string Role(GameObject go)
        {
            if (go.GetComponent<InputField>() != null)
            {
                return "text field";
            }

            if (go.GetComponent<Toggle>() != null)
            {
                return "toggle";
            }

            if (go.GetComponent<Slider>() != null)
            {
                return "slider";
            }

            if (go.GetComponent<Scrollbar>() != null)
            {
                return "scroll bar";
            }

            // No Dropdown check: Unity 5.0.1 predates that control entirely.

            if (go.GetComponent<HistoryItem>() != null)
            {
                return "search history entry";
            }

            if (go.GetComponent<Button>() != null)
            {
                return "button";
            }

            return string.Empty;
        }

        /// <summary>
        /// Current value or condition, where the element has one.
        /// </summary>
        internal static string State(GameObject go)
        {
            var toggle = go.GetComponent<Toggle>();
            if (toggle != null)
            {
                return toggle.isOn ? "on" : "off";
            }

            var slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                return Mathf.RoundToInt(slider.normalizedValue * 100f) + " percent";
            }

            var selectable = go.GetComponent<Selectable>();
            if (selectable != null && !selectable.IsInteractable())
            {
                return "unavailable";
            }

            return string.Empty;
        }

        /// <summary>
        /// Position among everything Tab will actually reach, so a player can
        /// tell how far through the screen they are. Results have their own
        /// phrasing, so this deliberately skips them.
        ///
        /// Counted against the reachable set rather than the immediate parent.
        /// Per-parent counting was accurate but meaningless: walking one screen
        /// announced "4 of 7", "5 of 7", "6 of 7", "1 of 7", "3 of 7", then
        /// "6 of 6", because the controls sat under several parents that the
        /// player has no way to perceive as separate groups. A number that
        /// resets without an audible boundary reads as focus having jumped
        /// somewhere else, which is precisely the confusion it was meant to
        /// prevent.
        /// </summary>
        private static string Position(GameObject go)
        {
            var reachable = FocusWatcher.Focusables();

            int index = 0;

            for (int i = 0; i < reachable.Count; i++)
            {
                if (reachable[i].gameObject == go)
                {
                    index = i + 1;
                    break;
                }
            }

            // One-of-one tells the player nothing they cannot already hear.
            if (reachable.Count < 2 || index == 0)
            {
                return string.Empty;
            }

            return index + " of " + reachable.Count;
        }

        /// <summary>
        /// Words that name a control's type rather than its purpose. Dropped
        /// only when they stand alone: "Button START" becomes "start", but
        /// "pcMovieTexture" keeps its Texture, because removing a token from the
        /// middle of a word produces noise ("pc movie ure") rather than clarity.
        /// </summary>
        private static readonly string[] NoiseWords =
        {
            "button", "image", "panel", "text", "inputfield", "input", "field",
            "toggle", "gameobject", "game", "ui", "clone", "obj", "object", "new",
            "prefab", "parent", "rect", "canvas",
        };

        /// <summary>
        /// Object names are developer shorthand — "Button START", "InputFieldMain".
        /// Split them into words, drop the ones that only name the control type,
        /// and return something a screen reader can read as English.
        /// </summary>
        internal static string Humanise(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            // Unity's clone suffix is a whole-string artefact, not a word.
            string name = raw.Replace("(Clone)", " ");

            // Split on camel-case boundaries and separators, so the noise filter
            // below sees whole words rather than substrings.
            var sb = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];

                if (c == '_' || c == '-' || c == '.' || c == '(' || c == ')')
                {
                    sb.Append(' ');
                    continue;
                }

                // Split camel case, but leave runs of capitals ("START") intact.
                // A capital followed by a lowercase also starts a word, which is
                // what separates the "Movie" in "pcMovieTexture".
                if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
                {
                    sb.Append(' ');
                }

                sb.Append(c);
            }

            var kept = new System.Collections.Generic.List<string>();
            foreach (var word in sb.ToString().Split(' '))
            {
                string trimmed = word.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (Array.IndexOf(NoiseWords, trimmed.ToLowerInvariant()) >= 0)
                {
                    continue;
                }

                kept.Add(trimmed);
            }

            // Everything was a type name. The object has no purpose-bearing name
            // at all, so say the type rather than echoing raw Unity punctuation
            // like "text (clone)" — the parenthetical is an engine artefact and
            // means nothing to a player.
            if (kept.Count == 0)
            {
                var typeWords = new System.Collections.Generic.List<string>();
                foreach (var word in sb.ToString().Split(' '))
                {
                    string trimmed = word.Trim(' ', '(', ')');
                    if (trimmed.Length > 0)
                    {
                        typeWords.Add(trimmed);
                    }
                }

                return string.Join(" ", typeWords.ToArray()).ToLowerInvariant();
            }

            return string.Join(" ", kept.ToArray()).ToLowerInvariant();
        }
    }
}
