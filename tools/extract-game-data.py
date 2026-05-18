"""Extract static game-data tables from the decompiled Assembly-CSharp source
into JSON. Output goes to `data/<table>.json` — one file per table.

Each *.cs file in decomp/DBLoad/ has an Init() method that calls
`new XData(arg1, arg2, ...)` many times to populate `m_dic`. We parse the
field names + types from XData.cs, then extract every constructor invocation
in X.cs, decode positional args, and emit a list of dicts.

Handles:
  - int / long / float / double
  - bool (true/false)
  - string literals with escape sequences
  - null
  - new int[N] { ... } (and float[], string[], etc.)
  - new int[N][] { new int[M] {...}, ... } (jagged arrays)
  - nested string concatenation (rare but seen)

Usage:
  python tools/extract-game-data.py             # extract all known tables
  python tools/extract-game-data.py Item WuXue  # only specific ones
  python tools/extract-game-data.py --list      # show available + sizes
"""

from __future__ import annotations
import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

DECOMP = Path(r"C:\dev\physickungfu_cheat\decomp\DBLoad")
OUT    = Path(r"C:\dev\physickungfu_cheat\data")

# Tables to extract by default. Pair = (TableClassName, DataClassName).
# Most are TableName + "Data", but a few have different names (e.g. ItemUse).
TABLES = [
    "Item", "WuXue", "Skill", "Equip", "Buff", "GlobalBuff",
    "Character", "Npc", "Monster", "Animal", "AnimalFood",
    "Achieve", "Effect", "Scene", "Area", "Gift",
    "DanFang", "DaZao", "Fish", "FishRod", "Hunt", "HuntBow",
    "Book", "BaoXiang", "Custom", "Difficult", "Dot",
    "Friend", "Information", "ItemUse", "Level", "Passive",
    "PointAttribute", "PointLv", "Prop", "Receive", "ReceivePost",
    "Res", "Reward", "Task", "TextDef", "Trap", "Tree",
    "Tutorial", "Unlock", "WanderDialog", "Language",
    # Special: Global has its own pattern (single instance per id)
    "Global",
]

# --- Lexer for C# argument lists ---

class Tokenizer:
    def __init__(self, src: str):
        self.src = src
        self.i = 0

    def eof(self) -> bool:
        return self.i >= len(self.src)

    def peek(self, n: int = 1) -> str:
        return self.src[self.i:self.i + n]

    def skip_ws_and_comments(self):
        while self.i < len(self.src):
            c = self.src[self.i]
            if c in " \t\r\n":
                self.i += 1
            elif self.peek(2) == "//":
                while self.i < len(self.src) and self.src[self.i] != "\n":
                    self.i += 1
            elif self.peek(2) == "/*":
                end = self.src.find("*/", self.i + 2)
                if end < 0: self.i = len(self.src); return
                self.i = end + 2
            else:
                break

    def expect(self, ch: str):
        self.skip_ws_and_comments()
        if self.eof() or self.src[self.i] != ch:
            raise ValueError(f"expected {ch!r} at offset {self.i}: {self.src[self.i:self.i+50]!r}")
        self.i += 1

    def read_string(self) -> str:
        """Read a C# string literal starting at current position (quote consumed)."""
        if self.src[self.i] == "@":
            # verbatim string @"..." — "" is escaped quote
            self.i += 1
            assert self.src[self.i] == '"'
            self.i += 1
            out = []
            while self.i < len(self.src):
                if self.src[self.i] == '"':
                    if self.i + 1 < len(self.src) and self.src[self.i + 1] == '"':
                        out.append('"'); self.i += 2
                    else:
                        self.i += 1
                        return "".join(out)
                else:
                    out.append(self.src[self.i]); self.i += 1
            raise ValueError("unterminated verbatim string")

        assert self.src[self.i] == '"'
        self.i += 1
        out = []
        while self.i < len(self.src):
            c = self.src[self.i]
            if c == "\\":
                e = self.src[self.i + 1]
                self.i += 2
                out.append({"n": "\n", "r": "\r", "t": "\t", "\\": "\\",
                            "\"": "\"", "'": "'", "0": "\0", "a": "\a",
                            "b": "\b", "f": "\f", "v": "\v"}.get(e, e))
            elif c == '"':
                self.i += 1
                return "".join(out)
            else:
                out.append(c); self.i += 1
        raise ValueError("unterminated string")

    def read_number(self) -> int | float:
        start = self.i
        if self.src[self.i] in "+-":
            self.i += 1
        while self.i < len(self.src) and self.src[self.i] in "0123456789":
            self.i += 1
        is_float = False
        if self.i < len(self.src) and self.src[self.i] == ".":
            is_float = True
            self.i += 1
            while self.i < len(self.src) and self.src[self.i] in "0123456789":
                self.i += 1
        if self.i < len(self.src) and self.src[self.i] in "eE":
            is_float = True
            self.i += 1
            if self.src[self.i] in "+-":
                self.i += 1
            while self.i < len(self.src) and self.src[self.i] in "0123456789":
                self.i += 1
        text = self.src[start:self.i]
        # trailing type suffix: f F m M d D l L u U
        if self.i < len(self.src) and self.src[self.i] in "fFmMdDlLuU":
            sfx = self.src[self.i]; self.i += 1
            if sfx in "fFdDmM": is_float = True
            # u/l might be followed by another suffix
            while self.i < len(self.src) and self.src[self.i] in "lLuU":
                self.i += 1
        return float(text) if is_float else int(text)

    def read_identifier(self) -> str:
        start = self.i
        while self.i < len(self.src) and (self.src[self.i].isalnum() or self.src[self.i] in "_."):
            self.i += 1
        return self.src[start:self.i]

