"""Every string a player reads goes through the language framework.

    python tests/words.py              check: fail on a prose string in Shell or Core outside L(...)/Plural(...)
    python tests/words.py --list       print every unwrapped prose string, allowed or not
    python tests/words.py --template   write src/PD2SPCompanion.Shell.Desktop/Languages/_template.json: every key
    python tests/words.py --pseudo P   write a pseudo-language pack (qps-ploc) to folder P, for coverage tests

A string is prose when it holds words: two letters and a space, or one capitalised word, or a sentence end.
Not checked: logs, nameof, attributes, regexes, SQL. A string that is not for the player (a table column, a
settings key, a file name) is listed in tests/words-allowed.txt as "path<TAB>text", one string at a time -
never a whole file, so a string added later is still checked. A reworded or moved one fails the check until
the list says so, which is the point. The check also fails when _template.json is not the code's keys, and
reports how each language pack stands against them (stale lines, not failures). XAML is guarded by the build (PD2UI011).

Also failed: an interpolation whose hole holds English words, $"{(done ? "put back" : "goes back")}" - the
words would reach a translator as a value, not as text. Split it into whole L(...) sentences.
Exit code: 0 clean, 1 something to fix.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
PROJECTS = ("PD2SPCompanion.Shell", "PD2SPCompanion.Core", "PD2SPCompanion.Shell.Desktop")
ALLOWED = Path(__file__).resolve().parent / "words-allowed.txt"
TEMPLATE = SRC / "PD2SPCompanion.Shell.Desktop" / "Languages" / "_template.json"
WRAPPERS = {"L", "Plural", "N"}   # N(...): a name kept English in code, translated where shown

NOISE = re.compile(r"\bLog\.(Info|Caught|Warning|Error|Fatal|Open)\b|\bDebug\.|\bTrace\.|GeneratedRegex|nameof\(|DllImport|"
                   r"GetEnvironmentVariable|^\s*\[\w+\(\"|SuppressMessage|OnPropertyChanged\(\"|Console\.")
SQL = re.compile(r"^\s*(SELECT|INSERT|UPDATE|DELETE|CREATE|PRAGMA|DROP|ALTER|WITH|VACUUM|BEGIN|COMMIT|ATTACH)\b|"
                 r"\b(FROM|WHERE|VALUES|TABLE)\b.*\b(FROM|WHERE|VALUES|TABLE|JOIN|SET)\b|COLLATE NOCASE|\$\w+\{|"
                 # A statement begun by a constant that holds its SELECT (Vault.RowSelect).
                 r"^\s*\{\w*Select\}")


def literals(text):
    """(start, end, kind, content, holes) of each string literal, comments skipped. kind: '' plain, '$' interpolated."""
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if text.startswith("//", i):
            i = text.find("\n", i)
            i = n if i < 0 else i
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == "'":
            j = i + 1
            while j < n and text[j] != "'":
                j += 2 if text[j] == "\\" else 1
            i = j + 1
            continue
        m = re.match(r'(\$*)(@?)(\$*)("""+|")', text[i:i + 12])
        if m and c in '$@"':
            prefix = m.group(1) + m.group(2) + m.group(3)
            quote = m.group(4)
            start = i + len(prefix) + len(quote)
            interp = "$" in prefix
            if len(quote) >= 3:
                end = text.find(quote, start)
                yield i, end + len(quote), "$" if interp else "", text[start:end], []
                i = end + len(quote)
                continue
            verbatim = "@" in prefix
            j, depth, holes, hole_at = start, 0, [], 0
            while j < n:
                ch = text[j]
                if interp and ch == "{":
                    if depth == 0 and text.startswith("{{", j):
                        j += 2
                        continue
                    if depth == 0:
                        hole_at = j + 1
                    depth += 1
                elif interp and ch == "}" and depth:
                    depth -= 1
                    if depth == 0:
                        holes.append(text[hole_at:j])
                elif depth:
                    if ch == '"':   # a string inside a hole: skip it whole
                        k = j + 1
                        while k < n and text[k] != '"':
                            k += 2 if text[k] == "\\" else 1
                        j = k
                elif verbatim and ch == '"':
                    if text.startswith('""', j):
                        j += 2
                        continue
                    break
                elif not verbatim and ch == "\\":
                    j += 2
                    continue
                elif ch == '"':
                    break
                j += 1
            yield i, j + 1, "$" if interp else "", text[start:j], holes
            i = j + 1
            continue
        i += 1


