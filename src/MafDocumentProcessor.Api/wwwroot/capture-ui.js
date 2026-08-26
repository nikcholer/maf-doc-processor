(function attachCaptureUi(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }

  if (root) {
    root.CaptureUi = api;
  }
}(typeof globalThis !== "undefined" ? globalThis : this, () => {
  const presentations = Object.freeze({
    Accepted: Object.freeze({ symbol: "✓", label: "Accepted", className: "accepted" }),
    Review: Object.freeze({ symbol: "?", label: "Needs review", className: "review" }),
    Rejected: Object.freeze({ symbol: "×", label: "Rejected", className: "rejected" })
  });

  function getDispositionPresentation(disposition) {
    return presentations[disposition] ?? presentations.Rejected;
  }

  function getMembersForSource(payload, sourceItemId) {
    const members = Array.isArray(payload?.members) ? payload.members : [];
    return members
      .filter((member) => member.sourceItemId === sourceItemId)
      .slice()
      .sort((left, right) => Number(left.index) - Number(right.index));
  }

  function chooseMemberId(payload, preferredMemberId = null) {
    const members = Array.isArray(payload?.members) ? payload.members : [];
    if (preferredMemberId && members.some((member) => member.memberId === preferredMemberId)) {
      return preferredMemberId;
    }

    return members[0]?.memberId ?? null;
  }

  function summarizeCapture(payload) {
    const sources = Array.isArray(payload?.sources) ? payload.sources : [];
    const members = Array.isArray(payload?.members) ? payload.members : [];
    return {
      sourceCount: sources.length,
      memberCount: members.length,
      acceptedCount: members.filter((member) => member.disposition === "Accepted").length,
      reviewCount: members.filter((member) => member.disposition === "Review").length,
      rejectedCount: members.filter((member) => member.disposition === "Rejected").length,
      failedSourceCount: sources.filter((source) => source.status === "Failed").length
    };
  }

  function getRegionShape(region) {
    const bounds = normalizeBounds(region?.bounds);
    const outline = Array.isArray(region?.outline) && region.outline.length === 4
      ? region.outline.map(normalizePoint)
      : null;

    return {
      type: outline ? "polygon" : "rectangle",
      points: outline?.map((point) => `${toPercentageNumber(point.x)},${toPercentageNumber(point.y)}`).join(" ") ?? null,
      bounds: {
        x: toPercentageNumber(bounds.x),
        y: toPercentageNumber(bounds.y),
        width: toPercentageNumber(bounds.width),
        height: toPercentageNumber(bounds.height)
      },
      marker: outline?.[0] ?? { x: bounds.x, y: bounds.y }
    };
  }

  function projectBounds(bounds, displayWidth, displayHeight) {
    const normalized = normalizeBounds(bounds);
    requirePositiveFinite(displayWidth, "displayWidth");
    requirePositiveFinite(displayHeight, "displayHeight");
    return {
      x: normalized.x * displayWidth,
      y: normalized.y * displayHeight,
      width: normalized.width * displayWidth,
      height: normalized.height * displayHeight
    };
  }

  function getMemberAccessibleLabel(member) {
    const presentation = getDispositionPresentation(member?.disposition);
    const category = member?.result?.category ? `, ${member.result.category}` : "";
    const confidenceValue = Number(member?.region?.confidence);
    const confidence = Number.isFinite(confidenceValue)
      ? `, detection confidence ${Math.round(confidenceValue * 100)} percent`
      : "";
    return `${member?.memberId ?? "Document region"}: ${presentation.label}${category}${confidence}`;
  }

  function hasMatchingOrientation(previewWidth, previewHeight, metadata) {
    const sourceWidth = Number(metadata?.orientedWidthPixels);
    const sourceHeight = Number(metadata?.orientedHeightPixels);
    if (![previewWidth, previewHeight, sourceWidth, sourceHeight].every((value) => Number.isFinite(value) && value > 0)) {
      return true;
    }

    const previewRatio = previewWidth / previewHeight;
    const sourceRatio = sourceWidth / sourceHeight;
    return Math.abs(previewRatio - sourceRatio) <= 0.01;
  }

  function normalizeBounds(bounds) {
    const normalized = {
      x: Number(bounds?.x),
      y: Number(bounds?.y),
      width: Number(bounds?.width),
      height: Number(bounds?.height)
    };
    if (!Object.values(normalized).every(Number.isFinite)
      || normalized.x < 0
      || normalized.y < 0
      || normalized.width <= 0
      || normalized.height <= 0
      || normalized.x + normalized.width > 1.000001
      || normalized.y + normalized.height > 1.000001) {
      throw new RangeError("Region bounds must be finite, positive, and contained in normalized image space.");
    }

    return normalized;
  }

  function normalizePoint(point) {
    const normalized = { x: Number(point?.x), y: Number(point?.y) };
    if (!Object.values(normalized).every(Number.isFinite)
      || normalized.x < 0
      || normalized.x > 1
      || normalized.y < 0
      || normalized.y > 1) {
      throw new RangeError("Outline points must be contained in normalized image space.");
    }

    return normalized;
  }

  function toPercentageNumber(value) {
    return Number((value * 100).toFixed(4));
  }

  function requirePositiveFinite(value, parameterName) {
    if (!Number.isFinite(value) || value <= 0) {
      throw new RangeError(`${parameterName} must be a positive finite number.`);
    }
  }

  return Object.freeze({
    chooseMemberId,
    getDispositionPresentation,
    getMemberAccessibleLabel,
    getMembersForSource,
    getRegionShape,
    hasMatchingOrientation,
    projectBounds,
    summarizeCapture
  });
}));