# --- Argument parser ---

def parse_value(tk: Tokenizer) -> Any:
    tk.skip_ws_and_comments()
    if tk.eof():
        raise ValueError("unexpected EOF parsing value")
    c = tk.src[tk.i]
    if c == '"' or (c == "@" and tk.peek(2)[1:2] == '"'):
        return tk.read_string()
    if c in "0123456789-+." :
        return tk.read_number()
    if c == 'n' and tk.src[tk.i:tk.i+4] == "null":
        tk.i += 4; return None
    if c == 't' and tk.src[tk.i:tk.i+4] == "true":
        tk.i += 4; return True
    if c == 'f' and tk.src[tk.i:tk.i+5] == "false":
        tk.i += 5; return False
    # `new T[...] { ... }` or `new T(...)` enum  or identifier
    ident = tk.read_identifier()
    if ident == "new":
        return parse_new_expr(tk)
    # Could be an enum value like `DiffucultEnum.Easy` — keep raw
    return ident

def parse_new_expr(tk: Tokenizer) -> Any:
    # we just consumed "new"
    tk.skip_ws_and_comments()
    type_name = tk.read_identifier()
    tk.skip_ws_and_comments()
    # `new int[N]` or `new int[N][]`
    if tk.peek(1) == "[":
        tk.expect("[")
        # Optional size inside [N]
        depth = 1
        while tk.i < len(tk.src) and depth > 0:
            c = tk.src[tk.i]
            if c == "[": depth += 1
            elif c == "]": depth -= 1
            tk.i += 1
        # Possible secondary `[]`
        tk.skip_ws_and_comments()
        while tk.peek(2) == "[]":
            tk.i += 2
            tk.skip_ws_and_comments()
        # `{ elements }` (may be missing for new int[N] without initializer — rare in our data)
        tk.skip_ws_and_comments()
        if tk.peek(1) == "{":
            return parse_initializer_list(tk)
        return []
    # `new XData(args)` — we're inside another call, this is a nested object/array element
    if tk.peek(1) == "(":
        tk.expect("(")
        items = []
        while True:
            tk.skip_ws_and_comments()
            if tk.peek(1) == ")":
                tk.i += 1; break
            items.append(parse_value(tk))
            tk.skip_ws_and_comments()
            if tk.peek(1) == ",": tk.i += 1
            elif tk.peek(1) == ")": tk.i += 1; break
        # Wrap as a tagged object so the caller knows it was a constructor
        return {"__type": type_name, "args": items}
    return type_name

def parse_initializer_list(tk: Tokenizer) -> list:
    tk.expect("{")
    items = []
    while True:
        tk.skip_ws_and_comments()
        if tk.peek(1) == "}":
            tk.i += 1; return items
        items.append(parse_value(tk))
        tk.skip_ws_and_comments()
        if tk.peek(1) == ",":
            tk.i += 1
        elif tk.peek(1) == "}":
            tk.i += 1; return items

def parse_call_args(text: str) -> list:
    """Parse the arg-list contents of an XData(...) call (no surrounding parens).
    Returns a list of values."""
    tk = Tokenizer(text)
    items = []
    while True:
        tk.skip_ws_and_comments()
        if tk.eof():
            return items
        items.append(parse_value(tk))
        tk.skip_ws_and_comments()
        if tk.eof():
            return items
        if tk.peek(1) == ",":
            tk.i += 1
        else:
            return items

# --- Extractor ---

