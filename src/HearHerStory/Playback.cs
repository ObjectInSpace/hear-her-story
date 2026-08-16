using System;
using System.Text;
using UnityEngine;

namespace HearHerStory
{
    /// <summary>
    /// Speech around clip playback: what started, what is being said, and what
    /// ended.
    ///
    /// The clips are speech, so the audio mostly carries itself — this is not a
    /// substitute for the video, it is the frame around it. Two things a sighted
    /// player gets for free need saying: that playback has begun (and what it is),
    /// and that it has stopped, which of the two ways it stopped, and where the
    /// player has landed.
    ///
    /// Spoken captions are the optional third piece, off by default. They are for
    /// deafblind players reading on a braille display, and for anyone who missed a
    /// line — but spoken aloud over a clip that is itself speech, they are
    /// unusable, so nothing turns them on but the player.
    /// </summary>
    internal static class Playback
    {
        /// <summary>
        /// Whether captions are spoken as the clip plays. Off by default: over
        /// audible dialogue this is two voices at once, and only the player knows
        /// whether that is what they want.
        /// </summary>
        internal static bool CaptionsEnabled { get; private set; }

        /// <summary>
        /// The caption cursor as of the last frame we looked. The game advances
        /// <c>ClipDetail.subtitlePosition</c> in its own Update; we watch that
        /// value rather than timing anything ourselves, so a caption is spoken
        /// exactly when the game displays it — including after a scrub, which
        /// resets the cursor and which no independent clock would follow.
        /// </summary>
        private static int _lastSubtitlePosition = -1;

        /// <summary>The clip currently playing, or -1. Identifies a restart.</summary>
        private static int _playingIndex = -1;

        internal static void ToggleCaptions()
        {
            CaptionsEnabled = !CaptionsEnabled;

            Speech.Say(
                CaptionsEnabled
                    ? "Spoken captions on."
                    : "Spoken captions off.",
                HhsTextType.SearchResult,
                true);
        }

        /// <summary>
        /// Playback has begun. Called from the patch on the game's own play path,
        /// so this fires for every route into a clip.
        /// </summary>
        internal static void Started(ClipDetail detail)
        {
            if (detail == null)
            {
                return;
            }

            _playingIndex = detail.myDBIndex;
            _lastSubtitlePosition = -1;

            // Starting playback moves focus onto the movie texture, which is
            // the game's doing rather than the player's.
            FocusWatcher.SuppressBriefly();

            var clip = ClipLibrary.Get(detail.myDBIndex);

            var sb = new StringBuilder();
            sb.Append("Playing");

            if (clip != null)
            {
                sb.Append(", ").Append(clip.SpokenDuration);
            }

            sb.Append('.');

            Speech.Say(sb.ToString(), HhsTextType.SearchResult);
        }

        /// <summary>
        /// Playback has ended.
        /// </summary>
        /// <param name="cancelled">
        /// True when the player stopped the clip, false when it ran to the end.
        /// The game scores these differently — <c>PlayerCancels</c> awards half
        /// credit — and the distinction matters to the player too: one is a
        /// choice, the other is the clip finishing.
        /// </param>
        internal static void Ended(bool cancelled)
        {
            // AbortTheVideo is the common exit for both paths and also runs when
            // nothing was playing. Only speak if we believed a clip was running.
            if (_playingIndex < 0)
            {
                return;
            }

            _playingIndex = -1;
            _lastSubtitlePosition = -1;

            Speech.Say(cancelled ? "Clip stopped." : "Clip ended.", HhsTextType.SearchResult);
        }

        /// <summary>
        /// Stops a playing clip on the player's behalf.
        ///
        /// Routed through <c>PlayerCancels</c> rather than <c>AbortTheVideo</c>
        /// deliberately: cancelling is what the game's own stop control does, and
        /// it carries a scoring consequence. Reaching past it to the lower-level
        /// abort would let a keyboard player quietly score differently from a
        /// mouse player doing the same thing.
        /// </summary>
        internal static bool StopIfPlaying()
        {
            var detail = CurrentDetail();

            if (detail == null || !detail.moviePlaying)
            {
                return false;
            }

            detail.PlayerCancels();
            return true;
        }

