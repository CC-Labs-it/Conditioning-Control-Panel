"""Read the port data in docs/avalonia-*.html (stdlib only; used by CI scripts and the port gate).

Format (written by the converter, kept by hand edits): every table row is one line, <tr><td>..</td>..</tr>, header rows
use <th>; main-sync ledgers carry <script type="application/json" id="ledger-meta">{"total": N}</script>."""
import html
import json
import re
from html.parser import HTMLParser


class _Row(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.cells, self.head, self._buf = [], False, None

    def handle_starttag(self, tag, attrs):
        if tag in ("td", "th"):
            self._buf, self.head = [], tag == "th"
        elif tag == "code" and self._buf is not None:
            self._buf.append("`")


    def handle_endtag(self, tag):
        if tag in ("td", "th") and self._buf is not None:
            self.cells.append(" ".join("".join(self._buf).split()))
            self._buf = None
        elif tag == "code" and self._buf is not None:
            self._buf.append("`")

    def handle_data(self, data):
        if self._buf is not None:
            self._buf.append(data)


def parse_row(line):
    """One <tr> line -> (is_header, [cell text]). Code spans come back in backticks, as they were in Markdown."""
    p = _Row()
    p.feed(line)
    p.close()
    return p.head, p.cells


def tables(text):
    """Every table as {"header": [...], "rows": [[...], ...], "lines": [line numbers of the rows]} (0-based)."""
    out, cur = [], None
    for n, line in enumerate(text.split("\n")):
        s = line.strip()
        if s.startswith("<table"):
            cur = None
        if not s.startswith("<tr"):
            continue
        head, cells = parse_row(s)
        if head:
            cur = {"header": cells, "rows": [], "lines": []}
            out.append(cur)
        elif cur is not None:
            cur["rows"].append(cells)
            cur["lines"].append(n)
    return out


def rows(text, first):
    """Rows of every table whose first header cell is `first` (e.g. "id" for parity, "#" for main-sync ledgers),
    as dicts keyed by header name."""
    return [dict(zip(t["header"], r)) for t in tables(text) if t["header"][:1] == [first] for r in t["rows"]]


def meta(text):
    m = re.search(r'<script type="application/json" id="ledger-meta">(.*?)</script>', text, re.S)
    return json.loads(html.unescape(m.group(1))) if m else None


def bad_lines(text):
    """Line numbers (1-based) where a table row does not sit on exactly one line."""
    return [n for n, line in enumerate(text.split("\n"), 1)
            if ("<tr" in line or "</tr>" in line) and not re.fullmatch(r"\s*<tr[^>]*>.*</tr>\s*", line)]
