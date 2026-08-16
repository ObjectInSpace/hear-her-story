using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace HearHerStory
{
    /// <summary>
    /// One row of the game's clip database, in the shape it takes at runtime.
    ///
    /// NewUIDatabase.Awake() parses the CSV TextAsset into an ArrayList of
    /// ArrayLists, and the runtime row is not the CSV row: Awake inserts a
    /// derived search-normalised field and a watched flag, so the indices below
    /// are the runtime ones and do not match the column numbers in the CSV.
    /// </summary>
    internal sealed class Clip
    {
        /// <summary>Index into dataUID. This is what ClickyBox.myIndex carries.</summary>
        internal int Index;

        /// <summary>Plain transcript of everything said in the clip.</summary>
        internal string Transcript = string.Empty;

        /// <summary>Runtime in seconds.</summary>
        internal int DurationSeconds;

        /// <summary>Whether the player has already watched this clip.</summary>
        internal bool Watched;

        /// <summary>The player's own tags for this clip, empty when untagged.</summary>
        internal string Tags = string.Empty;

        /// <summary>
        /// Timed captions: alternating text and end-time-in-seconds, as the game
        /// stores them. ClipDetail.Update() walks this same array during playback.
        /// </summary>
        internal string[] Subtitles = new string[0];

        /// <summary>"2 minutes 14 seconds", for speech rather than the game's "2 min 14 sec".</summary>
        internal string SpokenDuration
        {
            get
            {
                int minutes = DurationSeconds / 60;
                int seconds = DurationSeconds % 60;

                var sb = new StringBuilder();
                if (minutes > 0)
                {
                    sb.Append(minutes).Append(minutes == 1 ? " minute" : " minutes");
                }

                if (seconds > 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(' ');
                    }

                    sb.Append(seconds).Append(seconds == 1 ? " second" : " seconds");
                }

                return sb.Length == 0 ? "no length" : sb.ToString();
            }
        }

        /// <summary>
        /// The opening of the transcript, cut at a word boundary. This is what
        /// makes one search result distinguishable from another by ear, and it
        /// stands in for the thumbnail a sighted player sees.
        /// </summary>
        internal string Preview(int maxChars)
        {
            if (string.IsNullOrEmpty(Transcript))
            {
                return string.Empty;
            }

            string text = Transcript.Trim();
            if (text.Length <= maxChars)
            {
                return text;
            }

            int cut = text.LastIndexOf(' ', Math.Min(maxChars, text.Length - 1));
            if (cut < maxChars / 2)
            {
                cut = maxChars;
            }

            return text.Substring(0, cut).TrimEnd(',', '.', ';', ':', ' ') + "...";
        }
    }

    /// <summary>
    /// Reads the game's own clip database once and serves it to the rest of the
    /// mod. Everything spoken about a clip comes from here rather than from
    /// hardcoded strings, so labels stay true to the game's data.
    ///
    /// The database lives in a UnityScript class, so rows arrive as untyped
    /// ArrayLists. All extraction is defensive: a row that does not look the way
    /// we expect is skipped rather than taking the mod down.
    /// </summary>
    internal static class ClipLibrary
    {
        // Runtime field positions within a dataUID row. Derived from
        // NewUIDatabase.Awake(), and confirmed against DoTextBox() which reads
        // [2], [6], [8] and [9] to fill the clip detail panel.
        private const int FieldTranscript = 2;
        private const int FieldWatched = 4;
        private const int FieldClipIndex = 5;
        private const int FieldDuration = 6;
        private const int FieldTags = 7;
        private const int FieldSubtitles = 9;

        private static Clip[] _clips = new Clip[0];
        private static NewUIDatabase _database;

        internal static bool IsLoaded
        {
            get { return _clips.Length > 0; }
        }

        internal static int Count
        {
            get { return _clips.Length; }
        }

        /// <summary>The game's database component, once we have seen it.</summary>
        internal static NewUIDatabase Database
        {
            get { return _database; }
        }

        /// <summary>
        /// How many clips the player has watched. Read live rather than cached —
        /// the game flips the watched flag on the row as clips are played.
        /// </summary>
        internal static int WatchedCount
        {
            get
            {
                RefreshWatchedFlags();

                int n = 0;
                for (int i = 0; i < _clips.Length; i++)
                {
                    if (_clips[i].Watched)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        internal static Clip Get(int index)
        {
            if (index < 0 || index >= _clips.Length)
            {
                return null;
            }

            return _clips[index];
        }

        /// <summary>
        /// Loads from the live NewUIDatabase. Safe to call repeatedly; only the
        /// first successful load does work.
        /// </summary>
        internal static bool Load(NewUIDatabase database)
        {
            if (database == null)
            {
                return false;
            }

            _database = database;

            if (IsLoaded)
            {
                return true;
            }

            ArrayList rows = database.dataUID;
            if (rows == null || rows.Count == 0)
            {
                // Awake() has not run yet, or the TextAsset was missing.
                return false;
            }

            var clips = new System.Collections.Generic.List<Clip>(rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i] as ArrayList;
                if (row == null || row.Count <= FieldSubtitles)
                {
                    continue;
                }

                var clip = new Clip
                {
                    Index = AsInt(row[FieldClipIndex], i),
                    Transcript = AsString(row[FieldTranscript]),
                    DurationSeconds = AsInt(row[FieldDuration], 0),
                    Watched = AsBool(row[FieldWatched]),
                    Tags = AsString(row[FieldTags]),
                    Subtitles = SplitSubtitles(AsString(row[FieldSubtitles])),
                };

                // "BLANK" is the game's placeholder for an untagged clip.
                if (clip.Tags == "BLANK")
                {
                    clip.Tags = string.Empty;
                }

                clips.Add(clip);
            }

            _clips = clips.ToArray();

            if (Plugin.Log != null)
            {
                Plugin.Log.Msg("ClipLibrary: " + _clips.Length + " clips loaded from the game's database.");
            }

            return IsLoaded;
        }

        /// <summary>
        /// Tries to find the database component in the scene. The mod has no
        /// reference to it until the game has built the main window.
        /// </summary>
        internal static bool TryLoadFromScene()
        {
            if (IsLoaded)
            {
                return true;
            }

            var database = UnityEngine.Object.FindObjectOfType<NewUIDatabase>();
            return database != null && Load(database);
        }

        /// <summary>
        /// The game mutates the watched flag in place on its own rows, so our
        /// copies go stale. Re-read just that field rather than reloading.
        /// </summary>
        internal static void RefreshWatchedFlags()
        {
            if (_database == null || _database.dataUID == null)
            {
                return;
            }

            ArrayList rows = _database.dataUID;
            int limit = Math.Min(rows.Count, _clips.Length);

            for (int i = 0; i < limit; i++)
            {
                var row = rows[i] as ArrayList;
                if (row != null && row.Count > FieldTags)
                {
                    _clips[i].Watched = AsBool(row[FieldWatched]);

                    string tags = AsString(row[FieldTags]);
                    _clips[i].Tags = tags == "BLANK" ? string.Empty : tags;
                }
            }
        }

        /// <summary>
        /// The game appends its own terminator before splitting; we do not, so a
        /// caption array here is exactly the pairs the data carries.
        /// </summary>
        private static string[] SplitSubtitles(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return new string[0];
            }

            return raw.Split('%');
        }

        private static string AsString(object value)
        {
            return value == null ? string.Empty : value.ToString().Trim();
        }

        private static int AsInt(object value, int fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            if (value is int)
            {
                return (int)value;
            }

            int parsed;
            if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static bool AsBool(object value)
        {
            if (value is bool)
            {
                return (bool)value;
            }

            return value != null && string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
