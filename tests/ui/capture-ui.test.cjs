const assert = require("node:assert/strict");
const test = require("node:test");
const CaptureUi = require("../../src/MafDocumentProcessor.Api/wwwroot/capture-ui.js");

test("dispositions have distinct symbols and accessible labels", () => {
  assert.deepEqual(CaptureUi.getDispositionPresentation("Accepted"), {
    symbol: "✓",
    label: "Accepted",
    className: "accepted"
  });
  assert.equal(CaptureUi.getDispositionPresentation("Review").symbol, "?");
  assert.equal(CaptureUi.getDispositionPresentation("Rejected").symbol, "×");
  assert.equal(new Set(["Accepted", "Review", "Rejected"]
    .map((value) => CaptureUi.getDispositionPresentation(value).symbol)).size, 3);
});

test("member selection remains stable and falls back to the first returned member", () => {
  const payload = {
    members: [
      { memberId: "second", sourceItemId: "source-001", index: 2 },
      { memberId: "first", sourceItemId: "source-001", index: 1 },
      { memberId: "third", sourceItemId: "source-002", index: 3 }
    ]
  };

  assert.equal(CaptureUi.chooseMemberId(payload, "third"), "third");
  assert.equal(CaptureUi.chooseMemberId(payload, "missing"), "second");
  assert.deepEqual(
    CaptureUi.getMembersForSource(payload, "source-001").map((member) => member.memberId),
    ["first", "second"]);
});

test("outline coordinates are preferred and stay in normalized percentage space", () => {
  const shape = CaptureUi.getRegionShape({
    bounds: { x: 0.1, y: 0.2, width: 0.6, height: 0.5 },
    outline: [
      { x: 0.12, y: 0.2 },
      { x: 0.69, y: 0.22 },
      { x: 0.68, y: 0.7 },
      { x: 0.1, y: 0.68 }
    ]
  });

  assert.equal(shape.type, "polygon");
  assert.equal(shape.points, "12,20 69,22 68,70 10,68");
  assert.deepEqual(shape.bounds, { x: 10, y: 20, width: 60, height: 50 });
});

test("bounds fall back to an axis-aligned percentage rectangle", () => {
  const shape = CaptureUi.getRegionShape({
    bounds: { x: 0.125, y: 0.25, width: 0.5, height: 0.625 },
    outline: null
  });

  assert.equal(shape.type, "rectangle");
  assert.equal(shape.points, null);
  assert.deepEqual(shape.bounds, { x: 12.5, y: 25, width: 50, height: 62.5 });
});

test("normalized bounds project correctly at desktop, mobile, and rotated preview sizes", () => {
  const bounds = { x: 0.1, y: 0.25, width: 0.5, height: 0.4 };
  assert.deepEqual(CaptureUi.projectBounds(bounds, 1000, 600), {
    x: 100,
    y: 150,
    width: 500,
    height: 240
  });
  assert.deepEqual(CaptureUi.projectBounds(bounds, 320, 192), {
    x: 32,
    y: 48,
    width: 160,
    height: 76.80000000000001
  });
  assert.deepEqual(CaptureUi.projectBounds(bounds, 900, 1600), {
    x: 90,
    y: 400,
    width: 450,
    height: 640
  });
});

test("orientation comparison accepts scaled oriented previews and rejects swapped axes", () => {
  const metadata = { orientedWidthPixels: 900, orientedHeightPixels: 1600 };
  assert.equal(CaptureUi.hasMatchingOrientation(450, 800, metadata), true);
  assert.equal(CaptureUi.hasMatchingOrientation(800, 450, metadata), false);
});

test("capture summaries keep accepted, review, rejected, and source failures visible", () => {
  const summary = CaptureUi.summarizeCapture({
    sources: [{ status: "Succeeded" }, { status: "Failed" }],
    members: [
      { disposition: "Accepted" },
      { disposition: "Review" },
      { disposition: "Rejected" }
    ]
  });

  assert.deepEqual(summary, {
    sourceCount: 2,
    memberCount: 3,
    acceptedCount: 1,
    reviewCount: 1,
    rejectedCount: 1,
    failedSourceCount: 1
  });
});

test("region labels expose disposition, category, and confidence without relying on color", () => {
  const label = CaptureUi.getMemberAccessibleLabel({
    memberId: "source-001-document-002",
    disposition: "Review",
    result: { category: "Receipt" },
    region: { confidence: 0.83 }
  });

  assert.equal(
    label,
    "source-001-document-002: Needs review, Receipt, detection confidence 83 percent");
});

