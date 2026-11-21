// Builds docs/Gamma-Core-Log-User-Guide.docx (same content as docs/README.md) from docs/guide-images.
// Needs Node and the docx package: npm install docx@9.5.1 (in any folder), then
//   node tools/docs/build-user-guide.js <repository root>
// Open the result in Word once and update the table of contents (right-click > Update Field), then save.
const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, ImageRun, Table, TableRow, TableCell, WidthType,
  ShadingType, AlignmentType, LevelFormat, TableOfContents, PageBreak, BorderStyle, Footer, PageNumber,
} = require("docx");

const root = process.argv[2];
const img = (name) => path.join(root, "docs", "guide-images", name);
const FONT = "Segoe UI";
const CONTENT_WIDTH = 9026; // A4 with 1" margins, DXA

function pngSize(file) {
  const b = fs.readFileSync(file);
  return { w: b.readUInt32BE(16), h: b.readUInt32BE(20) };
}

function picture(name, caption, maxWidthPx = 600) {
  const { w, h } = pngSize(img(name));
  const width = Math.min(maxWidthPx, w);
  const height = Math.round((h * width) / w);
  return [
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 120, after: 60 },
      keepNext: true,
      children: [new ImageRun({ type: "png", data: fs.readFileSync(img(name)), transformation: { width, height },
        altText: { title: caption, description: caption, name } })],
    }),
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { after: 200 },
      children: [new TextRun({ text: caption, italics: true, size: 18, color: "555555" })],
    }),
  ];
}

const p = (text, opts = {}) => new Paragraph({ spacing: { after: 120 }, ...opts, children: runs(text) });

// **bold** inline markup
function runs(text) {
  return text.split(/(\*\*[^*]+\*\*)/).filter(Boolean).map((part) =>
    part.startsWith("**") ? new TextRun({ text: part.slice(2, -2), bold: true }) : new TextRun(part));
}

const bullet = (text) => new Paragraph({ numbering: { reference: "bullets", level: 0 }, spacing: { after: 60 }, children: runs(text) });
let listId = 0;
function steps(items) {
  const ref = `steps${listId++}`;
  numberingConfigs.push({ reference: ref, levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT,
    style: { paragraph: { indent: { left: 540, hanging: 360 } } } }] });
  return items.map((t) => new Paragraph({ numbering: { reference: ref, level: 0 }, spacing: { after: 80 }, children: runs(t) }));
}
const numberingConfigs = [
  { reference: "bullets", levels: [{ level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT,
    style: { paragraph: { indent: { left: 540, hanging: 360 } } } }] },
];

const border = { style: BorderStyle.SINGLE, size: 4, color: "C9CED6" };
const borders = { top: border, bottom: border, left: border, right: border };
function table(header, rows, widths = [2600, CONTENT_WIDTH - 2600]) {
  const cell = (text, isHeader, i) => new TableCell({
    borders,
    width: { size: widths[i], type: WidthType.DXA },
    shading: isHeader ? { fill: "E8EEF8", type: ShadingType.CLEAR, color: "auto" } : undefined,
    margins: { top: 60, bottom: 60, left: 100, right: 100 },
    children: [new Paragraph({ children: isHeader ? [new TextRun({ text, bold: true })] : runs(text) })],
  });
  return new Table({
    width: { size: CONTENT_WIDTH, type: WidthType.DXA },
    columnWidths: widths,
    rows: [
      new TableRow({ tableHeader: true, children: header.map((t, i) => cell(t, true, i)) }),
      ...rows.map((r) => new TableRow({ children: r.map((t, i) => cell(t, false, i)) })),
    ],
  });
}
const spacer = () => new Paragraph({ spacing: { after: 120 }, children: [] });
const h1 = (t) => new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun(t)] });
const h2 = (t) => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun(t)] });

