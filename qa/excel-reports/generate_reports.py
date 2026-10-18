"""
Generates qa/excel-reports/Test Results.xlsx and Bug List.xlsx from QA evidence. Python 3 standard library only.

    python qa/excel-reports/generate_reports.py                  # newest qa/regression/<run>/artifacts/results.csv
    python qa/excel-reports/generate_reports.py --run reg-20261017-01

Sources (read only):
  qa/planning/test-inventory.md              test IDs and order (the central list)
  qa/planning/*-test-cases.md                test titles and expected results
  qa/regression/<run>/artifacts/results.csv  the execution evidence (one row per test method)
  Tests/CT007.Tests/**/*.cs                  titles of executed tests that are not in the inventory yet
  BUGS / FAIL_CAUSES below                   the reviewed classification of failures (edit these when triaging)

Rules:
  OK      every test method of the ID passed in the run       (Date = run date)
  FAIL    at least one test method of the ID failed in the run (Date = run date; error in the last column)
  NOT OK  skipped, not implemented, manual or not in the run   (Date blank; the reason in the last column)
  A bug is FIXED only when every linked test is OK in the run. Existing workbooks are read first: bug dates and Git
  references are kept, bugs are never removed, and a result someone entered for a test the run did not execute
  (for example a manual test) is kept instead of being replaced by NOT OK.
"""
import argparse
import csv
import datetime as dt
import re
import zipfile
from pathlib import Path
from xml.etree import ElementTree
from xml.sax.saxutils import escape

REPO = Path(__file__).resolve().parents[2]
QA = REPO / "qa"
OUT = QA / "excel-reports"
CASE_FILES = ["frontend-test-cases.md", "backend-test-cases.md", "integration-test-cases.md", "manual-test-cases.md"]

# ---- Reviewed classification (update when a failure is triaged or a bug is fixed) -------------------------------
# A FAIL that is not a bug is explained here; a FAIL that is a bug is linked through BUGS[...]["tests"].
FAIL_CAUSES = {
    "FE-047": "Cause: test-implementation defect, not an application bug. The tsMs column header in "
              "Frontend/Pages/Measurement/Table/MeasurementTable.yaml was changed to \"Date\" (commit 3f7ec0f); the "
              "header is meant to be editable, but FE-047 still pins the original text \"ts_ms\". The test needs to be "
              "updated (team decision); see qa/guides/testing-troubleshooting.md.",
}

BUGS = [
    {
        "id": "BUG-001", "qa_ref": "DEF-001", "tests": ["FE-005"], "found": "2026-10-16", "git": "",
        "description": "Backend connection settings (Frontend/Api/ApiSettings.cs, api-settings.json beside the exe). "
                       "Actual: a non-numeric value, e.g. {\"requestTimeoutSeconds\":\"abc\"} (same for "
                       "statusIntervalSeconds), throws InvalidOperationException: \"The requested operation requires an "
                       "element of type 'Number', but the target element has type 'String'.\" Expected: invalid values "
                       "fall back to the defaults (http://localhost:5002, 5 s, 5 s), as the class documents. Probable "
                       "impact: the app does not start (inferred; not launched). Reproduction: ApiSettings.Load(path) "
                       "with that file. Test: FE-005. QA defect report: DEF-001 "
                       "(qa/reports/run-20261016-final/defect-report.md).",
    },
    {
        "id": "BUG-002", "qa_ref": "DEF-002", "tests": ["BE-030"], "found": "2026-10-16", "git": "",
        "description": "Capture duration setting (Frontend/Calculation/Measurement/CaptureDurationSettings.cs, "
                       "%LocalAppData%\\CT007_Measurement_Studio\\capture-settings.json). Actual: a non-numeric value, "
                       "e.g. {\"captureDurationSeconds\":\"7\"}, throws the same InvalidOperationException as BUG-001. "
                       "Expected: fall back to 5 s, as the class documents. Probable impact: the Measurement page cannot "
                       "be created (inferred). Reproduction: new CaptureDurationSettings(path).Load() with that file. "
                       "Test: BE-030. Same root-cause pattern as BUG-001. QA defect report: DEF-002.",
    },
]
NO_FIX_FOUND = "No fix commit found"

