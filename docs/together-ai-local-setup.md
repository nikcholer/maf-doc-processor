# TogetherAI Local Setup

For the first live-model slice, both configured roles can point at Gemma 4 on TogetherAI:

- Provider: `TogetherAI`
- Endpoint: `https://api.together.ai/v1`
- Model ID: `google/gemma-4-31B-it`
- API key environment variable: `TOGETHER_API_KEY`

Keep `ImageRecognition` and `TextTesting` as separate config roles even while they use the same model. This lets later work move individual tasks to different models without changing the workflow contracts.

Set the user-level API key in PowerShell:

```powershell
[Environment]::SetEnvironmentVariable("TOGETHER_API_KEY", "<your-key>", "User")
```

For the current terminal session only:

```powershell
$env:TOGETHER_API_KEY = "<your-key>"
```