const children = [
  new Paragraph({ heading: HeadingLevel.TITLE, children: [new TextRun("Gamma Core Log")] }),
  new Paragraph({ spacing: { after: 80 }, children: [new TextRun({ text: "User Guide", size: 32, color: "2563EB" })] }),
  new Paragraph({ spacing: { after: 320 }, children: [new TextRun({ text: "Application version 1.2.1 · November 2026", color: "555555" })] }),
  p("Gamma Core Log logs CT007 gamma readings along a drillhole. You pick (or create) a drillhole, press **Start**, and right-click once for every depth interval. Each capture averages the detector's readings over a few seconds and becomes one row in the table. Readings stay on the laptop until you export them and upload them to the central database."),
  p("Inside the app, press **F1** (or the **?** button at the top right of any page) to open Help at the page you are on."),
  new TableOfContents("Contents", { hyperlink: true, headingStyleRange: "1-2" }),
  new Paragraph({ children: [new PageBreak()] }),

  h1("1. Before you start"),
  ...steps([
    "Switch on the CT007 probe and pair it with the laptop over Bluetooth. Any CT007 variant works: names such as CT007-M, CT007-T or CT007-M-0001 are all recognised as the same device family.",
    "Open **GammaGuard** and connect the probe, so it starts writing readings.",
    "Start **Gamma Core Log**. It opens on Core Information.",
  ]),
  ...picture("gg-main.png", "GammaGuard with the probe connected", 520),

  h1("2. Core Information"),
  p("Pick the drillhole and check that the laptop is ready to measure."),
  ...picture("core-information.png", "Core Information: drillhole, operator, notes and Device Status"),
  table(["Field / button", "What it does"], [
    ["Existing / New", "Existing picks a drillhole from the list. New creates one that is not in the list yet (section 3)."],
    ["Drillhole", "Type part of the Hole ID, then pick it (Enter, or click). Drillholes you already measured are listed first; Local marks one created on this computer."],
    ["Project / Campaign", "Filled in from the drillhole (chosen by you in New)."],
    ["Operator / Notes", "Your name (filled with your Windows account) and remarks. Saved with every reading."],
    ["Device Status", "Green means ready. Red or amber says what needs fixing; Retry checks again."],
    ["Select / Change Database", "Pick the GammaGuard database yourself if the app cannot find it."],
    ["Save & Continue", "The only save button. Saves the choice (in New: creates the drillhole first) and opens the Measurement page."],
  ]),
  spacer(),
  ...picture("device-status-problem.png", "Device Status explaining a problem", 520),

  h1("3. Creating a new drillhole"),
  p("Use this when the drillhole is not in the list yet."),
  ...steps([
    "Click **New** in the Existing / New switch above the Drillhole box. Any drillhole you had picked is cleared.",
    "Type the **Drillhole ID**.",
    "Pick the **Project** and the **Campaign** from their lists. If yours is not there, choose **+ Add new project** (or **+ Add new campaign**) and type it; the **×** goes back to the list.",
    "Click **Save & Continue**. The drillhole is created, selected and the Measurement page opens, all in one step.",
  ]),
  p("Drillhole ID, Project and Campaign are all required. **Save & Continue** stays disabled until they are filled in, and an empty box shows what is missing. Spaces at the start and end are removed."),
  p("If the Drillhole ID already exists (in any letter case), the app says so: click **switch to Existing to select it** to pick that drillhole instead. A new Project or Campaign that already exists in another letter case (for example zoovch next to Zoovch) is refused too; pick it from the list."),
  ...picture("new-drillhole.png", "New mode: Drillhole ID and Project filled in, Campaign still missing"),
  p("The new drillhole is stored on this computer only, also when the app is offline, and is never sent to the central drillhole list. It is listed with a **Local** tag and is still there after a restart. Its new Project or Campaign is offered in the lists straight away."),
  p("Switching back to Existing with something typed asks **Discard new drillhole?** first: **Discard** drops it, **Keep editing** stays in New."),

  h1("4. Measurement"),
  p("Measure the drillhole one interval at a time."),
  ...picture("measurement.png", "Measurement: countdown, Live value with CPS / CPM switch, eye icon, graph with threshold line, and the table"),
  ...steps([
    "Enter **Starting Depth (m)** and **Step (cm)**. Optionally set **Threshold (CPS)** for an alarm.",
    "Press **Start**.",
    "Lower the probe to the interval and **right-click anywhere on the Measurement page**. The countdown runs while readings are collected (default 5 s; click the countdown to change it, 2–10 s).",
    "When the countdown ends, a row is added and the starting depth moves on by one step. Right-click for the next one.",
    "Press **Stop** when you are done.",
  ]),
  p("**Right-click only captures on the Measurement page.** On every other page a right-click does nothing to the table, and readings from the device are ignored. If you leave the Measurement page while a capture is running, that capture is cancelled and nothing is saved; measuring stays switched on, so come back and right-click again."),
  table(["Item", "What it does"], [
    ["Live", "The boxed value above the graph, left of the eye icon: the newest reading from the detector, updated every second while you are on this page (— when there is no recent reading). CPS / CPM beside it switches the unit (CPM = CPS × 60); the choice is kept until you close the app."],
    ["Graph", "One bar per interval: depth along the bottom, CPS up the side."],
    ["Threshold line", "The alarm threshold as a dashed red line across the graph, before, during and after measuring. The eye icon above the graph shows it (open eye) or hides it (crossed-out eye)."],
    ["Bell", "Alarm state. Red when the last reading was above the threshold; click to mute or unmute."],
    ["Table", "No, From, To, Step, Date and CPS of every interval. Click ✕ to delete a row (after confirming)."],
    ["Export CSV", "Saves the table as a file for the Upload Data page (the file also holds each capture's sample count)."],
    ["New Measurement", "Closes this drillhole; enter the next one in Core Information. Saved data is kept."],
  ]),
  spacer(),
  ...picture("measuring.png", "A capture in progress", 520),

  h1("5. Monitoring"),
  p("Live readings from every detector connected in GammaGuard. **Take sample** skips the next reading and averages the five after it. Click a detector row for its details."),
  ...picture("monitoring.png", "Monitoring"),

  h1("6. Upload Data"),
  p("Send an exported CSV to the central database. This is the only way readings reach it, and you need to be online (green dot in the sidebar)."),
  ...steps([
    "Click **Import CSV** and choose the file exported from the Measurement page.",
    "Check the rows; click ✕ to drop one.",
    "Click **Upload Data**. Uploading the same file twice stores it once.",
  ]),
  ...picture("upload-data.png", "Upload Data"),

  h1("7. Data Viewer"),
  p("See what has been uploaded to the central database. Pick a drillhole to list its uploaded readings by depth; **Refresh** loads the latest data."),
  ...picture("data-viewer.png", "Data Viewer"),

  h1("8. Help and keyboard shortcuts"),
  p("Help slides in from the right edge of the window and opens at the page you are on; it follows you when you change page. Each page's help has the same parts: what the page is for, **Before you start**, a picture with numbered markers, **Steps**, **Buttons and fields** (the numbers match the markers) and **Troubleshooting**. Search finds text on every page, the page list at the top switches page, and the **On this page** links jump to a part. Click the picture to see it larger. Drag Help's left edge to make it wider; the width is remembered. The app has no tooltips: what each field and button does is in **Buttons and fields**, and the **Keyboard shortcuts** entry also lists the controls found on every page (sidebar, header and Help itself)."),
  p("On the Measurement page Help does not dim the page or close when you click outside it, so you can keep measuring with Help open: right-click on the page still captures (right-click inside Help does not)."),
  ...picture("help-drawer.png", "Help, opened on the Measurement page"),
  table(["Keys", "What it does"], [
    ["F1", "Opens Help at the page you are on, or closes it. Works on every page, whatever has the keyboard focus."],
    ["? button", "Opens or closes Help, like F1. It is highlighted while Help is open."],
    ["Esc", "Closes an enlarged picture, then Help."],
    ["Ctrl+F", "While Help is open: jumps to its search box."],
    ["Right-click", "Captures one interval — on the Measurement page only, after Start, and not inside Help."],
    ["Enter / Esc", "In the capture duration editor: save / cancel."],
    ["↑ ↓ Enter", "In the Drillhole box: move through the list and pick a drillhole."],
  ]),

  h1("9. When something goes wrong"),
  table(["Problem", "What to do"], [
    ["Device Status is red for Bluetooth", "Check the probe is on and paired. Any CT007 name (CT007-M, CT007-T …) is accepted."],
    ["\"No samples were received\" after a capture", "GammaGuard is not recording the probe. Check it in GammaGuard and try again."],
    ["Right-click does nothing", "You are not on the Measurement page, or Start has not been pressed."],
    ["Save & Continue is greyed out in New", "Fill in Drillhole ID, Project and Campaign; the red box shows what is missing or already exists."],
    ["Sidebar shows Offline", "The backend cannot be reached. Measuring still works; upload later."],
  ], [3200, CONTENT_WIDTH - 3200]),
  spacer(),
  p("Your measurements are stored per Windows user in %LocalAppData%\\CT007_Measurement_Studio\\measurements.db."),
];