# ---- Collecting ---------------------------------------------------------------------------------------------------


def clean(text):
    text = re.sub(r"\[([^\]]+)\]\([^)]*\)", r"\1", text)
    return re.sub(r"\s+", " ", text.replace("**", "").replace("`", "")).strip()


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
    skip = ("preconditions", "actions", "note", "data", "environment", "source", "priority", "steps", "equipment", "objective",
            "input", "evidence", "existing")
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


def latest_run(run_id):
    runs = sorted(p.parent.parent.name for p in (QA / "regression").glob("*/artifacts/results.csv"))
    run_id = run_id or runs[-1]
    stamp = re.search(r"(\d{8})", run_id)
    date = dt.datetime.strptime(stamp.group(1), "%Y%m%d").date() if stamp else None
    results = {}
    with (QA / "regression" / run_id / "artifacts" / "results.csv").open(encoding="utf-8-sig", newline="") as f:
        for r in csv.DictReader(f):
            results.setdefault(r["id"], []).append(r)
    return run_id, date, results

def head_commit():
    """Short hash of HEAD, read from .git without running git (read-only)."""
    head = (REPO / ".git" / "HEAD").read_text().strip()
    if head.startswith("ref: "):
        ref = REPO / ".git" / head[5:]
        if ref.exists():
            return ref.read_text().strip()[:7]
        packed = (REPO / ".git" / "packed-refs").read_text().splitlines()
        return next(line.split()[0][:7] for line in packed if line.endswith(head[5:]))
    return head[:7]


def ur_list(text):
    return ", ".join("UR-" + part.strip() for part in text.split(",") if part.strip())


def executed_before(test_id, run_id):
    """The newest other results.csv under qa/ in which the test passed or failed (not skipped), or None."""
    found = []
    for path in QA.rglob("results.csv"):
        if run_id in path.parts:
            continue
        with path.open(encoding="utf-8-sig", newline="") as f:
            if any(r.get("id") == test_id and r.get("outcome") in ("PASS", "FAIL") for r in csv.DictReader(f)):
                found.append(path.relative_to(REPO).as_posix())
    return sorted(found)[-1] if found else None


# ---- Reading an existing workbook (shared or inline strings) -------------------------------------------------------


NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}


def read_rows(path):
    """Rows of the first sheet as lists of strings (dates as YYYY-MM-DD in date columns is handled by the caller)."""
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

# ---- Building the rows ---------------------------------------------------------------------------------------------


def test_rows(run_id, run_date, results):
    inv, cases, titles = inventory(), test_cases(), source_titles()
    bug_of = {t: b["id"] for b in BUGS for t in b["tests"]}
    ids = [r["id"] for r in inv]
    # Executed tests that the inventory does not list yet go after the last ID of their group.
    for extra in sorted(set(results) - set(ids)):
        group = [i for i in ids if i.split("-")[0] == extra.split("-")[0]]
        ids.insert(ids.index(group[-1]) + 1 if group else len(ids), extra)
    meta = {r["id"]: r for r in inv}
    previous = {r[1]: r for r in read_rows(OUT / "Test Results.xlsx") if len(r) > 1 and r[1]}

    rows = []
    for test_id in ids:
        case, item, runs = cases.get(test_id), meta.get(test_id), results.get(test_id, [])
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

        outcomes = [r["outcome"] for r in runs]
        date, detail = "", ""
        if runs and all(o == "PASS" for o in outcomes):
            result, date = "OK", run_date
        elif "FAIL" in outcomes:
            result, date = "FAIL", run_date
            failed = [r for r in runs if r["outcome"] == "FAIL"]
            detail = " | ".join(f"{r['test']}: {r['message']}" for r in failed)
            if len(runs) > len(failed):
                detail += f" ({len(runs) - len(failed)} other test method(s) of this ID passed.)"
            if test_id in bug_of:
                detail += f" Linked bug: {bug_of[test_id]}."
            elif test_id in FAIL_CAUSES:
                detail += " " + FAIL_CAUSES[test_id]
            else:
                detail += " Cause: not yet classified (application, test or environment)."
        elif runs:
            result = "NOT OK"
            reasons = sorted({r["message"].rstrip(".") + "." for r in runs if r["outcome"] == "SKIP"})
            earlier = executed_before(test_id, run_id)
            detail = (f"Skipped in {run_id} ({run_date}): " + "; ".join(reasons) + " " +
                      (f"Last executed in {earlier}." if earlier else "No recorded run in qa/ has executed it."))
        else:
            result = "NOT OK"
            automation = item["automation"] if item else "unknown"
            if test_id.startswith("MAN-"):
                detail = ("Manual test (bench with a CT007-M detector and Gamma Guard); no recorded execution: the "
                          "Result column of qa/reports/run-20261016-final/manual-testing-checklist.md is empty.")
            else:
                detail = (f"Not implemented as an automated test (inventory automation: {automation}"
                          f"{'; ' + ur_list(item['ur']) if item and item['ur'] else ''}), so there is no execution evidence. "
                          "See qa/generation/test-implementation-status.md.")
            # Keep a result a person recorded earlier for a test this run did not execute (e.g. a manual test).
            old = previous.get(test_id)
            if old and len(old) > 6 and old[4] in ("OK", "FAIL") and as_date(old[5]):
                result, date, detail = old[4], as_date(old[5]), old[6]
        rows.append([test_id, description, expectation, result, date, detail])
    return rows


