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
const extractedData = document.querySelector("#extractedData");
const policyReasons = document.querySelector("#policyReasons");
const rawJson = document.querySelector("#rawJson");

const fieldLabels = {
  storeName: "Store",
  totalAmount: "Total",
  purchaseDate: "Purchase date",
  paymentMethod: "Payment method",
  currencyCode: "Currency"
};

let previewUrl = null;

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
      message: "Choose a PNG or JPEG receipt image before processing.",
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

  try {
    const response = await fetch("/api/documents/process", {
      method: "POST",
      headers: {
        "X-Correlation-ID": crypto.randomUUID()
      },
      body
    });
    const payload = await response.json();

    if (!response.ok) {
      renderError(payload);
      return;
    }

    renderSuccess(payload);
  } catch (error) {
    renderError({
      code: "request_failed",
      message: error instanceof Error ? error.message : "The request failed.",
      target: null,
      traceId: "-"
    });
  } finally {
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
  processButton.querySelector("span").textContent = isBusy ? "Processing..." : "Process receipt";
  if (isBusy) {
    resultTitle.textContent = "Processing receipt";
    statusPill.textContent = "Running";
    statusPill.className = "status-pill";
  }
}

function renderSuccess(payload) {
  const document = payload.document;
  const policy = document?.policyResult;
  const decision = policy?.decision ?? (payload.isSuccess ? "Complete" : "NeedsReview");

  resultTitle.textContent = `${payload.category} processed`;
  categoryMetric.textContent = payload.category ?? "-";
  decisionMetric.textContent = decision;
  tokensMetric.textContent = payload.modelUsage?.totalTokens ?? "-";
  statusPill.textContent = decision;
  statusPill.className = `status-pill ${decision === "Approved" ? "approved" : "review"}`;

  renderData(document?.data ?? {});
  renderReasons([
    ...(policy?.reasons ?? []),
    ...(payload.warnings ?? []),
    ...(payload.errors ?? [])
  ]);
  rawJson.textContent = JSON.stringify(payload, null, 2);
}

function renderError(error) {
  resultTitle.textContent = "Request failed";
  categoryMetric.textContent = "-";
  decisionMetric.textContent = error.code ?? "Error";
  tokensMetric.textContent = "-";
  statusPill.textContent = "Error";
  statusPill.className = "status-pill error";

  renderData({
    code: error.code ?? "request_failed",
    target: error.target ?? "-",
    traceId: error.traceId ?? "-"
  });
  renderReasons([error.message ?? "The request failed."]);
  rawJson.textContent = JSON.stringify(error, null, 2);
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

  return String(value);
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