const doc = new Document({
  creator: "Gamma Core Log",
  title: "Gamma Core Log — User Guide",
  styles: {
    default: { document: { run: { font: FONT, size: 21 } } },
    paragraphStyles: [
      { id: "Title", name: "Title", basedOn: "Normal", run: { font: FONT, size: 52, bold: true, color: "111827" },
        paragraph: { spacing: { before: 0, after: 60 }, keepNext: true } },
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { font: FONT, size: 32, bold: true, color: "1E3A8A" },
        paragraph: { spacing: { before: 360, after: 160 }, keepNext: true, outlineLevel: 0 } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { font: FONT, size: 26, bold: true, color: "1E3A8A" },
        paragraph: { spacing: { before: 240, after: 120 }, keepNext: true, outlineLevel: 1 } },
    ],
  },
  numbering: { config: numberingConfigs },
  sections: [{
    properties: { page: { margin: { top: 1440, bottom: 1440, left: 1440, right: 1440 } } },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER,
      children: [new TextRun({ text: "Gamma Core Log User Guide · page ", size: 16, color: "777777" }),
        new TextRun({ children: [PageNumber.CURRENT], size: 16, color: "777777" })] })] }) },
    children,
  }],
});

Packer.toBuffer(doc).then((buf) => {
  const out = path.join(root, "docs", "Gamma-Core-Log-User-Guide.docx");
  fs.writeFileSync(out, buf);
  console.log("wrote", out, buf.length, "bytes");
});
