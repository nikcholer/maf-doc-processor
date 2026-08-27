const form = document.querySelector("#uploadForm");
const modeSwitch = document.querySelector(".mode-switch");
const modeInputs = [...document.querySelectorAll('input[name="processingMode"]')];
const imageInput = document.querySelector("#imageInput");
const sourceIdInput = document.querySelector("#sourceId");
const dropZone = document.querySelector("#dropZone");
const dropCopy = document.querySelector("#dropCopy");
const dropTitle = document.querySelector("#dropTitle");
const fileGuidance = document.querySelector("#fileGuidance");
const selectedFile = document.querySelector("#selectedFile");
const intakePreview = document.querySelector("#intakePreview");
const intakeTitle = document.querySelector("#intakeTitle");
const processButton = document.querySelector("#processButton");
const healthDot = document.querySelector("#healthDot");
const healthText = document.querySelector("#healthText");
const resultSurface = document.querySelector("#resultSurface");
const resultEyebrow = document.querySelector("#resultEyebrow");
const resultTitle = document.querySelector("#resultTitle");
const statusPill = document.querySelector("#statusPill");
const progressStrip = document.querySelector("#progressStrip");
const progressText = document.querySelector("#progressText");
const metricOneLabel = document.querySelector("#metricOneLabel");
const metricTwoLabel = document.querySelector("#metricTwoLabel");
const categoryMetric = document.querySelector("#categoryMetric");
const decisionMetric = document.querySelector("#decisionMetric");
const tokensMetric = document.querySelector("#tokensMetric");
const latencyMetric = document.querySelector("#latencyMetric");
const costMetric = document.querySelector("#costMetric");
const singleResult = document.querySelector("#singleResult");
const extractedData = document.querySelector("#extractedData");
const policyReasons = document.querySelector("#policyReasons");
const captureResult = document.querySelector("#captureResult");
const captureSummary = document.querySelector("#captureSummary");
const sourceGrid = document.querySelector("#sourceGrid");
const memberTitle = document.querySelector("#memberTitle");
const memberDetail = document.querySelector("#memberDetail");
const jsonPanel = document.querySelector("#jsonPanel");
const rawJson = document.querySelector("#rawJson");
const resultHeader = document.querySelector(".result-header");
const metricsGrid = document.querySelector(".metrics-grid");

const fieldLabels = {
  storeName: "Store",
  totalAmount: "Total",
  purchaseDate: "Purchase date",
  paymentMethod: "Payment method",
  currencyCode: "Currency",
  title: "Title",
  items: "Items",
  notes: "Notes",
  quadrantTotals: "Quadrant totals",
  givenCells: "Given cells",
  reportNumber: "Report number",
  claimantName: "Claimant",
  periodStart: "Period start",
  periodEnd: "Period end",
  claimedTotal: "Claimed total",
  lines: "Lines",
  visibleApprovalStatus: "Visible approval",
  receiptReference: "Receipt reference"
};

const svgNamespace = "http://www.w3.org/2000/svg";
const singleMaxUploadBytes = 5 * 1024 * 1024;
const captureMaxSourceBytes = 10 * 1024 * 1024;
const captureMaxAggregateBytes = 25 * 1024 * 1024;
const captureMaxSourceCount = 5;
const requestTimeoutByMode = { single: 65 * 1000, capture: 180 * 1000 };

let processingMode = "single";
let selectedFiles = [];
let selectedMemberId = null;
let capturePayload = null;
let selectedEditRegionId = null;
let nextEditRegionId = 1;
let activeRegionEdit = null;
let regionEditSubmissionRequested = false;
let regionEditBusy = false;
let regionEditError = null;
const previewUrls = new Map();

checkHealth();

modeInputs.forEach((input) => {
  input.addEventListener("change", () => {
    if (!input.checked || input.value === processingMode) {
      return;
    }

    processingMode = input.value;
    resetFiles();
    resetResult();
    configureMode();
  });
});

imageInput.addEventListener("change", () => {
  setSelectedFiles([...imageInput.files]);
});

["dragenter", "dragover"].forEach((eventName) => {
  dropZone.addEventListener(eventName, (event) => {
    event.preventDefault();
    dropZone.classList.add("dragging");
  });
});

["dragleave", "drop"].forEach((eventName) => {
  dropZone.addEventListener(eventName, (event) => {
    event.preventDefault();
    dropZone.classList.remove("dragging");
  });
});

dropZone.addEventListener("drop", (event) => {
  const droppedFiles = [...(event.dataTransfer?.files ?? [])];
  if (droppedFiles.length === 0) {
    return;
  }

  const files = processingMode === "capture" ? droppedFiles : droppedFiles.slice(0, 1);
  const transfer = new DataTransfer();
  files.forEach((file) => transfer.items.add(file));
  imageInput.files = transfer.files;
  imageInput.dispatchEvent(new Event("change"));
});

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  const isRegionEditRequest = processingMode === "capture"
    && regionEditSubmissionRequested
    && activeRegionEdit !== null;
  regionEditSubmissionRequested = false;
  if (activeRegionEdit && !isRegionEditRequest) {
    return;
  }

  const validationError = validateFiles(selectedFiles);
  if (validationError) {
    if (isRegionEditRequest) {
      renderRegionEditError(validationError);
    } else {
      renderRequestError(validationError);
    }
    return;
  }

  const body = new FormData();
  const fieldName = processingMode === "capture" ? "images" : "image";
  selectedFiles.forEach((file) => body.append(fieldName, file));
  const sourceId = sourceIdInput.value.trim();
  if (sourceId.length > 0) {
    body.append("sourceId", sourceId);
  }
  if (isRegionEditRequest) {
    const overrides = CaptureUi.serializeRegionOverrides([activeRegionEdit]);
    body.append("regionOverrides", JSON.stringify(overrides));
  }

  if (isRegionEditRequest) {
    setRegionEditBusy(true);
  } else {
    setBusy(true);
  }
  if (processingMode === "capture" && !isRegionEditRequest) {
    renderCapturePending();
  }

  const controller = new AbortController();
  const timeoutMs = requestTimeoutByMode[processingMode];
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(
      processingMode === "capture" ? "/api/document-captures/process" : "/api/documents/process",
      {
        method: "POST",
        headers: { "X-Correlation-ID": crypto.randomUUID() },
        body,
        signal: controller.signal
      });
    const responseText = await response.text();
    const parsedPayload = parseJsonPayload(responseText);

    if (!parsedPayload.ok) {
      const requestError = buildNonJsonResponseError(response, responseText);
      if (isRegionEditRequest) {
        renderRegionEditError(requestError);
      } else {
        renderRequestError(requestError);
      }
      return;
    }

    if (!response.ok) {
      if (isRegionEditRequest) {
        renderRegionEditError(parsedPayload.value);
      } else {
        renderRequestError(parsedPayload.value);
      }
      return;
    }

    if (processingMode === "capture") {
      const editedSourceId = isRegionEditRequest ? activeRegionEdit?.sourceItemId : null;
      if (isRegionEditRequest) {
        endRegionEdit();
      }
      renderCaptureSuccess(parsedPayload.value);
      if (editedSourceId) {
        queueMicrotask(() => focusEditRegionsButton(editedSourceId));
      }
    } else {
      renderDocumentSuccess(parsedPayload.value);
    }
  } catch (error) {
    const timedOut = error instanceof DOMException && error.name === "AbortError";
    const requestError = {
      code: timedOut ? "request_timeout" : "request_failed",
      message: timedOut
        ? `Processing exceeded ${Math.round(timeoutMs / 1000)} seconds. Check the API terminal logs for the last completed stage.`
        : error instanceof Error ? error.message : "The request failed.",
      target: null,
      traceId: "-"
    };
    if (isRegionEditRequest) {
      renderRegionEditError(requestError);
    } else {
      renderRequestError(requestError);
    }
  } finally {
    clearTimeout(timeoutId);
    if (isRegionEditRequest) {
      setRegionEditBusy(false);
    } else {
      setBusy(false);
    }
  }
});

