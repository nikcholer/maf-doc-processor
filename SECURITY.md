# Security Policy

## Supported Version

This repository is a local demonstration rather than a hosted service or published package. Security fixes target the latest commit on `main`; historical tags are reproducibility markers and are not separately supported release lines.

The default launch profile binds to loopback, and the API has no authentication. Do not expose it to another machine or an untrusted network without adding appropriate authentication, authorization, transport security, quotas, and deployment-specific resource limits.

## Reporting a Vulnerability

Do not disclose a vulnerability, credential, confidential document, or provider response in a public issue. Use the repository Security tab's **Report a vulnerability** flow to submit a private report:

<https://github.com/nikcholer/maf-doc-processor/security/advisories/new>

Include the affected commit, reproduction steps, impact, and any safe diagnostic evidence. Redact API keys, source documents, model responses, personal data, and precise private-system details.

Ordinary reproducible bugs that do not involve a security or privacy impact can use the public bug-report form.
