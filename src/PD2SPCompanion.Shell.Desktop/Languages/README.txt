Project Diablo II Single Player Companion - language packs

English (US) is built in. Each other language is one file in this folder, named by its code:
de-DE.json, fr-FR.json, pt-BR.json. Settings > Language lists every pack found here; a change
takes effect when the companion starts again.

To make one: copy _template.json, rename it, and fill it in.

  {
    "code": "de-DE",
    "name": "Deutsch",
    "plural": "en",
    "strings": {
      "No free space.": "Kein Platz frei.",
      "Moved {moved} items to {where}.": "{moved} Gegenstände nach {where} verschoben.",
      "{count} selected items": { "one": "1 ausgewählter Gegenstand", "other": "{count} ausgewählte Gegenstände" }
    }
  }

- The key is the English. Leave a translation empty and that line stays English.
- Keep every {name} of the English in your translation, in any order; you may leave one out, but
  a {name} the English does not have makes the line stay English.
- An entry with "one" and "other" is a count. Give the forms your language has: zero, one, two,
  few, many, other - and set "plural" to your language family (en, fr, ru, pl, cs, ar, ja ...).
- Item names, properties, skills and areas come from the game itself and stay as the game says them.
