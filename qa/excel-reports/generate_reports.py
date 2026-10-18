"""
Generates qa/excel-reports/Test Results.xlsx and Bug List.xlsx from the recorded test runs. Python 3 standard library
only. Runs automatically at the end of every Tests/regression/Invoke-Regression.ps1 run (locally and in CI); it can
also be run by hand:

    python qa/excel-reports/generate_reports.py
    python qa/excel-reports/generate_reports.py --run reg-20261018-01 --copy-to qa/regression/reg-20261018-01/artifacts

Sources (read only, except bugs.json):
  qa/regression/<run>/artifacts/results.csv  execution evidence, one row per test method (all recorded runs)
  qa/regression/<run>/artifacts/run-info.json   when the run finished and at which commit (selection.json for older runs)
  qa/planning/test-inventory.md              test IDs and order
  qa/planning/*-test-cases.md                test titles and expected results
  Tests/CT007.Tests/**/*.cs                  titles of executed tests that are not in the inventory yet
  qa/excel-reports/bugs.json                 bug registry and "not a bug" classifications (edited by people)

Rules:
  Each test shows its latest execution across all recorded runs, so a change-based run (a subset of the tests) does not
  hide the others; the Date column is the date of that execution.
  OK      every test method of the ID passed in its latest execution
  FAIL    at least one test method failed in its latest execution (error and cause in the last column)
  NOT OK  never executed: skipped (category or environment missing), not implemented, or manual (Date blank)
  A failing test that is neither linked to a bug nor listed in not_bugs gets a new SUSPECTED bug in bugs.json.
  A bug is FIXED only when every linked test is OK in its latest execution. Dates and Git references are kept, bugs
  are never removed, and a result someone entered in the workbook for a never-executed test (e.g. manual) is kept.
"""
import argparse
import csv
import datetime as dt
import json
import os
import re
import shutil
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree
from xml.sax.saxutils import escape

REPO = Path(__file__).resolve().parents[2]
QA = REPO / "qa"
OUT = QA / "excel-reports"
REGISTRY = OUT / "bugs.json"
CASE_FILES = ["frontend-test-cases.md", "backend-test-cases.md", "integration-test-cases.md", "manual-test-cases.md"]
TEST_ID = r"\b(?:FE|BE|INT|E2E|MAN)-\d{3}\b"

# ---- Collecting ---------------------------------------------------------------------------------------------------


def clean(text):
    text = re.sub(r"\[([^\]]+)\]\([^)]*\)", r"\1", text)
    return re.sub(r"\s+", " ", text.replace("**", "").replace("`", "")).strip()


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else {}


def inventory():
    rows = []
    for line in (QA / "planning" / "test-inventory.md").read_text(encoding="utf-8").splitlines():
        m = re.match(r"^\| \[([A-Z0-9]+-\d{3})\]\([^)]*\) \|(.*)$", line)
        if m:
            cells = [c.strip() for c in m.group(2).split("|")]
            rows.append(dict(id=m.group(1), automation=clean(cells[3]), status=clean(cells[4]), ur=cells[5].strip()))
    return rows