async function checkHealth() {
  try {
    const response = await fetch("/health");
    const payload = await response.json();
    healthDot.classList.toggle("ready", response.ok && payload.apiKeyConfigured);
    healthDot.classList.toggle("warn", response.ok && !payload.apiKeyConfigured);
    healthDot.classList.toggle("failed", !response.ok);
    healthText.textContent = response.ok
      ? `${payload.aiProvider} / ${payload.imageModel}`
      : "API unavailable";
  } catch {
    healthDot.classList.add("failed");
    healthText.textContent = "API unavailable";
  }
}

function configureMode() {
  const isCapture = processingMode === "capture";
  imageInput.multiple = isCapture;
  imageInput.name = isCapture ? "images" : "image";
  intakeTitle.textContent = isCapture ? "Composite capture" : "One document";
  dropTitle.textContent = isCapture ? "Drop one or more capture images" : "Drop a document image";
  fileGuidance.textContent = isCapture
    ? "Up to five PNG or JPEG sources. Every detected document gets its own result."
    : "PNG or JPEG, up to the configured local API limit.";
  processButton.firstElementChild.textContent = isCapture ? "Survey capture set" : "Process document";
}

function setSelectedFiles(files) {
  releasePreviewUrls();
  selectedEditRegionId = null;
  activeRegionEdit = null;
  regionEditError = null;
  setRegionEditModal(false);
  selectedFiles = files;
  intakePreview.replaceChildren();
  const isEmpty = selectedFiles.length === 0;
  intakePreview.hidden = isEmpty;
  dropCopy.hidden = !isEmpty;
  dropZone.classList.toggle("has-preview", !isEmpty);

  if (isEmpty) {
    selectedFile.textContent = "No file selected";
    return;
  }

  const aggregateBytes = selectedFiles.reduce((total, file) => total + file.size, 0);
  selectedFile.textContent = selectedFiles.length === 1
    ? `${selectedFiles[0].name} (${formatBytes(selectedFiles[0].size)})`
    : `${selectedFiles.length} sources / ${formatBytes(aggregateBytes)} total`;

  selectedFiles.forEach((file, index) => {
    const figure = document.createElement("figure");
    figure.className = "intake-thumbnail";
    const image = document.createElement("img");
    image.src = getPreviewUrl(file);
    image.alt = "";
    const caption = document.createElement("figcaption");
    caption.textContent = `${String(index + 1).padStart(2, "0")} · ${file.name}`;
    figure.append(image, caption);
    intakePreview.appendChild(figure);
  });
}

function resetFiles() {
  imageInput.value = "";
  selectedFiles = [];
  selectedMemberId = null;
  capturePayload = null;
  selectedEditRegionId = null;
  activeRegionEdit = null;
  regionEditError = null;
  setRegionEditModal(false);
  releasePreviewUrls();
  setSelectedFiles([]);
}

function releasePreviewUrls() {
  previewUrls.forEach((url) => URL.revokeObjectURL(url));
  previewUrls.clear();
}

function getPreviewUrl(file) {
  if (!previewUrls.has(file)) {
    previewUrls.set(file, URL.createObjectURL(file));
  }

  return previewUrls.get(file);
}

function validateFiles(files) {
  if (files.length === 0) {
    return {
      code: "missing_file",
      message: processingMode === "capture"
        ? "Choose at least one PNG or JPEG capture image before processing."
        : "Choose a PNG or JPEG document image before processing.",
      target: processingMode === "capture" ? "images" : "image",
      traceId: "-"
    };
  }

  if (processingMode === "single" && files[0].size > singleMaxUploadBytes) {
    return localFileError(`Choose an image smaller than ${formatBytes(singleMaxUploadBytes)}.`);
  }

  if (processingMode === "capture") {
    if (files.length > captureMaxSourceCount) {
      return localFileError(`Choose at most ${captureMaxSourceCount} capture images.`);
    }

    const oversized = files.find((file) => file.size > captureMaxSourceBytes);
    if (oversized) {
      return localFileError(`${oversized.name} exceeds the ${formatBytes(captureMaxSourceBytes)} per-source limit.`);
    }

    const aggregateBytes = files.reduce((total, file) => total + file.size, 0);
    if (aggregateBytes > captureMaxAggregateBytes) {
      return localFileError(`The selected images exceed the ${formatBytes(captureMaxAggregateBytes)} combined limit.`);
    }
  }

  return null;
}

function localFileError(message) {
  return { code: "file_too_large", message, target: processingMode === "capture" ? "images" : "image", traceId: "-" };
}

function setBusy(isBusy) {
  processButton.disabled = isBusy;
  imageInput.disabled = isBusy;
  sourceIdInput.disabled = isBusy;
  modeInputs.forEach((input) => {
    input.disabled = isBusy;
  });
  resultSurface.setAttribute("aria-busy", String(isBusy));
  progressStrip.hidden = !isBusy;
  if (isBusy) {
    resultTitle.textContent = processingMode === "capture" ? "Surveying capture set" : "Processing document";
    progressText.textContent = processingMode === "capture"
      ? `Detecting and routing documents across ${selectedFiles.length} source${selectedFiles.length === 1 ? "" : "s"}`
      : "Classifying and extracting the selected document";
    statusPill.textContent = "Running";
    statusPill.className = "status-pill running";
    processButton.firstElementChild.textContent = processingMode === "capture" ? "Surveying..." : "Processing...";
    jsonPanel.open = false;
  } else {
    processButton.firstElementChild.textContent = processingMode === "capture" ? "Survey capture set" : "Process document";
  }
}