def bug_rows(results):
    """Curated bugs, merged with the existing workbook: dates and Git references are kept, no bug is removed."""
    previous = {r[1]: r for r in read_rows(OUT / "Bug List.xlsx") if len(r) > 1 and r[1]}
    rows = []
    for bug in BUGS:
        linked = [r for t in bug["tests"] for r in results.get(t, [])]
        fixed = bool(linked) and all(r["outcome"] == "PASS" for r in linked)
        old = previous.pop(bug["id"], None)
        found = as_date(old[3]) if old and as_date(old[3]) else dt.date.fromisoformat(bug["found"])
        git = (old[5] if old and len(old) > 5 and old[5] and old[5] != NO_FIX_FOUND else bug["git"]) or NO_FIX_FOUND
        description = bug["description"]
        if not fixed and git != NO_FIX_FOUND:
            description += " Fix committed; verification pending: the linked test does not pass yet."
        rows.append([bug["id"], description, found, "FIXED" if fixed else "NOT FIXED", git])
    for old in previous.values():   # bugs only in the old workbook are kept unchanged
        rows.append([old[1], old[2], as_date(old[3]) or "", old[4] if old[4] in ("FIXED", "NOT FIXED") else "NOT FIXED", old[5]])
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


def write_book(path, title, sheets):
    """sheets: list of (name, sheet xml, filter range or None)."""
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
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", types)
        z.writestr("_rels/.rels", root_rels)
        z.writestr("docProps/core.xml", core)
        z.writestr("docProps/app.xml", app)
        z.writestr("xl/workbook.xml", workbook)
        z.writestr("xl/_rels/workbook.xml.rels", rels)
        z.writestr("xl/styles.xml", STYLES)
        for i, (_, xml, _) in enumerate(sheets):
            z.writestr(f"xl/worksheets/sheet{i + 1}.xml", xml)


def notes_sheet(lines):
    rows = [[k, v] for k, v in lines]
    widths = [26, 110]
    return sheet_xml(["Item", "Details"], widths, [TEXT, TEXT], rows, heights=[row_height(r, widths) for r in rows], frozen=False)