def test_cases():
    """Title and expected result per ID. Tables become "input → expected" pairs; long text points to the document."""
    skip = ("preconditions", "actions", "note", "data", "environment", "source", "priority", "steps", "equipment",
            "objective", "input", "evidence", "existing")
    cases = {}
    for name in CASE_FILES:
        lines = (QA / "planning" / name).read_text(encoding="utf-8").splitlines()
        for i, line in enumerate(lines):
            m = re.match(r"^### ([A-Z0-9]+-\d{3}) · (.+)$", line)
            if not m:
                continue
            parts, take, tables = [], False, False
            for k in range(i + 2, len(lines)):
                row, following = lines[k], (lines[k + 1].strip() if k + 1 < len(lines) else "")
                if row.startswith(("### ", "<a id", "---", "## ")):
                    break
                label = re.match(r"^- \*\*([^*]+)\*\*(.*)$", row)
                if label:
                    key = label.group(1).strip().rstrip(":").lower()
                    take = "expect" in key or not key.startswith(skip)
                    tables = take or key.startswith("input")      # an Input table can carry an Expected column
                    if take:
                        text = clean(label.group(2).lstrip(":").strip())
                        prefix = "" if key.startswith("expected") else label.group(1).strip().rstrip(":") + ": "
                        if text or prefix:
                            parts.append(prefix + text if text else prefix.rstrip(": ") + ":")
                    continue
                if not tables:
                    continue
                sub = re.match(r"^\s+(?:\d+\.|-) (.*)$", row)
                cells = [c.strip() for c in row.strip().strip("|").split("|")] if row.strip().startswith("|") else None
                if sub and take:
                    parts.append(clean(sub.group(1)))
                elif cells and not all(set(c) <= set("-: ") for c in cells) and not re.match(r"^\|[-:| ]+\|$", following):
                    parts.append(clean(" → ".join(cells)) + ";")
            text = " ".join(p for p in parts if p)
            if len(text) > 700:
                text = text[:690].rsplit(" ", 1)[0] + f" … (full: qa/planning/{name}#{m.group(1).lower()})"
            cases[m.group(1)] = dict(title=clean(m.group(2)), expected=text)
    return cases


def source_titles():
    titles = {}
    for path in (REPO / "Tests" / "CT007.Tests").rglob("*.cs"):
        for m in re.finditer(r'\[Test\("([A-Z0-9]+-\d{3})",\s*"((?:[^"\\]|\\.)*)"', path.read_text(encoding="utf-8")):
            titles.setdefault(m.group(1), (m.group(2).replace('\\"', '"'), path.relative_to(REPO).as_posix()))
    return titles


def load_runs():
    """Every recorded run with results, oldest first. Each: id, date, head (commit), results {test ID: [rows]}."""
    runs = []
    for path in (QA / "regression").glob("*/artifacts/results.csv"):
        folder, run_id = path.parent, path.parent.parent.name
        info, selection = read_json(folder / "run-info.json"), read_json(folder / "selection.json")
        stamp = re.search(r"(\d{8})", run_id)
        if info.get("finished"):
            when = dt.datetime.fromisoformat(info["finished"])
        elif stamp:
            when = dt.datetime.strptime(stamp.group(1), "%Y%m%d")
        else:
            when = dt.datetime.fromtimestamp(path.stat().st_mtime)
        results = {}
        with path.open(encoding="utf-8-sig", newline="") as f:
            for r in csv.DictReader(f):
                results.setdefault(r["id"], []).append(r)
        runs.append(dict(id=run_id, when=when, date=when.date(), results=results,
                         head=(info.get("head") or selection.get("head") or "")[:7]))
    return sorted(runs, key=lambda r: (r["when"], r["id"]))


def ur_list(text):
    return ", ".join("UR-" + part.strip() for part in text.split(",") if part.strip())

# ---- Reading an existing workbook (shared or inline strings) -------------------------------------------------------


NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}


def read_rows(path):
    """Data rows of the first sheet as lists of strings (a date cell gives its serial number)."""
    if not path.exists():
        return []
    with zipfile.ZipFile(path) as z:
        shared = []
        if "xl/sharedStrings.xml" in z.namelist():
            for si in ElementTree.fromstring(z.read("xl/sharedStrings.xml")).findall("m:si", NS):
                shared.append("".join(t.text or "" for t in si.iter(f"{{{NS['m']}}}t")))
        sheet = ElementTree.fromstring(z.read("xl/worksheets/sheet1.xml"))
    rows = []
    for row in sheet.find("m:sheetData", NS).findall("m:row", NS):
        values = {}
        for c in row.findall("m:c", NS):
            col = re.match(r"[A-Z]+", c.get("r")).group(0)
            kind, v = c.get("t"), c.find("m:v", NS)
            if kind == "inlineStr":
                text = "".join(t.text or "" for t in c.iter(f"{{{NS['m']}}}t"))
            elif kind == "s":
                text = shared[int(v.text)]
            else:
                text = v.text if v is not None else ""
            values[col] = text or ""
        rows.append([values.get(chr(65 + i), "") for i in range(max(len(values), 7))])
    return rows[1:]


