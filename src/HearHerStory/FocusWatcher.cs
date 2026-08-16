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
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HearHerStory
{
    /// <summary>
    /// Watches Unity's own selection and speaks whatever gains focus.
    ///
    /// The mod deliberately owns no cursor. Phase 1 confirmed a live
    /// StandaloneInputModule with sendNavigationEvents on and navigation set to
    /// Automatic, so Unity is already capable of driving focus — it simply never
    /// starts anywhere, and never says anything. This class fixes both without
    /// introducing a second, competing notion of "where the player is".
    /// </summary>
    internal sealed class FocusWatcher : MonoBehaviour
    {
        private MelonLogger.Instance _log;
        private GameObject _lastSpoken;
        private string _lastScene;
        private float _nextSeedAttempt;

        /// <summary>
        /// Selection is restored to this when a screen's focus is lost — for
        /// example after the game destroys whatever was selected.
        /// </summary>
        private GameObject _preferredAnchor;

        /// <summary>
        /// The last search result the player was on. Opening a clip moves focus
        /// into the detail panel, and closing it leaves focus on whatever
        /// internal object the game happens to select — a video texture, in
        /// practice. Remembering the result lets us put the player back where
        /// they were rather than somewhere meaningless.
        /// </summary>
        private GameObject _lastResult;

        /// <summary>
        /// Selection seen but not yet announced, and when it arrived. A
        /// selection has to hold for <see cref="SettleSeconds"/> before it is
        /// spoken, so the intermediate values a panel rebuild passes through
        /// never reach the player.
        /// </summary>
        private GameObject _pendingSelection;

        private float _pendingSince;

        /// <summary>
        /// Long enough to swallow a rebuild's intermediate frames, short enough
        /// that deliberate navigation still feels immediate. Arrowing through
        /// results in play sat around 120ms per step, so this stays well under
        /// that.
        /// </summary>
        private const float SettleSeconds = 0.06f;

        internal static FocusWatcher Instance { get; private set; }

        internal static FocusWatcher Create(MelonLogger.Instance log)
        {
            var host = new GameObject("HearHerStory.FocusWatcher");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;

            var watcher = host.AddComponent<FocusWatcher>();
            watcher._log = log;
            Instance = watcher;
            return watcher;
        }

        private void Update()
        {
            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }

            // A scene change invalidates every cached object we hold.
            string scene = Application.loadedLevelName;
            if (scene != _lastScene)
            {
                _lastScene = scene;
                _lastSpoken = null;
                _preferredAnchor = null;
                _nextSeedAttempt = 0f;
            }

            // The mod drives the arrow keys now, so Unity must not drive them
            // too. Left on, both would act on one keypress: Unity's geometric
            // search would move focus somewhere, then the group walk would move
            // it somewhere else, and the result was the inconsistency arrow keys
            // showed everywhere the mod had not wired explicit links.
            if (es.sendNavigationEvents)
            {
                es.sendNavigationEvents = false;
            }

            var current = es.currentSelectedGameObject;

            // Nothing focused, or focused on something the player can no longer
            // see: put focus somewhere sensible so the arrow keys have a
            // starting point. The game never sets firstSelectedGameObject.
            //
            // Both tests earn their place, for different closures. A panel's own
            // close button runs HideParent, which deactivates the whole panel —
            // caught by activeInHierarchy. But some controls are hidden by being
            // faded to alpha 0 while left active, and selection parked on one of
            // those would otherwise keep the mod waiting for a selection change
            // that never comes.
            if (current == null || !current.activeInHierarchy || !IsSelectionVisible(current))
            {
                SeedSelection(es);
                return;
            }

            if (current == _lastSpoken)
            {
                return;
            }

            // Let the selection settle before speaking it.
            //
            // Opening or rebuilding a panel walks selection through several
            // objects in consecutive frames — in one session, a result, a
            // thumbnail and a scrollbar inside 31ms. Announcing each of those
            // makes focus sound like it is jumping on its own, which is what
            // makes the whole thing feel unreliable. Only the value that holds
            // still is real; the ones in between are the game mid-rebuild.
            if (current != _pendingSelection)
            {
                _pendingSelection = current;
                _pendingSince = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - _pendingSince < SettleSeconds)
            {
                return;
            }

            _lastSpoken = current;
            _preferredAnchor = current;

            if (current.GetComponent<ClickyBox>() != null)
            {
                _lastResult = current;
            }

            Speak(current);
        }

        /// <summary>
        /// Focus moves before this time are the game's doing, not the player's,
        /// and are not announced.
        ///
        /// Opening a clip moves focus twice on the game's own initiative — onto
        /// a thumbnail as the panel builds, then onto the movie texture when
        /// playback starts — and announcing those talks over the clip
        /// description. A brief window after the open is the right instrument:
        /// suppressing for as long as the panel is *open* silences the player's
        /// own navigation inside it too, which reads as the keys having stopped
        /// working.
        /// </summary>
        private static float _suppressUntil;

        /// <summary>
        /// Called when the game is about to move focus by itself. The window is
        /// short: the involuntary moves land within a frame or two, while a
        /// player's next keypress is much further off.
        /// </summary>
        internal static void SuppressBriefly()
        {
            _suppressUntil = Time.unscaledTime + 0.35f;
        }

        private void Speak(GameObject go)
        {
            string label = Labeller.Describe(go);
            if (string.IsNullOrEmpty(label))
            {
                return;
            }

            if (Time.unscaledTime < _suppressUntil)
            {
                return;
            }

            // Focus interrupts: a keypress means the player has moved on, and
            // hearing the previous element finish is worse than losing it.
            Speech.Say(label, HhsTextType.Focus);
        }

        /// <summary>
        /// Returns focus to the result the player opened a clip from. Called
        /// when the detail panel closes, because the game leaves selection on an
        /// internal object of its own and the player would otherwise land
        /// nowhere useful.
        /// </summary>
        internal void RestoreResultFocus()
        {
            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }

            if (_lastResult == null || !_lastResult.activeInHierarchy)
            {
                // The results were rebuilt or cleared; let the normal seeding
                // logic pick somewhere sensible instead.
                es.SetSelectedGameObject(null);
                return;
            }

            // Force a re-announcement: the player has changed context, so
            // hearing where they landed matters even though it is the same
            // object they left.
            _lastSpoken = null;
            es.SetSelectedGameObject(_lastResult);
        }

        /// <summary>
        /// Puts focus on the most sensible element of the current screen when
        /// nothing is focused. Retried on a timer rather than every frame:
        /// screens build over several frames, and seeding too early lands on
        /// whatever happens to exist first.
        /// </summary>
        private void SeedSelection(EventSystem es)
        {
            if (Time.unscaledTime < _nextSeedAttempt)
            {
                return;
            }

            _nextSeedAttempt = Time.unscaledTime + 0.25f;

            // Prefer whatever was focused before, if it is still alive — this is
            // the common case where the game destroyed and rebuilt a panel.
            if (_preferredAnchor != null && _preferredAnchor.activeInHierarchy)
            {
                var anchorSelectable = _preferredAnchor.GetComponent<Selectable>();
                if (anchorSelectable != null && anchorSelectable.IsInteractable())
                {
                    es.SetSelectedGameObject(_preferredAnchor);
                    return;
                }
            }

            var target = ChooseStartingElement();
            if (target == null)
            {
                return;
            }

            es.SetSelectedGameObject(target);
        }

        /// <summary>
        /// The search box is the heart of the game, so it wins whenever it is on
        /// screen. Otherwise take the first interactable element in reading
        /// order, which on the title screen is the menu.
        /// </summary>
        private static GameObject ChooseStartingElement()
        {
            var candidates = Focusables();
            if (candidates.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].GetComponent<InputField>() != null)
                {
                    return candidates[i].gameObject;
                }
            }

            return candidates[0].gameObject;
        }

        /// <summary>
        /// Whether an element is actually on screen, as opposed to merely
        /// active.
        ///
        /// This game does not destroy a panel when it closes — it leaves the
        /// objects active behind whatever is on top, and hides them by turning
        /// their graphics transparent or scaling the panel away. An audit of one
        /// desktop found 34 "focusable" controls at once, spanning windows that
        /// were not open: five separate close buttons, settings, the readme, the
        /// clock. Tab walked all of them, and pressing Enter fired the real
        /// onClick of an invisible panel — which does something, just nothing
        /// the player can perceive. That is indistinguishable from a dead key,
        /// and it is what made focus feel unreliable.
        ///
        /// An F8 audit of one desktop settled which mechanisms this game
        /// actually uses, and it turned out to be two: the graphic's own colour
        /// alpha, and a parent CanvasGroup's alpha. Nine close buttons were live
        /// at once, one per window, and the seven belonging to closed windows
        /// were split between those two mechanisms — so testing either alone
        /// misses most of them.
        /// </summary>
        internal static bool IsVisible(Selectable s)
        {
            // The element's own graphic. This is the common case: the game
            // fades a closed window's controls to alpha 0 and leaves them
            // active behind whatever is on top.
            var graphic = s.GetComponent<Graphic>();
            if (graphic != null && graphic.color.a <= 0.01f)
            {
                return false;
            }

            // A targetGraphic set to something other than the object itself,
            // which is how the larger invisible hit-boxes are built.
            if (s.targetGraphic != null && s.targetGraphic.color.a <= 0.01f)
            {
                return false;
            }

            var t = s.transform;

            while (t != null)
            {
                var group = t.GetComponent<CanvasGroup>();
                if (group != null && group.alpha <= 0.01f)
                {
                    return false;
                }

                var rect = t as RectTransform;
                if (rect != null)
                {
                    Vector3 scale = rect.localScale;
                    if (Mathf.Abs(scale.x) <= 0.001f || Mathf.Abs(scale.y) <= 0.001f)
                    {
                        return false;
                    }
                }

                t = t.parent;
            }

            // A canvas that has been switched off hides everything under it.
            var canvas = s.GetComponentInParent<Canvas>();
            if (canvas != null && !canvas.enabled)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// The window an element belongs to, or null for anything outside the
        /// windowed area.
        ///
        /// An F8 audit settled the structure: this game parents every window as
        /// a direct child of "Parent Panel" — Main Window, Panel History List,
        /// Settings Window, OthelloPanel, DELETE Window, Recycle Bin, and the
        /// desktop icons. Each window's own close button lives inside it. So the
        /// window is simply the ancestor whose parent is that panel; no name
        /// list and no draw-order guessing is involved.
        ///
        /// Sibling index looked like the obvious way to find the topmost window
        /// and is not: every window reports the same index.
        /// </summary>
        /// <summary>
        /// Whether an element is worth stopping on at all.
        ///
        /// Scrollbars are not. The F8 audit found them to be the only genuinely
        /// inert controls on screen — no onClick, no pointer handler, no submit
        /// handler — because a scrollbar is dragged rather than activated. A
        /// keyboard player who lands on one has nothing to do and no way to
        /// tell that from a control that is simply broken, and the content they
        /// scroll is reachable by arrowing through it directly.
        /// </summary>
        private static bool IsWorthFocusing(Selectable s)
        {
            if (s.GetComponent<Scrollbar>() != null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether the currently selected object is still something the player
        /// can see, applying the same rule <see cref="Focusables"/> uses.
        /// </summary>
        private static bool IsSelectionVisible(GameObject go)
        {
            var selectable = go.GetComponent<Selectable>();

            // Not a Selectable at all: the game parks selection on internal
            // objects during playback, and those are not ours to judge.
            return selectable == null || IsVisible(selectable);
        }

        private static Transform WindowOf(Transform t)
        {
            while (t != null)
            {
                var parent = t.parent;

                if (parent != null && parent.name == WindowContainerName)
                {
                    return t;
                }

                t = parent;
            }

            return null;
        }

        private const string WindowContainerName = "Parent Panel";

        /// <summary>
        /// Every element the player could focus right now, in reading order:
        /// top to bottom, then left to right. Unity's own geometric navigation
        /// uses the same idea, so this keeps announcements consistent with where
        /// the arrow keys actually go.
        ///
        /// Not scoped to a single window, deliberately. An earlier version was,
        /// on the assumption that this game stacks windows and that anything
        /// outside the front one was buried. The audit's geometry disproved it:
        /// the panels tile rather than overlap — history sits at x≈562, settings
        /// at x≈690, the toolbar at y≈433, the desktop icons down the left at
        /// x=76 — so they are all genuinely on screen at once, like windows
        /// arranged on a desk rather than a stack of cards.
        ///
        /// Scoping therefore made the panels unreachable rather than tidy: Tab
        /// could never leave the window it started in. The reachability problem
        /// it was really solving is handled by <see cref="IsVisible"/>, which
        /// drops the controls of closed panels — those are hidden by fading to
        /// alpha 0, not by deactivation.
        /// </summary>
        internal static List<Selectable> Focusables()
        {
            var candidates = new List<Selectable>();
            var all = Selectable.allSelectables;

            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s != null && s.gameObject.activeInHierarchy && s.IsInteractable()
                    && IsVisible(s) && IsWorthFocusing(s))
                {
                    candidates.Add(s);
                }
            }

            candidates.Sort(CompareByReadingOrder);
            return candidates;
        }


        // Tab-order bands. Geometry alone puts the results panel after the
        // toolbar buttons, so Tab skipped past the results the announcement had
        // just told the player to go and read. Ordering by band first keeps the
        // walk in the order the player thinks in: type, then read what came back.
        private const int BandSearchBox = 0;
        private const int BandResults = 1;
        private const int BandEverythingElse = 2;

        private static int Band(Selectable s)
        {
            if (s.GetComponent<InputField>() != null)
            {
                return BandSearchBox;
            }

            if (s.GetComponent<ClickyBox>() != null)
            {
                return BandResults;
            }

            return BandEverythingElse;
        }

        /// <summary>
        /// Moves focus within the current group only, stopping at its edges.
        ///
        /// Arrow keys were Unity's until now, resolved by geometric search over
        /// every Selectable on screen. That search has no notion of windows or
        /// lists: it measures direction and distance in screen space, so an
        /// arrow could jump between windows, skip a near neighbour, or refuse to
        /// move at all. Only result tiles and history entries had explicit links,
        /// which is why those two felt solid and everything else did not.
        ///
        /// A group is the set of focusables sharing a parent — which is exactly
        /// how this game lays out the things a player thinks of as a list: the
        /// result tiles, the history entries, the desktop icons, a window's
        /// buttons. Arrows stay inside it and stop at the ends rather than
        /// escaping, which is what a screen-reader user expects of a list; Tab
        /// remains the way to cross a boundary.
        /// </summary>
        internal void StepWithinGroup(int direction)
        {
            StepWithinGroup(direction, false);
        }

        /// <summary>
        /// As above, but <paramref name="fallThrough"/> continues into the next
        /// group at the edges instead of stopping — what Tab needs, since a Tab
        /// that does nothing reads as a broken key.
        /// </summary>
        internal void StepWithinGroup(int direction, bool fallThrough)
        {
            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }

            var current = es.currentSelectedGameObject;
            if (current == null)
            {
                return;
            }

            var group = Group(current.transform);
            var candidates = Focusables();
            var members = new List<Selectable>();

            for (int i = 0; i < candidates.Count; i++)
            {
                if (Group(candidates[i].transform) == group)
                {
                    members.Add(candidates[i]);
                }
            }

            int at = -1;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].gameObject == current)
                {
                    at = i;
                    break;
                }
            }

            if (at < 0)
            {
                return;
            }

            int next = at + direction;

            if (next < 0 || next >= members.Count)
            {
                // Arrows stop at the edge: wrapping in a short list is
                // indistinguishable from not having moved. Tab must always go
                // somewhere, so it continues into the next group instead.
                if (fallThrough)
                {
                    StepGroup(direction);
                }

                return;
            }

            es.SetSelectedGameObject(members[next].gameObject);
        }


        private static int CompareByReadingOrder(Selectable a, Selectable b)
        {
            int bandA = Band(a);
            int bandB = Band(b);

            if (bandA != bandB)
            {
                return bandA.CompareTo(bandB);
            }

            Vector3 pa = a.transform.position;
            Vector3 pb = b.transform.position;

            // Screen y grows upwards, so higher y is earlier. Treat rows within
            // a few pixels as the same row and order those left to right.
            if (Mathf.Abs(pa.y - pb.y) > 4f)
            {
                return pb.y.CompareTo(pa.y);
            }

            return pa.x.CompareTo(pb.x);
        }

        /// <summary>
        /// The group an element belongs to: its immediate parent. This game
        /// parents each visual list under one object — the result tiles, the
        /// history entries, the toolbar buttons, the desktop icons — so the
        /// parent is the grouping the player already perceives.
        /// </summary>
        private static Transform Group(Transform t)
        {
            return t != null ? t.parent : null;
        }

        /// <summary>
        /// Moves between windows, landing on the first control of the next.
        ///
        /// The outermost of three levels: arrows move within a group, Tab
        /// between groups, and this between whole windows. The desktop earns all
        /// three — its windows tile rather than stack, and each holds several
        /// distinct groups, so collapsing any two levels into one key either
        /// strands the player inside a group or makes them walk dozens of
        /// controls to cross the screen.
        /// </summary>
        internal void StepWindow(int direction)
        {
            StepBy(direction, true);
        }

        /// <summary>
        /// Moves between groups within the current window, landing on the first
        /// control of the next group.
        /// </summary>
        internal void StepGroup(int direction)
        {
            StepBy(direction, false);
        }

        private void StepBy(int direction, bool byWindow)
        {
            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }

            var candidates = Focusables();
            if (candidates.Count == 0)
            {
                Speech.Say("Nothing to focus here.", HhsTextType.Focus);
                return;
            }

            var current = es.currentSelectedGameObject;

            // Tab walks every group on screen rather than stopping at the edge
            // of the current window. Confining it there made it silently inert
            // in most of this desktop: only Main Window holds more than one
            // group, so in the icon strip, the history list and the settings
            // panel there was never a second group to move to, and the key did
            // nothing at all. Falling through into the next window's first group
            // keeps Tab always useful, and Ctrl+Tab still jumps window to window
            // directly for anyone who wants the bigger step.
            var scope = candidates;

            var units = new List<Transform>();
            for (int i = 0; i < scope.Count; i++)
            {
                var unit = byWindow
                    ? WindowOf(scope[i].transform)
                    : Group(scope[i].transform);

                if (unit != null && !units.Contains(unit))
                {
                    units.Add(unit);
                }
            }

            if (units.Count == 0)
            {
                return;
            }

            var currentUnit = current != null
                ? (byWindow ? WindowOf(current.transform) : Group(current.transform))
                : null;

            int at = currentUnit != null ? units.IndexOf(currentUnit) : -1;

            int next = at < 0
                ? (direction > 0 ? 0 : units.Count - 1)
                : ((at + direction) % units.Count + units.Count) % units.Count;

            var target = units[next];

            for (int i = 0; i < scope.Count; i++)
            {
                var unit = byWindow
                    ? WindowOf(scope[i].transform)
                    : Group(scope[i].transform);

                if (unit != target)
                {
                    continue;
                }

                int size = 0;
                for (int j = 0; j < scope.Count; j++)
                {
                    var u = byWindow
                        ? WindowOf(scope[j].transform)
                        : Group(scope[j].transform);

                    if (u == target)
                    {
                        size++;
                    }
                }

                // Name where the player has landed as well as what is under
                // them. Hearing only "close, button" says nothing about which of
                // several panels they arrived in, and the count tells them how
                // much is here to arrow through.
                var go = scope[i].gameObject;

                Speech.Say(
                    UnitName(target, byWindow) + ", " + size
                    + (size == 1 ? " item. " : " items. ")
                    + Labeller.Describe(go),
                    HhsTextType.Focus,
                    true);

                es.SetSelectedGameObject(go);
                _lastSpoken = go;
                _pendingSelection = go;
                return;
            }
        }

        /// <summary>
        /// A readable name for whatever the player has just moved to.
        ///
        /// A window names itself. A group inside one usually does not — the
        /// containers are called things like "Panel" or "Content" — so a group
        /// falls back to naming what it holds, which is what the player is
        /// actually arriving at.
        /// </summary>
        private static string UnitName(Transform unit, bool isWindow)
        {
            if (isWindow)
            {
                return Labeller.Humanise(unit.name);
            }

            string own = Labeller.Humanise(unit.name);

            // Container names carry no information once humanised away to
            // nothing, or to a bare "panel". Name the window instead, which at
            // least locates the player.
            if (string.IsNullOrEmpty(own))
            {
                var window = WindowOf(unit);
                return window != null ? Labeller.Humanise(window.name) : "group";
            }

            return own;
        }

        /// <summary>
        /// Moves focus by hand, in reading order — every control on screen,
        /// ignoring panel boundaries. Kept as the underlying walk that
        /// <see cref="StepWithinGroup"/> and <see cref="StepPanel"/> are built
        /// from, and as a way through anything the grouping gets wrong.
        /// </summary>
        internal void Step(int direction)
        {
            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }

            var candidates = Focusables();
            if (candidates.Count == 0)
            {
                Speech.Say("Nothing to focus here.", HhsTextType.Focus);
                return;
            }

            int index = 0;
            var current = es.currentSelectedGameObject;

            if (current != null)
            {
                int found = -1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].gameObject == current)
                    {
                        found = i;
                        break;
                    }
                }

                if (found >= 0)
                {
                    index = found + direction;

                    // Wrap, so a group can be walked without getting stuck.
                    if (index < 0)
                    {
                        index = candidates.Count - 1;
                    }
                    else if (index >= candidates.Count)
                    {
                        index = 0;
                    }
                }
            }

            es.SetSelectedGameObject(candidates[index].gameObject);
        }

        /// <summary>
        /// Activates whatever is focused.
        ///
        /// The game's own click handlers are called directly where the object
        /// carries one, because that is exact. Everything else goes through a
        /// synthesised pointer click, which reaches Button.onClick and any
        /// IPointerClickHandler without the mod needing to know which a given
        /// element uses — an F8 audit showed every real control carries both,
        /// and that no EventTrigger wiring exists in this game at all.
        ///
        /// Logged, because a button that does nothing is indistinguishable from
        /// a key that never arrived, and several of this game's buttons toggle a
        /// window rather than producing any audible effect.
        /// </summary>
        internal void ActivateFocused()
        {
            var es = EventSystem.current;
            var current = es != null ? es.currentSelectedGameObject : null;

            if (current == null)
            {
                return;
            }

            // The known direct handlers first. These are reached by the pointer
            // path too, but calling them outright is exact — it cannot be
            // swallowed by a raycast target sitting over the element, which the
            // clipped results viewport makes a real possibility.
            var clicky = current.GetComponent<ClickyBox>();
            if (clicky != null)
            {
                clicky.JustBeenClicked();
                return;
            }

            // History entries have their own mouse handler, for the same reason
            // the result tiles do: neither carries a Button to invoke.
            var history = current.GetComponent<HistoryItem>();
            if (history != null)
            {
                history.ClickHistoryItem();
                return;
            }

            var button = current.GetComponent<Button>();
            if (button != null && button.IsInteractable())
            {

                // Exactly one activation. ToggleGUI.Toggle flips
                // SetActive(!activeInHierarchy), so invoking onClick and then
                // also sending a pointer click would open a window and close it
                // again within the same frame — indistinguishable from the key
                // doing nothing, which is the symptom this is meant to fix.
                var before = WindowsOnScreen();
                // Buttons whose whole job is to reveal a panel. ShowHistory is
                // one by type; the settings button is wired straight to
                // GameObject.SetActive in the scene, so it is recognised by the
                // method name its listener targets rather than by a component.
                bool showsPanel = current.GetComponent<ShowHistory>() != null
                                  || TargetsSetActive(button);


                button.onClick.Invoke();
                StartCoroutine(AnnounceWindowChange(before, Labeller.Name(current), showsPanel));
                return;
            }

            if (_log != null)
            {
                _log.Msg("[activate] " + Labeller.Name(current) + " via synthetic click");
            }

            // A control that carries state — the settings toggles — is activated
            // here rather than through the Button path above, because a Unity
            // Toggle is a Selectable with no Button on it. Its state is read
            // before and after so the change can be spoken.
            string stateBefore = Labeller.State(current);

            ClickAt(current);

            AnnounceStateChange(current, stateBefore);
        }

        /// <summary>
        /// Says a control's new state when activating it changed one.
        ///
        /// Focus does not move when a toggle is flipped, so nothing else ever
        /// speaks again: <see cref="Update"/> compares the selection against
        /// <see cref="_lastSpoken"/>, finds it unchanged, and returns. The state
        /// was correct the moment the player arrived and then silently went
        /// stale — pressing the subtitles toggle produced no output at all, which
        /// is indistinguishable from a key that did nothing. So the setting could
        /// only be read by leaving the control and coming back to it.
        ///
        /// Only the state is spoken, not the whole label. The player just pressed
        /// this control and knows what it is; repeating its name and position on
        /// every press is noise around the one word they are waiting for.
        ///
        /// Silent when nothing changed, which is the common case: most of this
        /// game's controls are buttons with no state at all, and <see
        /// cref="Labeller.State"/> returns empty for those.
        /// </summary>
        private void AnnounceStateChange(GameObject go, string before)
        {
            string after = Labeller.State(go);

            if (string.IsNullOrEmpty(after) || after == before)
            {
                return;
            }

            // Forced past duplicate suppression: flipping a toggle off and on
            // again is two deliberate presses, and the second must be heard even
            // though it says the same word as the one before last.
            Speech.Say(Labeller.Name(go) + ", " + after, HhsTextType.Focus, true);

            // The watcher would otherwise say nothing more about this object,
            // but re-baseline anyway so a later forced re-announcement is not
            // suppressed as a repeat of a stale label.
            _lastSpoken = go;
            _pendingSelection = go;
        }

        /// <summary>
        /// Whether a button's only job is to put a panel on screen.
        ///
        /// Three wirings in this game amount to that, and they are not
        /// interchangeable:
        ///
        /// <list type="bullet">
        /// <item>a listener straight onto <c>GameObject.SetActive</c> — the
        /// settings button;</item>
        /// <item><c>ShowHistory.ShowHistory</c>, which is SetActive(true) on a
        /// fixed panel and is matched by component elsewhere;</item>
        /// <item><c>ToggleGUI.Toggle</c>, which flips the panel's active state —
        /// how the desktop icons open the clock and the database checker.</item>
        /// </list>
        ///
        /// Toggle is the one that matters here and the one originally missed.
        /// Because it flips rather than shows, pressing it while the panel is up
        /// genuinely closes it — so the "already open" wording must never be
        /// reached for a toggle. The window diff sees that close as a close now
        /// that closed panels are visible to it, which is what makes reporting
        /// it possible at all.
        /// </summary>
        private static bool TargetsSetActive(Button button)
        {
            int n = button.onClick.GetPersistentEventCount();

            for (int i = 0; i < n; i++)
            {
                string method = button.onClick.GetPersistentMethodName(i);

                if (method == "SetActive" || method == "Toggle" || method == "ShowHistory")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The set of windows currently on screen.
        ///
        /// Derived from the hierarchy, not from <see cref="Focusables"/>. An
        /// earlier version collected the windows that owned a reachable control,
        /// which quietly defined "window" as "window containing a Selectable" —
        /// and this game has panels that contain none. The clock and the
        /// database checker are read-only readouts: text, no controls. They were
        /// invisible to the diff in both directions, so opening one changed
        /// nothing this method could see, and the caller fell through to telling
        /// the player it was "already open" — of a panel that had just that
        /// moment appeared.
        ///
        /// Walking the container instead means a window counts as open when the
        /// game has activated it, which is what the player is being told about.
        /// </summary>
        private static List<Transform> WindowsOnScreen()
        {
            var windows = new List<Transform>();

            var containers = WindowContainers();
            for (int i = 0; i < containers.Count; i++)
            {
                var container = containers[i];

                for (int c = 0; c < container.childCount; c++)
                {
                    var child = container.GetChild(c);

                    // activeInHierarchy, not activeSelf: a panel inside a
                    // deactivated container is not on screen however its own
                    // flag reads.
                    if (child.gameObject.activeInHierarchy && !windows.Contains(child))
                    {
                        windows.Add(child);
                    }
                }
            }

            return windows;
        }

        /// <summary>
        /// Every "Parent Panel" container in the scene.
        ///
        /// Found by search rather than assumed to be one known object, because
        /// <see cref="WindowOf"/> only ever established that windows sit under a
        /// container with this name — not that there is exactly one of them, nor
        /// that panels without controls are parented the same way as panels with
        /// them. Anything parented elsewhere is picked up by the fallback in
        /// <see cref="AnnounceWindowChange"/>.
        /// </summary>
        private static List<Transform> WindowContainers()
        {
            var containers = new List<Transform>();
            var all = Resources.FindObjectsOfTypeAll<Transform>();

            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];

                // FindObjectsOfTypeAll also returns assets and internal template
                // objects, which must not be treated as windows. GameObject.scene
                // is the modern test for that and does not exist on this Unity's
                // .NET 3.5 profile, so filter on hideFlags: anything the engine
                // keeps out of the hierarchy is not a panel the player can see.
                if (t != null && t.name == WindowContainerName
                    && t.gameObject.hideFlags == HideFlags.None
                    && !containers.Contains(t))
                {
                    containers.Add(t);
                }
            }

            return containers;
        }

        /// <summary>
        /// The open window a toolbar button appears to name, or null.
        ///
        /// Used only to describe a panel that was already up, so a miss costs
        /// nothing beyond the older, vaguer wording. Matching is by shared word
        /// rather than equality because the two names are written by different
        /// hands — a "Clock" button over a "ClockPanel" — and both sides are put
        /// through <see cref="Labeller.Humanise"/> first so the comparison is
        /// between words a player would recognise.
        /// </summary>
        private static Transform OpenWindowNamed(string buttonName)
        {
            if (string.IsNullOrEmpty(buttonName))
            {
                return null;
            }

            var words = Labeller.Humanise(buttonName).ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var open = WindowsOnScreen();

            for (int i = 0; i < open.Count; i++)
            {
                var window = open[i];

                // The panel's own heading beats its object name. "DB Checker"
                // opens a panel named FragmentPanel whose first line reads
                // "Database Checker" — the two names share no word at all, and
                // the one the player would recognise is the one on screen.
                string heading = " " + HeadingOf(window).ToLowerInvariant() + " ";
                string name = " " + Labeller.Humanise(window.name).ToLowerInvariant() + " ";

                for (int w = 0; w < words.Length; w++)
                {
                    // Short words ("of", "the") match everything and identify
                    // nothing; a real panel name shares a substantial word.
                    if (words[w].Length < 4)
                    {
                        continue;
                    }

                    string probe = " " + words[w] + " ";
                    if (name.Contains(probe) || heading.Contains(probe))
                    {
                        return window;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// A window's first line of text — what it calls itself on screen.
        ///
        /// Object names and headings are written by different hands in this
        /// game and often disagree; FragmentPanel titles itself "Database
        /// Checker". The heading is the one the player would say out loud.
        /// </summary>
        private static string HeadingOf(Transform window)
        {
            if (window == null)
            {
                return string.Empty;
            }

            var texts = window.GetComponentsInChildren<Text>();

            for (int i = 0; i < texts.Length; i++)
            {
                var text = texts[i];

                if (text == null || !text.gameObject.activeInHierarchy
                    || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                string line = text.text.Trim();

                // The mosaic layers are "text" too, and would otherwise be
                // taken for a heading. A heading is short and has letters in it.
                if (line.Length > 0 && line.Length <= 40 && HasLetter(line))
                {
                    return line;
                }
            }

            return string.Empty;
        }

        private static bool HasLetter(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsLetter(s[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The first reachable control inside a window, in reading order.
        /// </summary>
        private static Selectable FirstIn(Transform window)
        {
            var candidates = Focusables();

            for (int i = 0; i < candidates.Count; i++)
            {
                if (WindowOf(candidates[i].transform) == window)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        /// <summary>
        /// The spoken form of a panel whose visible content is not prose, or
        /// empty for the ordinary panels that can simply be read.
        ///
        /// Two of this game's panels draw rather than write — an analogue clock
        /// face and a mosaic of block characters — and both defeat a reader that
        /// concatenates Text components. They are handled by description instead,
        /// which is what a sighted player gets from them anyway.
        /// </summary>
        private static string SpecialTextOf(Transform window)
        {
            string clock = ClockTextOf(window);
            if (clock.Length > 0)
            {
                return clock;
            }

            return FragmentTextOf(window);
        }

        /// <summary>
        /// What the database checker panel says, or empty if this is not one.
        ///
        /// The panel behind the "DB Checker" icon is FragmentPanel, and what it
        /// shows is a mosaic rather than prose: FragmentManager.RefreshContent
        /// walks all of dataUID and builds three overlaid strings of block
        /// characters, one glyph per clip, 24 to a row — red for clips seen,
        /// blue for clips not, yellow marking the last one watched.
        ///
        /// Read as text that is several hundred blocks and spaces. Speaking it
        /// is worse than saying nothing: it is minutes of noise carrying one
        /// fact, and the fact is a proportion. So the panel is described by the
        /// count it is drawing — the same information a sighted player takes
        /// from the shape of the mosaic at a glance.
        ///
        /// The count comes from Narrator rather than from counting glyphs here.
        /// Both would read the same watched flag in the end, and two ways of
        /// answering one question is two things to keep in agreement — the
        /// mosaic is only a rendering of what Ctrl+P already reports.
        /// </summary>
        private static string FragmentTextOf(Transform window)
        {
            if (window == null
                || window.GetComponentInChildren<FragmentManager>() == null)
            {
                return string.Empty;
            }

            return "Database checker. " + Narrator.ProgressSentence();
        }

        /// <summary>
        /// What an analogue clock panel says, or empty if this is not one.
        /// The sentence itself lives in Narrator, which Ctrl+L also reads.
        /// </summary>
        private static string ClockTextOf(Transform window)
        {
            if (window == null || window.GetComponentInChildren<ClockManager>() == null)
            {
                return string.Empty;
            }

            return Narrator.ClockSentence();
        }

        /// <summary>
        /// The readable prose inside a window, if it has any.
        ///
        /// A window's content is not always a control. The readme files are
        /// plain Text objects with no Selectable, so nothing in the focus model
        /// ever reaches them — opening one announced the window and its close
        /// button and left the actual document unread, which is the whole point
        /// of opening it. Anything a player would call the body of the window
        /// belongs in the announcement that opens it.
        /// </summary>
        private static string BodyTextOf(Transform window)
        {
            var texts = window.GetComponentsInChildren<Text>();
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < texts.Length; i++)
            {
                var text = texts[i];

                if (text == null || !text.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // Skip anything that is a control's own label: those are spoken
                // as part of the control, and repeating them here would double
                // every button in the window.
                if (text.GetComponentInParent<Selectable>() != null)
                {
                    continue;
                }

                string body = text.text;
                if (string.IsNullOrEmpty(body))
                {
                    continue;
                }

                body = body.Trim();
                if (body.Length == 0)
                {
                    continue;
                }

                sb.Append(' ').Append(Narrator.Soften(body));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Reads the prose of the window the player is currently in, if it has
        /// any. Returns false when there is nothing to read.
        /// </summary>
        internal bool ReadCurrentWindowBody()
        {
            var es = EventSystem.current;
            var current = es != null ? es.currentSelectedGameObject : null;

            if (current == null)
            {
                return false;
            }

            var window = WindowOf(current.transform);
            if (window == null)
            {
                return false;
            }

            // Recomputed rather than replayed from memory: the clock's seconds
            // have moved on since it was announced, and the checker's count
            // changes as the player finds clips. A repeat key that returns a
            // stale answer is worse than one that says nothing.
            string body = SpecialTextOf(window);
            if (body.Length == 0)
            {
                body = BodyTextOf(window).Trim();
            }

            if (string.IsNullOrEmpty(body))
            {
                return false;
            }

            Speech.Remember(body);
            Speech.Say(body, HhsTextType.Caption, true);
            return true;
        }

        /// <summary>
        /// Says what a button did, when what it did was open or close a window.
        ///
        /// Several of this game's toolbar buttons are pure window toggles —
        /// history and settings both call SetActive on a panel — and produce no
        /// sound, no focus change and no other observable effect. A sighted
        /// player sees the panel appear; without this a blind player gets
        /// nothing at all, and reports the button as broken. Pressing it twice
        /// to check then closes the window again, which confirms the wrong
        /// conclusion.
        ///
        /// Detected by comparing which windows hold reachable controls rather
        /// than by knowing what each button does, so it covers every toggle
        /// including any this mod has not looked at.
        /// </summary>
        private System.Collections.IEnumerator AnnounceWindowChange(
            List<Transform> before, string buttonName, bool showsPanel)
        {
            // The panel is activated during the click, but its controls are not
            // reachable until Unity has rebuilt the layout.
            yield return null;
            yield return null;

            var after = WindowsOnScreen();

            for (int i = 0; i < after.Count; i++)
            {
                if (!before.Contains(after[i]))
                {
                    // Move into the window that just opened. Announcing it while
                    // leaving focus on the button outside would tell the player
                    // something appeared and give them no way to reach it —
                    // and Tab is window-scoped, so they would be walking the
                    // window they are still standing in.
                    var entry = FirstIn(after[i]);

                    // The window's prose goes out as a Caption rather than as
                    // part of the focus announcement, so Ctrl+Shift+R can bring
                    // a long readme back. Focus text is deliberately not stored
                    // for repeat, and a document the player has to re-open to
                    // re-read is barely readable at all.
                    string body = SpecialTextOf(after[i]);
                    if (body.Length == 0)
                    {
                        body = BodyTextOf(after[i]);
                    }

                    // A window with nothing reachable in it is a readout, not a
                    // place to stand: focus stays on the button that opened it,
                    // which is also the button that closes it again, and the
                    // content arrives as the Caption below. The clock is one —
                    // its only control is a fake scrollbar, which Focusables
                    // excludes — so entry is null there and the description is
                    // the whole announcement.
                    //
                    // The database checker is not: FragmentPanel has a real
                    // Refresh button, so focus does move into it and the summary
                    // follows as a Caption.
                    Speech.Say(
                        Labeller.Humanise(after[i].name) + " opened."
                        + (entry != null
                            ? " " + Labeller.Describe(entry.gameObject)
                            : string.Empty),
                        HhsTextType.Focus,
                        true);

                    if (entry != null)
                    {
                        var es = EventSystem.current;
                        if (es != null)
                        {
                            es.SetSelectedGameObject(entry.gameObject);

                            // Mark it spoken: this method just said it, and the
                            // watcher would otherwise repeat the label a frame
                            // later once the selection settled.
                            _lastSpoken = entry.gameObject;
                            _pendingSelection = entry.gameObject;
                        }
                    }

                    if (!string.IsNullOrEmpty(body))
                    {
                        Speech.Remember(body.Trim());
                        Speech.Say(body.Trim(), HhsTextType.Caption);
                    }

                    yield break;
                }
            }

            for (int i = 0; i < before.Count; i++)
            {
                if (!after.Contains(before[i]))
                {
                    Speech.Say(
                        Labeller.Humanise(before[i].name) + " closed.",
                        HhsTextType.Focus,
                        true);
                    yield break;
                }
            }

            // Nothing changed, and the button was one whose only job is to show
            // a panel. Showing and hiding are separate components in this game —
            // ShowHistory calls SetActive(true), the panel's own close button
            // runs HideParent which calls SetActive(false) on its parent — so a
            // toolbar button pressed while its panel is already up genuinely
            // does nothing, for a mouse user as much as a keyboard one. Saying
            // so is the difference between a button the player can dismiss and
            // one they keep pressing because they cannot tell it from a dead
            // key.
            //
            // Restricted to those buttons on purpose: most buttons here do
            // something with no panel change at all, and announcing "already
            // open" after a search would be nonsense.
            //
            // "Tab to reach it" is only true of a panel that actually holds a
            // focusable control. For a readout panel Tab goes nowhere, so the
            // player is told what the panel says instead — the same thing they
            // would have got had this been the frame it opened on.
            //
            // Reached only by the non-toggle show-a-panel buttons: a ToggleGUI
            // icon that finds its panel already up has just closed it, and is
            // reported by the close branch above rather than here.
            if (showsPanel && !string.IsNullOrEmpty(buttonName))
            {
                var open = OpenWindowNamed(buttonName);
                string body = string.Empty;

                if (open != null)
                {
                    body = SpecialTextOf(open);
                    if (body.Length == 0)
                    {
                        body = BodyTextOf(open).Trim();
                    }
                }

                if (open != null && FirstIn(open) == null)
                {
                    Speech.Say(
                        buttonName + " is already open.",
                        HhsTextType.Focus,
                        true);

                    if (!string.IsNullOrEmpty(body))
                    {
                        Speech.Remember(body);
                        Speech.Say(body, HhsTextType.Caption);
                    }

                    yield break;
                }

                Speech.Say(
                    buttonName + " is already open. Tab to reach it.",
                    HhsTextType.Focus,
                    true);
            }
        }

        /// <summary>
        /// Sends a synthetic pointer click to an object, as though the player
        /// had clicked it with the mouse.
        ///
        /// Unity's Submit event is not enough on its own: it only reaches
        /// ISubmitHandler, which a bare MonoBehaviour wired through an
        /// EventTrigger does not implement. The pointer sequence — down, up,
        /// click — is what the scene's own wiring expects.
        ///
        /// Submit is a fallback rather than a second shot, and that distinction
        /// matters for anything that toggles. Unity's own Toggle implements both
        /// IPointerClickHandler and ISubmitHandler, and both call InternalToggle,
        /// which flips isOn — so firing the two unconditionally turned the
        /// settings toggles on and straight back off within one keypress. The
        /// control ended up exactly where it started, which is why pressing it
        /// appeared to do nothing at all.
        /// </summary>
        private static void ClickAt(GameObject go)
        {
            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }

            var data = new PointerEventData(es)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                position = ScreenPointOf(go),
            };

            try
            {
                ExecuteEvents.Execute(go, data, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(go, data, ExecuteEvents.pointerUpHandler);

                // Execute reports whether a handler actually took the event, so
                // the fallback can be conditional rather than a guess about what
                // this particular element implements.
                bool clicked = ExecuteEvents.Execute(
                    go, data, ExecuteEvents.pointerClickHandler);

                if (!clicked)
                {
                    ExecuteEvents.Execute(go, data, ExecuteEvents.submitHandler);
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("Synthetic click failed: " + ex);
                }
            }
        }

        /// <summary>
        /// Where on screen an element sits, so the synthetic event carries a
        /// plausible position. Some handlers read it; most ignore it.
        /// </summary>
        private static Vector2 ScreenPointOf(GameObject go)
        {
            var rect = go.transform as RectTransform;
            if (rect == null)
            {
                return Vector2.zero;
            }

            var canvas = go.GetComponentInParent<Canvas>();
            Vector3 world = rect.position;

            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                && canvas.worldCamera != null)
            {
                return RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, world);
            }

            return new Vector2(world.x, world.y);
        }
    }
}
