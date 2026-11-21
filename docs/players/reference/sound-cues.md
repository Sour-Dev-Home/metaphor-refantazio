# Sound cues

The mod plays a few short sounds of its own, so you know what happened before the screen reader finishes a sentence.
Each one lasts less than a fifth of a second and plays alongside your screen reader's speech. This page lists every
sound and what it means.

`[NEEDS VERIFICATION by ear]` The sounds have not been heard in the game yet.

## The sounds

| Sound | What it means | What it sounds like |
|---|---|---|
| Ally hit | Someone in your party was hit. It plays just before the damage number is read. | Two low notes going down, like "uh-oh". |
| Enemy hit | An enemy was hit. It plays just before the damage number is read. | Two high notes going up, quicker and brighter. |
| Guidance beep | The way to what you asked the mod to guide you to in the field ([keys](../how-to/install-the-mod.md#in-the-field-what-is-near-and-where-you-are)). | A short beep that repeats: from the side the thing is on, high ahead and low behind, about once a second far away and five times a second close. |

A simple way to remember them: low and falling is bad news for you, high and rising is good news.

## Turn the sounds off or change their volume

1. Find the mod's settings folder: `User\Mods\metaphor.accessibility` inside your Reloaded-II folder.
   `[NEEDS VERIFICATION]` The folder is checked in the game later.
2. In it, create or open the text file `settings.json`.
3. Put in it, keeping any other settings already there, with a comma between each:

   ```json
   { "soundCues": true, "soundCueVolume": 50 }
   ```

4. To turn the sounds off, change `true` to `false`. To change how loud they are, change `50` to a whole number from 0,
   silent, to 100, the loudest. Save the file.

The change applies at once, and the mod says "Sound cues: on" or "Sound cues: off", or "Sound cue volume:" and the new
number. You don't need to restart the game.

If a value can't be used, such as a volume of 120 or the word "off" instead of `false`, the mod uses the default:
sounds on, at volume 50.
