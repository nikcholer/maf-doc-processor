const form = document.querySelector("#uploadForm");
const imageInput = document.querySelector("#imageInput");
const sourceIdInput = document.querySelector("#sourceId");
const dropZone = document.querySelector("#dropZone");
const dropCopy = document.querySelector("#dropCopy");
const selectedFile = document.querySelector("#selectedFile");
const previewImage = document.querySelector("#previewImage");
const processButton = document.querySelector("#processButton");
const healthDot = document.querySelector("#healthDot");
const healthText = document.querySelector("#healthText");
const resultTitle = document.querySelector("#resultTitle");
const statusPill = document.querySelector("#statusPill");
const categoryMetric = document.querySelector("#categoryMetric");
const decisionMetric = document.querySelector("#decisionMetric");
const tokensMetric = document.querySelector("#tokensMetric");
const latencyMetric = document.querySelector("#latencyMetric");
const costMetric = document.querySelector("#costMetric");
const extractedData = document.querySelector("#extractedData");
const policyReasons = document.querySelector("#policyReasons");
const jsonPanel = document.querySelector("#jsonPanel");
const rawJson = document.querySelector("#rawJson");

const fieldLabels = {
  storeName: "Store",
  totalAmount: "Total",
  purchaseDate: "Purchase date",
  paymentMethod: "Payment method",
  currencyCode: "Currency",
  title: "Title",
  items: "Items",
  notes: "Notes"
};

let previewUrl = null;
const maxUploadBytes = 5 * 1024 * 1024;
const requestTimeoutMs = 65 * 1000;

checkHealth();

imageInput.addEventListener("change", () => {
  const file = imageInput.files?.[0];
  setSelectedFile(file);
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
  const file = event.dataTransfer.files?.[0];
  if (!file) {
    return;
  }

  const transfer = new DataTransfer();
  transfer.items.add(file);
  imageInput.files = transfer.files;
  imageInput.dispatchEvent(new Event("change"));
});

