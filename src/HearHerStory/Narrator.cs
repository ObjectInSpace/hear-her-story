using System;
using System.Text;

namespace HearHerStory
{
    /// <summary>
    /// Composes everything the mod says that is not a plain focus label. Kept in
    /// one place so phrasing stays consistent and tunable, and so the
    /// interrupt-versus-append policy is decided centrally rather than at each
    /// call site.
    /// </summary>
    internal static class Narrator
    {
        /// <summary>
        /// The outcome of a query. The game writes its own summary text — "14
        /// entries found. ACCESS LIMITED TO FIRST 5 ENTRIES." — which is already
        /// the right information; it is simply never spoken, and it shouts.
        /// </summary>
        internal static void AnnounceSearchResults(string gameSummary, int reachableResults)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(gameSummary))
            {
                sb.Append(Soften(gameSummary));
            }
            else if (reachableResults > 0)
            {
                sb.Append(reachableResults).Append(reachableResults == 1 ? " entry found." : " entries found.");
            }
            else
            {
                sb.Append("No entries found.");
            }

            if (reachableResults > 0)
            {
                sb.Append(" Press down arrow to reach the results.");
            }
            else
            {
                // A dead end is the one case where the player needs to know what
                // the game actually searched for, rather than what they typed.
                string note = NormalisationNote();
                if (!string.IsNullOrEmpty(note))
                {
                    sb.Append(' ').Append(note);
                }
            }