function resetResult() {
  resultEyebrow.textContent = "Workflow result";
  resultTitle.textContent = "Waiting for upload";
  statusPill.textContent = "Idle";
  statusPill.className = "status-pill";
  metricOneLabel.textContent = processingMode === "capture" ? "Sources" : "Category";
  metricTwoLabel.textContent = processingMode === "capture" ? "Documents" : "Decision";
  [categoryMetric, decisionMetric, tokensMetric, latencyMetric, costMetric].forEach((element) => {
    element.textContent = "-";
  });
  singleResult.hidden = false;
  captureResult.hidden = true;
  rawJson.textContent = "{}";
  jsonPanel.open = false;
}

function renderDocumentSuccess(payload) {
  singleResult.hidden = false;
  captureResult.hidden = true;
  metricOneLabel.textContent = "Category";
  metricTwoLabel.textContent = "Decision";
  const documentResult = payload.document;
  const policy = documentResult?.policyResult ?? documentResult?.expensePolicy;
  const humanReview = payload.humanReview;
  const hasHumanReview = humanReview?.status && humanReview.status !== "NotRequired";
  const decision = policy?.decision ?? (hasHumanReview ? humanReview.status : (payload.isSuccess ? "Complete" : "Needs review"));

  resultEyebrow.textContent = "Workflow result";
  resultTitle.textContent = `${payload.category} processed`;
  categoryMetric.textContent = payload.category ?? "-";
  decisionMetric.textContent = decision;
  renderModelUsage(payload.modelUsage);
  statusPill.textContent = decision;
  statusPill.className = `status-pill ${decision === "Approved" || decision === "Complete" ? "approved" : "review"}`;

  renderData(extractedData, documentResult?.data ?? {});
  renderReasons(policyReasons, [
    ...(humanReview?.reasons ?? []),
    ...(policy?.reasons ?? []),
    ...(payload.warnings ?? []),
    ...(payload.errors ?? [])
  ]);
  showRawJson(payload);
}

function renderCapturePending() {
  singleResult.hidden = true;
  captureResult.hidden = false;
  metricOneLabel.textContent = "Sources";
  metricTwoLabel.textContent = "Documents";
  categoryMetric.textContent = selectedFiles.length.toString();
  decisionMetric.textContent = "Scanning";
  tokensMetric.textContent = "-";
  latencyMetric.textContent = "-";
  costMetric.textContent = "-";
  captureSummary.textContent = "Sources stay visible while the bounded workflow runs.";
  sourceGrid.replaceChildren();
  memberTitle.textContent = "Awaiting regions";
  memberDetail.replaceChildren(createParagraph("Detection and document processing are in progress.", "empty-state"));

  selectedFiles.forEach((file, index) => {
    const source = {
      index: index + 1,
      sourceItemId: `source-${String(index + 1).padStart(3, "0")}`,
      metadata: { fileName: file.name },
      status: "Running",
      errors: [],
      warnings: []
    };
    sourceGrid.appendChild(createSourceCard(source, file, [], true));
  });
}

function renderCaptureSuccess(payload) {
  capturePayload = payload;
  selectedMemberId = CaptureUi.chooseMemberId(payload, selectedMemberId);
  singleResult.hidden = true;
  captureResult.hidden = false;
  metricOneLabel.textContent = "Sources";
  metricTwoLabel.textContent = "Documents";
  const summary = CaptureUi.summarizeCapture(payload);

  resultEyebrow.textContent = "Capture aggregate";
  resultTitle.textContent = `${summary.memberCount} document${summary.memberCount === 1 ? "" : "s"} surveyed`;
  categoryMetric.textContent = summary.sourceCount.toString();
  decisionMetric.textContent = summary.memberCount.toString();
  renderModelUsage(payload.modelUsage);
  statusPill.textContent = sentenceCase(payload.status ?? "Complete");
  statusPill.className = `status-pill ${captureStatusClass(payload.status)}`;
  captureSummary.textContent = [
    `${summary.acceptedCount} accepted`,
    `${summary.reviewCount} review`,
    `${summary.rejectedCount} rejected`,
    summary.failedSourceCount > 0 ? `${summary.failedSourceCount} failed source${summary.failedSourceCount === 1 ? "" : "s"}` : null
  ].filter(Boolean).join(" · ");

  renderCaptureSources();

  updateMemberSelection(selectedMemberId, false);
  showRawJson(payload, false);
}

function renderCaptureSources() {
  sourceGrid.replaceChildren();
  const sources = Array.isArray(capturePayload?.sources) ? capturePayload.sources : [];
  const visibleSources = activeRegionEdit
    ? sources.filter((source) => source.sourceItemId === activeRegionEdit.sourceItemId)
    : sources;
  visibleSources.forEach((source) => {
    const file = selectedFiles[Number(source.index) - 1];
    const members = CaptureUi.getMembersForSource(capturePayload, source.sourceItemId);
    sourceGrid.appendChild(createSourceCard(source, file, members, false));
  });
}