# ---- Main ------------------------------------------------------------------------------------------------------------


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run", help="regression run ID under qa/regression (default: the newest with results.csv)")
    run_id, run_date, results = latest_run(parser.parse_args().run)
    OUT.mkdir(exist_ok=True)
    generated = dt.date.today().isoformat()

    tests = test_rows(run_id, run_date, results)
    counts = {k: sum(r[3] == k for r in tests) for k in ("OK", "NOT OK", "FAIL")}
    headers = ["Number", "Test Code Name", "Test Description", "Expectation", "Result", "Date", "Fail Description (Include Error)"]
    widths = [11, 17, 34, 60, 11, 12, 60]
    kinds = [CENTER, CENTER, TEXT, TEXT, CENTER, DATE, TEXT]
    rows = [[n] + r for n, r in enumerate(tests, start=1)]
    results_sheet = sheet_xml(headers, widths, kinds, rows, choice_col=4, choices=("OK", "NOT OK", "FAIL"),
                              colours={"OK": 0, "NOT OK": 1, "FAIL": 2}, heights=[row_height(r, widths) for r in rows])
    notes = notes_sheet([
        ("Generated", f"{generated} by qa/excel-reports/generate_reports.py (re-run it after each test run)."),
        ("Execution evidence", f"qa/regression/{run_id}/artifacts/results.csv — run {run_id}, {run_date}: "
                               f"{sum(len(v) for v in results.values())} test methods in {len(results)} test IDs."),
        ("Test list", "qa/planning/test-inventory.md (IDs and order) and qa/planning/*-test-cases.md (descriptions and "
                      "expectations). Executed tests missing from the inventory are added after their group."),
        ("Totals", f"{len(tests)} tests: {counts['OK']} OK, {counts['NOT OK']} NOT OK, {counts['FAIL']} FAIL."),
        ("OK", "Executed in the run and every test method of the ID passed."),
        ("FAIL", "Executed in the run and at least one test method failed; the error and its cause (linked bug, test "
                 "defect, or not yet classified) are in the last column."),
        ("NOT OK", "Not verified: skipped in the run (category or environment missing), not implemented, or manual "
                   "without a recorded result. Date is blank because the test was not executed."),
        ("UNRESOLVED", "Expectation marked UNRESOLVED (UR-xx): the expected result is not approved yet; see "
                       "qa/planning/unresolved-requirements.md. All other expectations are inferred from the code (UR-01)."),
        ("History", "One row per test with its latest status. Earlier runs stay in qa/execution, qa/regression and qa/reports."),
    ])
    write_book(OUT / "Test Results.xlsx", "CT007 Test Results",
               [("Test Results", results_sheet, f"$A$1:$G${len(rows) + 1}"), ("Notes", notes, None)])

    bugs = bug_rows(results)
    if bugs:
        headers = ["Number", "Bug Code Name", "Bug Description", "Date", "Status", "Git Commit / Push"]
        widths = [11, 17, 95, 12, 13, 34]
        kinds = [CENTER, CENTER, TEXT, DATE, CENTER, TEXT]
        rows = [[n] + r for n, r in enumerate(bugs, start=1)]
        bug_sheet = sheet_xml(headers, widths, kinds, rows, choice_col=4, choices=("FIXED", "NOT FIXED"),
                              colours={"FIXED": 0, "NOT FIXED": 2}, heights=[row_height(r, widths) for r in rows])
        fixed = sum(b[3] == "FIXED" for b in bugs)
        notes = notes_sheet([
            ("Generated", f"{generated} by qa/excel-reports/generate_reports.py, against run {run_id} ({run_date})."),
            ("Totals", f"{len(bugs)} bugs: {fixed} FIXED, {len(bugs) - fixed} NOT FIXED."),
            ("Scope", "Application defects confirmed by a failing test. Failures caused by test code or the environment "
                      "are explained in Test Results.xlsx instead. Suspected issues without a failing test (SUS-01…08) "
                      "stay in qa/reports/run-20261016-final/defect-report.md."),
            ("Status", "FIXED only when every linked test passes in the latest run. A committed fix without a passing "
                       "test stays NOT FIXED with \"verification pending\" in the description."),
            ("Git Commit / Push", "Commit hash and branch of the fix, and \"pushed\" only when the remote branch is "
                                  "confirmed to contain it; otherwise \"push not verified\". "
                                  f"\"{NO_FIX_FOUND}\": git log of the bug's source file shows no change after the bug "
                                  f"was found (checked {generated} at {head_commit()})."),
            ("Date", "The date the bug was first identified; kept when the report is regenerated."),
        ])
        write_book(OUT / "Bug List.xlsx", "CT007 Bug List",
                   [("Bug List", bug_sheet, f"$A$1:$F${len(rows) + 1}"), ("Notes", notes, None)])
    print(f"Test Results.xlsx: {len(tests)} tests, {counts}. Bug List.xlsx: {len(bugs)} bugs.")


if __name__ == "__main__":
    main()
