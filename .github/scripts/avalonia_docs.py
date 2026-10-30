"""Read the port data in docs/avalonia-*.html (stdlib only; used by CI scripts and the port gate).

Format (written by the converter, kept by hand edits): every table row is one line, <tr><td>..</td>..</tr>, header rows
use <th>; main-sync ledgers carry <script type="application/json" id="ledger-meta">{"total": N}</script>."""
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
        elif tag == "br" and self._buf is not None:
            self._buf.append(" ")


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
    """Every table as {"header": [...], "rows": [[...], ...]}."""
    out, cur = [], None
    for line in text.split("\n"):
        s = line.strip()
        if s.startswith("<table"):
            cur = None
        if not s.startswith("<tr"):
            continue
        head, cells = parse_row(s)
        if head:
            cur = {"header": cells, "rows": []}
            out.append(cur)
        elif cur is not None:
            cur["rows"].append(cells)
    return out


def rows(text, first):
    """Rows of every table whose first header cell is `first` (e.g. "id" for parity, "#" for main-sync ledgers),
    as dicts keyed by header name."""
    return [dict(zip(t["header"], r)) for t in tables(text) if t["header"][:1] == [first] for r in t["rows"]]


def meta(text):
    m = re.search(r'<script type="application/json" id="ledger-meta">(.*?)</script>', text, re.S)
    return json.loads(m.group(1)) if m else None


def bad_lines(text):
    """Line numbers (1-based) where a table row does not sit on exactly one line."""
    return [n for n, line in enumerate(text.split("\n"), 1)
            if ("<tr" in line or "</tr>" in line)
            and (line.count("<tr") != 1 or line.count("</tr>") != 1 or not re.fullmatch(r"\s*<tr[^>]*>.*</tr>\s*", line))]


if __name__ == "__main__":  # self-check: python3 .github/scripts/avalonia_docs.py
    t = ("<table>\n<tr><th>id</th><th>status</th><th>notes</th></tr>\n"
         "<tr><td>a-b</td><td>wired</td><td>x.<br>Y <code>F.cs</code> 1|2 &amp; &lt;slug&gt;</td></tr>\n</table>\n"
         '<script type="application/json" id="ledger-meta">{"total": 1}</script>')
    assert rows(t, "id") == [{"id": "a-b", "status": "wired", "notes": "x. Y `F.cs` 1|2 & <slug>"}], rows(t, "id")
    assert meta(t) == {"total": 1} and bad_lines(t) == []
    assert bad_lines("<tr><td>a</td></tr><tr><td>b</td></tr>\n<tr><td>c\n</td></tr>") == [1, 2, 3]
    print("avalonia_docs self-check OK")
