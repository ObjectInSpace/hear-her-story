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
using System.Globalization;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HearHerStory
{
    /// <summary>
    /// Phase 1 diagnostic. Answers the questions static analysis could not:
    /// is there a live input module, do the Selectables carry usable navigation,
    /// is a starting selection set, and do the runtime-built result thumbnails
    /// participate in navigation at all.
    ///
    /// Writes to a report file as well as the log, so the findings survive the
    /// session and can be handed to a fresh one.
    /// </summary>
    internal sealed class InputSurvey : MonoBehaviour
    {
        private MelonLogger.Instance _log;
        private string _reportPath;
        private int _snapshotCount;
        private GameObject _lastSelected;

        internal static InputSurvey Create(MelonLogger.Instance log)
        {
            var host = new GameObject("HearHerStory.InputSurvey");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;

            var survey = host.AddComponent<InputSurvey>();
            survey._log = log;
            survey._reportPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? ".",
                "HearHerStory-survey.txt");
            return survey;
        }

        /// <summary>
        /// True when the full Phase 1 survey was asked for on the command line.
        /// Without it this component still runs, but only to serve the F8
        /// activation audit — the continuous logging below would otherwise fill
        /// the report on every ordinary session.
        /// </summary>
        internal bool Verbose { get; set; }

        private void Start()
        {
            if (!Verbose)
            {
                return;
            }

            Write("=== Hear Her Story — input survey ===");
            Write("started " + DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
            Write("unity " + Application.unityVersion + "   scene " + Application.loadedLevelName);
            Write("");
            Write("Keys:  F9 snapshot   F10 walk selection   F11 dump hierarchy   F12 repeat speech");
            Write("");

            Snapshot("startup");
        }

        private void Update()
        {
            if (Verbose)
            {
                // Report focus changes as they happen — this is the signal that
                // tells us whether anything drives selection during normal play.
                var current = EventSystem.current != null
                    ? EventSystem.current.currentSelectedGameObject
                    : null;

                if (current != _lastSelected)
                {
                    _lastSelected = current;
                    Write("[focus] -> " + Describe(current));
                }

                // The one question Phase 1 left open: are the runtime-instantiated
                // search-result thumbnails focusable? They are rebuilt on every
                // query by NewUIDatabase.PopulateDatabaseList(), so we watch for
                // the selectable count changing rather than relying on a
                // well-timed keypress.
                WatchForResults();
            }

            // F8 is available in any session: it answers a live design question
            // and writes only when pressed.
            if (Input.GetKeyDown(KeyCode.F8))
            {
                AuditActivation();
                return;
            }

            if (!Verbose)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                Snapshot("manual");
            }
            else if (Input.GetKeyDown(KeyCode.F10))
            {
                WalkSelection();
            }
            else if (Input.GetKeyDown(KeyCode.F11))
            {
                DumpSelectables();
            }
            else if (Input.GetKeyDown(KeyCode.F12))
            {
                Speech.RepeatLast();
            }
        }

        private int _lastSelectableCount = -1;
        private float _nextResultCheck;
        private bool _reportedResults;

        /// <summary>
        /// Watches for the search-results panel appearing. Result thumbnails are
        /// instantiated fresh on every query, so the interesting moment is when
        /// the selectable set grows. Reports what those new objects are and
        /// whether they carry usable navigation.
        /// </summary>
        private void WatchForResults()
        {
            // Once a second is plenty and keeps this off the hot path.
            if (Time.unscaledTime < _nextResultCheck)
            {
                return;
            }

            _nextResultCheck = Time.unscaledTime + 1f;

            int count = 0;
            var all = Selectable.allSelectables;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            if (count == _lastSelectableCount)
            {
                return;
            }

            int previous = _lastSelectableCount;
            _lastSelectableCount = count;

            if (previous < 0)
            {
                return;
            }

            Write("");
            Write("[change] active selectables " + previous + " -> " + count);

            // ClickyBox is the component the game attaches to each result
            // thumbnail (and to favourited clips), carrying the clip's database
            // index. Its presence is what identifies a result tile.
            ReportClipTiles();
        }

        /// <summary>
        /// Looks for result thumbnails specifically, and reports whether they are
        /// navigable. This is the finding Phase 2 is scoped against.
        /// </summary>
        private void ReportClipTiles()
        {
            var clicky = UnityEngine.Object.FindObjectsOfType<ClickyBox>();
            if (clicky == null || clicky.Length == 0)
            {
                return;
            }

            if (_reportedResults)
            {
                return;
            }

            _reportedResults = true;

            Write("");
            Write("=== RESULT THUMBNAILS FOUND: " + clicky.Length + " ===");

            for (int i = 0; i < clicky.Length; i++)
            {
                var box = clicky[i];
                if (box == null)
                {
                    continue;
                }

                var go = box.gameObject;
                var selectable = go.GetComponent<Selectable>();

                var line = "  [" + i + "] " + HierarchyPath(go)
                         + "  clipIndex=" + box.myIndex
                         + "  active=" + go.activeInHierarchy;

                if (selectable == null)
                {
                    // The likely case: mouse-click handlers only, no Selectable,
                    // so keyboard navigation cannot reach them at all.
                    line += "  Selectable=NONE  <- not focusable";
                }
                else
                {
                    line += "  Selectable=" + selectable.GetType().Name
                          + "  nav=" + selectable.navigation.mode
                          + "  interactable=" + selectable.IsInteractable();
                }

                Write(line);
            }

            Write("");
            Write("Verdict: " + (clicky[0] != null && clicky[0].GetComponent<Selectable>() != null
                ? "thumbnails ARE Selectables — Phase 2 only needs navigation repair + labels."
                : "thumbnails are NOT Selectables — Phase 2 must add them in PopulateDatabaseList()."));
            Write("");
        }

        /// <summary>
        /// For every focusable on screen, reports what pressing Enter would
        /// actually reach — and, crucially, what it would not.
        ///
        /// This exists because "focusable" and "activatable" turned out to be
        /// different sets. The game's activation handlers live on bare
        /// MonoBehaviours wired in the Unity scene rather than in code, so the
        /// assembly cannot tell us which elements do anything; only the live
        /// object can. Elements that answer NONE to every column are the ones
        /// that take focus and then sit there inert, which is worse than not
        /// being focusable at all — the player cannot tell a dead control from
        /// one they have failed to work out how to use.
        /// </summary>
        private void AuditActivation()
        {
            Write("");
            Write("--- activation audit  scene=" + Application.loadedLevelName + " ---");
            Write("columns: name | Button.onClick | known handler | EventTrigger | IPointerClick | ISubmit");

            var focusables = FocusWatcher.Focusables();
            Write("focusable count: " + focusables.Count);

            int inert = 0;

            for (int i = 0; i < focusables.Count; i++)
            {
                var go = focusables[i].gameObject;

                var button = go.GetComponent<Button>();
                bool hasOnClick = button != null && button.onClick != null
                                  && button.onClick.GetPersistentEventCount() > 0;

                string known = KnownHandler(go);

                // Unity 5.0.1 calls this list "delegates"; the rename to
                // "triggers" came in a later version.
                var trigger = go.GetComponent<EventTrigger>();
                bool hasTrigger = trigger != null && trigger.delegates != null
                                  && trigger.delegates.Count > 0;

                bool pointer = go.GetComponent<IPointerClickHandler>() != null;
                bool submit = go.GetComponent<ISubmitHandler>() != null;

                bool dead = !hasOnClick && known == "none" && !hasTrigger && !pointer && !submit;
                if (dead)
                {
                    inert++;
                }

                // Only the inert ones are worth a line each now. The activation
                // question is settled — everything else has onClick and a
                // pointer handler — and 100 identical rows buried the structure
                // that actually matters.
                if (dead)
                {
                    Write(string.Format(
                        "  INERT {0} | onClick={1} | handler={2} | trigger={3} | pointer={4} | submit={5} | {6}",
                        Labeller.Name(go),
                        hasOnClick,
                        known,
                        hasTrigger,
                        pointer,
                        submit,
                        Visibility(go)));
                }
            }

            Write("inert (focusable but nothing to activate): " + inert + " of " + focusables.Count);

            WriteWindowStructure(focusables);

            // Focusables() now applies the visibility filter, so report what it
            // is excluding by walking the raw set — otherwise this always reads
            // zero and tells us nothing about whether the filter is right.
            int hiddenCount = 0;
            var hidden = new StringBuilder();
            var all = Selectable.allSelectables;

            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.gameObject.activeInHierarchy || !s.IsInteractable())
                {
                    continue;
                }

                if (FocusWatcher.IsVisible(s))
                {
                    continue;
                }

                hiddenCount++;

                if (hidden.Length > 0)
                {
                    hidden.Append(", ");
                }

                hidden.Append(Labeller.Name(s.gameObject));
            }

            Write("visibility filter excluded " + hiddenCount + " hidden control(s)"
                  + (hiddenCount > 0 ? ": " + hidden : string.Empty));
            Speech.Say(
                inert + " of " + focusables.Count + " focusable items do nothing when activated.",
                HhsTextType.Diagnostic,
                true);
        }

        /// <summary>
        /// Groups the focusables by the window they belong to, so the structure
        /// of the screen is visible rather than a flat list of a hundred
        /// controls.
        ///
        /// This is the question the flat audit could not answer. One desktop
        /// carried 105 focusables at once — 64 of them an Othello board, plus a
        /// quit dialog's Quit and Cancel, three readme windows, the settings
        /// panel and the clip detail, all live simultaneously. The alpha filter
        /// removes only the fully transparent ones; everything here is drawn.
        /// Tab therefore walks every window on the desktop at once, which is
        /// what still makes navigation feel boundless.
        ///
        /// Fixing that needs a notion of which window is active, and that in
        /// turn needs to know how windows are actually parented — hence this
        /// dump, rather than another guess.
        /// </summary>
        private void WriteWindowStructure(List<Selectable> focusables)
        {
            Write("");
            Write("  --- what the navigation keys actually see ---");

            var byWindow = new Dictionary<string, List<string>>();
            var windowOrder = new List<string>();

            // Walk the raw selectables, not Focusables(), so controls the mod
            // deliberately excludes still appear here — marked. A control that
            // disappears from the walk should be traceable to the rule that
            // dropped it rather than simply absent.
            var raw = new List<Selectable>();
            var everything = Selectable.allSelectables;

            for (int i = 0; i < everything.Count; i++)
            {
                var s = everything[i];
                if (s != null && s.gameObject.activeInHierarchy && s.IsInteractable()
                    && FocusWatcher.IsVisible(s))
                {
                    raw.Add(s);
                }
            }

            for (int i = 0; i < raw.Count; i++)
            {
                var t = raw[i].transform;

                // Mirror FocusWatcher's own two levels exactly, so this reports
                // what the keys do rather than what the hierarchy looks like.
                string w = "(no window)";
                var walk = t;
                while (walk != null)
                {
                    if (walk.parent != null && walk.parent.name == "Parent Panel")
                    {
                        w = walk.name;
                        break;
                    }

                    walk = walk.parent;
                }

                string g = t.parent != null ? t.parent.name : "(no parent)";
                string key = w + "  >>  " + g;

                if (!byWindow.ContainsKey(key))
                {
                    byWindow[key] = new List<string>();
                    windowOrder.Add(key);
                }

                string label = Labeller.Name(raw[i].gameObject);

                // Mark anything the exclusion rules drop, so a control missing
                // from the walk is traceable to a rule rather than simply gone.
                if (!focusables.Contains(raw[i]))
                {
                    label += raw[i].GetComponent<Scrollbar>() != null
                        ? " <excluded: scrollbar>"
                        : " <excluded>";
                }

                byWindow[key].Add(label);
            }

            for (int i = 0; i < windowOrder.Count; i++)
            {
                var members = byWindow[windowOrder[i]];
                Write("   window >> group : " + windowOrder[i]);
                Write("        [" + members.Count + "] " + string.Join(", ", members.ToArray()));
            }

            WriteControllessPanels();

            Write("");
            Write("  --- grouped by parent chain ---");

            // Depth 2 from the canvas is, by inspection, where this game puts a
            // window; deeper is the controls inside it. Report a few levels so
            // the real boundary is visible rather than assumed.
            var groups = new Dictionary<string, List<string>>();
            var order = new List<string>();

            for (int i = 0; i < focusables.Count; i++)
            {
                var go = focusables[i].gameObject;
                string chain = ParentChain(go);

                if (!groups.ContainsKey(chain))
                {
                    groups[chain] = new List<string>();
                    order.Add(chain);
                }

                groups[chain].Add(Labeller.Name(go));
            }

            // Sibling index of each window's root, because Unity draws later
            // siblings last — on top. If the topmost window is simply the
            // highest sibling index, that is the scoping rule, and it needs no
            // guessing about names or components.
            var rootIndex = new Dictionary<string, int>();

            for (int i = 0; i < focusables.Count; i++)
            {
                string chain = ParentChain(focusables[i].gameObject);

                if (!rootIndex.ContainsKey(chain))
                {
                    rootIndex[chain] = WindowSiblingIndex(focusables[i].gameObject);
                }
            }

            for (int i = 0; i < order.Count; i++)
            {
                var members = groups[order[i]];
                Write("   [" + members.Count + "] sibling=" + rootIndex[order[i]]
                      + "  " + order[i]);

                // Long runs of identical controls (the Othello grid) say nothing
                // useful repeated 64 times.
                if (members.Count <= 6)
                {
                    Write("        " + string.Join(", ", members.ToArray()));
                }
                else
                {
                    Write("        " + members[0] + ", " + members[1] + ", … , "
                          + members[members.Count - 1]);
                }
            }

            Write("  windows (distinct parent chains): " + order.Count);

            Write("  reachable: " + focusables.Count + " of " + raw.Count
                  + " visible (" + (raw.Count - focusables.Count) + " excluded)");
        }

        /// <summary>
        /// Where this object's top-level window sits among its siblings. Unity
        /// renders siblings in order, so the highest index is drawn last and is
        /// therefore the window on top.
        /// </summary>
        private static int WindowSiblingIndex(GameObject go)
        {
            var t = go.transform;
            Transform child = t;

            while (t != null)
            {
                if (t.GetComponent<Canvas>() != null)
                {
                    return child.GetSiblingIndex();
                }

                child = t;
                t = t.parent;
            }

            return -1;
        }

        /// <summary>
        /// The object's ancestry up to the canvas, which is what identifies the
        /// window it lives in.
        /// </summary>
        private static string ParentChain(GameObject go)
        {
            var parts = new List<string>();
            var t = go.transform.parent;
            int depth = 0;

            while (t != null && depth < 6)
            {
                parts.Insert(0, t.name);

                if (t.GetComponent<Canvas>() != null)
                {
                    break;
                }

                t = t.parent;
                depth++;
            }

            return string.Join(" / ", parts.ToArray());
        }

        /// <summary>
        /// How this object is being hidden, if it is.
        ///
        /// The audit found 34 focusables on one desktop, across panels that were
        /// not open — this game leaves closed windows active rather than
        /// destroying them. Which mechanism it uses to hide them decides how the
        /// mod should filter, so report the raw values rather than guessing:
        /// canvas group alpha, local scale, canvas enabled, and the screen rect.
        /// </summary>
        private static string Visibility(GameObject go)
        {
            var sb = new StringBuilder();

            var rect = go.transform as RectTransform;
            if (rect != null)
            {
                sb.Append("scale=").Append(rect.localScale.x.ToString("0.##"))
                  .Append(',').Append(rect.localScale.y.ToString("0.##"));

                Vector3 p = rect.position;
                sb.Append(" pos=").Append(p.x.ToString("0")).Append(',').Append(p.y.ToString("0"));
                sb.Append(" size=").Append(rect.rect.width.ToString("0"))
                  .Append('x').Append(rect.rect.height.ToString("0"));
            }

            // Nearest canvas group up the chain, which is how a whole panel is
            // usually faded out at once.
            var t = go.transform;
            while (t != null)
            {
                var group = t.GetComponent<CanvasGroup>();
                if (group != null)
                {
                    sb.Append(" cgAlpha=").Append(group.alpha.ToString("0.##"));
                    sb.Append(" cgInteract=").Append(group.interactable);
                    break;
                }

                t = t.parent;
            }

            var canvas = go.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                sb.Append(" canvas=").Append(canvas.name)
                  .Append(canvas.enabled ? "(on)" : "(OFF)");
            }

            var graphic = go.GetComponent<Graphic>();
            if (graphic != null)
            {
                sb.Append(" alpha=").Append(graphic.color.a.ToString("0.##"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Which of the game's own click handlers an object carries, if any.
        /// These are the ones the mod can invoke directly.
        /// </summary>
        private static string KnownHandler(GameObject go)
        {
            if (go.GetComponent<ClickyBox>() != null)
            {
                return "ClickyBox";
            }

            if (go.GetComponent<HistoryItem>() != null)
            {
                return "HistoryItem";
            }

            if (go.GetComponent<MainMenuClick>() != null)
            {
                return "MainMenuClick";
            }

            if (go.GetComponent<OthelloButton>() != null)
            {
                return "OthelloButton";
            }

            if (go.GetComponent<ChatBox>() != null)
            {
                return "ChatBox";
            }

            return "none";
        }

        /// <summary>
        /// The core question set: what is driving input, and where is focus.
        /// </summary>
        private void Snapshot(string reason)
        {
            _snapshotCount++;
            Write("");
            Write("--- snapshot " + _snapshotCount + " (" + reason + ") scene=" + Application.loadedLevelName + " ---");

            var es = EventSystem.current;
            if (es == null)
            {
                Write("EventSystem.current: NONE  <- no event system at all");
                return;
            }

            Write("EventSystem: " + es.name);
            Write("  sendNavigationEvents : " + es.sendNavigationEvents);
            Write("  firstSelected        : " + Describe(es.firstSelectedGameObject));
            Write("  currentSelected      : " + Describe(es.currentSelectedGameObject));

            var module = es.currentInputModule;
            Write("  inputModule          : " + (module == null ? "NONE" : module.GetType().FullName));

            var standalone = module as StandaloneInputModule;
            if (standalone != null)
            {
                Write("    horizontalAxis : " + standalone.horizontalAxis);
                Write("    verticalAxis   : " + standalone.verticalAxis);
                Write("    submitButton   : " + standalone.submitButton);
                Write("    cancelButton   : " + standalone.cancelButton);
                Write("    actionsPerSec  : " + standalone.inputActionsPerSecond.ToString(CultureInfo.InvariantCulture));
            }

            // Are the axes actually wired in the project's input settings? A
            // missing axis throws rather than returning zero, and that alone
            // would explain navigation silently not working.
            ProbeAxis("Horizontal");
            ProbeAxis("Vertical");
            ProbeButton("Submit");
            ProbeButton("Cancel");

            // The controller is a second surface on the same event path: Unity
            // binds these axes to gamepad sticks and buttons by default. Not a
            // feature we build, just extra evidence the module is live.
            var joysticks = Input.GetJoystickNames();
            Write("  joysticks            : " + (joysticks.Length == 0
                ? "none connected"
                : string.Join(", ", joysticks)));

            SummariseSelectables();
        }

        private void ProbeAxis(string axis)
        {
            try
            {
                float raw = Input.GetAxisRaw(axis);
                Write("  axis " + axis + " : ok (raw=" + raw.ToString("0.##", CultureInfo.InvariantCulture) + ")");
            }
            catch (Exception ex)
            {
                Write("  axis " + axis + " : NOT DEFINED (" + ex.GetType().Name + ")");
            }
        }

        private void ProbeButton(string button)
        {
            try
            {
                bool down = Input.GetButton(button);
                Write("  button " + button + " : ok (down=" + down + ")");
            }
            catch (Exception ex)
            {
                Write("  button " + button + " : NOT DEFINED (" + ex.GetType().Name + ")");
            }
        }

        /// <summary>
        /// The decisive question for Phase 2: do these Selectables have usable
        /// navigation, or is it switched off?
        /// </summary>
        private void SummariseSelectables()
        {
            var all = Selectable.allSelectables;
            Write("  selectables          : " + all.Count);

            var byMode = new Dictionary<Navigation.Mode, int>();
            int interactable = 0;
            int active = 0;

            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null)
                {
                    continue;
                }

                if (s.IsInteractable())
                {
                    interactable++;
                }

                if (s.gameObject.activeInHierarchy)
                {
                    active++;
                }

                var mode = s.navigation.mode;
                byMode[mode] = byMode.TryGetValue(mode, out int n) ? n + 1 : 1;
            }

            Write("    interactable       : " + interactable);
            Write("    active in hierarchy: " + active);

            foreach (var pair in byMode)
            {
                Write("    navigation." + pair.Key + " : " + pair.Value);
            }
        }

        /// <summary>
        /// Every active selectable with its navigation neighbours. This is what
        /// tells us whether focus order would make sense to a blind player, and
        /// which elements (the dynamic result thumbnails especially) are missing.
        /// </summary>
        private void DumpSelectables()
        {
            Write("");
            Write("--- selectable dump: scene=" + Application.loadedLevelName + " ---");

            var all = Selectable.allSelectables;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var sb = new StringBuilder();
                sb.Append("  ").Append(HierarchyPath(s.gameObject));
                sb.Append("  [").Append(s.GetType().Name).Append("]");
                sb.Append(" nav=").Append(s.navigation.mode);
                sb.Append(" interactable=").Append(s.IsInteractable());

                if (s.navigation.mode != Navigation.Mode.None)
                {
                    sb.Append(" up=").Append(Name(s.FindSelectableOnUp()));
                    sb.Append(" down=").Append(Name(s.FindSelectableOnDown()));
                    sb.Append(" left=").Append(Name(s.FindSelectableOnLeft()));
                    sb.Append(" right=").Append(Name(s.FindSelectableOnRight()));
                }

                Write(sb.ToString());
            }
        }

        /// <summary>
        /// Moves selection to the next selectable by hand. Confirms whether
        /// SetSelectedGameObject actually takes, independently of whether the
        /// input module is routing move events.
        /// </summary>
        private void WalkSelection()
        {
            var es = EventSystem.current;
            if (es == null)
            {
                Write("[walk] no EventSystem");
                return;
            }

            var candidates = new List<Selectable>();
            var all = Selectable.allSelectables;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s != null && s.gameObject.activeInHierarchy && s.IsInteractable())
                {
                    candidates.Add(s);
                }
            }

            if (candidates.Count == 0)
            {
                Write("[walk] nothing interactable on this screen");
                Speech.Say("Nothing to focus here.", HhsTextType.Diagnostic);
                return;
            }

            int index = 0;
            var current = es.currentSelectedGameObject;
            if (current != null)
            {
                int found = candidates.FindIndex(c => c.gameObject == current);
                if (found >= 0)
                {
                    index = (found + 1) % candidates.Count;
                }
            }

            var target = candidates[index];
            es.SetSelectedGameObject(target.gameObject);

            string label = Label(target.gameObject);
            Write("[walk] " + (index + 1) + "/" + candidates.Count + " -> " + HierarchyPath(target.gameObject));
            Speech.Say(label + ", " + (index + 1) + " of " + candidates.Count, HhsTextType.Diagnostic);
        }

        /// <summary>
        /// A first pass at turning a GameObject into something speakable. Phase 2
        /// replaces this properly; here it just shows how much is recoverable.
        /// </summary>
        private static string Label(GameObject go)
        {
            if (go == null)
            {
                return "nothing";
            }

            var text = go.GetComponentInChildren<Text>();
            if (text != null && !string.IsNullOrEmpty(text.text))
            {
                return text.text.Trim();
            }

            var input = go.GetComponent<InputField>();
            if (input != null)
            {
                return string.IsNullOrEmpty(input.text) ? "text field, empty" : "text field, " + input.text;
            }

            return go.name;
        }

        private static string Describe(GameObject go)
        {
            return go == null ? "null" : HierarchyPath(go) + " [" + Label(go) + "]";
        }

        private static string Name(Selectable s)
        {
            return s == null ? "-" : s.gameObject.name;
        }

        private static string HierarchyPath(GameObject go)
        {
            if (go == null)
            {
                return "null";
            }

            var sb = new StringBuilder(go.name);
            var t = go.transform.parent;
            int guard = 0;
            while (t != null && guard++ < 12)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Every panel under a "Parent Panel" container, whether or not it holds
        /// a control, with its Selectable count and its text.
        ///
        /// The rest of this survey reports what the navigation keys can see,
        /// which is precisely why it could not answer the question that mattered
        /// for the clock and the database checker: a panel containing no
        /// Selectable is absent from every listing above, and absence read as
        /// "not open" rather than "open but unreachable".
        ///
        /// Selectables=0 is the signal. Such a panel needs its content spoken on
        /// open, because Tab will never take the player into it.
        /// </summary>
        private void WriteControllessPanels()
        {
            Write("");
            Write("  --- panels by container (controls or not) ---");

            var all = Resources.FindObjectsOfTypeAll<Transform>();
            var reachable = FocusWatcher.Focusables();
            int reported = 0;

            for (int i = 0; i < all.Length; i++)
            {
                var container = all[i];

                if (container == null || container.name != "Parent Panel"
                    || container.gameObject.hideFlags != HideFlags.None)
                {
                    continue;
                }

                for (int c = 0; c < container.childCount; c++)
                {
                    var panel = container.GetChild(c);
                    var selectables = panel.GetComponentsInChildren<Selectable>();

                    // Visible and reachable are different sets, and the gap is
                    // what decides how a panel gets announced: a scrollbar is
                    // visible but excluded from Focusables, so a panel whose
                    // only control is one has nothing to focus and must have its
                    // content spoken instead. Naming the controls is what makes
                    // that difference readable rather than inferred from counts.
                    int visible = 0;
                    var names = new System.Text.StringBuilder();

                    for (int s = 0; s < selectables.Length; s++)
                    {
                        var sel = selectables[s];
                        bool vis = FocusWatcher.IsVisible(sel);
                        if (vis)
                        {
                            visible++;
                        }

                        if (names.Length > 0)
                        {
                            names.Append(", ");
                        }

                        names.Append(sel.gameObject.name)
                             .Append('(').Append(sel.GetType().Name)
                             .Append(vis ? "" : ",hidden")
                             .Append(reachable.Contains(sel) ? "" : ",unreachable")
                             .Append(')');
                    }

                    // Trimmed hard: some panels hold a whole readme, and the
                    // point here is the shape of the panel, not its prose.
                    string text = PanelText(panel);
                    if (text.Length > 160)
                    {
                        text = text.Substring(0, 160) + "...";
                    }

                    Write("   " + panel.name
                          + "  active=" + panel.gameObject.activeInHierarchy
                          + "  selectables=" + selectables.Length
                          + " (visible " + visible + ")"
                          + "  controls=[" + names + "]"
                          + "  text=\"" + text + "\"");

                    reported++;
                }
            }

            Write("  panels reported: " + reported);
        }

        /// <summary>
        /// A panel's own text, flattened to one line for the log.
        /// </summary>
        private static string PanelText(Transform panel)
        {
            var texts = panel.GetComponentsInChildren<Text>();
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null || string.IsNullOrEmpty(texts[i].text))
                {
                    continue;
                }

                string t = texts[i].text.Replace("\r", " ").Replace("\n", " ").Trim();
                if (t.Length > 0)
                {
                    sb.Append(sb.Length > 0 ? " | " : string.Empty).Append(t);
                }
            }

            return sb.ToString();
        }

        private void Write(string line)
        {
            _log.Msg(line);
            try
            {
                File.AppendAllText(_reportPath, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // A read-only install directory shouldn't take the survey down;
                // the MelonLoader log still has everything.
            }
        }
    }
}
