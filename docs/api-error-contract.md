# API Error Contract

The local demo API returns a consistent JSON error body for validation, configuration, model, and workflow failures:

```json
{
  "code": "machine_readable_error_code",
  "message": "Human-readable explanation.",
  "target": "optional field or target",
  "traceId": "ASP.NET trace identifier"
}
```

## Error Codes

| Code | HTTP status | Target | Meaning |
| --- | ---: | --- | --- |
| `invalid_document_upload` | `400` | Upload field, usually `image` or `form` | The request was not multipart, did not include the configured image field, exceeded size limits, or used an unsupported content type or extension. |
| `model_configuration_invalid` | `500` | `null` | Required model configuration is missing or invalid, most commonly the configured API key environment variable. |
| `model_response_invalid` | `502` | `null` | The model returned an empty response, invalid JSON, or JSON that could not be parsed into the expected schema. |
| `model_timeout` | `504` | `null` | The provider did not return within the configured model timeout. |
| `model_provider_failed` | `502` | `null` | The configured provider returned a non-timeout failure after retry handling. |
| `document_processing_failed` | `502` | `null` | A known processing failure occurred outside structured model parsing, such as a transport failure. |
| `document_processing_unhandled` | `500` | `null` | An unexpected exception escaped the known failure paths. API logs should be used with `traceId`. |

Client/request cancellation is not converted to this error contract. The API lets cancellation propagate so the host can treat it as an aborted request instead of reporting a model or workflow failure.

## Response Notes

- `traceId` is always populated for API-generated errors and should be shown in logs or bug reports.
- `target` is populated only when a specific request field caused the failure.
- Error `message` values are intended to be readable in the local demo UI, but clients should branch on `code`.

The offline `ApiIntegrationTests` suite exercises every error code in this table, the validation-specific `target` values, populated trace identifiers, and request-cancellation propagation.