def as_date(text):
    if re.fullmatch(r"\d{4}-\d{2}-\d{2}", text or ""):
        return dt.date.fromisoformat(text)
    if re.fullmatch(r"\d+(\.0+)?", text or ""):
        return dt.date(1899, 12, 30) + dt.timedelta(days=int(float(text)))
    return None

# ---- Results per test ----------------------------------------------------------------------------------------------


def test_status(runs):
    """Per test ID: the latest run that executed it (PASS/FAIL), and the latest run that only skipped it."""
    executed, skipped = {}, {}
    for run in runs:
        for test_id, rows in run["results"].items():
            target = executed if any(r["outcome"] in ("PASS", "FAIL") for r in rows) else skipped
            target[test_id] = (run, rows)
    return executed, skipped


def collect_tests(runs):
    """One record per test: id, description, expectation, result, date, detail (bug link added later), run."""
    inv, cases, titles = inventory(), test_cases(), source_titles()
    executed, skipped = test_status(runs)
    ids = [r["id"] for r in inv]
    for extra in sorted((set(executed) | set(skipped)) - set(ids)):   # executed but not in the inventory yet
        group = [i for i in ids if i.split("-")[0] == extra.split("-")[0]]
        ids.insert(ids.index(group[-1]) + 1 if group else len(ids), extra)
    meta = {r["id"]: r for r in inv}
    previous = {r[1]: r for r in read_rows(OUT / "Test Results.xlsx") if len(r) > 1 and r[1]}

    tests = []
    for test_id in ids:
        case, item = cases.get(test_id), meta.get(test_id)
        if case:
            description, expectation = case["title"], case["expected"]
        else:
            title, file = titles.get(test_id, ("", ""))
            description = title
            expectation = (f"No approved test case in qa/planning; taken from the test's own title ({file}): {title}"
                           if title else "")
        if item and item["status"].lower().startswith("unresolved"):
            expectation = f"UNRESOLVED ({ur_list(item['ur']) or 'see test case'}): " + (
                expectation or "no expected result is defined yet.")
        elif not expectation:
            expectation = "Not defined in the test case."

        record = dict(id=test_id, description=description, expectation=expectation, result="NOT OK", date="",
                      detail="", run=None, failed=[])
        if test_id in executed:
            run, rows = executed[test_id]
            outcomes = [r["outcome"] for r in rows]
            record.update(run=run, date=run["date"])
            if "FAIL" in outcomes:
                failed = [r for r in rows if r["outcome"] == "FAIL"]
                detail = " | ".join(f"{r['test']}: {r['message']}" for r in failed) + f" (run {run['id']})"
                if len(rows) > len(failed):
                    detail += f"; {len(rows) - len(failed)} other test method(s) of this ID passed"
                record.update(result="FAIL", detail=detail + ".", failed=failed)
            elif all(o == "PASS" for o in outcomes):
                record.update(result="OK")
            else:
                record.update(detail=f"Only partly executed in run {run['id']}: "
                                     + "; ".join(f"{r['test']} {r['outcome']} {r['message']}".strip() for r in rows))
        elif test_id in skipped:
            run, rows = skipped[test_id]
            reasons = sorted({r["message"].rstrip(".") + "." for r in rows})
            record["detail"] = (f"Never executed: skipped in every recorded run, most recently {run['id']} "
                                f"({run['date']}): " + " ".join(reasons))
        elif test_id.startswith("MAN-"):
            record["detail"] = ("Manual test (bench with a CT007-M detector and Gamma Guard); no recorded execution: "
                                "enter the result and date in this row after running it (kept on regeneration).")
        else:
            automation = item["automation"] if item else "unknown"
            record["detail"] = (f"Not implemented as an automated test (inventory automation: {automation}"
                                f"{'; ' + ur_list(item['ur']) if item and item['ur'] else ''}), so there is no "
                                "execution evidence. See qa/generation/test-implementation-status.md.")
        # Keep a result a person entered for a test that no recorded run executed (e.g. a manual test).
        old = previous.get(test_id)
        if record["run"] is None and old and len(old) > 6 and old[4] in ("OK", "FAIL") and as_date(old[5]):
            record.update(result=old[4], date=as_date(old[5]), detail=old[6], manual=True)
        tests.append(record)
    return tests