function createSourceCard(source, file, members, pending) {
  const card = document.createElement("article");
  card.className = `source-card source-${String(source.status).toLowerCase()}`;
  card.dataset.sourceId = source.sourceItemId;

  const header = document.createElement("header");
  const headingWrap = document.createElement("div");
  const sourceIndex = createParagraph(`Source ${String(source.index).padStart(2, "0")}`, "source-index");
  const heading = document.createElement("h4");
  heading.textContent = source.metadata?.fileName ?? file?.name ?? source.sourceItemId;
  headingWrap.append(sourceIndex, heading);
  const sourceStatus = document.createElement("span");
  sourceStatus.className = `source-status ${captureStatusClass(source.status)}`;
  sourceStatus.textContent = pending
    ? "Processing"
    : activeRegionEdit?.sourceItemId === source.sourceItemId ? "Editing regions"
      : source.detection?.usedRegionOverrides ? "Manual regions"
        : sentenceCase(source.status);
  const headerActions = document.createElement("div");
  headerActions.className = "source-header-actions";
  headerActions.appendChild(sourceStatus);
  const editState = activeRegionEdit?.sourceItemId === source.sourceItemId
    ? activeRegionEdit
    : null;
  if (!pending && !editState) {
    const editButton = createSmallButton("Edit regions");
    editButton.classList.add("edit-regions-button");
    editButton.dataset.editSourceId = source.sourceItemId;
    editButton.addEventListener("click", () => {
      beginEditingSource(source, members);
      renderCaptureSources();
      renderRegionEditorDetail(source.sourceItemId);
      queueMicrotask(focusInitialRegionEditControl);
    });
    headerActions.appendChild(editButton);
  }
  header.append(headingWrap, headerActions);

  if (editState) {
    card.classList.add("region-edit-dialog");
  }

  const frame = document.createElement("div");
  frame.className = "source-preview-frame";
  if (file) {
    const image = document.createElement("img");
    image.src = getPreviewUrl(file);
    image.alt = `Uploaded source ${source.index}: ${source.metadata?.fileName ?? file.name}`;
    image.addEventListener("load", () => {
      if (!pending && !CaptureUi.hasMatchingOrientation(image.naturalWidth, image.naturalHeight, source.metadata)) {
        card.classList.add("orientation-warning");
        appendFinding(card, "Preview orientation differs from the normalized API dimensions; inspect overlay alignment.", "warning");
      }
    });
    frame.appendChild(image);
  } else {
    frame.appendChild(createParagraph("Local source preview is unavailable.", "preview-unavailable"));
  }

  if (pending) {
    const scan = document.createElement("span");
    scan.className = "scan-line";
    scan.setAttribute("aria-hidden", "true");
    frame.appendChild(scan);
  } else if (editState) {
    frame.appendChild(createRegionEditorOverlay(source, editState));
  } else if (members.length > 0) {
    frame.appendChild(createOverlay(members));
  }

  const findings = [
    ...(source.errors ?? []).map((text) => ({ text, type: "error" })),
    ...(source.warnings ?? []).map((text) => ({ text, type: "warning" }))
  ];
  if (findings.length > 0) {
    const list = document.createElement("ul");
    list.className = "source-findings";
    findings.forEach((finding) => {
      const item = document.createElement("li");
      item.className = finding.type;
      item.textContent = finding.text;
      list.appendChild(item);
    });
    card.append(header, frame, list);
  } else {
    card.append(header, frame);
  }

  if (!pending) {
    const memberList = document.createElement("div");
    memberList.className = "member-list";
    memberList.setAttribute("aria-label", `Documents in source ${source.index}`);
    if (editState) {
      memberList.classList.add("editing");
      const toolbar = document.createElement("div");
      toolbar.className = "region-editor-toolbar";
      const guidance = createParagraph(
        "Drag to move. Use corner handles to resize. Arrow keys move; Alt + arrows resize.",
        "editor-guidance");
      const addButton = createSmallButton("+ Add region");
      addButton.classList.add("add-region-button");
      addButton.addEventListener("click", () => addEditorRegion(source.sourceItemId));
      toolbar.append(guidance, addButton);
      memberList.appendChild(toolbar);
      if (editState.regions.length === 0) {
        memberList.appendChild(createParagraph("No regions. Add one or reprocess this source as empty.", "empty-state compact"));
      } else {
        editState.regions.forEach((region, index) => {
          memberList.appendChild(createEditorRegionRow(source.sourceItemId, region, index, editState.regions.length));
        });
      }
      memberList.appendChild(createRegionEditorActions(source));
    } else if (members.length === 0) {
      memberList.appendChild(createParagraph("No document regions returned.", "empty-state compact"));
    } else {
      members.forEach((member) => memberList.appendChild(createMemberButton(member)));
    }
    card.appendChild(memberList);
  }

  return card;
}

function beginEditingSource(source, members) {
  activeRegionEdit = CaptureUi.createRegionEditSession(source, members);
  regionEditError = null;
  const regions = activeRegionEdit.regions;
  selectedEditRegionId = regions[0]?.id ?? null;
  setRegionEditModal(true);
}

function createRegionEditorActions(source) {
  const container = document.createElement("div");
  container.className = "region-editor-actions";
  container.setAttribute("role", "group");
  container.setAttribute("aria-label", `Region edit actions for source ${source.index}`);

  const feedback = document.createElement("div");
  feedback.className = "region-editor-feedback";
  const status = createParagraph("", "region-edit-status");
  status.setAttribute("aria-live", "polite");
  const error = createParagraph("", "region-edit-error");
  error.setAttribute("role", "alert");
  feedback.append(status, error);

  const buttons = document.createElement("div");
  const cancelButton = createSmallButton("Cancel");
  cancelButton.classList.add("cancel-region-edit-button");
  cancelButton.addEventListener("click", cancelRegionEdit);
  const saveButton = createSmallButton("Save and reprocess");
  saveButton.classList.add("secondary-action", "save-region-edit-button");
  saveButton.addEventListener("click", saveRegionEdit);
  buttons.append(cancelButton, saveButton);
  container.append(feedback, buttons);
  updateRegionEditControls(container);
  return container;
}

function cancelRegionEdit() {
  if (!activeRegionEdit || regionEditBusy) return;
  const sourceItemId = activeRegionEdit.sourceItemId;
  endRegionEdit();
  if (capturePayload) {
    renderCaptureSuccess(capturePayload);
    queueMicrotask(() => focusEditRegionsButton(sourceItemId));
  }
}

function saveRegionEdit() {
  if (!activeRegionEdit || regionEditBusy || !CaptureUi.hasRegionChanges(activeRegionEdit)) return;
  regionEditError = null;
  updateRegionEditControls();
  regionEditSubmissionRequested = true;
  form.requestSubmit();
}

function endRegionEdit() {
  activeRegionEdit = null;
  selectedEditRegionId = null;
  regionEditError = null;
  setRegionEditModal(false);
}

function setRegionEditModal(isEditing) {
  document.body.classList.toggle("region-edit-active", isEditing);
  modeSwitch.inert = isEditing;
  form.inert = isEditing;
  resultHeader.inert = isEditing;
  metricsGrid.inert = isEditing;
  jsonPanel.inert = isEditing;
  if (isEditing) {
    captureResult.setAttribute("role", "dialog");
    captureResult.setAttribute("aria-modal", "true");
    captureResult.setAttribute("aria-labelledby", "sourceGalleryTitle");
    document.querySelector("#sourceGalleryTitle").textContent = "Edit document regions";
    captureSummary.textContent = "Changes stay local until you save and reprocess.";
    jsonPanel.open = false;
  } else {
    captureResult.removeAttribute("role");
    captureResult.removeAttribute("aria-modal");
    captureResult.removeAttribute("aria-labelledby");
    document.querySelector("#sourceGalleryTitle").textContent = "Annotated captures";
  }
}

function setRegionEditBusy(isBusy) {
  regionEditBusy = isBusy;
  processButton.disabled = isBusy;
  imageInput.disabled = isBusy;
  sourceIdInput.disabled = isBusy;
  modeInputs.forEach((input) => {
    input.disabled = isBusy;
  });
  resultSurface.setAttribute("aria-busy", String(isBusy));

  if (activeRegionEdit) {
    if (isBusy) {
      sourceGrid.querySelectorAll("button, input").forEach((control) => {
        control.disabled = true;
      });
      memberDetail.querySelectorAll("button, input").forEach((control) => {
        control.disabled = true;
      });
      updateRegionEditControls();
    } else {
      renderCaptureSources();
      renderRegionEditorDetail(activeRegionEdit.sourceItemId);
      queueMicrotask(() => sourceGrid.querySelector(".save-region-edit-button")?.focus());
    }
  }
}

