# TogetherAI Local Setup

The local demo uses separate TogetherAI model roles so fast classification can move independently from slower extraction:

- `DocumentClassification`: `Qwen/Qwen3.5-9B`
  - Input price: `$0.10` per 1M tokens
  - Output price: `$0.15` per 1M tokens
- `DocumentExtraction`: `Qwen/Qwen3.5-9B`
  - Input price: `$0.10` per 1M tokens
  - Output price: `$0.15` per 1M tokens
- `DocumentRegionDetection`: `Qwen/Qwen3.5-9B`
  - Input price: `$0.10` per 1M tokens
  - Output price: `$0.15` per 1M tokens
- `TextTesting`: `google/gemma-4-31B-it`

All roles use:

- Provider: `TogetherAI`
- Endpoint: `https://api.together.ai/v1`
- API key environment variable: `TOGETHER_API_KEY`
- Request timeout: `60` seconds
- Transient retry policy: `2` retries, starting at `500` ms backoff

## Data Boundary

The web app and workflow run locally, but model inference does not. For individual uploads, prepared classification and extraction images are sent over HTTPS to the configured model provider. For capture sets, the prepared whole-source image is also sent for region detection, followed by the accepted member crops for classification and extraction. A transient retry can resend the same prepared image.

The application does not persist uploaded images or provider responses, but that does not control provider-side processing or retention. Review the configured provider's current privacy, retention, and acceptable-use terms before submitting material, and use only non-confidential samples unless those terms and your own obligations permit otherwise.

Legacy `AiModels:ImageRecognition` config is still accepted as a fallback for classification and extraction, but new config should use `DocumentClassification` and `DocumentExtraction`. Region detection always uses its explicit `DocumentRegionDetection` role.

`TextTesting` is reserved configuration for the opt-in quality-review prototype and other explicit text experiments. The individual-document path uses `DocumentClassification` and `DocumentExtraction`. Composite capture also uses `DocumentRegionDetection` for layout proposals; accepted crops then reuse classification and extraction.

Set the user-level API key in PowerShell:

```powershell
[Environment]::SetEnvironmentVariable("TOGETHER_API_KEY", "<your-key>", "User")
```

For the current terminal session only:

```powershell
$env:TOGETHER_API_KEY = "<your-key>"
```
