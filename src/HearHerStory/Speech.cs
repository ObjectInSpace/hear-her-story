using System;
using MelonLoader;
using UnityAccessibilityLib;

namespace HearHerStory
{
    /// <summary>
    /// Bridges UnityAccessibilityLib's logging interface to MelonLoader's logger.
    /// </summary>
    internal sealed class MelonAccessibilityLogger : IAccessibilityLogger
    {
        private readonly MelonLogger.Instance _log;

        public MelonAccessibilityLogger(MelonLogger.Instance log)
        {
            _log = log;
        }

        public void Msg(string message)
        {
            _log.Msg(message);
        }

        public void Warning(string message)
        {
            _log.Warning(message);
        }

        public void Error(string message)
        {
            _log.Error(message);
        }
    }

    /// <summary>
    /// Text categories used by this mod. The library reserves everything below
    /// <see cref="TextType.CustomBase"/> for its own built-in types.
    /// </summary>
    internal static class HhsTextType
    {
        /// <summary>An element gaining focus: role, name, state, position in group.</summary>
        internal const int Focus = TextType.CustomBase + 1;

        /// <summary>The outcome of a database query.</summary>
        internal const int SearchResult = TextType.CustomBase + 2;

        /// <summary>A line of clip transcript, spoken during playback.</summary>
        internal const int Caption = TextType.CustomBase + 3;

        /// <summary>Diagnostic output from the Phase 1 survey.</summary>
        internal const int Diagnostic = TextType.CustomBase + 4;

        /// <summary>
        /// Typing echo from the search field: one utterance per keystroke.
        ///
        /// Distinct from <see cref="Focus"/> because it is a different kind of
        /// event — a character the player just produced, not an element they
        /// moved to. Separating them keeps the log readable while typing, and
        /// gives the two a separate handle should captions and echo ever need
        /// a priority policy between them.
        /// </summary>
        internal const int Echo = TextType.CustomBase + 5;
    }

    /// <summary>
    /// Speech output for the mod. Thin wrapper over the library's SpeechManager,
    /// which already handles duplicate suppression, the repeat buffer, and braille.
    /// </summary>
    internal static class Speech
    {
        private static bool _initialized;

        internal static bool IsAvailable
        {
            get { return _initialized; }
        }

        /// <summary>True when a real screen reader is driving output rather than the SAPI fallback.</summary>
        internal static bool UsingScreenReader { get; private set; }

        internal static bool Initialize(MelonLogger.Instance log)
        {
            if (_initialized)
            {
                return true;
            }

            AccessibilityLog.Logger = new MelonAccessibilityLogger(log);

            // Name our custom types so the library's own logging stays readable.
            SpeechManager.TextTypeNames = new System.Collections.Generic.Dictionary<int, string>
            {
                { HhsTextType.Focus, "Focus" },
                { HhsTextType.SearchResult, "SearchResult" },
                { HhsTextType.Caption, "Caption" },
                { HhsTextType.Diagnostic, "Diagnostic" },
                { HhsTextType.Echo, "Echo" },
            };

            // The mod keeps its own repeat buffer — see Remember — so nothing
            // needs to go into the library's. Its predicate cannot express what
            // is wanted here anyway: it stores by text type, which would sweep
            // up "No caption at this point" alongside the transcript, and it
            // never sees forced utterances at all.
            SpeechManager.ShouldStoreForRepeatPredicate = textType => false;

            try
            {
                _initialized = SpeechManager.Initialize();
            }
            catch (Exception ex)
            {
                // A missing or wrong-bitness UniversalSpeech.dll surfaces here.
                log.Error("Speech initialization threw: " + ex);
                _initialized = false;
            }

            if (_initialized)
            {
                try
                {
                    UsingScreenReader = UniversalSpeechWrapper.IsScreenReaderActive();
                }
                catch (Exception)
                {
                    UsingScreenReader = false;
                }

                // IsScreenReaderActive() is unreliable here: on Unity 5.0.1's Mono
                // it has been observed returning true while output went to the
                // SAPI fallback, and false while NVDA was audibly speaking. Log it
                // for reference but do not present it as fact, and report which
                // client libraries are actually on disk — that is the thing which
                // determines whether a reader can be reached at all.
                log.Msg("Speech ready.");
                log.Msg("  screen reader clients present: " + DescribeClients());
                log.Msg("  IsScreenReaderActive() reports: " + UsingScreenReader
                        + " (unreliable on this runtime; ignore if speech works)");
            }
            else
            {
                log.Error(
                    "Speech unavailable. Check that a 32-bit UniversalSpeech.dll sits next to HerStory.exe.");
            }

            return _initialized;
        }