function renderRegionEditError(error) {
  regionEditError = {
    code: error?.code ?? "request_failed",
    message: error?.message ?? "The corrected regions could not be reprocessed."
  };
  updateRegionEditControls();
}

function updateRegionEditControls(root = document) {
  if (!activeRegionEdit) return;
  const hasChanges = CaptureUi.hasRegionChanges(activeRegionEdit);
  const status = root.querySelector?.(".region-edit-status") ?? document.querySelector(".region-edit-status");
  const error = root.querySelector?.(".region-edit-error") ?? document.querySelector(".region-edit-error");
  const saveButton = root.querySelector?.(".save-region-edit-button") ?? document.querySelector(".save-region-edit-button");
  const cancelButton = root.querySelector?.(".cancel-region-edit-button") ?? document.querySelector(".cancel-region-edit-button");
  if (status) {
    status.textContent = regionEditBusy
      ? "Saving corrected regions and reprocessing documents…"
      : regionEditError ? "Save failed. Your corrections are still available."
        : hasChanges ? "Unsaved region changes"
          : "No region changes yet";
  }
  if (error) {
    error.textContent = regionEditError ? `${regionEditError.code}: ${regionEditError.message}` : "";
    error.hidden = !regionEditError;
  }
  if (saveButton) {
    saveButton.disabled = regionEditBusy || !hasChanges;
    saveButton.textContent = regionEditBusy ? "Saving and reprocessing…" : "Save and reprocess";
  }
  if (cancelButton) {
    cancelButton.disabled = regionEditBusy;
  }
}

function focusInitialRegionEditControl() {
  const control = sourceGrid.querySelector("[data-edit-region-id]")
    ?? sourceGrid.querySelector(".add-region-button");
  control?.focus();
}

function focusEditRegionsButton(sourceItemId) {
  sourceGrid.querySelector(`[data-edit-source-id="${sourceItemId}"]`)?.focus();
}

function createRegionEditorOverlay(source, editState) {
  const layer = document.createElement("div");
  layer.className = "region-editor-layer";
  layer.setAttribute("aria-label", `Editable document regions for source ${source.index}`);
  editState.regions.forEach((region, index) => {
    const box = document.createElement("div");
    box.className = "editable-region";
    box.tabIndex = 0;
    box.dataset.editRegionId = region.id;
    box.classList.toggle("selected", region.id === selectedEditRegionId);
    box.setAttribute("role", "button");
    box.setAttribute("aria-label", `Region ${index + 1}. Drag to move; Alt plus arrow keys resize.`);
    box.setAttribute("aria-pressed", String(region.id === selectedEditRegionId));
    updateEditorBoxStyle(box, region.bounds);
    box.addEventListener("focus", () => selectEditorRegion(source.sourceItemId, region.id, false));
    box.addEventListener("keydown", (event) => handleEditorKey(event, source.sourceItemId, region.id, box));
    box.addEventListener("pointerdown", (event) => {
      if (event.target.closest?.(".resize-handle")) return;
      startRegionPointerEdit(event, source.sourceItemId, region.id, box, null);
    });
    ["nw", "ne", "sw", "se"].forEach((handleName) => {
      const handle = document.createElement("span");
      handle.className = `resize-handle ${handleName}`;
      handle.dataset.handle = handleName;
      handle.setAttribute("aria-hidden", "true");
      handle.addEventListener("pointerdown", (event) => {
        event.stopPropagation();
        startRegionPointerEdit(event, source.sourceItemId, region.id, box, handleName);
      });
      box.appendChild(handle);
    });
    const number = document.createElement("span");
    number.className = "editable-region-number";
    number.textContent = String(index + 1).padStart(2, "0");
    box.appendChild(number);
    layer.appendChild(box);
  });
  return layer;
}

function startRegionPointerEdit(event, sourceItemId, regionId, box, handle) {
  event.preventDefault();
  selectEditorRegion(sourceItemId, regionId, false);
  const frame = box.parentElement.getBoundingClientRect();
  const editState = getActiveRegionEdit(sourceItemId);
  const region = editState?.regions.find((candidate) => candidate.id === regionId);
  if (!region || frame.width <= 0 || frame.height <= 0) return;
  const startBounds = { ...region.bounds };
  const startX = event.clientX;
  const startY = event.clientY;
  box.setPointerCapture(event.pointerId);
  const move = (moveEvent) => {
    const dx = (moveEvent.clientX - startX) / frame.width;
    const dy = (moveEvent.clientY - startY) / frame.height;
    region.bounds = handle
      ? CaptureUi.resizeBounds(startBounds, handle, dx, dy)
      : CaptureUi.moveBounds(startBounds, dx, dy);
    regionEditError = null;
    updateEditorBoxStyle(box, region.bounds);
    renderRegionEditorDetail(sourceItemId);
    updateRegionEditControls();
  };
  const finish = () => {
    box.removeEventListener("pointermove", move);
    box.removeEventListener("pointerup", finish);
    box.removeEventListener("pointercancel", finish);
  };
  box.addEventListener("pointermove", move);
  box.addEventListener("pointerup", finish);
  box.addEventListener("pointercancel", finish);
}

function handleEditorKey(event, sourceItemId, regionId, box) {
  const deltas = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
  if (!deltas[event.key]) return;
  event.preventDefault();
  const editState = getActiveRegionEdit(sourceItemId);
  const region = editState?.regions.find((candidate) => candidate.id === regionId);
  if (!region) return;
  const step = event.shiftKey ? 0.02 : 0.005;
  const [x, y] = deltas[event.key];
  region.bounds = event.altKey
    ? CaptureUi.resizeBounds(region.bounds, "se", x * step, y * step)
    : CaptureUi.moveBounds(region.bounds, x * step, y * step);
  regionEditError = null;
  updateEditorBoxStyle(box, region.bounds);
  renderRegionEditorDetail(sourceItemId);
  updateRegionEditControls();
}

function updateEditorBoxStyle(box, bounds) {
  box.style.left = `${bounds.x * 100}%`;
  box.style.top = `${bounds.y * 100}%`;
  box.style.width = `${bounds.width * 100}%`;
  box.style.height = `${bounds.height * 100}%`;
}