            Speech.Say(sb.ToString(), HhsTextType.SearchResult);
        }

        /// <summary>
        /// Explains the gap between what was typed and what was searched, when
        /// there is one.
        ///
        /// The game normalises before matching: <c>CheckAmerican()</c> lower-cases
        /// and rewrites US spellings to UK ones, and a query wrapped in double
        /// quotes switches from substring matching to exact-phrase matching. A
        /// sighted player never needs this — but on a zero-result search a blind
        /// player cannot tell a typo from a genuinely absent word, and "colour"
        /// coming back empty after typing "color" is indistinguishable from the
        /// word simply not being in the archive. Naming the normalised form turns
        /// a dead end into information.
        /// </summary>
        /// <remarks>
        /// The normalised string exists only as a local inside the RunQuery
        /// coroutine, so it cannot be read back off the database. Recomputing it
        /// through the game's own public CheckAmerican is what keeps this
        /// truthful: if the spelling tables change, this follows them.
        /// </remarks>
        private static string NormalisationNote()
        {
            var database = ClipLibrary.Database;
            if (database == null || database.myTextField == null)
            {
                return string.Empty;
            }

            string typed = database.myTextField.text;
            if (string.IsNullOrEmpty(typed))
            {
                return string.Empty;
            }

            string normalised;
            try
            {
                normalised = database.CheckAmerican(typed);
            }
            catch (Exception)
            {
                // Never let a narration nicety break the announcement itself.
                return string.Empty;
            }

            if (string.IsNullOrEmpty(normalised))
            {
                return string.Empty;
            }

            bool exact = normalised.StartsWith("\"") && normalised.EndsWith("\"") && normalised.Length > 1;

            var sb = new StringBuilder();

            // Case alone is not worth reporting: it changes nothing the player
            // can act on, and it would fire on almost every search.
            if (!string.Equals(normalised, typed, StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("Searched for ").Append(normalised.Replace("\"", string.Empty)).Append('.');
            }

            if (exact)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append("Exact phrase match, because the query is quoted.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// The clip detail panel opening. The player has chosen a clip and wants
        /// to know what they got.
        /// </summary>
        internal static void AnnounceClipOpened(int clipIndex)
        {
            // The game is about to move focus onto the panel's own internals.
            // Those moves are not the player's and must not talk over what we
            // are about to say about the clip.
            FocusWatcher.SuppressBriefly();

            var clip = ClipLibrary.Get(clipIndex);
            if (clip == null)
            {
                Speech.Say("Clip opened.", HhsTextType.SearchResult);
                return;
            }

            ClipLibrary.RefreshWatchedFlags();

            var sb = new StringBuilder();
            sb.Append("Clip opened, ").Append(clip.SpokenDuration);

            if (clip.Watched)
            {
                sb.Append(", already watched");
            }

            if (!string.IsNullOrEmpty(clip.Tags))
            {
                sb.Append(", tagged ").Append(clip.Tags);
            }

            sb.Append(". ").Append(clip.Preview(140));

            Speech.Say(sb.ToString(), HhsTextType.SearchResult);
        }

        /// <summary>
        /// Progress through the archive. There is no win condition in this game,
        /// so this is orientation rather than score.
        /// </summary>
        internal static void AnnounceProgress()
        {
            Speech.Say(ProgressSentence(), HhsTextType.SearchResult);
        }

        /// <summary>
        /// What the desktop clock is showing, as one sentence.
        ///
        /// The clock is a face, not a readout: ClockManager rotates three hand
        /// Images and writes the time nowhere. Its one Text is set once in Start
        /// to a fixed date string, so reading the panel's prose gives a date the
        /// player did not ask for and no clock at all. The time the hands are
        /// drawn from is DateTime.Now, so that is what gets spoken.
        ///
        /// The hour hand is hardcoded to 7 o'clock in ClockManager — set
        /// dressing, not a bug to route around — so only minutes and seconds
        /// track real time. Reporting the true hour would tell a blind player
        /// something a sighted player cannot see, so the face is described as
        /// drawn.
        /// </summary>
        internal static string ClockSentence()
        {
            var now = DateTime.Now;

            return "Seven o'clock. Minute hand at " + now.Minute
                   + ", second hand at " + now.Second + ".";
        }

        /// <summary>
        /// How far through the archive the player is, as one sentence.
        ///
        /// Shared with the database checker panel, which draws this same number
        /// as a mosaic of blocks. One sentence, one source: a panel that
        /// disagreed with Ctrl+P would leave the player with no way to tell
        /// which had lied.
        /// </summary>
        internal static string ProgressSentence()
        {
            if (!ClipLibrary.IsLoaded)
            {
                return "The clip database is not loaded yet.";
            }

            int watched = ClipLibrary.WatchedCount;
            int total = ClipLibrary.Count;

            return "You have watched " + watched + " of " + total + " clips.";
        }

        /// <summary>
        /// Reads a focused or open clip's transcript in full — the single most
        /// useful affordance in a game built on re-reading testimony.
        /// </summary>
        internal static void ReadTranscript(int clipIndex)
        {
            var clip = ClipLibrary.Get(clipIndex);

            if (clip == null || string.IsNullOrEmpty(clip.Transcript))
            {
                Speech.Say("No transcript available for this clip.", HhsTextType.Caption);
                return;
            }

            Speech.Remember(clip.Transcript);
            Speech.Say(clip.Transcript, HhsTextType.Caption);
        }

        /// <summary>
        /// The game's on-screen text is upper-cased for the 1994 terminal look
        /// — "ACCESS LIMITED TO FIRST 5 ENTRIES." Screen readers often spell out
        /// or over-emphasise all-caps runs, so bring shouted text back to
        /// ordinary sentence case.
        ///
        /// A run of shouted words is treated as one sentence rather than word by
        /// word: capitalising each of them individually produces Title Case,
        /// which reads as emphasis of its own. Short connecting words inside a
        /// run are softened along with it, so "TO" does not survive a rule meant
        /// to protect two-letter acronyms.
        /// </summary>
        internal static string Soften(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var words = text.Split(' ');

            // Find each maximal run of consecutive shouted words. A single
            // upper-case word among lower-case ones is an acronym — "FBI",
            // "ADMIN" — and is left alone. Two or more in a row is the game
            // shouting, and gets brought down to sentence case.
            int i = 0;
            while (i < words.Length)
            {
                if (!IsShouted(words[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                int end = i;

                // Numbers and bare punctuation carry no case, so they sit inside
                // a run without breaking it — "FIRST 5 ENTRIES" is one shout.
                // Only a genuinely lower-case word ends the run.
                int lookahead = i;
                while (lookahead + 1 < words.Length)
                {
                    string next = words[lookahead + 1];

                    if (IsShouted(next))
                    {
                        lookahead++;
                        end = lookahead;
                        continue;
                    }

                    if (!HasLetter(next))
                    {
                        lookahead++;
                        continue;
                    }

                    break;
                }

                if (end > start)
                {
                    // The run opens a sentence, so only its first word keeps a
                    // capital; capitalising each one would produce Title Case,
                    // which reads as emphasis of its own.
                    words[start] = words[start].Substring(0, 1)
                                 + words[start].Substring(1).ToLowerInvariant();

                    for (int j = start + 1; j <= end; j++)
                    {
                        if (IsShouted(words[j]))
                        {
                            words[j] = words[j].ToLowerInvariant();
                        }
                    }
                }

                i = end + 1;
            }

            return string.Join(" ", words);
        }

        private static bool HasLetter(string word)
        {
            for (int i = 0; i < word.Length; i++)
            {
                if (char.IsLetter(word[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsShouted(string word)
        {
            bool hasLetter = false;

            for (int i = 0; i < word.Length; i++)
            {
                char c = word[i];

                if (char.IsLetter(c))
                {
                    hasLetter = true;

                    if (!char.IsUpper(c))
                    {
                        return false;
                    }
                }
            }

            return hasLetter;
        }
    }
}
