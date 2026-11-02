# DebugPilot AI — Privacy Notice (v0.1.1, Phase 5C)

DebugPilot diagnoses .NET diagnostics you supply. Deterministic analysis works fully offline.

## When data leaves your machine
Only when **all** of these are true:
1. You saved an AI provider (endpoint, model, API key) in the **AI & Privacy** tab, and
2. a deterministic rule found nothing, or you clicked **Ask AI to go deeper**, and
3. you reviewed the **exact redacted payload** in the consent dialog and ticked the (unchecked-by-default) consent box.

Cancel sends nothing. The consent hash must match the payload that is sent. Each request counts against a per-session cap shown in the UI.

## What is in the payload
Diagnostic code/message, file location, a bounded source excerpt (±30 lines), relevant project/package metadata, trimmed build output, stack trace and test failure details — only what the host supplies for the current error. Debugger locals are excluded unless explicitly enabled. Files such as `.env*`, `secrets.json`, `appsettings*.json`, `*.pfx/.p12/.pem/.key/.snk`, SSH keys, `.netrc`, and anything under `UserSecrets`, `.aws`, `.ssh` never contribute source text.

## Redaction (defense in depth, not a guarantee)
Before preview and before sending, DebugPilot masks: private-key blocks, URL credentials, JWTs, AWS/GitHub/Slack/Google/`sk-` API keys, Bearer/Basic credentials, connection-string secrets, quoted and unquoted `password/token/secret/api_key…` assignments, e-mail addresses and user-profile paths. Redaction runs **before** the byte budget is applied, and **fails closed**: if redaction cannot complete safely the text is withheld. New secret formats may not be recognized — always read the preview.

## Credentials and endpoints
API keys live in Windows Credential Manager (current user). Provider settings (endpoint/model/deployment) are stored without secrets in `%LOCALAPPDATA%\DebugPilot\ai-provider.json`. Remote endpoints must be HTTPS; HTTP is allowed only for loopback (e.g. Ollama). HTTP redirects are refused so an API key can never be forwarded to another host. Provider error bodies are never logged or shown.

## Telemetry and local data
No telemetry is collected. Session history is in memory only and is lost when Visual Studio closes. When you apply a fix, a checkpoint containing a copy of the file as it was before the change is stored under `%LOCALAPPDATA%\DebugPilot\checkpoints` so it can be rolled back. After a fix, DebugPilot rebuilds the owning project in a child process (`dotnet build` or Visual Studio's MSBuild); that build behaves like any build you run (restore may contact your configured NuGet feeds), and DebugPilot itself sends nothing. A "Delete all data" command and retention controls are **not yet implemented** and are required before a Marketplace release.

## Status
This document describes the current implementation, not a completed privacy review. See `docs/PHASE-5-AUDIT.md` and `docs/PHASE-5C.md`.