function createEditorRegionRow(sourceItemId, region, index, count) {
  const row = document.createElement("div");
  row.className = "editor-region-row";
  row.classList.toggle("selected", region.id === selectedEditRegionId);
  const selectButton = createSmallButton(`Region ${String(index + 1).padStart(2, "0")}`);
  selectButton.addEventListener("click", () => selectEditorRegion(sourceItemId, region.id, true));
  const actions = document.createElement("span");
  const up = createSmallButton("↑", `Move region ${index + 1} earlier`);
  const down = createSmallButton("↓", `Move region ${index + 1} later`);
  const remove = createSmallButton("Delete", `Delete region ${index + 1}`);
  up.disabled = index === 0;
  down.disabled = index === count - 1;
  up.addEventListener("click", () => reorderEditorRegion(sourceItemId, index, index - 1));
  down.addEventListener("click", () => reorderEditorRegion(sourceItemId, index, index + 1));
  remove.addEventListener("click", () => deleteEditorRegion(sourceItemId, region.id));
  actions.append(up, down, remove);
  row.append(selectButton, actions);
  return row;
}

function createSmallButton(text, accessibleLabel = text) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "small-action";
  button.textContent = text;
  button.setAttribute("aria-label", accessibleLabel);
  return button;
}

function selectEditorRegion(sourceItemId, regionId, scroll) {
  selectedEditRegionId = regionId;
  sourceGrid.querySelectorAll("[data-edit-region-id]").forEach((element) => {
    const selected = element.dataset.editRegionId === regionId;
    element.classList.toggle("selected", selected);
    element.setAttribute("aria-pressed", String(selected));
  });
  renderRegionEditorDetail(sourceItemId);
  if (scroll) document.querySelector("#memberInspector")?.scrollIntoView({ behavior: "smooth", block: "nearest" });
}

function addEditorRegion(sourceItemId) {
  const editState = getActiveRegionEdit(sourceItemId);
  if (!editState) return;
  const region = {
    id: `edit-${nextEditRegionId++}`,
    bounds: { x: 0.3, y: 0.3, width: 0.4, height: 0.4 }
  };
  editState.regions.push(region);
  regionEditError = null;
  selectedEditRegionId = region.id;
  renderCaptureSources();
  renderRegionEditorDetail(sourceItemId);
}

function deleteEditorRegion(sourceItemId, regionId) {
  const editState = getActiveRegionEdit(sourceItemId);
  if (!editState) return;
  editState.regions = editState.regions.filter((region) => region.id !== regionId);
  regionEditError = null;
  selectedEditRegionId = editState.regions[0]?.id ?? null;
  renderCaptureSources();
  renderRegionEditorDetail(sourceItemId);
}

function reorderEditorRegion(sourceItemId, fromIndex, toIndex) {
  const editState = getActiveRegionEdit(sourceItemId);
  if (!editState) return;
  editState.regions = CaptureUi.reorderRegions(editState.regions, fromIndex, toIndex);
  regionEditError = null;
  renderCaptureSources();
  renderRegionEditorDetail(sourceItemId);
}

function renderRegionEditorDetail(sourceItemId) {
  const editState = getActiveRegionEdit(sourceItemId);
  const region = editState?.regions.find((candidate) => candidate.id === selectedEditRegionId);
  memberDetail.replaceChildren();
  memberTitle.textContent = region ? "Adjust region" : "Region correction";
  if (!region) {
    memberDetail.appendChild(createParagraph(
      "Add a rectangle to identify a document, or reprocess with no regions for this source.",
      "empty-state"));
    return;
  }

  memberDetail.appendChild(createParagraph(
    `Source ${String(editState.sourceIndex).padStart(2, "0")} · normalized coordinates`,
    "member-identity"));
  const formGrid = document.createElement("div");
  formGrid.className = "coordinate-grid";
  ["x", "y", "width", "height"].forEach((fieldName) => {
    const label = document.createElement("label");
    label.className = "coordinate-field";
    const labelText = document.createElement("span");
    labelText.textContent = fieldName;
    const input = document.createElement("input");
    input.type = "number";
    input.min = "0";
    input.max = "1";
    input.step = "0.005";
    input.name = fieldName;
    input.value = Number(region.bounds[fieldName]).toFixed(4);
    const applyCoordinateValue = () => {
      if (input.value.trim() === "") return false;
      const value = Number(input.value);
      if (!Number.isFinite(value)) return false;
      region.bounds = CaptureUi.clampBounds({ ...region.bounds, [fieldName]: value });
      regionEditError = null;
      const box = sourceGrid.querySelector(`[data-edit-region-id="${region.id}"]`);
      if (box) updateEditorBoxStyle(box, region.bounds);
      updateRegionEditControls();
      return true;
    };
    input.addEventListener("input", applyCoordinateValue);
    input.addEventListener("change", () => {
      if (!applyCoordinateValue()) return;
      renderRegionEditorDetail(sourceItemId);
      queueMicrotask(() => memberDetail.querySelector(`input[name="${fieldName}"]`)?.focus());
    });
    label.append(labelText, input);
    formGrid.appendChild(label);
  });
  const deleteButton = createSmallButton("Delete selected region");
  deleteButton.classList.add("danger-action");
  deleteButton.addEventListener("click", () => deleteEditorRegion(sourceItemId, region.id));
  memberDetail.append(formGrid, deleteButton, createParagraph(
    "These working changes stay in this page until you choose Save and reprocess or Cancel.",
    "editor-note"));
}

function getActiveRegionEdit(sourceItemId) {
  return activeRegionEdit?.sourceItemId === sourceItemId ? activeRegionEdit : null;
}

function createOverlay(members) {
  const svg = document.createElementNS(svgNamespace, "svg");
  svg.setAttribute("class", "region-overlay");
  svg.setAttribute("viewBox", "0 0 100 100");
  svg.setAttribute("preserveAspectRatio", "none");
  svg.setAttribute("aria-label", "Document regions");

  members.forEach((member) => {
    const presentation = CaptureUi.getDispositionPresentation(member.disposition);
    const shape = CaptureUi.getRegionShape(member.region);
    const group = document.createElementNS(svgNamespace, "g");
    group.setAttribute("class", `region ${presentation.className}`);
    group.setAttribute("role", "button");
    group.setAttribute("tabindex", "0");
    group.setAttribute("aria-label", CaptureUi.getMemberAccessibleLabel(member));
    group.setAttribute("aria-pressed", String(member.memberId === selectedMemberId));
    group.dataset.memberId = member.memberId;
    group.addEventListener("click", () => updateMemberSelection(member.memberId, true));
    group.addEventListener("keydown", (event) => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        updateMemberSelection(member.memberId, true);
      }
    });

    const boundary = document.createElementNS(svgNamespace, shape.type === "polygon" ? "polygon" : "rect");
    boundary.setAttribute("class", "region-boundary");
    boundary.setAttribute("vector-effect", "non-scaling-stroke");
    if (shape.type === "polygon") {
      boundary.setAttribute("points", shape.points);
    } else {
      Object.entries(shape.bounds).forEach(([name, value]) => boundary.setAttribute(name, value));
    }

    const markerX = clamp(shape.marker.x * 100 + 3.5, 4.5, 95.5);
    const markerY = clamp(shape.marker.y * 100 + 3.5, 4.5, 95.5);
    const marker = document.createElementNS(svgNamespace, "circle");
    marker.setAttribute("class", "region-marker");
    marker.setAttribute("cx", markerX);
    marker.setAttribute("cy", markerY);
    marker.setAttribute("r", "3.1");
    marker.setAttribute("vector-effect", "non-scaling-stroke");
    const symbol = document.createElementNS(svgNamespace, "text");
    symbol.setAttribute("class", "region-symbol");
    symbol.setAttribute("x", markerX);
    symbol.setAttribute("y", markerY + 1.15);
    symbol.setAttribute("text-anchor", "middle");
    symbol.textContent = presentation.symbol;
    group.append(boundary, marker, symbol);
    svg.appendChild(group);
  });

  return svg;
}