def prose(s):
    plain = re.sub(r"\{[^{}]*\}", " ", s)
    if s != plain and re.search(r"(^|[^A-Za-z{])[A-Za-z]{3,}([^A-Za-z]|$)", plain) and not re.search(r"\w\{|\}\w", s):
        return True    # a label around a value: "max {n}", "{n} found", "Equipped  ({n})"
    if not re.search(r"[A-Za-z]{2}", plain):
        return False
    if " " in s.strip() and re.search(r"[A-Za-z]{3,}", plain) and not re.search(r"[\/]|\.\w{2,4}$", s):
        return True    # words and numbers: "Level 85 + Cows"
    if re.fullmatch(r"[A-Z][a-z]+-[a-z]+", plain.strip()):
        return True    # a hyphenated word: Off-hand
    if re.fullmatch(r"[A-Z][A-Z' ]{2,}[A-Z.!?]*", plain.strip()):
        return True    # a button's word: OPEN, MIGRATE, KEEP GAME'S
    if re.fullmatch(r"[\w.\-/\\:#]+", plain.strip()) and not re.fullmatch(r"[A-Z][a-z]+[.!?]?", plain.strip()):
        return False   # identifiers, keys, paths, codes
    return bool(re.search(r"[A-Za-z]+ [A-Za-z]|^\s*[A-Z][a-z]+[.!?]?\s*$|^\s*[A-Z]{3,}\s*$|[a-z][.!?]\s*$", plain))


def blanked(text):
    """The text with every string literal's inside blanked, so brackets in strings do not count."""
    chars = list(text)
    for start, end, kind, content, holes in literals(text):
        for i in range(start + 1, end - 1):
            if chars[i] not in "\n\"":
                chars[i] = " "
    return "".join(chars)


def enclosing_call(text, at):
    """The name of the call whose parentheses hold position at, or ''. Pass blanked text."""
    return call_at(text, at)[0]


def call_at(text, at):
    """(name, where its '(' is) of the call holding position at; ('', -1) when none."""
    depth = 0
    k = at - 1
    while k >= 0:
        ch = text[k]
        if ch == ")":
            depth += 1
        elif ch == "(":
            if depth == 0:
                m = re.search(r"([A-Za-z_]\w*)\s*$", text[max(0, k - 60):k])
                return (m.group(1), k) if m else ("", -1)
            depth -= 1
        elif ch in ";{}" and depth == 0:
            return "", -1
        k -= 1
    return "", -1


def hole_words(holes):
    """Holes that hold an English word in a string: {(done ? "put back" : "go back")}."""
    found = []
    for hole in holes:
        for inner in re.findall(r'"((?:[^"\\]|\\.)*)"', hole):
            if re.search(r"[A-Za-z]{2}", inner) and " " in inner.strip() or re.fullmatch(r"[A-Za-z]{3,}", inner.strip() or "-"):
                found.append(hole)
                break
    return found


def scan():
    """Every prose literal: (path, line, text, wrapped, hole_problem, kind)."""
    for project in PROJECTS:
        for path in sorted((SRC / project).rglob("*.cs")):
            if "obj" in path.parts or "bin" in path.parts:
                continue
            text = path.read_text(encoding="utf-8-sig")
            lines = text.split("\n")
            shadow = blanked(text)
            for start, end, kind, content, holes in literals(text):
                wrapped = enclosing_call(shadow, start) in WRAPPERS
                if not wrapped and not prose(content):
                    continue
                line = text.count("\n", 0, start) + 1
                context = lines[line - 1]
                if not wrapped and (NOISE.search(context) or SQL.search(content)):
                    continue
                yield (path.relative_to(SRC).as_posix(), line, content, wrapped,
                       hole_words(holes) if wrapped and kind == "$" else [], kind)