# ---- Bugs ------------------------------------------------------------------------------------------------------------


AUTO_GIT = ("No fix commit", "Fix verified by")


def update_registry(tests):
    """Adds bugs found only in the old workbook, then a SUSPECTED bug per unclassified failing test."""
    registry = read_json(REGISTRY) or {"bugs": [], "not_bugs": {}}
    registry.setdefault("bugs", [])
    registry.setdefault("not_bugs", {})
    known = {b["id"]: b for b in registry["bugs"]}
    for old in read_rows(OUT / "Bug List.xlsx"):           # never lose a bug or a Git reference someone entered
        if len(old) < 6 or not old[1]:
            continue
        git = old[5] if old[5] and not old[5].startswith(AUTO_GIT) else ""
        if old[1] not in known:
            found = as_date(old[3])
            registry["bugs"].append(dict(id=old[1], tests=sorted(set(re.findall(TEST_ID, old[2]))),
                                         found=found.isoformat() if found else "", suspected=False, git=git,
                                         description=old[2]))
        elif git and not known[old[1]].get("git"):
            known[old[1]]["git"] = git

    linked = {t for b in registry["bugs"] for t in b.get("tests", [])}
    numbers = [int(m.group(1)) for b in registry["bugs"] if (m := re.fullmatch(r"BUG-(\d+)", b["id"]))]
    for test in tests:
        if test["result"] != "FAIL" or test["id"] in linked or test["id"] in registry["not_bugs"] or test.get("manual"):
            continue
        numbers.append(max(numbers, default=0) + 1)
        failure = test["failed"][0]
        registry["bugs"].append(dict(
            id=f"BUG-{numbers[-1]:03d}", tests=[test["id"]], found=test["date"].isoformat(), suspected=True, git="",
            description=f"SUSPECTED, confirmation pending (created automatically from a failing test). {test['id']} "
                        f"({test['description']}) failed in run {test['run']['id']} on {test['date']}: "
                        f"{failure['test']}: {failure['message']} Expected: {test['expectation']} Triage in "
                        "qa/excel-reports/bugs.json: confirm and describe the defect (set suspected to false, link "
                        "related tests), or move the test to not_bugs if the test or the environment is at fault."))
        linked.add(test["id"])
    return registry


def bug_rows(registry, tests):
    by_id = {t["id"]: t for t in tests}
    rows = []
    for bug in registry["bugs"]:
        linked = [by_id[t] for t in bug.get("tests", []) if t in by_id]
        fixed = bool(linked) and all(t["result"] == "OK" and t["run"] for t in linked)
        git, description = bug.get("git", ""), bug["description"]
        if fixed and not git:
            last = max(linked, key=lambda t: t["run"]["when"])["run"]
            git = (f"Fix verified by {', '.join(t['id'] for t in linked)} OK in run {last['id']} ({last['date']}"
                   f"{', commit ' + last['head'] if last['head'] else ''}); fix commit not recorded in bugs.json; "
                   "push not verified")
        elif not fixed and git:
            description += " Fix committed; verification pending: a linked test does not pass yet."
        rows.append([bug["id"], description, as_date(bug.get("found", "")) or "", "FIXED" if fixed else "NOT FIXED",
                     git or "No fix commit recorded"])
    return rows