function createMemberButton(member) {
  const presentation = CaptureUi.getDispositionPresentation(member.disposition);
  const button = document.createElement("button");
  button.type = "button";
  button.className = `member-row ${presentation.className}`;
  button.dataset.memberId = member.memberId;
  button.setAttribute("aria-pressed", String(member.memberId === selectedMemberId));
  button.setAttribute("aria-label", CaptureUi.getMemberAccessibleLabel(member));

  const symbol = document.createElement("span");
  symbol.className = "member-symbol";
  symbol.setAttribute("aria-hidden", "true");
  symbol.textContent = presentation.symbol;
  const copy = document.createElement("span");
  const title = document.createElement("b");
  title.textContent = member.result?.category ?? `Document ${member.index}`;
  const subtitle = document.createElement("small");
  subtitle.textContent = `${presentation.label} · ${member.memberId}`;
  copy.append(title, subtitle);
  button.append(symbol, copy);
  button.addEventListener("click", () => updateMemberSelection(member.memberId, true));
  return button;
}

function updateMemberSelection(memberId, moveFocus) {
  selectedMemberId = CaptureUi.chooseMemberId(capturePayload, memberId);
  sourceGrid.querySelectorAll("[data-member-id]").forEach((element) => {
    const isSelected = element.dataset.memberId === selectedMemberId;
    element.classList.toggle("selected", isSelected);
    element.setAttribute("aria-pressed", String(isSelected));
  });

  const member = capturePayload?.members?.find((candidate) => candidate.memberId === selectedMemberId);
  renderMemberDetail(member);
  if (moveFocus) {
    document.querySelector("#memberInspector")?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }
}

function renderMemberDetail(member) {
  memberDetail.replaceChildren();
  if (!member) {
    memberTitle.textContent = "No document selected";
    memberDetail.appendChild(createParagraph("This capture did not return a selectable region.", "empty-state"));
    return;
  }

  const presentation = CaptureUi.getDispositionPresentation(member.disposition);
  memberTitle.textContent = member.result?.category ?? `Document ${member.index}`;
  const identity = createParagraph(member.memberId, "member-identity");
  const disposition = document.createElement("p");
  disposition.className = `inspector-disposition ${presentation.className}`;
  disposition.textContent = `${presentation.symbol} ${presentation.label}`;

  const metadata = document.createElement("dl");
  metadata.className = "data-list compact-data";
  const confidence = member.region?.confidence === null || member.region?.confidence === undefined
    ? Number.NaN
    : Number(member.region.confidence);
  metadata.append(
    createDataRow("Workflow status", sentenceCase(member.status ?? "-")),
    createDataRow("Detection confidence", Number.isFinite(confidence) ? `${Math.round(confidence * 100)}%` : "Not reported"),
    createDataRow("Classification", member.result?.classification?.confidence === null || member.result?.classification?.confidence === undefined
      ? "Not available"
      : `${Math.round(Number(member.result.classification.confidence) * 100)}%`)
  );
  memberDetail.append(identity, disposition, metadata);

  const documentData = member.result?.document?.data;
  if (documentData && Object.keys(documentData).length > 0) {
    memberDetail.appendChild(createSectionHeading("Extracted data"));
    const dataList = document.createElement("dl");
    dataList.className = "data-list compact-data";
    renderData(dataList, documentData);
    memberDetail.appendChild(dataList);
  }

  const reasons = [
    ...(member.dispositionReasons ?? []),
    ...(member.region?.warnings ?? []),
    ...(member.result?.humanReview?.reasons ?? []),
    ...(member.result?.warnings ?? []),
    ...(member.result?.errors ?? []),
    member.error?.message
  ].filter(Boolean);
  memberDetail.appendChild(createSectionHeading("Reasons and findings"));
  const list = document.createElement("ul");
  list.className = "reason-list compact-reasons";
  renderReasons(list, reasons);
  memberDetail.appendChild(list);
}

function renderRequestError(error) {
  singleResult.hidden = false;
  captureResult.hidden = true;
  metricOneLabel.textContent = "Category";
  metricTwoLabel.textContent = "Decision";
  resultEyebrow.textContent = "Request boundary";
  resultTitle.textContent = "Request failed";
  categoryMetric.textContent = "-";
  decisionMetric.textContent = error.code ?? "Error";
  tokensMetric.textContent = "-";
  latencyMetric.textContent = "-";
  costMetric.textContent = "-";
  statusPill.textContent = "Error";
  statusPill.className = "status-pill error";
  renderData(extractedData, {
    code: error.code ?? "request_failed",
    target: error.target ?? "-",
    traceId: error.traceId ?? "-"
  });
  renderReasons(policyReasons, [error.message ?? "The request failed."]);
  showRawJson(error);
}

function renderModelUsage(modelUsage = {}) {
  tokensMetric.textContent = formatInteger(modelUsage.totalTokens);
  latencyMetric.textContent = formatDuration(getModelDuration(modelUsage));
  costMetric.textContent = formatUsdCost(modelUsage.estimatedTotalCostUsd);
}

function showRawJson(payload, shouldOpen = true) {
  rawJson.textContent = JSON.stringify(payload, null, 2);
  jsonPanel.open = shouldOpen;
}

function appendFinding(card, text, type) {
  let list = card.querySelector(".source-findings");
  if (!list) {
    list = document.createElement("ul");
    list.className = "source-findings";
    card.appendChild(list);
  }
  const item = document.createElement("li");
  item.className = type;
  item.textContent = text;
  list.appendChild(item);
}

function parseJsonPayload(responseText) {
  if (!responseText) {
    return { ok: true, value: {} };
  }

  try {
    return { ok: true, value: JSON.parse(responseText) };
  } catch {
    return { ok: false, value: null };
  }
}

function buildNonJsonResponseError(response, responseText) {
  const status = response.status > 0 ? `HTTP ${response.status}` : "the request";
  const preview = summarizeResponseText(responseText);
  return {
    code: response.ok ? "invalid_api_response" : "request_failed",
    message: `The API returned ${status} with a non-JSON response: ${preview}`,
    target: null,
    traceId: response.headers.get("x-trace-id") ?? "-"
  };
}

