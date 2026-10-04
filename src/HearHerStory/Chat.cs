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
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HearHerStory
{
    /// <summary>
    /// Screen-reader behaviour for the chat window that opens near the end of
    /// the game.
    ///
    /// ChatBox draws every line through <c>AddText</c>, and a single message
    /// arrives as several calls in one frame — the game wraps by hand, one Text
    /// per visual line, so "SB: Good. So... you think you" and "understand why
    /// your mother did" are separate calls. Speaking each as it arrived would
    /// cut the message into fragments that interrupt one another, so lines are
    /// collected for the frame and spoken together on the next.
    ///
    /// The answer box is the other half. <c>ChatBox.ValidateInput</c> silently
    /// drops any character that does not continue one of the accepted answers,
    /// so a player who cannot see the screen has no way to know what the game
    /// will take: every wrong key is indistinguishable from a dead keyboard.
    /// The accepted answers are therefore read out with each question, and a
    /// rejected key says which letters would have been taken instead.
    /// </summary>
    internal static class Chat
    {
        /// <summary>Lines added this frame, not yet spoken.</summary>
        private static readonly List<string> Pending = new List<string>();

        private static int _pendingFrame;

        private static ChatBox _pendingBox;

        /// <summary>
        /// The prefix ChatBox writes on the player's own lines. Spoken as "You"
        /// — the game's in-fiction terminal name says nothing to the player.
        /// </summary>
        private const string PlayerPrefix = "Archive_PC:";

        /// <summary>
        /// The marker the game writes when the other side leaves the chat.
        /// Spelled out, because a screen reader either skips angle brackets or
        /// TextCleaner takes the line for a rich-text tag and strips it.
        /// </summary>
        private const string HangUpMarker = "<hangs up>";

        /// <summary>
        /// Called from the AddText patch with each line as the game draws it.
        /// </summary>
        internal static void Line(ChatBox box, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Pending.Add(text);
            _pendingFrame = Time.frameCount;
            _pendingBox = box;
        }

        /// <summary>
        /// Speaks whatever arrived in an earlier frame. Polled from the key
        /// handler's Update, the same way captions are.
        /// </summary>
        internal static void Tick()
        {
            if (Pending.Count == 0 || Time.frameCount <= _pendingFrame)
            {
                return;
            }

            string message = Spoken(Pending);
            var box = _pendingBox;

            Pending.Clear();
            _pendingBox = null;

            if (message.Length == 0)
            {
                return;
            }

            // A question is followed by the game focusing the answer box, and
            // that focus announcement would cut the question off a frame later.
            // The answer box is described here instead, as part of the prompt.
            FocusWatcher.SuppressBriefly();

            string prompt = AwaitingAnswer(box) ? " " + AnswerHint(box) : string.Empty;

            Speech.Remember(message);
            Speech.Say(message + prompt, HhsTextType.Chat);
        }

        /// <summary>
        /// Reads the whole conversation so far, from what is on screen. Taken
        /// from the window rather than from what the mod has spoken, so a chat
        /// restored from a save reads in full too.
        /// </summary>
        internal static bool ReadConversation()
        {
            var box = FocusedBox();
            if (box == null)
            {
                return false;
            }

            string text = Describe(box);

            Speech.Remember(text);
            Speech.Say(text, HhsTextType.Chat, true);
            return true;
        }

        /// <summary>
        /// The conversation so far, and what the game will accept next if it is
        /// waiting. Also what the window says when it opens.
        /// </summary>
        internal static string Describe(ChatBox box)
        {
            string text = Spoken(OnScreenLines(box));

            if (text.Length == 0)
            {
                text = "The chat is empty.";
            }

            if (AwaitingAnswer(box))
            {
                text += " " + AnswerHint(box);
            }

            return text;
        }

        /// <summary>
        /// The answer box's focus announcement, in place of the generic text
        /// field one — which would otherwise call it a search box.
        /// </summary>
        internal static string DescribeOnFocus(ChatBox box, InputField field)
        {
            SearchBox.Sync(field);

            string text = field.text ?? string.Empty;
            var sb = new StringBuilder("Chat answer, text field, ");

            sb.Append(text.Length == 0 ? "empty." : "containing " + text + ".");

            if (!field.interactable)
            {
                sb.Append(" Waiting for a reply.");
            }

            sb.Append(" Control T reads the chat.");
            return sb.ToString();
        }

        /// <summary>
        /// Says why a key did nothing. Called when ValidateInput has rejected a
        /// character, with the text as it stood before the key.
        /// </summary>
        internal static void Rejected(ChatBox box, string text, char added)
        {
            if (char.IsControl(added))
            {
                return;
            }

            text = text ?? string.Empty;
            var next = NextLetters(box, text);

            var sb = new StringBuilder();
            sb.Append(SearchBox.SpeakChar(char.ToLowerInvariant(added))).Append(" not accepted.");

            if (next.Count > 0)
            {
                sb.Append(" Next letter can be ");
                for (int i = 0; i < next.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(i == next.Count - 1 ? " or " : ", ");
                    }

                    sb.Append(SearchBox.SpeakChar(next[i]));
                }

                sb.Append('.');
            }
            else if (text.Length > 0)
            {
                sb.Append(" Answer complete, press Enter.");
            }

            Speech.Say(sb.ToString(), HhsTextType.Echo, true);
        }

        /// <summary>
        /// The chat that owns this field, or null when it is any other field.
        /// </summary>
        internal static ChatBox Owning(InputField field)
        {
            if (field == null)
            {
                return null;
            }

            var box = UnityEngine.Object.FindObjectOfType<ChatBox>();
            return box != null && box.myInputField == field ? box : null;
        }

        private static ChatBox FocusedBox()
        {
            var es = EventSystem.current;
            var current = es != null ? es.currentSelectedGameObject : null;

            if (current != null)
            {
                var inside = current.GetComponentInParent<ChatBox>();
                if (inside != null)
                {
                    return inside;
                }
            }

            // While the chat is open it is the thing in front of the player
            // wherever focus happens to sit: the game dims everything else
            // behind it. FindObjectOfType only returns active objects, so this
            // is null whenever the window is closed.
            return UnityEngine.Object.FindObjectOfType<ChatBox>();
        }

        /// <summary>
        /// Whether the game is waiting on the player. The box is made
        /// non-interactable while SB is "typing" and after the chat ends, so
        /// that flag is the game's own answer.
        /// </summary>
        private static bool AwaitingAnswer(ChatBox box)
        {
            return box != null
                   && box.myInputField != null
                   && box.myInputField.gameObject.activeInHierarchy
                   && box.myInputField.interactable;
        }

        /// <summary>
        /// The accepted answers, grouped by what they mean to the game.
        ///
        /// SubmitChat reduces whatever is typed to a single yes or no through
        /// <c>acceptableStringsBooleans</c>, so that is the grouping that tells
        /// the player what each answer will do. The whole word matters: a
        /// partial entry is matched by substring rather than prefix, so "y" can
        /// land on an answer meaning no.
        /// </summary>
        private static string AnswerHint(ChatBox box)
        {
            var strings = box.acceptableStrings;
            var meanings = box.acceptableStringsBooleans;

            if (strings == null || strings.Length == 0)
            {
                return "Type your answer and press Enter.";
            }

            var yes = new List<string>();
            var no = new List<string>();

            for (int i = 0; i < strings.Length; i++)
            {
                string s = strings[i];
                if (string.IsNullOrEmpty(s))
                {
                    continue;
                }

                bool saidYes = meanings != null && i < meanings.Length && meanings[i];
                var list = saidYes ? yes : no;

                if (!list.Contains(s))
                {
                    list.Add(s);
                }
            }

            var sb = new StringBuilder("Type a whole answer and press Enter.");

            if (yes.Count > 0)
            {
                sb.Append(" Yes answers: ").Append(string.Join(", ", yes.ToArray())).Append('.');
            }

            if (no.Count > 0)
            {
                sb.Append(" No answers: ").Append(string.Join(", ", no.ToArray())).Append('.');
            }

            return sb.ToString();
        }

        /// <summary>
        /// The letters ValidateInput would accept after this text, mirroring
        /// its own test: an accepted string longer than the text that starts
        /// with it.
        /// </summary>
        private static List<char> NextLetters(ChatBox box, string text)
        {
            var next = new List<char>();
            var strings = box != null ? box.acceptableStrings : null;

            if (strings == null)
            {
                return next;
            }

            for (int i = 0; i < strings.Length; i++)
            {
                string s = strings[i];
                if (s != null && s.Length > text.Length && s.StartsWith(text, StringComparison.Ordinal))
                {
                    char c = s[text.Length];
                    if (!next.Contains(c))
                    {
                        next.Add(c);
                    }
                }
            }

            next.Sort();
            return next;
        }

        /// <summary>
        /// The chat's lines in order, as drawn.
        ///
        /// AddText draws each speaker line twice: the full line in grey, then
        /// the "SB:" prefix alone on top of it in the speaker's colour, added
        /// straight after. The overlay is skipped, or every message would name
        /// its speaker twice.
        /// </summary>
        private static List<string> OnScreenLines(ChatBox box)
        {
            var lines = new List<string>();

            if (box.myTextContainer == null)
            {
                return lines;
            }

            var parent = box.myTextContainer.transform;
            string previous = null;

            for (int i = 0; i < parent.childCount; i++)
            {
                var text = parent.GetChild(i).GetComponent<Text>();
                if (text == null || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                string line = text.text;

                if (previous != null && line.EndsWith(":") && previous.StartsWith(line))
                {
                    continue;
                }

                lines.Add(line);
                previous = line;
            }

            return lines;
        }

        /// <summary>
        /// Lines as they should be heard: joined into sentences, the player's
        /// own lines attributed to "You", and the hang-up marker put into words.
        /// </summary>
        private static string Spoken(List<string> lines)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line == HangUpMarker)
                {
                    line = "SB hung up.";
                }
                else if (line.StartsWith(PlayerPrefix))
                {
                    line = "You: " + line.Substring(PlayerPrefix.Length).Trim() + ".";
                }

                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(line);
            }

            return sb.ToString();
        }
    }
}