def flat(text):
    """A string as words-allowed.txt writes it: one line, tabs as spaces."""
    return text.replace("\t", " ").replace("\r", "").replace("\n", "\\n")


def allowed():
    entries = set()
    if ALLOWED.exists():
        for raw in ALLOWED.read_text(encoding="utf-8").splitlines():
            if raw.strip() and not raw.startswith("#") and "\t" in raw:
                path, text = raw.split("\t", 1)
                entries.add((path, text))
    return entries


ESCAPES = {"n": "\n", "t": "\t", "r": "\r", '"': '"', "'": "'", "\\": "\\", "0": "\0"}


def unescape(text):
    """A C# literal's escapes, read as the compiler reads them: \\n, \\", \\u00B7."""
    return re.sub(r'\\(u[0-9a-fA-F]{4}|x[0-9a-fA-F]{1,4}|.)',
                  lambda m: chr(int(m.group(1)[1:], 16)) if m.group(1)[0] in "ux" else ESCAPES.get(m.group(1), m.group(1)),
                  text)


def key_of(content, kind):
    """The key a call site makes: holes named by their expression, format specifiers dropped."""
    if kind != "$":
        return unescape(content)
    def hole(m):
        inner = m.group(1)
        depth = 0
        for i, ch in enumerate(inner):   # cut ":format" or ",alignment" at depth 0
            if ch in "([{":
                depth += 1
            elif ch in ")]}":
                depth -= 1
            elif ch in ":," and depth == 0:
                inner = inner[:i]
                break
        inner = inner.strip()
        # The compiler names a hole by its expression without the outer brackets: {(a ?? b):d} is {a ?? b}.
        while inner.startswith("(") and closes_at_end(inner):
            inner = inner[1:-1].strip()
        return "{" + inner + "}"
    key = re.sub(r"\{((?:[^{}]|\{[^{}]*\})*)\}", hole, content.replace("{{", "\x01").replace("}}", "\x02"))
    key = key.replace("\x01", "{").replace("\x02", "}")
    return unescape(key)


def closes_at_end(text):
    """Whether the '(' text starts with is closed by its last character."""
    depth = 0
    for i, ch in enumerate(text):
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                return i == len(text) - 1
    return False


def template_keys():
    """Every key, and whether it is a plural's: L("..."), N("..."), the other form of Plural(n, one, other), XAML."""
    plain, plural = set(), set()
    for project in PROJECTS:
        for path in sorted((SRC / project).rglob("*.cs")):
            if "obj" in path.parts or "bin" in path.parts:
                continue
            text = path.read_text(encoding="utf-8-sig")
            shadow = blanked(text)
            forms = {}
            for start, end, kind, content, holes in literals(text):
                name, at = call_at(shadow, start)
                if name in ("L", "N"):
                    plain.add(key_of(content, kind))
                elif name == "Plural":
                    forms.setdefault(at, []).append(key_of(content, kind))
            for one_other in forms.values():
                plural.add(one_other[-1])
    plain |= xaml_keys()
    return plain - plural, plural


