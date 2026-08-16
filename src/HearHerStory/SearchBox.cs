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
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HearHerStory
{
    /// <summary>
    /// Screen-reader behaviour for the search field.
    ///
    /// Free text entry is the whole mechanic of this game — you hear a name in a
    /// clip, then type it — so the search box is the most-used control there is,
    /// and it needs the echo behaviour a screen-reader user expects from any
    /// text field: one utterance per keystroke as it is typed, with deletions
    /// named rather than silent. Space is spoken like any other character —
    /// the field carries no word-level state, so nothing can fall out of sync
    /// with what the game actually holds.
    ///
    /// State lives here rather than in the patch so the "what changed" decision
    /// is made by comparing successive snapshots of the field. That is what makes
    /// one hook cover insertion, backspace, delete, paste and caret movement
    /// alike: we never interpret the keystroke, only its effect.
    /// </summary>
    internal static class SearchBox
    {
        /// <summary>The field's contents as of the last keystroke we observed.</summary>
        private static string _lastText = string.Empty;

        /// <summary>Caret position as of the last keystroke, for reporting movement.</summary>
        private static int _lastCaret;

        /// <summary>
        /// Unity 5.0.1 keeps <c>InputField.caretPosition</c> protected — the
        /// public property arrived in a later version — so the caret is read from
        /// the backing field instead. Resolved once and cached; a null result
        /// simply costs us caret reporting, not the rest of the echo.
        /// </summary>
        private static readonly FieldInfo CaretField =
            AccessTools.Field(typeof(InputField), "m_CaretPosition");

        private static int Caret(InputField field)
        {
            if (field == null || CaretField == null)
            {
                return 0;
            }

            try
            {
                return (int)CaretField.GetValue(field);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Called after the field has processed a keystroke, with the field in
        /// its post-keystroke state. Speaks whatever the keystroke did.
        /// </summary>
        internal static void AfterKey(InputField field)
        {
            if (field == null)
            {
                return;
            }

            string now = field.text ?? string.Empty;
            int caret = Caret(field);

            string before = _lastText;
            int beforeCaret = _lastCaret;

            _lastText = now;
            _lastCaret = caret;

            if (now == before)
            {
                // The text is unchanged, so this was navigation rather than
                // editing. Announce the character the caret moved onto, which is
                // how a screen reader reports arrow keys in a text field.
                if (caret != beforeCaret)
                {
                    AnnounceCaret(now, caret, beforeCaret);
                }

                return;
            }

            if (now.Length > before.Length)
            {
                AnnounceInsertion(before, now);
            }
            else
            {
                AnnounceDeletion(before, now);
            }
        }

        /// <summary>
        /// Text got longer. Echo what arrived — a character for ordinary typing,
        /// and the whole run for a paste, which is not worth spelling out.
        /// </summary>
        private static void AnnounceInsertion(string before, string now)
        {
            string added = Added(before, now);

            if (string.IsNullOrEmpty(added))
            {
                return;
            }

            if (added.Length > 1)
            {
                // A paste or an autocomplete: read it as text rather than
                // spelling out a run the player did not type by hand.
                Speech.Say("Inserted " + added, HhsTextType.Echo);
                return;
            }

            char c = added[0];

            Speech.Say(SpeakChar(c), HhsTextType.Echo);
        }

        /// <summary>
        /// Text got shorter. Naming the removed character is the whole point —
        /// a field that falls silent on backspace leaves the player unable to
        /// tell a deletion from a key that did not register.
        /// </summary>
        private static void AnnounceDeletion(string before, string now)
        {
            string removed = Removed(before, now);

            if (string.IsNullOrEmpty(now))
            {
                Speech.Say("Search box empty.", HhsTextType.Echo);
                return;
            }

            if (string.IsNullOrEmpty(removed))
            {
                return;
            }

            if (removed.Length > 1)
            {
                Speech.Say("Deleted " + removed, HhsTextType.Echo);
                return;
            }

            Speech.Say(SpeakChar(removed[0]) + " deleted", HhsTextType.Echo);
        }

        /// <summary>
        /// Reports the character the caret moved onto. Moving right reads the
        /// character just passed over, moving left reads the one now under the
        /// caret — in both cases, the character between the old position and the
        /// new one, which is what the player just traversed.
        /// </summary>
        private static void AnnounceCaret(string text, int caret, int previous)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int index = caret > previous ? caret - 1 : caret;

            if (index < 0 || index >= text.Length)
            {
                Speech.Say(caret <= 0 ? "Start of text." : "End of text.", HhsTextType.Echo);
                return;
            }

            Speech.Say(SpeakChar(text[index]), HhsTextType.Echo);
        }

        /// <summary>
        /// Reads the field's whole contents on demand, with the caret's position
        /// in it — the text-field equivalent of "where am I".
        /// </summary>
        internal static void ReadAll(InputField field)
        {
            if (field == null)
            {
                Speech.Say("No search box here.", HhsTextType.Focus);
                return;
            }

            string text = field.text ?? string.Empty;

            if (text.Length == 0)
            {
                Speech.Say("Search box empty.", HhsTextType.Focus, true);
                return;
            }

            Speech.Say(
                "Search box contains " + text + ". "
                + text.Length + (text.Length == 1 ? " character." : " characters."),
                HhsTextType.Focus,
                true);
        }

        /// <summary>
        /// Announces the field on focus: what it is, and what is already in it.
        /// Called from the label path rather than spoken directly, so the focus
        /// announcement stays a single utterance.
        /// </summary>
        internal static string DescribeOnFocus(InputField field)
        {
            Sync(field);

            string text = field != null ? (field.text ?? string.Empty) : string.Empty;

            return text.Length == 0
                ? "Search box, text field, empty."
                : "Search box, text field, containing " + text + ".";
        }

        /// <summary>
        /// Re-baselines the snapshot against the field's real contents.
        ///
        /// The game writes the field directly in several places — restoring a
        /// save, replaying a history entry, clearing after an admin command —
        /// none of which pass through the keystroke hook. Without this the next
        /// keystroke would diff against a stale baseline and announce a deletion
        /// of everything the game had just changed.
        /// </summary>
        internal static void Sync(InputField field)
        {
            _lastText = field != null ? (field.text ?? string.Empty) : string.Empty;
            _lastCaret = Caret(field);
        }

        /// <summary>
        /// Finds what was inserted, by matching the unchanged text at each end.
        /// Works regardless of where in the string the edit happened, which
        /// matters because the caret can be moved before typing.
        /// </summary>
        private static string Added(string before, string now)
        {
            int prefix = CommonPrefix(before, now);
            int suffix = CommonSuffix(before, now, prefix);

            int length = now.Length - prefix - suffix;

            return length <= 0 ? string.Empty : now.Substring(prefix, length);
        }

        /// <summary>Finds what was removed, by the same end-matching.</summary>
        private static string Removed(string before, string now)
        {
            int prefix = CommonPrefix(before, now);
            int suffix = CommonSuffix(before, now, prefix);

            int length = before.Length - prefix - suffix;

            return length <= 0 ? string.Empty : before.Substring(prefix, length);
        }

        private static int CommonPrefix(string a, string b)
        {
            int max = Math.Min(a.Length, b.Length);
            int i = 0;

            while (i < max && a[i] == b[i])
            {
                i++;
            }

            return i;
        }

        /// <summary>
        /// Matching suffix length, stopping before the shared prefix so the two
        /// scans cannot overlap and count the same characters twice.
        /// </summary>
        private static int CommonSuffix(string a, string b, int prefix)
        {
            int i = 0;
            int maxA = a.Length - prefix;
            int maxB = b.Length - prefix;
            int max = Math.Min(maxA, maxB);

            while (i < max && a[a.Length - 1 - i] == b[b.Length - 1 - i])
            {
                i++;
            }

            return i;
        }

        /// <summary>
        /// How a single character should be spoken. Punctuation and whitespace
        /// have no pronunciation of their own, so they get named; letters and
        /// digits are spoken as themselves.
        /// </summary>
        private static string SpeakChar(char c)
        {
            switch (c)
            {
                case ' ': return "space";
                case '"': return "quote";
                case '\'': return "apostrophe";
                case '.': return "dot";
                case ',': return "comma";
                case '-': return "dash";
                case '_': return "underscore";
                case '?': return "question mark";
                case '!': return "exclamation";
                case ':': return "colon";
                case ';': return "semicolon";
                case '(': return "left paren";
                case ')': return "right paren";
                case '\t': return "tab";
                default: return c.ToString();
            }
        }
    }
}
