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

Legacy `AiModels:ImageRecognition` config is still accepted as a fallback for classification and extraction, but new config should use `DocumentClassification` and `DocumentExtraction`. Region detection always uses its explicit `DocumentRegionDetection` role.

`TextTesting` is reserved configuration for future text-only experiments. The individual-document path uses `DocumentClassification` and `DocumentExtraction`. The composite-capture source boundary also uses `DocumentRegionDetection` for layout proposals; accepted crops then reuse the existing classification and extraction preprocessing. The capture API and UI are still being delivered through E3.

Set the user-level API key in PowerShell:

```powershell
[Environment]::SetEnvironmentVariable("TOGETHER_API_KEY", "<your-key>", "User")
```

For the current terminal session only:

```powershell
$env:TOGETHER_API_KEY = "<your-key>"
```