def extract_call_bodies(src: str, class_name: str) -> list[str]:
    """Find every `new ClassName(...)` and return the text between matching parens."""
    bodies: list[str] = []
    needle = f"new {class_name}("
    i = 0
    while True:
        idx = src.find(needle, i)
        if idx < 0: break
        i = idx + len(needle)
        depth = 1
        start = i
        in_string = False
        verbatim = False
        while i < len(src) and depth > 0:
            c = src[i]
            if in_string:
                if verbatim:
                    if c == '"' and src[i + 1:i + 2] == '"':
                        i += 2; continue
                    if c == '"':
                        in_string = False; verbatim = False
                else:
                    if c == "\\":
                        i += 2; continue
                    if c == '"':
                        in_string = False
                i += 1; continue
            if c == '"':
                in_string = True
                if i > 0 and src[i - 1] == "@":
                    verbatim = True
                i += 1; continue
            if c == "(":
                depth += 1
            elif c == ")":
                depth -= 1
                if depth == 0: break
            i += 1
        bodies.append(src[start:i])
        i += 1
    return bodies

CTOR_PARAM_RE = re.compile(
    r"public\s+(\w+)\(([^)]*)\)", re.DOTALL
)

def read_data_class_fields(data_class_path: Path, class_name: str) -> list[tuple[str, str]]:
    """Return [(name, csharp_type), ...] from the public ctor's parameters."""
    if not data_class_path.exists():
        raise FileNotFoundError(data_class_path)
    text = data_class_path.read_text(encoding="utf-8")
    # Find ctor matching the class name
    for m in CTOR_PARAM_RE.finditer(text):
        if m.group(1) != class_name:
            continue
        params = m.group(2)
        if not params.strip():
            return []
        fields = []
        depth = 0
        cur = ""
        for c in params:
            if c == "<": depth += 1
            elif c == ">": depth -= 1
            if c == "," and depth == 0:
                fields.append(cur.strip()); cur = ""
            else:
                cur += c
        if cur.strip(): fields.append(cur.strip())
        out = []
        for f in fields:
            tokens = f.split()
            # last token is the param name (may start with _)
            name = tokens[-1].lstrip("_")
            type_ = " ".join(tokens[:-1])
            out.append((name, type_))
        return out
    raise ValueError(f"no public {class_name}(...) ctor found in {data_class_path.name}")

def normalize(val: Any) -> Any:
    """Strip the __type wrapper from nested ctor objects when we don't need them."""
    if isinstance(val, dict) and "__type" in val:
        return [normalize(x) for x in val["args"]]
    if isinstance(val, list):
        return [normalize(x) for x in val]
    return val

def extract_table(name: str) -> int:
    """Extract one table. Returns count of entries."""
    data_class = name + "Data"
    table_path = DECOMP / f"{name}.cs"
    data_path  = DECOMP / f"{data_class}.cs"
    if not table_path.exists():
        print(f"  SKIP {name}: {table_path.name} not found")
        return 0
    if not data_path.exists():
        print(f"  SKIP {name}: {data_class}.cs not found")
        return 0

    fields = read_data_class_fields(data_path, data_class)
    src = table_path.read_text(encoding="utf-8")
    bodies = extract_call_bodies(src, data_class)
    rows: list[dict] = []
    for body in bodies:
        try:
            args = parse_call_args(body)
        except Exception as e:
            print(f"  PARSE-FAIL in {name}: {e} — first 200 chars: {body[:200]!r}")
            continue
        if len(args) != len(fields):
            # Some classes use multiple ctor overloads or initializer trailing params; skip mismatched
            continue
        row: dict[str, Any] = {}
        for (fname, ftype), val in zip(fields, args):
            row[fname] = normalize(val)
        rows.append(row)

    OUT.mkdir(parents=True, exist_ok=True)
    out_path = OUT / f"{name.lower()}.json"
    out_path.write_text(json.dumps(rows, ensure_ascii=False, separators=(",", ":")) + "\n",
                        encoding="utf-8")
    print(f"  OK   {name:15s}  {len(rows):6d} entries → {out_path.relative_to(OUT.parent)}")
    return len(rows)

def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("tables", nargs="*", help="Specific tables to extract (default: all)")
    p.add_argument("--list", action="store_true", help="List known tables and exit")
    args = p.parse_args()

    if args.list:
        for t in TABLES: print(f"  {t}")
        return 0

    targets = args.tables or TABLES
    total = 0
    for t in targets:
        try:
            n = extract_table(t)
            total += n
        except Exception as e:
            print(f"  FAIL {t}: {type(e).__name__}: {e}")
    print(f"\nExtracted {total} total entries across {len(targets)} tables to {OUT}")

if __name__ == "__main__":
    sys.exit(main())
