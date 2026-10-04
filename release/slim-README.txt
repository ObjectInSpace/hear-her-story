Hear Her Story — mod-only package
=================================

This package is for players who ALREADY have MelonLoader installed for Her
Story. It contains the mod and its support files only.

If you do not have MelonLoader, download the full self-contained package
instead (HearHerStory-<version>.zip) and ignore this one.


Installing
----------

1. Close the game.
2. Extract this archive into your Her Story folder -- the one containing
   HerStory.exe -- letting it merge with what is there.
3. Run HerStory.exe.


IMPORTANT: this overwrites MelonLoader/net35/Tomlet.dll
-------------------------------------------------------

That overwrite is required, and it is deliberate. Stock Tomlet.dll throws
TypeLoadException on the old Mono runtime Her Story uses, and because that
happens inside a type initializer reached by every melon's registration, it
stops EVERY mod from loading -- with nothing written to the log. Keeping your
existing Tomlet.dll will leave you with a game that launches normally and has
no accessibility at all.

The replacement is Tomlet rebuilt with one generic constraint removed. Nothing
else in MelonLoader is touched, and MelonLoader.dll itself is not included
here. See MelonLoader/MODIFICATIONS.txt.

If MelonLoader later auto-updates, it may restore the stock Tomlet.dll and
break the mod. Re-extract this archive to fix it.


What is in this package
-----------------------

  Mods/HearHerStory.dll                the mod
  UserLibs/UnityAccessibilityLib.dll   accessibility support library
  UniversalSpeech.dll                  speech output
  nvdaControllerClient.dll             NVDA support
  MelonLoader/net35/Tomlet.dll         rebuilt -- see above
  MelonLoader/MODIFICATIONS.txt        Apache-2.0 change notice
  MelonLoader/Documentation/           MelonLoader licence and notices

UniversalSpeech.dll and nvdaControllerClient.dll belong in the game root,
beside HerStory.exe. Both are 32-bit, matching the game.


Screen readers
--------------

NVDA works as shipped. JAWS, Dolphin and System Access users should copy their
own client (jfwapi.dll, dolapi32.dll or SAAPI32.dll) next to HerStory.exe.
Without one, speech falls back silently to SAPI, which is easy to mistake for
working output.


Checking it worked
------------------

A normal-looking launch is not proof; the failure mode is silent. Open
MelonLoader/Latest.log and look for:

    1 Mod loaded.
    [Hear_Her_Story] Speech ready.
    [Hear_Her_Story] screen reader clients present: nvdaControllerClient.dll

If you see "0 Mods loaded", the Tomlet.dll overwrite did not take.
