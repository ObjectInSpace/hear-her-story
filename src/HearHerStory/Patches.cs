using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HearHerStory
{
    /// <summary>
    /// Makes the search-result thumbnails reachable by keyboard.
    ///
    /// This is the gap Phase 1 predicted and decompilation confirmed:
    /// PopulateDatabaseList() instantiates each result from imageBoxPrefab and
    /// attaches a ClickyBox, which handles mouse clicks only. There is no
    /// Button, no Selectable, and therefore no way for Unity's navigation to
    /// reach a result at all — the single largest hole in the game's
    /// keyboard reachability.
    ///
    /// Patching after the method runs is enough: the tiles exist by then, and
    /// their clip index is already assigned.
    /// </summary>
    [HarmonyPatch(typeof(NewUIDatabase), "PopulateDatabaseList")]
    internal static class PopulateDatabaseListPatch
    {
        private static void Postfix(NewUIDatabase __instance)
        {
            try
            {
                ClipLibrary.Load(__instance);

                int made = MakeResultsFocusable(__instance);

                // Announce the outcome of the query. summaryText is what a
                // sighted player reads, and the game has just written the
                // entry count and the access cap into it.
                string summary = __instance.summaryText != null
                    ? __instance.summaryText.text
                    : string.Empty;

                Narrator.AnnounceSearchResults(summary, made);
            }
            catch (Exception ex)
            {
                // A throwing patch would break the game's core loop. Log and
                // let the query finish unannounced.
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("PopulateDatabaseList patch failed: " + ex);
                }
            }
        }

        /// <summary>
        /// Adds a Selectable to each freshly-built result tile and links them
        /// left-to-right, which is how they are laid out. Returns how many
        /// results are now reachable.
        /// </summary>
        private static int MakeResultsFocusable(NewUIDatabase database)
        {
            if (database.panelParent == null)
            {
                return 0;
            }

            var panel = database.panelParent.transform;
            var tiles = new System.Collections.Generic.List<Selectable>();

            for (int i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                if (child.GetComponent<ClickyBox>() == null)
                {
                    continue;
                }

                tiles.Add(EnsureSelectable(child.gameObject));
            }

            // Explicit horizontal links. The tiles sit in a scrolling row, and
            // geometric navigation is unreliable across a viewport that clips
            // most of them out of view.
            for (int i = 0; i < tiles.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };

                if (i > 0)
                {
                    nav.selectOnLeft = tiles[i - 1];
                }

                if (i < tiles.Count - 1)
                {
                    nav.selectOnRight = tiles[i + 1];
                }

                // Up from a result returns to the search box, which is the
                // element the player came from and will want next.
                if (database.myTextField != null)
                {
                    nav.selectOnUp = database.myTextField;
                }

                tiles[i].navigation = nav;
            }

            // Down from the search box reaches the first result, completing the
            // round trip. Set here because the results only now exist.
            if (database.myTextField != null && tiles.Count > 0)
            {
                var searchNav = database.myTextField.navigation;
                searchNav.mode = Navigation.Mode.Explicit;
                searchNav.selectOnDown = tiles[0];
                database.myTextField.navigation = searchNav;
            }

            return tiles.Count;
        }

        /// <summary>
        /// Gives a tile the minimum needed to be focusable. A bare Selectable
        /// rather than a Button: the game routes clicks through ClickyBox, and
        /// adding a Button here would create a second, empty activation path.
        /// </summary>
        private static Selectable EnsureSelectable(GameObject tile)
        {
            var existing = tile.GetComponent<Selectable>();
            if (existing != null)
            {
                return existing;
            }

            var selectable = tile.AddComponent<Selectable>();

            // The tile's Image is already the visual; letting Unity tint it on
            // focus gives sighted and partially-sighted players a focus
            // indicator the game otherwise lacks entirely.
            var image = tile.GetComponent<Image>();
            if (image != null)
            {
                selectable.targetGraphic = image;
            }

            selectable.transition = Selectable.Transition.ColorTint;

            return selectable;
        }
    }

    /// <summary>
    /// Makes the search-history entries reachable by keyboard.
    ///
    /// The same gap as the result thumbnails, in a different panel: HistoryItem
    /// is a bare MonoBehaviour with a rawSearch string and a ClickHistoryItem()
    /// mouse handler, and historyItemPrefab is a Text — so there is no Button,
    /// no Selectable, and no keyboard route to a past query.
    ///
    /// This matters more here than the component list suggests. Her Story has no
    /// objective list and no map; the queries you have already tried *are* the
    /// state of the investigation. A sighted player keeps that in the sidebar,
    /// and without this a blind player has to hold every one of them in memory
    /// across a session.
    /// </summary>
    [HarmonyPatch(typeof(NewUIDatabase), "DrawHistoryList")]
    internal static class DrawHistoryListPatch
    {
        private static void Postfix(NewUIDatabase __instance)
        {
            try
            {
                MakeHistoryFocusable(__instance);
            }
            catch (Exception ex)
            {
                // History is an aid, not the core loop: never let it throw into
                // the game's own redraw.
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("DrawHistoryList patch failed: " + ex);
                }
            }
        }

        /// <summary>
        /// Adds a Selectable per history entry and links them vertically, which
        /// is how the list is laid out — unlike the results row, which links
        /// left to right.
        /// </summary>
        private static int MakeHistoryFocusable(NewUIDatabase database)
        {
            if (database.historyParent == null)
            {
                return 0;
            }

            var parent = database.historyParent.transform;
            var items = new System.Collections.Generic.List<Selectable>();

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.GetComponent<HistoryItem>() == null)
                {
                    continue;
                }

                items.Add(EnsureSelectable(child.gameObject));
            }

            for (int i = 0; i < items.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };

                if (i > 0)
                {
                    nav.selectOnUp = items[i - 1];
                }

                if (i < items.Count - 1)
                {
                    nav.selectOnDown = items[i + 1];
                }

                items[i].navigation = nav;
            }

            return items.Count;
        }

        /// <summary>
        /// A bare Selectable, for the same reason as the result tiles: the game
        /// activates through ClickHistoryItem(), so a Button would add a second
        /// activation path that does nothing.
        ///
        /// The prefab is a Text rather than an Image, so the tint target is the
        /// label itself — which is also the only graphic an entry has.
        /// </summary>
        private static Selectable EnsureSelectable(GameObject item)
        {
            var existing = item.GetComponent<Selectable>();
            if (existing != null)
            {
                return existing;
            }

            var selectable = item.AddComponent<Selectable>();

            var graphic = item.GetComponent<Graphic>();
            if (graphic != null)
            {
                selectable.targetGraphic = graphic;
            }

            selectable.transition = Selectable.Transition.ColorTint;

            return selectable;
        }
    }

    /// <summary>
    /// Makes the favourites panel reachable by keyboard.
    ///
    /// The third instance of the same gap. <c>FillFavorites()</c> builds the
    /// panel from <c>favedClips</c> using <c>favedImageBoxPrefab</c>, in the
    /// same mouse-only shape as the search results — so the clips a player has
    /// deliberately flagged as mattering are the ones they cannot reach.
    ///
    /// Favourites are the closest thing this game has to a notebook: there is no
    /// objective list, so a flagged clip is the player saying "come back to
    /// this". Tiles carry a ClickyBox exactly as the result tiles do, so both
    /// the labelling and the activation path already work once a Selectable
    /// exists.
    /// </summary>
    [HarmonyPatch(typeof(NewUIDatabase), "FillFavorites")]
    internal static class FillFavoritesPatch
    {
        // FillFavorites returns bool, not void. The postfix does not read or
        // change it, but the signature has to leave it alone rather than
        // declare void, or Harmony rejects the patch.
        private static void Postfix(NewUIDatabase __instance)
        {
            try
            {
                MakeFavouritesFocusable(__instance);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("FillFavorites patch failed: " + ex);
                }
            }
        }

        /// <summary>
        /// Attaches a Selectable per favourite tile and links them left to
        /// right, matching the results row they are laid out like.
        /// </summary>
        private static int MakeFavouritesFocusable(NewUIDatabase database)
        {
            var parent = FavouritesParent(database);
            if (parent == null)
            {
                return 0;
            }

            var tiles = new System.Collections.Generic.List<Selectable>();

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.GetComponent<ClickyBox>() == null)
                {
                    continue;
                }

                tiles.Add(EnsureSelectable(child.gameObject));
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };

                if (i > 0)
                {
                    nav.selectOnLeft = tiles[i - 1];
                }

                if (i < tiles.Count - 1)
                {
                    nav.selectOnRight = tiles[i + 1];
                }

                tiles[i].navigation = nav;
            }

            return tiles.Count;
        }

        /// <summary>
        /// Where the favourite tiles live.
        ///
        /// <c>panelParentSession</c> is the favourites container, paired with
        /// <c>panelParent</c> for the search results — the same relationship
        /// <c>favedImageBoxPrefab</c> has to <c>imageBoxPrefab</c>. Named
        /// directly rather than inferred from the tiles, so this cannot pick up
        /// a result tile that happens to share artwork.
        /// </summary>
        private static Transform FavouritesParent(NewUIDatabase database)
        {
            return database.panelParentSession != null
                ? database.panelParentSession.transform
                : null;
        }

        private static Selectable EnsureSelectable(GameObject tile)
        {
            var existing = tile.GetComponent<Selectable>();
            if (existing != null)
            {
                return existing;
            }

            var selectable = tile.AddComponent<Selectable>();

            var image = tile.GetComponent<Image>();
            if (image != null)
            {
                selectable.targetGraphic = image;
            }

            selectable.transition = Selectable.Transition.ColorTint;

            return selectable;
        }
    }

    /// <summary>
    /// Announces the clip detail panel when the player opens a result, and
    /// keeps ClipLibrary's view of the database current.
    /// </summary>
    [HarmonyPatch(typeof(NewUIDatabase), "DoTextBox")]
    internal static class DoTextBoxPatch
    {
        private static void Postfix(NewUIDatabase __instance, int myIndex)
        {
            try
            {
                ClipLibrary.Load(__instance);

                // SaveLoad.Load() reopens the last-viewed clip to restore the
                // player's session. That is not the player opening anything, and
                // announcing it means a returning player hears a clip summary
                // before they have even reached the title screen.
                if (SaveLoadPatch.IsRestoring)
                {
                    return;
                }

                Narrator.AnnounceClipOpened(myIndex);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("DoTextBox patch failed: " + ex);
                }
            }
        }
    }

    /// <summary>
    /// Brackets the save restore so everything it drives stays silent.
    ///
    /// Load() replays a whole session's worth of state through the same methods
    /// the player would have used — reopening the last clip, refilling the query,
    /// redrawing history. Each of those is worth announcing when the player does
    /// it and noise when the save file does.
    /// </summary>
    /// <remarks>
    /// Targets the <c>Load(string)</c> overload specifically. The parameterless
    /// <c>Load()</c> only resolves a path and delegates here, so this one point
    /// covers both entry points — and naming it explicitly avoids an ambiguous
    /// match between the two overloads.
    /// </remarks>
    [HarmonyPatch(typeof(SaveLoad), "Load", new Type[] { typeof(string) })]
    internal static class SaveLoadPatch
    {
        /// <summary>True while the game is replaying saved state.</summary>
        internal static bool IsRestoring { get; private set; }

        private static void Prefix()
        {
            IsRestoring = true;
        }

        private static void Postfix()
        {
            IsRestoring = false;
        }

        /// <summary>
        /// A throwing Load() would otherwise leave the flag stuck on and silence
        /// the mod for the rest of the session. Harmony finalizers run whether or
        /// not the original threw.
        ///
        /// The search box is re-baselined here too. A restore refills the field
        /// with the saved query without going through the keystroke hook, so the
        /// echo's snapshot would otherwise still hold the pre-restore text and
        /// report the player's first real keypress as a bulk deletion.
        /// </summary>
        private static void Finalizer()
        {
            IsRestoring = false;

            try
            {
                var database = ClipLibrary.Database;
                if (database != null)
                {
                    SearchBox.Sync(database.myTextField);
                }
            }
            catch (Exception)
            {
                // A stale snapshot costs one wrong announcement; a throw here
                // would propagate out of the game's own load path.
            }
        }
    }

    /// <summary>
    /// Returns focus to the result list when the clip detail panel closes.
    /// Without this the game leaves selection on an internal video object, which
    /// is both unlabellable and a dead end for the keyboard.
    /// </summary>
    [HarmonyPatch(typeof(NewUIDatabase), "ShutDetailText")]
    internal static class ShutDetailTextPatch
    {
        private static void Postfix()
        {
            try
            {
                // Clear playback state before restoring focus: a stale caption
                // cursor would make the next clip's opening line look like a
                // position the mod had already spoken.
                Playback.Reset();

                if (FocusWatcher.Instance != null)
                {
                    FocusWatcher.Instance.RestoreResultFocus();
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("ShutDetailText patch failed: " + ex);
                }
            }
        }
    }

    /// <summary>
    /// Speaks the search field as it is typed in.
    ///
    /// <c>InputField.KeyPressed</c> is the chokepoint every keystroke passes
    /// through — insertion, backspace, delete, and caret movement alike — which
    /// makes it the one hook that covers the whole of text entry. The game's own
    /// <c>InputValidate.checkChar</c> is not sufficient: it is a validation
    /// callback and sees insertions only, so a field patched there falls silent
    /// on exactly the edits a player most needs confirmed.
    ///
    /// Running as a postfix matters. The method is what applies the edit, so
    /// before it runs the field still holds the previous text and there is
    /// nothing to report.
    /// </summary>
    /// <remarks>
    /// Targeted through <see cref="HarmonyLib.AccessTools"/> rather than by
    /// attribute: <c>KeyPressed</c> is protected, and its <c>EditState</c> return
    /// type is a non-public nested enum this assembly cannot name. The postfix
    /// therefore ignores <c>__result</c> entirely and reads the field's state
    /// instead, which is what we actually care about.
    /// </remarks>
    [HarmonyPatch]
    internal static class InputFieldKeyPressedPatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(InputField), "KeyPressed");
        }

        private static void Postfix(InputField __instance)
        {
            try
            {
                // Only the search box. The game has a second field for user tags,
                // and echoing every keystroke of both would be noise the moment
                // focus is anywhere else.
                var database = ClipLibrary.Database;
                if (database == null || __instance != database.myTextField)
                {
                    return;
                }

                SearchBox.AfterKey(__instance);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("KeyPressed patch failed: " + ex);
                }
            }
        }
    }

    /// <summary>
    /// Re-baselines the search box whenever the game writes it directly.
    ///
    /// <c>RunQuery</c> strips newlines from the field, admin commands clear it,
    /// and a restored save refills it — none of which pass through the keystroke
    /// hook. Left alone, the next real keypress would diff against a stale
    /// snapshot and announce a deletion of text the player never removed.
    /// </summary>
    [HarmonyPatch(typeof(NewUIDatabase), "PopulateDatabaseList")]
    internal static class SearchBoxSyncPatch
    {
        private static void Postfix(NewUIDatabase __instance)
        {
            try
            {
                SearchBox.Sync(__instance.myTextField);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("Search box sync failed: " + ex);
                }
            }
        }
    }

    /// <summary>
    /// Announces the start of playback.
    ///
    /// <c>NowPlayTheVideo</c> rather than <c>PlayTheVideo</c>: the latter only
    /// delegates to it, and the game reaches the former by other routes too, so
    /// this is the point every play actually passes through.
    /// </summary>
    [HarmonyPatch(typeof(ClipDetail), "NowPlayTheVideo")]
    internal static class NowPlayTheVideoPatch
    {
        private static void Postfix(ClipDetail __instance)
        {
            try
            {
                Playback.Started(__instance);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("NowPlayTheVideo patch failed: " + ex);
                }
            }
        }
    }

    /// <summary>
    /// Distinguishes the player stopping a clip from the clip running out.
    ///
    /// Both paths end in <c>AbortTheVideo</c>, so the abort alone cannot tell
    /// them apart. <c>PlayerCancels</c> is the cancel route — it awards half
    /// score and then calls the abort — so bracketing it lets the abort patch
    /// below report which of the two happened.
    /// </summary>
    [HarmonyPatch(typeof(ClipDetail), "PlayerCancels")]
    internal static class PlayerCancelsPatch
    {
        /// <summary>True while a cancel is unwinding into AbortTheVideo.</summary>
        internal static bool IsCancelling { get; private set; }

        private static void Prefix()
        {
            IsCancelling = true;
        }

        /// <summary>
        /// A finalizer rather than a postfix: if the game's cancel path throws,
        /// a stuck flag would mislabel every subsequent clip ending as a cancel.
        /// </summary>
        private static void Finalizer()
        {
            IsCancelling = false;
        }
    }

    /// <summary>
    /// Announces the end of playback, and clears the mod's playback state.
    ///
    /// This runs on every exit from a clip — finishing, cancelling, and the
    /// panel being torn down — so <see cref="Playback.Ended"/> is responsible for
    /// staying quiet when nothing was playing.
    /// </summary>
    [HarmonyPatch(typeof(ClipDetail), "AbortTheVideo")]
    internal static class AbortTheVideoPatch
    {
        private static void Postfix()
        {
            try
            {
                Playback.Ended(PlayerCancelsPatch.IsCancelling);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("AbortTheVideo patch failed: " + ex);
                }
            }
        }
    }

    /// <summary>
    /// Loads the clip database as soon as the game has parsed it, so labels are
    /// available from the first frame the player can act.
    /// </summary>
    [HarmonyPatch(typeof(NewUIDatabase), "Awake")]
    internal static class DatabaseAwakePatch
    {
        private static void Postfix(NewUIDatabase __instance)
        {
            try
            {
                ClipLibrary.Load(__instance);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.Error("Awake patch failed: " + ex);
                }
            }
        }
    }
}