form.addEventListener("submit", async (event) => {
  event.preventDefault();

  const file = imageInput.files?.[0];
  if (!file) {
    renderError({
      code: "missing_file",
      message: "Choose a PNG or JPEG document image before processing.",
      target: "image",
      traceId: "-"
    });
    return;
  }

  if (file.size > maxUploadBytes) {
    renderError({
      code: "file_too_large",
      message: `Choose an image smaller than ${formatBytes(maxUploadBytes)}.`,
      target: "image",
      traceId: "-"
    });
    return;
  }

  const body = new FormData();
  body.append("image", file);

  const sourceId = sourceIdInput.value.trim();
  if (sourceId.length > 0) {
    body.append("sourceId", sourceId);
  }

  setBusy(true);
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), requestTimeoutMs);

  try {
    const response = await fetch("/api/documents/process", {
      method: "POST",
      headers: {
        "X-Correlation-ID": crypto.randomUUID()
      },
      body,
      signal: controller.signal
    });
    const responseText = await response.text();
    const parsedPayload = parseJsonPayload(responseText);

    if (!parsedPayload.ok) {
      renderError(buildNonJsonResponseError(response, responseText));
      return;
    }

    if (!response.ok) {
      renderError(parsedPayload.value);
      return;
    }

    renderSuccess(parsedPayload.value);
  } catch (error) {
    renderError({
      code: error instanceof DOMException && error.name === "AbortError"
        ? "request_timeout"
        : "request_failed",
      message: error instanceof DOMException && error.name === "AbortError"
        ? "Processing exceeded 65 seconds. Check the API terminal logs for the last completed stage."
        : error instanceof Error ? error.message : "The request failed.",
      target: null,
      traceId: "-"
    });
  } finally {
    clearTimeout(timeoutId);
    setBusy(false);
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

function setSelectedFile(file) {
  selectedFile.textContent = file ? `${file.name} (${formatBytes(file.size)})` : "No file selected";

  if (previewUrl) {
    URL.revokeObjectURL(previewUrl);
    previewUrl = null;
  }

  if (!file) {
    previewImage.removeAttribute("src");
    dropZone.classList.remove("has-preview");
    dropCopy.hidden = false;
    return;
  }

  previewUrl = URL.createObjectURL(file);
  previewImage.src = previewUrl;
  dropZone.classList.add("has-preview");
  dropCopy.hidden = true;
}

function setBusy(isBusy) {
  processButton.disabled = isBusy;
  processButton.querySelector("span").textContent = isBusy ? "Processing..." : "Process document";
  if (isBusy) {
    resultTitle.textContent = "Processing document";
    statusPill.textContent = "Running";
    statusPill.className = "status-pill";
    jsonPanel.open = false;
  }
}

function renderSuccess(payload) {
  const document = payload.document;
  const policy = document?.policyResult;
  const decision = policy?.decision ?? (payload.isSuccess ? "Complete" : "NeedsReview");

  resultTitle.textContent = `${payload.category} processed`;
  categoryMetric.textContent = payload.category ?? "-";
  decisionMetric.textContent = decision;
  const modelUsage = payload.modelUsage ?? {};
  tokensMetric.textContent = formatInteger(modelUsage.totalTokens);
  latencyMetric.textContent = formatDuration(getModelDuration(modelUsage));
  costMetric.textContent = formatUsdCost(modelUsage.estimatedTotalCostUsd);
  statusPill.textContent = decision;
  statusPill.className = `status-pill ${decision === "Approved" ? "approved" : "review"}`;

  renderData(document?.data ?? {});
  renderReasons([
    ...(policy?.reasons ?? []),
    ...(payload.warnings ?? []),
    ...(payload.errors ?? [])
  ]);
  rawJson.textContent = JSON.stringify(payload, null, 2);
  jsonPanel.open = true;
}

function renderError(error) {
  resultTitle.textContent = "Request failed";
  categoryMetric.textContent = "-";
  decisionMetric.textContent = error.code ?? "Error";
  tokensMetric.textContent = "-";
  latencyMetric.textContent = "-";
  costMetric.textContent = "-";
  statusPill.textContent = "Error";
  statusPill.className = "status-pill error";

  renderData({
    code: error.code ?? "request_failed",
    target: error.target ?? "-",
    traceId: error.traceId ?? "-"
  });
  renderReasons([error.message ?? "The request failed."]);
  rawJson.textContent = JSON.stringify(error, null, 2);
  jsonPanel.open = true;
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

function renderData(data) {
  const entries = Object.entries(data);
  extractedData.replaceChildren();

  if (entries.length === 0) {
    extractedData.appendChild(createDataRow("Document", "No extracted fields returned"));
    return;
  }

  for (const [key, value] of entries) {
    extractedData.appendChild(createDataRow(fieldLabels[key] ?? sentenceCase(key), formatValue(key, value)));
  }
}

function renderReasons(reasons) {
  policyReasons.replaceChildren();
  const items = reasons.filter(Boolean);

  for (const reason of items.length > 0 ? items : ["No review reasons returned."]) {
    const item = document.createElement("li");
    item.textContent = reason;
    policyReasons.appendChild(item);
  }
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

function formatValue(key, value) {
  if (value === null || value === undefined || value === "") {
    return "-";
  }

  if (key === "totalAmount" && typeof value === "number") {
    return value.toFixed(2);
  }

  if (typeof value === "number") {
    return Number.isInteger(value) ? value.toString() : value.toFixed(2);
  }

  if (Array.isArray(value)) {
    return value.length === 0
      ? "-"
      : value.map(formatArrayItem).join(", ");
  }

  return String(value);
}

function formatArrayItem(value) {
  if (value && typeof value === "object" && !Array.isArray(value)) {
    const name = value.name ?? value.item ?? "Item";
    const quantity = value.quantity === null || value.quantity === undefined
      ? null
      : Number(value.quantity);
    const quantityText = quantity === null || Number.isNaN(quantity)
      ? null
      : Number.isInteger(quantity) ? quantity.toString() : quantity.toFixed(2);
    const unit = value.unit ? ` ${value.unit}` : "";
    const checked = value.isChecked === true ? " (checked)" : "";

    return quantityText
      ? `${quantityText}${unit} ${name}${checked}`
      : `${name}${checked}`;
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
  return seconds < 10
    ? `${seconds.toFixed(1)} s`
    : `${Math.round(seconds)} s`;
}

function getModelDuration(modelUsage) {
  if (modelUsage.totalDurationMilliseconds !== null
    && modelUsage.totalDurationMilliseconds !== undefined) {
    return modelUsage.totalDurationMilliseconds;
  }

  const calls = Array.isArray(modelUsage.calls) ? modelUsage.calls : [];
  const durations = calls
    .map((call) => Number(call.durationMilliseconds))
    .filter(Number.isFinite);

  return durations.length === 0
    ? null
    : durations.reduce((total, duration) => total + duration, 0);
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

function sentenceCase(value) {
  return value
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
