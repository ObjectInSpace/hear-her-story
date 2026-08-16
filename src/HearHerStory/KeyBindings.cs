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
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HearHerStory
{
    /// <summary>
    /// The mod's own keys.
    ///
    /// Everything sits behind Ctrl for one reason: the search box is focused for
    /// most of the game and swallows plain keys as text, which is correct — free
    /// typing is the whole mechanic. Tab is the exception, being a navigation
    /// key no screen-reader user would expect to type.
    /// </summary>
    internal sealed class KeyBindings : MonoBehaviour
    {
        private MelonLogger.Instance _log;

        internal static KeyBindings Create(MelonLogger.Instance log)
        {
            var host = new GameObject("HearHerStory.KeyBindings");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;

            var keys = host.AddComponent<KeyBindings>();
            keys._log = log;
            return keys;
        }

        private void Update()
        {
            // Spoken captions are polled rather than pushed: the game advances
            // its own caption cursor inside ClipDetail.Update with no event to
            // hook, so noticing the cursor move is the only way to stay in step
            // with what is actually on screen. Cheap, and a no-op unless the
            // player has switched captions on.
            Playback.Tick();

            // Three levels of movement, because the desktop has three: arrows
            // within a group, Tab between the groups of a window, Ctrl+Tab
            // between windows. The windows tile rather than stack and each holds
            // several groups, so collapsing any two of these into one key either
            // strands the player inside a group or makes them walk dozens of
            // controls to cross the screen.
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                bool backwards = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (FocusWatcher.Instance != null)
                {
                    if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    {
                        FocusWatcher.Instance.StepWindow(backwards ? -1 : 1);
                    }
                    else if (SearchBoxHasFocus())
                    {
                        // The toolbar shares a parent with the search field, so
                        // Search, history and settings live in the same group as
                        // the box itself. Group-stepping from there jumps over
                        // all three, and the arrows that would reach them are
                        // given to the text caret while the field has focus —
                        // between them, the three buttons were unreachable in
                        // practice despite being perfectly focusable. Tab out of
                        // the field steps within the group instead.
                        FocusWatcher.Instance.StepWithinGroup(backwards ? -1 : 1, true);
                    }
                    else
                    {
                        FocusWatcher.Instance.StepGroup(backwards ? -1 : 1);
                    }
                }

                return;
            }

            // Arrows move within the current group. Taken over from Unity, whose
            // geometric search had no notion of windows or lists and so moved
            // unpredictably everywhere the mod had not wired explicit links.
            //
            // Not while the search box has focus: there, left and right move the
            // caret through the query, which is the whole point of a text field.
            if (!SearchBoxHasFocus())
            {
                if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow))
                {
                    if (FocusWatcher.Instance != null)
                    {
                        FocusWatcher.Instance.StepWithinGroup(1);
                    }

                    return;
                }

                if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    if (FocusWatcher.Instance != null)
                    {
                        FocusWatcher.Instance.StepWithinGroup(-1);
                    }

                    return;
                }
            }

            // Enter activates a focused result. The search box handles its own
            // Enter (InputValidate.CheckSubmit), so only step in when focus is
            // on something the game cannot activate from the keyboard.
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                // The search box keeps its own Enter: InputValidate.CheckSubmit
                // runs the query, and stepping in would fight it.
                if (!SearchBoxHasFocus() && FocusWatcher.Instance != null)
                {
                    FocusWatcher.Instance.ActivateFocused();
                }

                return;
            }

            // Space activates too, and has to be handled here now: taking the
            // arrow keys meant switching off Unity's navigation events, which
            // carried Submit as well. Space was observed in play to be the key
            // that actually worked on the title screen, so losing it silently
            // would be a real regression.
            //
            // Never while a text field has focus, where space is a character.
            if (Input.GetKeyDown(KeyCode.Space) && !SearchBoxHasFocus())
            {
                if (FocusWatcher.Instance != null)
                {
                    FocusWatcher.Instance.ActivateFocused();
                }

                return;
            }

            // Backtick repeats the last thing worth re-hearing — transcripts,
            // captions, a window's text. There is no separate key for
            // re-announcing the focused control: moving off it and back does
            // that already, and a second key for the same job is one more thing
            // to remember.
            //
            // Not while the search box has focus, where it is a character the
            // player is trying to type.
            if (Input.GetKeyDown(KeyCode.BackQuote) && !SearchBoxHasFocus())
            {
                Speech.RepeatLast();
                return;
            }

            // Escape stops a playing clip. Unmodified deliberately: this is the
            // one urgent action in the game — a clip you did not mean to start is
            // 30 seconds of speech over everything else — and reaching for a
            // modifier to stop it is the wrong shape. Safe as a plain key because
            // it only acts while something is actually playing; otherwise it
            // falls through to the game's own handling untouched.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Playback.StopIfPlaying();
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrl)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                ReadTranscript();
            }
            else if (Input.GetKeyDown(KeyCode.P))
            {
                Narrator.AnnounceProgress();
            }
            else if (Input.GetKeyDown(KeyCode.L))
            {
                // "cLock". Ctrl+C would be the mnemonic and is not available:
                // Unity's InputField eats Ctrl+A/C/V/X while the search box has
                // focus, which is most of the game.
                //
                // A key rather than only a panel because the clock panel is
                // pure chrome to a screen-reader user — an analogue face whose
                // one control is a decorative scrollbar. The panel stays
                // reachable for players who use both mouse and speech; this is
                // the short way in.
                Speech.Say(Narrator.ClockSentence(), HhsTextType.SearchResult);
            }
            else if (Input.GetKeyDown(KeyCode.D))
            {
                // Flag the clip in front of the player, the way the game's own
                // star button does for a mouse. Favourites are the closest this
                // game has to a notebook, and marking one was mouse-only.
                AddFavourite();
            }
            else if (Input.GetKeyDown(KeyCode.J))
            {
                // Not Ctrl+C, despite "captions": Unity's InputField consumes
                // Ctrl+A, C, V and X for clipboard work while the search box has
                // focus, which is most of the game. Binding there would toggle
                // captions every time the player copied their query.
                Playback.ToggleCaptions();
            }
            else if (Input.GetKeyDown(KeyCode.K))
            {
                // Re-read the caption on screen right now. Deliberately works
                // whether or not continuous captions are on — "I missed that
                // line" is most useful to the player who does not want a second
                // voice running over the whole clip.
                Playback.RepeatCurrentCaption();
            }
        }

        /// <summary>
        /// Reads the transcript of whichever clip the player is pointed at:
        /// the open clip detail panel if there is one, otherwise the focused
        /// search result.
        /// </summary>
        private void ReadTranscript()
        {
            int index = CurrentClipIndex();

            if (index >= 0)
            {
                Narrator.ReadTranscript(index);
                return;
            }

            // No clip here, so read whatever prose the current window holds
            // instead — the readme files are the case that matters, and they are
            // plain Text with no control to focus. Ctrl+T already means "read
            // the thing I am pointed at" and this is the same request.
            if (FocusWatcher.Instance != null && FocusWatcher.Instance.ReadCurrentWindowBody())
            {
                return;
            }

            Speech.Say("Nothing to read here.", HhsTextType.Caption);
        }


        /// <summary>
        /// Adds the clip in front of the player to the favourites panel.
        ///
        /// Add-only, because the game's <c>FavoriteClip</c> is: it scans
        /// <c>favedClips</c> for the index and returns without doing anything if
        /// it is already there. There is no un-favourite on this code path, so
        /// the mod does not offer one — a key that claimed to toggle would lie
        /// on every second press.
        ///
        /// The count comes from the panel afterwards rather than from state of
        /// the mod's own, so it stays true if the player also uses the mouse.
        /// </summary>
        private void AddFavourite()
        {
            var database = ClipLibrary.Database;
            int index = CurrentClipIndex();

            if (database == null || index < 0)
            {
                Speech.Say("No clip selected.", HhsTextType.SearchResult, true);
                return;
            }

            int before = FavouriteCount(database);

            try
            {
                database.FavoriteClip(index);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("FavoriteClip failed: " + ex);
                }

                return;
            }

            int after = FavouriteCount(database);

            // Distinguishing "added" from "was already there" is the whole value
            // of the announcement: the game itself gives no feedback at all when
            // the clip is a duplicate.
            Speech.Say(
                after > before
                    ? "Favourited. " + after + (after == 1 ? " favourite." : " favourites.")
                    : "Already in favourites.",
                HhsTextType.SearchResult,
                true);
        }

        /// <summary>
        /// How many tiles the favourites panel is showing. Read from the panel
        /// rather than from <c>favedClips</c>, whose UnityScript array type this
        /// assembly cannot name.
        /// </summary>
        private static int FavouriteCount(NewUIDatabase database)
        {
            if (database.panelParentSession == null)
            {
                return 0;
            }

            var parent = database.panelParentSession.transform;
            int n = 0;

            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).GetComponent<ClickyBox>() != null)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// The clip the player is currently pointed at, or -1.
        /// </summary>
        private static int CurrentClipIndex()
        {
            var database = ClipLibrary.Database;

            // An open detail panel wins: it is the clip in front of the player.
            if (database != null
                && database.textBoxParent != null
                && database.textBoxParent.gameObject.activeInHierarchy)
            {
                return database.textBoxParent.myDBIndex;
            }

            var es = EventSystem.current;
            var current = es != null ? es.currentSelectedGameObject : null;

            if (current != null)
            {
                var clicky = current.GetComponent<ClickyBox>();
                if (clicky != null)
                {
                    return clicky.myIndex;
                }
            }

            return -1;
        }

        /// <summary>
        /// True while the game's search field holds focus, where the arrow keys
        /// belong to the text rather than to navigation.
        /// </summary>
        private static bool SearchBoxHasFocus()
        {
            var es = EventSystem.current;
            var current = es != null ? es.currentSelectedGameObject : null;

            return current != null && current.GetComponent<InputField>() != null;
        }

    }
}