function summarizeResponseText(value) {
  const preview = value.replace(/\s+/g, " ").trim();
  if (preview.length === 0) {
    return "(empty response)";
  }

  return preview.length > 240 ? `${preview.slice(0, 240)}...` : preview;
}

function renderData(target, data) {
  const entries = Object.entries(data);
  target.replaceChildren();
  if (entries.length === 0) {
    target.appendChild(createDataRow("Document", "No extracted fields returned"));
    return;
  }

  entries.forEach(([key, value]) => {
    target.appendChild(createDataRow(fieldLabels[key] ?? sentenceCase(key), formatValue(key, value)));
  });
}

function renderReasons(target, reasons) {
  target.replaceChildren();
  const items = reasons.filter(Boolean);
  (items.length > 0 ? items : ["No review reasons returned."]).forEach((reason) => {
    const item = document.createElement("li");
    item.textContent = reason;
    target.appendChild(item);
  });
}

function createDataRow(label, value) {
  const wrapper = document.createElement("div");
  const term = document.createElement("dt");
  const description = document.createElement("dd");
  term.textContent = label;
  description.textContent = value;
  wrapper.append(term, description);
  return wrapper;
}

function createSectionHeading(text) {
  const heading = document.createElement("h4");
  heading.className = "detail-heading";
  heading.textContent = text;
  return heading;
}

function createParagraph(text, className) {
  const paragraph = document.createElement("p");
  paragraph.className = className;
  paragraph.textContent = text;
  return paragraph;
}

function formatValue(key, value) {
  if (value === null || value === undefined || value === "") {
    return "-";
  }
  if ((key === "totalAmount" || key === "claimedTotal" || key === "amount") && typeof value === "number") {
    return value.toFixed(2);
  }
  if (typeof value === "number") {
    return Number.isInteger(value) ? value.toString() : value.toFixed(2);
  }
  if (Array.isArray(value)) {
    return value.length === 0 ? "-" : value.map(formatArrayItem).join(", ");
  }
  if (typeof value === "object") {
    return Object.entries(value)
      .map(([entryKey, entryValue]) => `${fieldLabels[entryKey] ?? sentenceCase(entryKey)}: ${formatValue(entryKey, entryValue)}`)
      .join(", ");
  }
  return String(value);
}

function formatArrayItem(value) {
  if (value && typeof value === "object" && !Array.isArray(value)) {
    if ("row" in value && "column" in value && "value" in value) {
      return `r${value.row}c${value.column}=${value.value}`;
    }
    if ("description" in value && "amount" in value) {
      const date = value.date ? `${value.date} · ` : "";
      const amount = Number(value.amount);
      const amountText = Number.isFinite(amount) ? amount.toFixed(2) : String(value.amount);
      const reference = value.receiptReference ? ` (${value.receiptReference})` : "";
      return `${date}${value.description} ${amountText}${reference}`;
    }
    const name = value.name ?? value.item ?? "Item";
    const quantity = value.quantity === null || value.quantity === undefined ? null : Number(value.quantity);
    const quantityText = quantity === null || Number.isNaN(quantity)
      ? null
      : Number.isInteger(quantity) ? quantity.toString() : quantity.toFixed(2);
    const unit = value.unit ? ` ${value.unit}` : "";
    const checked = value.isChecked === true ? " (checked)" : "";
    return quantityText ? `${quantityText}${unit} ${name}${checked}` : `${name}${checked}`;
  }
  return String(value);
}

function formatInteger(value) {
  if (value === null || value === undefined || value === "") {
    return "-";
  }
  const number = Number(value);
  return Number.isFinite(number) ? number.toLocaleString("en-US") : "-";
}

function formatDuration(value) {
  if (value === null || value === undefined || value === "") {
    return "-";
  }
  const milliseconds = Number(value);
  if (!Number.isFinite(milliseconds)) {
    return "-";
  }
  if (milliseconds < 1000) {
    return `${Math.round(milliseconds)} ms`;
  }
  const seconds = milliseconds / 1000;
  return seconds < 10 ? `${seconds.toFixed(1)} s` : `${Math.round(seconds)} s`;
}

function getModelDuration(modelUsage) {
  if (modelUsage.totalDurationMilliseconds !== null && modelUsage.totalDurationMilliseconds !== undefined) {
    return modelUsage.totalDurationMilliseconds;
  }
  const calls = Array.isArray(modelUsage.calls) ? modelUsage.calls : [];
  const durations = calls.map((call) => Number(call.durationMilliseconds)).filter(Number.isFinite);
  return durations.length === 0 ? null : durations.reduce((total, duration) => total + duration, 0);
}

function formatUsdCost(value) {
  if (value === null || value === undefined || value === "") {
    return "-";
  }
  const amount = Number(value);
  if (!Number.isFinite(amount)) {
    return "-";
  }
  if (amount === 0) {
    return "$0.00";
  }
  if (Math.abs(amount) < 0.01) {
    return formatSubCentUsd(amount);
  }
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
    minimumFractionDigits: 2,
    maximumFractionDigits: 4
  }).format(amount);
}

function formatSubCentUsd(amount) {
  const rounded = roundToSignificantFigures(amount, 2);
  if (Math.abs(rounded) >= 0.00000001) {
    const decimalPlaces = Math.min(8, significantDecimalPlaces(rounded, 2));
    const rendered = rounded.toFixed(decimalPlaces);
    return `$${rendered.replace(/0+$/, "").replace(/\.$/, "")}`;
  }
  return "<$0.00000001";
}

function roundToSignificantFigures(value, significantFigures) {
  const magnitude = Math.floor(Math.log10(Math.abs(value)));
  const scale = 10 ** (significantFigures - magnitude - 1);
  return Math.round(value * scale) / scale;
}

function significantDecimalPlaces(value, significantFigures) {
  const magnitude = Math.floor(Math.log10(Math.abs(value)));
  return Math.max(2, significantFigures - magnitude - 1);
}

function captureStatusClass(status) {
  if (status === "Succeeded") {
    return "approved";
  }
  if (status === "PartiallySucceeded") {
    return "review";
  }
  if (status === "Running") {
    return "running";
  }
  return "error";
}

function sentenceCase(value) {
  return String(value)
    .replace(/([A-Z])/g, " $1")
    .replace(/^./, (letter) => letter.toUpperCase())
    .trim();
}

function formatBytes(bytes) {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  const kilobytes = bytes / 1024;
  if (kilobytes < 1024) {
    return `${kilobytes.toFixed(1)} KB`;
  }
  return `${(kilobytes / 1024).toFixed(2)} MB`;
}

function clamp(value, minimum, maximum) {
  return Math.min(Math.max(value, minimum), maximum);
}
