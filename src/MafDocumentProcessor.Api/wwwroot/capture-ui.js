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

  function createEditableRegions(members) {
    return (Array.isArray(members) ? members : []).map((member, index) => ({
      id: member?.memberId ?? `region-${index + 1}`,
      bounds: normalizeBounds(member?.region?.bounds)
    }));
  }

  function createRegionEditSession(source, members) {
    const originalRegions = cloneRegions(createEditableRegions(members));
    return {
      sourceItemId: source?.sourceItemId ?? "",
      sourceIndex: Number(source?.index),
      originalRegions,
      regions: cloneRegions(originalRegions)
    };
  }

  function hasRegionChanges(session) {
    const originalRegions = Array.isArray(session?.originalRegions) ? session.originalRegions : [];
    const regions = Array.isArray(session?.regions) ? session.regions : [];
    if (originalRegions.length !== regions.length) {
      return true;
    }

    return regions.some((region, index) => {
      const original = originalRegions[index];
      if (!original || region?.id !== original.id) {
        return true;
      }

      const bounds = normalizeBounds(region?.bounds);
      const originalBounds = normalizeBounds(original.bounds);
      return ["x", "y", "width", "height"]
        .some((fieldName) => bounds[fieldName] !== originalBounds[fieldName]);
    });
  }

  function clampBounds(bounds, minimumSize = 0.02) {
    requirePositiveFinite(minimumSize, "minimumSize");
    if (minimumSize > 1) {
      throw new RangeError("minimumSize cannot exceed one.");
    }

    const values = {
      x: Number(bounds?.x),
      y: Number(bounds?.y),
      width: Number(bounds?.width),
      height: Number(bounds?.height)
    };
    if (!Object.values(values).every(Number.isFinite)) {
      throw new RangeError("Region bounds must contain finite numbers.");
    }

    const width = Math.min(1, Math.max(minimumSize, values.width));
    const height = Math.min(1, Math.max(minimumSize, values.height));
    return {
      x: roundNormalized(Math.min(1 - width, Math.max(0, values.x))),
      y: roundNormalized(Math.min(1 - height, Math.max(0, values.y))),
      width: roundNormalized(width),
      height: roundNormalized(height)
    };
  }

  function moveBounds(bounds, deltaX, deltaY) {
    const normalized = normalizeBounds(bounds);
    return clampBounds({
      ...normalized,
      x: normalized.x + Number(deltaX),
      y: normalized.y + Number(deltaY)
    });
  }

  function resizeBounds(bounds, handle, deltaX, deltaY, minimumSize = 0.02) {
    const normalized = normalizeBounds(bounds);
    let left = normalized.x;
    let top = normalized.y;
    let right = normalized.x + normalized.width;
    let bottom = normalized.y + normalized.height;
    const dx = Number(deltaX);
    const dy = Number(deltaY);
    if (![dx, dy].every(Number.isFinite) || !["nw", "ne", "sw", "se"].includes(handle)) {
      throw new RangeError("Resize requires a corner handle and finite deltas.");
    }

    if (handle.includes("w")) left = Math.min(right - minimumSize, Math.max(0, left + dx));
    if (handle.includes("e")) right = Math.max(left + minimumSize, Math.min(1, right + dx));
    if (handle.includes("n")) top = Math.min(bottom - minimumSize, Math.max(0, top + dy));
    if (handle.includes("s")) bottom = Math.max(top + minimumSize, Math.min(1, bottom + dy));
    return clampBounds({ x: left, y: top, width: right - left, height: bottom - top }, minimumSize);
  }

  function reorderRegions(regions, fromIndex, toIndex) {
    const copy = (Array.isArray(regions) ? regions : []).slice();
    if (!Number.isInteger(fromIndex) || !Number.isInteger(toIndex)
      || fromIndex < 0 || fromIndex >= copy.length || toIndex < 0 || toIndex >= copy.length) {
      return copy;
    }

    const [region] = copy.splice(fromIndex, 1);
    copy.splice(toIndex, 0, region);
    return copy;
  }

  function serializeRegionOverrides(editedSources) {
    return {
      sources: (Array.isArray(editedSources) ? editedSources : [])
        .slice()
        .sort((left, right) => Number(left.sourceIndex) - Number(right.sourceIndex))
        .map((source) => ({
          sourceIndex: Number(source.sourceIndex),
          regions: (Array.isArray(source.regions) ? source.regions : []).map((region) => ({
            bounds: normalizeBounds(region?.bounds)
          }))
        }))
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

  function cloneRegions(regions) {
    return (Array.isArray(regions) ? regions : []).map((region) => ({
      ...region,
      bounds: { ...normalizeBounds(region?.bounds) }
    }));
  }

  function toPercentageNumber(value) {
    return Number((value * 100).toFixed(4));
  }

  function roundNormalized(value) {
    return Number(value.toFixed(6));
  }

  function requirePositiveFinite(value, parameterName) {
    if (!Number.isFinite(value) || value <= 0) {
      throw new RangeError(`${parameterName} must be a positive finite number.`);
    }
  }

  return Object.freeze({
    clampBounds,
    chooseMemberId,
    createEditableRegions,
    createRegionEditSession,
    getDispositionPresentation,
    getMemberAccessibleLabel,
    getMembersForSource,
    getRegionShape,
    hasRegionChanges,
    hasMatchingOrientation,
    moveBounds,
    projectBounds,
    reorderRegions,
    resizeBounds,
    serializeRegionOverrides,
    summarizeCapture
  });
}));