# ---- Minimal .xlsx writer (styles, frozen header, filter, dropdown, conditional colours) -------------------------


def col(n):
    return chr(65 + n)


def cell(ref, value, style):
    if value is None or value == "":
        return f'<c r="{ref}" s="{style}"/>'
    if isinstance(value, dt.date):
        return f'<c r="{ref}" s="{style}"><v>{(value - dt.date(1899, 12, 30)).days}</v></c>'
    if isinstance(value, int):
        return f'<c r="{ref}" s="{style}"><v>{value}</v></c>'
    text = escape(re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f]", "", str(value)))
    return f'<c r="{ref}" s="{style}" t="inlineStr"><is><t xml:space="preserve">{text}</t></is></c>'


STYLES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy-mm-dd"/></numFmts>
<fonts count="2"><font><sz val="10"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="10"/><color rgb="FF1F2937"/><name val="Calibri"/><family val="2"/></font></fonts>
<fills count="4"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill>
<fill><patternFill patternType="solid"><fgColor rgb="FFD9E1F2"/><bgColor indexed="64"/></patternFill></fill>
<fill><patternFill patternType="solid"><fgColor rgb="FFF5F7FA"/><bgColor indexed="64"/></patternFill></fill></fills>
<borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border>
<border><left style="thin"><color rgb="FFBFBFBF"/></left><right style="thin"><color rgb="FFBFBFBF"/></right><top style="thin"><color rgb="FFBFBFBF"/></top><bottom style="thin"><color rgb="FFBFBFBF"/></bottom><diagonal/></border></borders>
<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
<cellXfs count="8">
<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
<xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
<xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
<xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="top"/></xf>
<xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="top"/></xf>
<xf numFmtId="0" fontId="0" fillId="3" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
<xf numFmtId="0" fontId="0" fillId="3" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="top"/></xf>
<xf numFmtId="164" fontId="0" fillId="3" borderId="1" xfId="0" applyNumberFormat="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="top"/></xf>
</cellXfs>
<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
<dxfs count="3">
<dxf><font><b/><color rgb="FF006100"/></font><fill><patternFill><bgColor rgb="FFC6EFCE"/></patternFill></fill></dxf>
<dxf><font><b/><color rgb="FF9C5700"/></font><fill><patternFill><bgColor rgb="FFFFEB9C"/></patternFill></fill></dxf>
<dxf><font><b/><color rgb="FF9C0006"/></font><fill><patternFill><bgColor rgb="FFFFC7CE"/></patternFill></fill></dxf>
</dxfs>
</styleSheet>"""

TEXT, CENTER, DATE = 2, 3, 4   # +3 for the shaded rows


def sheet_xml(headers, widths, kinds, rows, choice_col=None, choices=(), colours=None, heights=None, frozen=True):
    last = col(len(headers) - 1)
    out = ['<?xml version="1.0" encoding="UTF-8" standalone="yes"?>',
           '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
           'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">',
           f'<dimension ref="A1:{last}{len(rows) + 1}"/>']
    view = ('<pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/>'
            '<selection pane="bottomLeft" activeCell="A2" sqref="A2"/>') if frozen else ""
    out.append(f'<sheetViews><sheetView workbookViewId="0">{view}</sheetView></sheetViews>')
    out.append('<sheetFormatPr defaultRowHeight="13.8"/><cols>' + "".join(
        f'<col min="{i + 1}" max="{i + 1}" width="{w}" customWidth="1"/>' for i, w in enumerate(widths)) + "</cols>")
    out.append('<sheetData><row r="1" ht="20" customHeight="1">' +
               "".join(cell(f"{col(i)}1", h, 1) for i, h in enumerate(headers)) + "</row>")
    for n, values in enumerate(rows, start=2):
        shade = 3 if n % 2 == 1 else 0
        height = f' ht="{heights[n - 2]}" customHeight="1"' if heights else ""
        out.append(f'<row r="{n}"{height}>' + "".join(
            cell(f"{col(i)}{n}", v, kinds[i] + shade) for i, v in enumerate(values)) + "</row>")
    out.append("</sheetData>")
    if frozen:
        out.append(f'<autoFilter ref="A1:{last}{len(rows) + 1}"/>')
    if choice_col is not None:
        rng = f"{col(choice_col)}2:{col(choice_col)}1000"
        rules = "".join(f'<cfRule type="cellIs" dxfId="{dxf}" priority="{p}" operator="equal"><formula>"{value}"</formula></cfRule>'
                        for p, (value, dxf) in enumerate(colours.items(), start=1))
        out.append(f'<conditionalFormatting sqref="{rng}">{rules}</conditionalFormatting>')
        out.append(f'<dataValidations count="1"><dataValidation type="list" allowBlank="1" showErrorMessage="1" '
                   f'errorTitle="Invalid value" error="Choose one of: {", ".join(choices)}." sqref="{rng}">'
                   f'<formula1>"{",".join(choices)}"</formula1></dataValidation></dataValidations>')
    out.append('<pageMargins left="0.5" right="0.5" top="0.6" bottom="0.6" header="0.3" footer="0.3"/>'
               '<pageSetup orientation="landscape" fitToHeight="0"/></worksheet>')
    return "".join(out)


def row_height(values, widths):
    """Wrapped text needs an explicit height: Excel does not grow rows on opening a generated file."""
    lines = 1
    for value, width in zip(values, widths):
        if isinstance(value, str) and value:
            per_line = max(1.0, width * 0.95)   # characters per line at 10 pt, with room for word wrapping
            lines = max(lines, sum(max(1, -(-len(part) // int(per_line))) for part in value.split("\n")))
    return min(409, round(13.2 * lines + 3, 1))


def write_book(path, title, sheets, copies=()):
    """sheets: list of (name, sheet xml, filter range or None); copies: extra folders that get the same workbook.
    Returns False when the file is locked (open in Excel); the copies are written regardless."""
    defined = "".join(
        f'<definedName name="_xlnm._FilterDatabase" localSheetId="{i}" hidden="1">\'{name}\'!{rng}</definedName>'
        for i, (name, _, rng) in enumerate(sheets) if rng)
    workbook = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
                '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
                'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>' +
                "".join(f'<sheet name="{escape(name)}" sheetId="{i + 1}" r:id="rId{i + 1}"/>' for i, (name, _, _) in enumerate(sheets)) +
                "</sheets>" + (f"<definedNames>{defined}</definedNames>" if defined else "") + "</workbook>")
    rels = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
            "".join(f'<Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" '
                    f'Target="worksheets/sheet{i + 1}.xml"/>' for i in range(len(sheets))) +
            f'<Relationship Id="rId{len(sheets) + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>'
            "</Relationships>")
    types = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
             '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
             '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
             '<Default Extension="xml" ContentType="application/xml"/>'
             '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
             '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>' +
             "".join(f'<Override PartName="/xl/worksheets/sheet{i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
                     for i in range(len(sheets))) +
             '<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>'
             '<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>'
             "</Types>")
    root_rels = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
                 '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                 '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
                 '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>'
                 '<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>'
                 "</Relationships>")
    now = dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    core = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" '
            'xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" '
            'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">'
            f"<dc:title>{escape(title)}</dc:title><dc:creator>qa/excel-reports/generate_reports.py</dc:creator>"
            f'<dcterms:created xsi:type="dcterms:W3CDTF">{now}</dcterms:created>'
            f'<dcterms:modified xsi:type="dcterms:W3CDTF">{now}</dcterms:modified></cp:coreProperties>')
    app = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
           '<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">'
           "<Application>Microsoft Excel</Application></Properties>")
    temporary = path.with_name(path.stem + ".tmp.xlsx")
    with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", types)
        z.writestr("_rels/.rels", root_rels)
        z.writestr("docProps/core.xml", core)
        z.writestr("docProps/app.xml", app)
        z.writestr("xl/workbook.xml", workbook)
        z.writestr("xl/_rels/workbook.xml.rels", rels)
        z.writestr("xl/styles.xml", STYLES)
        for i, (_, xml, _) in enumerate(sheets):
            z.writestr(f"xl/worksheets/sheet{i + 1}.xml", xml)
    for folder in copies:
        folder.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(temporary, folder / path.name)
    try:
        os.replace(temporary, path)
        return True
    except PermissionError:
        temporary.unlink(missing_ok=True)
        print(f"WARNING: {path.relative_to(REPO)} is open in another program and was not updated. Close it and run "
              f"python {Path(__file__).relative_to(REPO).as_posix()} again.", file=sys.stderr)
        return False


def notes_sheet(lines):
    rows = [[k, v] for k, v in lines]
    widths = [26, 110]
    return sheet_xml(["Item", "Details"], widths, [TEXT, TEXT], rows, heights=[row_height(r, widths) for r in rows], frozen=False)

# ---- Main ------------------------------------------------------------------------------------------------------------


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run", help="the run that just finished (for the notes); default: the newest recorded run")
    parser.add_argument("--copy-to", help="also copy both workbooks into this folder (e.g. the run's artifacts)")
    args = parser.parse_args()
    runs = load_runs()
    if not runs:
        print("No recorded runs with results.csv under qa/regression; nothing to report.")
        return 0
    current = next((r for r in runs if r["id"] == args.run), runs[-1])
    OUT.mkdir(exist_ok=True)
    generated = dt.date.today().isoformat()

    tests = collect_tests(runs)
    registry = update_registry(tests)
    linked = {t: b["id"] for b in registry["bugs"] for t in b.get("tests", [])}
    for test in tests:
        if test["result"] == "FAIL" and not test.get("manual"):
            if test["id"] in linked:
                test["detail"] += f" Linked bug: {linked[test['id']]}."
            elif test["id"] in registry["not_bugs"]:
                test["detail"] += " " + registry["not_bugs"][test["id"]]
    REGISTRY.write_text(json.dumps(registry, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    counts = {k: sum(t["result"] == k for t in tests) for k in ("OK", "NOT OK", "FAIL")}
    headers = ["Number", "Test Code Name", "Test Description", "Expectation", "Result", "Date", "Fail Description (Include Error)"]
    widths = [11, 17, 34, 60, 11, 12, 60]
    kinds = [CENTER, CENTER, TEXT, TEXT, CENTER, DATE, TEXT]
    rows = [[n, t["id"], t["description"], t["expectation"], t["result"], t["date"], t["detail"]]
            for n, t in enumerate(tests, start=1)]
    results_sheet = sheet_xml(headers, widths, kinds, rows, choice_col=4, choices=("OK", "NOT OK", "FAIL"),
                              colours={"OK": 0, "NOT OK": 1, "FAIL": 2}, heights=[row_height(r, widths) for r in rows])
    notes = notes_sheet([
        ("Generated", f"{generated} by qa/excel-reports/generate_reports.py, automatically at the end of every "
                      "Tests/regression/Invoke-Regression.ps1 run (also in CI)."),
        ("Latest run", f"{current['id']} ({current['date']}{', commit ' + current['head'] if current['head'] else ''}): "
                       f"{sum(len(v) for v in current['results'].values())} test methods in {len(current['results'])} IDs."),
        ("Runs included", f"{len(runs)} recorded runs under qa/regression/*/artifacts/results.csv. Each test shows its "
                          "latest execution; the Date column is the date of that execution."),
        ("Test list", "qa/planning/test-inventory.md (IDs and order) and qa/planning/*-test-cases.md (descriptions and "
                      "expectations). Executed tests missing from the inventory are added after their group."),
        ("Totals", f"{len(tests)} tests: {counts['OK']} OK, {counts['NOT OK']} NOT OK, {counts['FAIL']} FAIL."),
        ("OK", "Every test method of the ID passed in its latest execution."),
        ("FAIL", "At least one test method failed in its latest execution; the error, the run and the cause (linked "
                 "bug, suspected bug, or a reason it is not a bug) are in the last column."),
        ("NOT OK", "Never executed: skipped in every recorded run (category or environment missing), not implemented, "
                   "or manual without a recorded result. Date is blank."),
        ("Manual tests", "Enter OK or FAIL, the date and notes in the row of a manual test after running it; "
                         "regeneration keeps them."),
        ("UNRESOLVED", "Expectation marked UNRESOLVED (UR-xx): the expected result is not approved yet; see "
                       "qa/planning/unresolved-requirements.md. All other expectations are inferred from the code (UR-01)."),
    ])
    copies = [Path(args.copy_to)] if args.copy_to else []
    written = [write_book(OUT / "Test Results.xlsx", "CT007 Test Results",
                          [("Test Results", results_sheet, f"$A$1:$G${len(rows) + 1}"), ("Notes", notes, None)], copies)]

    bugs = bug_rows(registry, tests)
    if bugs:
        headers = ["Number", "Bug Code Name", "Bug Description", "Date", "Status", "Git Commit / Push"]
        widths = [11, 17, 95, 12, 13, 34]
        kinds = [CENTER, CENTER, TEXT, DATE, CENTER, TEXT]
        rows = [[n] + r for n, r in enumerate(bugs, start=1)]
        bug_sheet = sheet_xml(headers, widths, kinds, rows, choice_col=4, choices=("FIXED", "NOT FIXED"),
                              colours={"FIXED": 0, "NOT FIXED": 2}, heights=[row_height(r, widths) for r in rows])
        fixed = sum(b[3] == "FIXED" for b in bugs)
        suspected = sum(b.get("suspected", False) for b in registry["bugs"])
        notes = notes_sheet([
            ("Generated", f"{generated} by qa/excel-reports/generate_reports.py after run {current['id']}."),
            ("Totals", f"{len(bugs)} bugs: {fixed} FIXED, {len(bugs) - fixed} NOT FIXED, {suspected} suspected "
                       "(confirmation pending)."),
            ("Source", "qa/excel-reports/bugs.json is the bug registry: edit descriptions, links, Git references and "
                       "classifications there (changes made only in this workbook are overwritten, except new bugs and "
                       "Git references, which are imported)."),
            ("Automatic bugs", "A failing test that is neither linked to a bug nor listed in not_bugs gets a SUSPECTED "
                               "bug automatically. Triage it in bugs.json: confirm it, group related tests, or move the "
                               "test to not_bugs when the test or the environment is at fault."),
            ("Status", "FIXED only when every linked test is OK in its latest execution. A recorded fix commit without "
                       "a passing test stays NOT FIXED with \"verification pending\" in the description."),
            ("Git Commit / Push", "From bugs.json: commit hash, branch, and \"pushed\" only when the remote branch is "
                                  "confirmed to contain the commit. For a fix verified without a recorded commit, the "
                                  "run and commit at which the linked tests passed are shown, with \"push not verified\"."),
            ("Date", "The date the bug was first identified; never changed by regeneration."),
            ("Scope", "Application defects confirmed or suspected from failing tests. Suspected issues without a failing "
                      "test (SUS-01…08) stay in qa/reports/run-20261016-final/defect-report.md."),
        ])
        written.append(write_book(OUT / "Bug List.xlsx", "CT007 Bug List",
                                  [("Bug List", bug_sheet, f"$A$1:$F${len(rows) + 1}"), ("Notes", notes, None)], copies))
    print(f"Excel reports: {len(tests)} tests ({counts['OK']} OK, {counts['NOT OK']} NOT OK, {counts['FAIL']} FAIL), "
          f"{len(bugs)} bugs ({sum(b[3] == 'FIXED' for b in bugs)} FIXED) -> {OUT.relative_to(REPO).as_posix()}/")
    return 0 if all(written) else 3


if __name__ == "__main__":
    sys.exit(main())