def main():
    args = sys.argv[1:]
    found = list(scan())
    ok = allowed()

    if "--template" in args or "--pseudo" in args:
        plain, plural = template_keys()
        keys = sorted(plain | plural)
        if "--template" in args:
            TEMPLATE.parent.mkdir(parents=True, exist_ok=True)
            TEMPLATE.write_text(json.dumps({
                "code": "xx-XX", "name": "Language name in itself", "plural": "en",
                "strings": {k: {"one": "", "other": ""} if k in plural else "" for k in keys}},
                ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            print(f"{len(keys)} keys ({len(plural)} plural) -> {TEMPLATE.relative_to(ROOT)}")
        if "--pseudo" in args:
            folder = Path(args[args.index("--pseudo") + 1])
            folder.mkdir(parents=True, exist_ok=True)
            pack = {"code": "qps-ploc", "name": "Pseudo", "plural": "en",
                    "strings": {k: {"one": pseudo(k), "other": pseudo(k)} if k in plural else pseudo(k) for k in keys}}
            (folder / "qps-ploc.json").write_text(json.dumps(pack, ensure_ascii=False, indent=1), encoding="utf-8")
            print(f"{len(keys)} keys -> {folder / 'qps-ploc.json'}")
        return 0

    if "--prune" in args:
        live = {(path, flat(text)) for path, _, text, wrapped, _, _ in found if not wrapped}
        header = [raw for raw in ALLOWED.read_text(encoding="utf-8").splitlines() if raw.startswith("#")]
        kept = sorted(ok & live)
        ALLOWED.write_text("\n".join(header + [f"{p}\t{t}" for p, t in kept]) + "\n", encoding="utf-8")
        print(f"words-allowed.txt: {len(kept)} kept, {len(ok) - len(kept)} gone")
        return 0

    problems = 0
    for path, line, text, wrapped, holes, kind in found:
        if holes:
            problems += 1
            print(f"{path}:{line}: words in a hole {holes[0]!r} - split into whole sentences: {text[:90]}")
        if wrapped:
            continue
        listed = (path, flat(text)) in ok
        if "--list" in args:
            print(f"{'  ' if listed else '! '}{path}:{line}\t{text[:120]}")
        elif not listed:
            problems += 1
            print(f"{path}:{line}: not through L(...): {text[:110]}")
    if "--list" in args:
        return 0

    # The translators' template is the code's keys, exactly: a string added or reworded since it was
    # written would never reach a translator. Regenerate with --template.
    plain, plural = template_keys()
    current = plain | plural
    written = set(json.loads(TEMPLATE.read_text(encoding="utf-8"))["strings"]) if TEMPLATE.exists() else set()
    if written != current:
        problems += 1
        print(f"{TEMPLATE.relative_to(ROOT)} out of date: {len(current - written)} keys missing, "
              f"{len(written - current)} gone - run: python tests/words.py --template")

    wrapped_count = sum(1 for f in found if f[3])
    print(f"words: {wrapped_count} through the framework, {len(ok)} allowed, {problems} to fix"
          f"; {pack_report(current, plural)}")
    return 1 if problems else 0


def pack_report(current, plural):
    """
    Each language pack beside the template, measured against the code's keys: lines whose English
    changed or went (the code was reworded or a hole renamed - the translation now falls back to
    English), and keys not translated yet. A report, not a failure: a pack lags the code by nature.
    """
    packs = [p for p in sorted(TEMPLATE.parent.glob("*.json")) if not p.name.startswith("_")]
    if not packs:
        return "no language packs"
    said = []
    for path in packs:
        strings = json.loads(path.read_text(encoding="utf-8-sig")).get("strings", {})
        done = {k for k, v in strings.items() if v}
        stale = sorted(done - current)
        said.append(f"{path.stem}: {len(done & current)}/{len(current)} translated, {len(stale)} stale")
        for key in stale[:10]:
            print(f"  {path.name}: no such English any more: {key[:100]!r}")
    return "; ".join(said)


def xaml_keys():
    """Every {L '...'} in the Shell's views."""
    keys = set()
    for path in (SRC / "PD2SPCompanion.Shell").rglob("*.axaml"):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        for m in re.finditer(r"\{L '((?:[^'\\]|\\.)*)'\}", path.read_text(encoding="utf-8-sig")):
            text = m.group(1).replace("\\'", "'").replace("\\\\", "\\")
            keys.add(text.replace("&#x0a;", "\n").replace("&#10;", "\n").replace("&amp;", "&")
                     .replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", '"'))
    return keys


ACCENTS = str.maketrans("aceinoruyACEINORUY", "àçëïñöŕüÿÀÇËÏÑÖŔÜŸ")


def pseudo(key):
    """[Ŧëxţ] with every hole kept: plain English left on screen is a missed string."""
    parts = re.split(r"(\{[^{}]+\})", key)
    return "[" + "".join(p if p.startswith("{") else p.translate(ACCENTS) for p in parts) + "]"


if __name__ == "__main__":
    sys.exit(main())