test("drag and keyboard movement clamp rectangles to normalized image bounds", () => {
  const original = { x: 0.1, y: 0.2, width: 0.4, height: 0.5 };

  assert.deepEqual(CaptureUi.moveBounds(original, 0.15, -0.1), {
    x: 0.25, y: 0.1, width: 0.4, height: 0.5
  });
  assert.deepEqual(CaptureUi.moveBounds(original, 1, 1), {
    x: 0.6, y: 0.5, width: 0.4, height: 0.5
  });
});

test("corner resize preserves a minimum size and stays inside the image", () => {
  const original = { x: 0.2, y: 0.2, width: 0.4, height: 0.4 };

  assert.deepEqual(CaptureUi.resizeBounds(original, "se", 0.3, 0.5), {
    x: 0.2, y: 0.2, width: 0.7, height: 0.8
  });
  assert.deepEqual(CaptureUi.resizeBounds(original, "nw", 0.5, 0.5), {
    x: 0.58,
    y: 0.58,
    width: 0.02,
    height: 0.02
  });
});

test("editor regions support add, delete, reorder, and partial-source serialization", () => {
  const regions = CaptureUi.createEditableRegions([
    { memberId: "one", region: { bounds: { x: 0.1, y: 0.1, width: 0.3, height: 0.4 } } },
    { memberId: "two", region: { bounds: { x: 0.5, y: 0.2, width: 0.3, height: 0.4 } } }
  ]);
  const added = [...regions, { id: "three", bounds: { x: 0.35, y: 0.35, width: 0.3, height: 0.3 } }];
  const reordered = CaptureUi.reorderRegions(added, 2, 0);
  const afterDelete = reordered.filter((region) => region.id !== "one");

  assert.deepEqual(afterDelete.map((region) => region.id), ["three", "two"]);
  assert.deepEqual(CaptureUi.serializeRegionOverrides([
    { sourceIndex: 2, regions: [] },
    { sourceIndex: 1, regions: afterDelete }
  ]), {
    sources: [
      {
        sourceIndex: 1,
        regions: [
          { bounds: { x: 0.35, y: 0.35, width: 0.3, height: 0.3 } },
          { bounds: { x: 0.5, y: 0.2, width: 0.3, height: 0.4 } }
        ]
      },
      { sourceIndex: 2, regions: [] }
    ]
  });
});

test("region edit sessions isolate the working copy and begin unchanged", () => {
  const members = [
    { memberId: "one", region: { bounds: { x: 0.1, y: 0.1, width: 0.3, height: 0.4 } } },
    { memberId: "two", region: { bounds: { x: 0.5, y: 0.2, width: 0.3, height: 0.4 } } }
  ];
  const session = CaptureUi.createRegionEditSession(
    { sourceItemId: "source-001", index: 1 },
    members);

  assert.equal(CaptureUi.hasRegionChanges(session), false);
  assert.notEqual(session.regions, session.originalRegions);
  assert.notEqual(session.regions[0].bounds, session.originalRegions[0].bounds);

  session.regions[0].bounds.x = 0.2;

  assert.equal(CaptureUi.hasRegionChanges(session), true);
  assert.equal(session.originalRegions[0].bounds.x, 0.1);
  assert.equal(members[0].region.bounds.x, 0.1);
});

test("region edit sessions detect add, delete, and reorder without marking entry dirty", () => {
  const createSession = () => CaptureUi.createRegionEditSession(
    { sourceItemId: "source-001", index: 1 },
    [
      { memberId: "one", region: { bounds: { x: 0.1, y: 0.1, width: 0.3, height: 0.4 } } },
      { memberId: "two", region: { bounds: { x: 0.5, y: 0.2, width: 0.3, height: 0.4 } } }
    ]);

  const added = createSession();
  added.regions.push({ id: "three", bounds: { x: 0.3, y: 0.3, width: 0.2, height: 0.2 } });
  assert.equal(CaptureUi.hasRegionChanges(added), true);

  const deleted = createSession();
  deleted.regions.pop();
  assert.equal(CaptureUi.hasRegionChanges(deleted), true);

  const reordered = createSession();
  reordered.regions = CaptureUi.reorderRegions(reordered.regions, 1, 0);
  assert.equal(CaptureUi.hasRegionChanges(reordered), true);
});

test("a saved region edit serializes only its active source working copy", () => {
  const session = CaptureUi.createRegionEditSession(
    { sourceItemId: "source-002", index: 2 },
    [{ memberId: "one", region: { bounds: { x: 0.1, y: 0.1, width: 0.3, height: 0.4 } } }]);
  session.regions[0].bounds = CaptureUi.moveBounds(session.regions[0].bounds, 0.1, 0.05);

  assert.deepEqual(CaptureUi.serializeRegionOverrides([session]), {
    sources: [{
      sourceIndex: 2,
      regions: [{ bounds: { x: 0.2, y: 0.15, width: 0.3, height: 0.4 } }]
    }]
  });
});