        /// <summary>
        /// UniversalSpeech reaches each screen reader through that vendor's own
        /// client library, loaded from the executable's directory. A missing
        /// client means silent fallback to SAPI, which is easy to mistake for
        /// working speech — so list what is actually there.
        /// </summary>
        private static string DescribeClients()
        {
            string[] clients =
            {
                "nvdaControllerClient.dll",  // NVDA
                "SAAPI32.dll",               // System Access
                "jfwapi.dll",                // JAWS
                "dolapi32.dll",              // Dolphin
                "Tolk.dll",                  // Tolk, if used alongside
            };

            var found = new System.Collections.Generic.List<string>();
            string dir;

            try
            {
                dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetEntryAssembly() != null
                        ? System.Reflection.Assembly.GetEntryAssembly().Location
                        : UnityEngine.Application.dataPath);

                // Application.dataPath points at <game>_Data; the DLLs sit
                // beside the executable, one level up.
                if (!string.IsNullOrEmpty(dir) && dir.EndsWith("_Data"))
                {
                    dir = System.IO.Path.GetDirectoryName(dir);
                }
            }
            catch (Exception)
            {
                return "unknown (could not resolve game directory)";
            }

            if (string.IsNullOrEmpty(dir))
            {
                return "unknown (could not resolve game directory)";
            }

            foreach (var c in clients)
            {
                try
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(dir, c)))
                    {
                        found.Add(c);
                    }
                }
                catch (Exception)
                {
                    // Ignore and keep checking the rest.
                }
            }

            return found.Count == 0
                ? "NONE — speech will fall back to SAPI regardless of what is running"
                : string.Join(", ", found.ToArray());
        }

        internal static void Say(string text, int textType)
        {
            Say(text, textType, false);
        }

        /// <summary>
        /// Speaks text of the given category.
        ///
        /// Set <paramref name="force"/> when the player has explicitly asked to
        /// hear something again. SpeechManager drops text identical to the last
        /// utterance within its duplicate window — right for incidental
        /// repeats, wrong for a deliberate one — and it exposes no way to
        /// override that, so a forced repeat goes straight to the speech
        /// backend. It bypasses the repeat buffer too, which is correct: the
        /// buffer should still hold whatever was last worth re-hearing.
        /// </summary>
        internal static void Say(string text, int textType, bool force)
        {
            if (!_initialized || string.IsNullOrEmpty(text))
            {
                return;
            }

            string clean = TextCleaner.Clean(text);

            if (!force)
            {
                SpeechManager.Announce(clean, textType);
                return;
            }

            try
            {
                // Announce() is what normally logs, so a forced utterance would
                // otherwise be spoken aloud and leave no trace — which made
                // whole sessions look emptier than they were, and hid Ctrl+J,
                // Ctrl+R, Ctrl+Q, Ctrl+E and Ctrl+K from every log. Log it here
                // in the same shape Announce uses.
                if (AccessibilityLog.Logger != null)
                {
                    AccessibilityLog.Logger.Msg("[" + TypeName(textType) + "] " + clean);
                }

                UniversalSpeechWrapper.Speak(clean);

                if (SpeechManager.EnableBraille)
                {
                    UniversalSpeechWrapper.DisplayBraille(clean);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning("Forced repeat failed, falling back: " + ex.Message);
                SpeechManager.Announce(clean, textType);
            }
        }

        /// <summary>
        /// The log name for a text type, matching what SpeechManager prints for
        /// the non-forced path so both routes read alike in a log.
        /// </summary>
        private static string TypeName(int textType)
        {
            string name;

            return SpeechManager.TextTypeNames != null
                   && SpeechManager.TextTypeNames.TryGetValue(textType, out name)
                ? name
                : textType.ToString();
        }

        internal static void Announce(string text)
        {
            Say(text, TextType.System);
        }

        /// <summary>
        /// The last piece of readable text spoken — a transcript, a caption, the
        /// body of a readme. Kept here rather than left to the library's own
        /// buffer for two reasons: the library does not store forced utterances
        /// at all, which is exactly how a transcript read on demand reaches the
        /// player, and its buffer takes anything matching the predicate rather
        /// than only the text worth re-reading.
        /// </summary>
        private static string _lastReadable;

        /// <summary>
        /// Records text as re-readable. Call for content the player is reading,
        /// not for labels or status lines.
        /// </summary>
        internal static void Remember(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                _lastReadable = text;
            }
        }

        internal static void RepeatLast()
        {
            if (!_initialized)
            {
                return;
            }

            if (string.IsNullOrEmpty(_lastReadable))
            {
                Say("Nothing to repeat.", HhsTextType.Focus, true);
                return;
            }

            Say(_lastReadable, HhsTextType.Caption, true);
        }

        internal static void Stop()
        {
            if (_initialized)
            {
                SpeechManager.Stop();
            }
        }
    }
}
