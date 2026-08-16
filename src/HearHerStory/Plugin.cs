using System;
using MelonLoader;

[assembly: MelonInfo(typeof(HearHerStory.Plugin), "Hear Her Story", "0.4.0", "amock")]
[assembly: MelonGame("Sam Barlow", "HerStory")]

namespace HearHerStory
{
    public class Plugin : MelonMod
    {
        internal static MelonLogger.Instance Log { get; private set; }

        /// <summary>
        /// The Phase 1 survey stays available, switched on with --hhs-survey on
        /// the command line. It costs nothing when off, and it is how any future
        /// "why is focus doing that" question gets answered against the running
        /// game rather than by guessing.
        /// </summary>
        private static bool SurveyRequested()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, "--hhs-survey", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Log.Msg("Hear Her Story 0.4.0 starting.");

            bool speechReady = Speech.Initialize(Log);

            FocusWatcher.Create(Log);
            KeyBindings.Create(Log);

            // The activation audit answers a question that is still open — which
            // focusable controls actually do anything — so the survey host runs
            // unconditionally for now. The other survey keys stay behind the
            // flag by only reporting when asked.
            var survey = InputSurvey.Create(Log);
            Log.Msg("  F8 audits which focusable controls can be activated.");

            if (SurveyRequested())
            {
                survey.Verbose = true;
                Log.Msg("Survey enabled: F9 snapshot, F10 walk, F11 dump, F12 repeat.");
            }

            if (speechReady)
            {
                Speech.Announce("Hear Her Story loaded.");
            }

            VerifyPatches();

            Log.Msg("Focus, labels, search and playback active.");
            Log.Msg("  Arrows move within a group, Tab between groups, Ctrl+Tab between windows.");
            Log.Msg("  Backtick repeats the last announcement.");
            Log.Msg("  Ctrl+T transcript, Ctrl+P progress, Ctrl+D favourite.");
            Log.Msg("  Esc stop clip, Ctrl+J toggle spoken captions, Ctrl+K repeat caption.");
        }

        /// <summary>
        /// Confirms every patch actually attached.
        ///
        /// Harmony reports a missing target by throwing during PatchAll, which
        /// MelonLoader catches and logs — but a patch that attaches to the wrong
        /// overload, or a game method that turns out not to be virtual where we
        /// assumed, fails quietly and shows up only as a feature that never
        /// fires. Checking the patch registry turns that into a startup error.
        /// </summary>
        private static void VerifyPatches()
        {
            // Paired with their type so a missing target can name itself. The
            // list spans two classes now, and "something on NewUIDatabase is
            // gone" is not a diagnosis anyone can act on.
            var expected = new[]
            {
                new { Type = typeof(NewUIDatabase), Name = "PopulateDatabaseList" },
                new { Type = typeof(NewUIDatabase), Name = "DoTextBox" },
                new { Type = typeof(NewUIDatabase), Name = "Awake" },
                new { Type = typeof(NewUIDatabase), Name = "ShutDetailText" },
                new { Type = typeof(NewUIDatabase), Name = "DrawHistoryList" },
                new { Type = typeof(NewUIDatabase), Name = "FillFavorites" },
                new { Type = typeof(ClipDetail), Name = "NowPlayTheVideo" },
                new { Type = typeof(ClipDetail), Name = "AbortTheVideo" },
            };

            foreach (var target in expected)
            {
                string full = target.Type.Name + "." + target.Name;
                var method = target.Type.GetMethod(target.Name);

                if (method == null)
                {
                    Log.Error("Patch target no longer exists: " + full
                              + ". The feature depending on it will be silent.");
                    continue;
                }

                var info = HarmonyLib.Harmony.GetPatchInfo(method);
                bool patched = info != null && info.Postfixes != null && info.Postfixes.Count > 0;

                if (!patched)
                {
                    Log.Error("Patch did not attach: " + full
                              + ". The feature depending on it will be silent.");
                }
            }

            // PlayerCancels is bracketed rather than observed: without it, a clip
            // the player stops is announced as though it had run to the end.
            var cancels = typeof(ClipDetail).GetMethod("PlayerCancels");
            if (cancels == null)
            {
                Log.Error("ClipDetail.PlayerCancels not found. Stopped clips will announce as finished.");
            }
            else
            {
                var cancelInfo = HarmonyLib.Harmony.GetPatchInfo(cancels);
                bool bracketed = cancelInfo != null
                                 && cancelInfo.Prefixes != null && cancelInfo.Prefixes.Count > 0;

                if (!bracketed)
                {
                    Log.Error("Patch did not attach: ClipDetail.PlayerCancels. "
                              + "Stopped clips will announce as finished.");
                }
            }

            // InputField.KeyPressed is protected, so it is resolved by name rather
            // than by signature and cannot fail at compile time — which makes it
            // the patch most likely to go quietly missing if the UI assembly ever
            // differs from the one this was built against. Without it the search
            // box types silently, which is the core of Phase 3.
            var keyPressed = HarmonyLib.AccessTools.Method(typeof(UnityEngine.UI.InputField), "KeyPressed");
            if (keyPressed == null)
            {
                Log.Error("InputField.KeyPressed not found. The search box will not echo as you type.");
            }
            else
            {
                var keyInfo = HarmonyLib.Harmony.GetPatchInfo(keyPressed);
                bool echoing = keyInfo != null && keyInfo.Postfixes != null && keyInfo.Postfixes.Count > 0;

                if (!echoing)
                {
                    Log.Error("Patch did not attach: InputField.KeyPressed. Typing will be silent.");
                }
            }

            // SaveLoad.Load(string) is bracketed rather than observed, so it is
            // checked separately — if this one fails to attach, a returning
            // player hears their last clip announced before the title screen.
            var load = typeof(SaveLoad).GetMethod("Load", new[] { typeof(string) });
            if (load == null)
            {
                Log.Error("SaveLoad.Load(string) not found. Save restores will announce spuriously.");
            }
            else
            {
                var loadInfo = HarmonyLib.Harmony.GetPatchInfo(load);
                bool bracketed = loadInfo != null
                                 && loadInfo.Prefixes != null && loadInfo.Prefixes.Count > 0;

                if (!bracketed)
                {
                    Log.Error("Patch did not attach: SaveLoad.Load. Save restores will announce spuriously.");
                }
            }
        }

        public override void OnApplicationQuit()
        {
            try
            {
                Speech.Stop();
            }
            catch (Exception)
            {
                // Shutdown ordering during quit is unreliable; never throw here.
            }
        }
    }
}
