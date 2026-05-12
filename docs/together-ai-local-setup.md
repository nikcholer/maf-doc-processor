# TogetherAI Local Setup

The local demo uses separate TogetherAI model roles so fast classification can move independently from slower extraction:

- `DocumentClassification`: `Qwen/Qwen3.5-9B`
  - Input price: `$0.10` per 1M tokens
  - Output price: `$0.15` per 1M tokens
- `DocumentExtraction`: `google/gemma-4-31B-it`
  - Input price: `$0.20` per 1M tokens
  - Output price: `$0.50` per 1M tokens
- `TextTesting`: `google/gemma-4-31B-it`

All roles use:

- Provider: `TogetherAI`
- Endpoint: `https://api.together.ai/v1`
- API key environment variable: `TOGETHER_API_KEY`

Legacy `AiModels:ImageRecognition` config is still accepted as a fallback for classification and extraction, but new config should use `DocumentClassification` and `DocumentExtraction`.

Set the user-level API key in PowerShell:

```powershell
[Environment]::SetEnvironmentVariable("TOGETHER_API_KEY", "<your-key>", "User")
```

For the current terminal session only:

```powershell
$env:TOGETHER_API_KEY = "<your-key>"
```