        /// <summary>True while a clip is actually running.</summary>
        internal static bool IsPlaying()
        {
            var detail = CurrentDetail();
            return detail != null && detail.moviePlaying;
        }

        /// <summary>
        /// True while the clip detail panel is actually on screen, playing or
        /// not.
        ///
        /// Wider than <see cref="IsPlaying"/> on purpose: the game moves focus
        /// onto its own internals as the panel opens, before playback starts,
        /// so a check on <c>moviePlaying</c> alone misses the first of those
        /// moves.
        ///
        /// The <c>activeInHierarchy</c> test is the whole of it.
        /// <see cref="CurrentDetail"/> returns a serialised field that points at
        /// the panel component for the entire run, so it is non-null whether the
        /// panel is showing or not — every other caller pairs it with
        /// <c>moviePlaying</c> and so never noticed.
        /// </summary>
        internal static bool IsDetailOpen()
        {
            var detail = CurrentDetail();
            return detail != null && detail.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// Speaks each caption as the game reaches it. Driven from the mod's own
        /// Update, but the decision of *which* caption is live is entirely the
        /// game's — we only notice the cursor moving.
        /// </summary>
        internal static void Tick()
        {
            if (!CaptionsEnabled)
            {
                return;
            }

            var detail = CurrentDetail();

            if (detail == null || !detail.moviePlaying || !detail.movieLoaded)
            {
                return;
            }

            int position = detail.subtitlePosition;

            if (position == _lastSubtitlePosition)
            {
                return;
            }

            _lastSubtitlePosition = position;

            string line = CaptionAt(detail, position);

            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            // Captions interrupt each other: a caption the player has moved past
            // is worse than useless, and the next line is already due.
            Speech.Remember(line);
            Speech.Say(line, HhsTextType.Caption);
        }

        /// <summary>
        /// The caption text at a cursor position.
        ///
        /// <c>subtitleArray</c> alternates text and end-time-in-seconds, so the
        /// cursor always lands on a text slot and advances by two. The game
        /// appends a "% %9999" sentinel before splitting, which gives the walk in
        /// its Update a final entry that no playback position can pass — reading
        /// that back would speak "9999", so the sentinel is skipped here.
        /// </summary>
        private static string CaptionAt(ClipDetail detail, int position)
        {
            var array = detail.subtitleArray;

            if (array == null || position < 0 || position >= array.Length)
            {
                return string.Empty;
            }

            string line = array[position];

            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }

            line = line.Trim();

            // The sentinel's text slot is a single space, which trims to nothing;
            // this is belt and braces against the terminator being read aloud.
            return line == "9999" ? string.Empty : line;
        }

        /// <summary>
        /// The clip detail panel, or null when no clip is open.
        /// </summary>
        private static ClipDetail CurrentDetail()
        {
            var database = ClipLibrary.Database;

            if (database == null || database.textBoxParent == null)
            {
                return null;
            }

            return database.textBoxParent;
        }

        /// <summary>
        /// Re-reads the caption currently on screen, whether or not spoken
        /// captions are switched on. This is the "I missed that line" key, and it
        /// is useful precisely to players who do not want continuous captions.
        /// </summary>
        internal static void RepeatCurrentCaption()
        {
            var detail = CurrentDetail();

            if (detail == null || !detail.moviePlaying)
            {
                Speech.Say("No clip is playing.", HhsTextType.Caption);
                return;
            }

            string line = CaptionAt(detail, detail.subtitlePosition);

            Speech.Say(
                string.IsNullOrEmpty(line) ? "No caption at this point." : line,
                HhsTextType.Caption,
                true);
        }

        /// <summary>
        /// Forgets any playback state. Called when the detail panel closes, so a
        /// stale index cannot make the next clip's first caption look unchanged.
        /// </summary>
        internal static void Reset()
        {
            _playingIndex = -1;
            _lastSubtitlePosition = -1;
        }
    }
}
