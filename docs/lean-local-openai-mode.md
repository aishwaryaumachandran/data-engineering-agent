# Lean Local Mode (OpenAI-Only) for `src-dotnet` + `frontend`

This guide describes how to run the current solution in a **lean local mode** that keeps Azure OpenAI for pseudocode/code generation, while minimizing or removing other Azure dependencies.

The goal is to keep the existing implementation intact and switch modes with a parameter.

## Proposed runtime switch

Use a single config flag:

- `Runtime__Mode=Full` (default, current behavior)
- `Runtime__Mode=Lean` (minimal local behavior)

Keep **Full** as default so production and existing flows remain unchanged.

---

## Current availability (what exists today)

| Capability | Full mode | Lean mode |
|---|---|---|
| Runtime switch via config (`Runtime__Mode`) | Available | Available |
| Runtime switch via request (`runtime_mode`) | Available | Available |
| OpenAI pseudocode/code generation | Available | Available |
| Local file-backed data service | Not applicable | Available (`LocalDataService`) |
| Local conversation store | Not applicable | Available (`LeanConversationService`) |
| Spark execution in lean | Databricks run | Available via local PySpark execution (`LeanExecutionService`) |
| Retry and correction loop | Fix config from Spark/Integrity errors | Revises pseudocode from errors, regenerates code, retries |

> Full mode remains unchanged and default.  
> Lean mode now runs with OpenAI + local adapters + local PySpark execution.

---

## Full vs Lean (target behavior after lean implementation)

| Area | Full mode (current) | Lean mode (proposed) |
|---|---|---|
| OpenAI | Azure OpenAI via `OpenAiService` | Same (required) |
| Data access | ADLS Gen2 via `AdlsService` | Local file-backed service implementing `IAdlsService` (reads from repo folders like `input_data\`, writes to `output\`) |
| Conversation state | Cosmos DB via `CosmosService` | In-memory or local JSON store implementing `ICosmosService` |
| Spark execution | Databricks via `DatabricksService` | Real local execution via `LeanExecutionService` (Python + local PySpark) |
| Integrity checks | Uses `IIntegrityService` + ADLS output reads | Either skip in lean or run limited local checks against local output |
| Durable flow shape | 6-phase orchestration | Same high-level phases, but execution/integrity become lean-safe branches |
| Approved code reuse | `approved-code\{client}\...` | Same |
| Frontend API contract | Existing endpoints | Same endpoints; optional mode selector in request |
| Required cloud resources for local run | OpenAI + ADLS + Cosmos + Databricks | **Azure OpenAI only** |

---

## What to change in `src-dotnet`

## 1) Add runtime options

Create a config class (for example `RuntimeOptions`) and bind:

- `Runtime:Mode` => `Full` or `Lean`

Optional local-path options for lean:

- `Lean:MappingsRoot` (default `input_data`)
- `Lean:DataRoot` (default `input_data`)
- `Lean:OutputRoot` (default `output`)

## 2) Conditional DI in `Program.cs`

Current `Program.cs` always wires Azure SDK clients and full services.  
Update DI to branch by `Runtime__Mode`:

- **Full**: keep current registrations exactly as-is.
- **Lean**:
  - register Azure OpenAI client + `IOpenAiService`
  - replace `IAdlsService` with local implementation
  - replace `ICosmosService` with in-memory/local implementation
  - replace `IDatabricksService` with lean execution implementation
  - avoid requiring Cosmos/Databricks/ADLS options validation in lean

## 3) Add lean service implementations (new classes)

- `LocalDataService : IAdlsService`
  - maps logical paths (`mappings/...`, `data/...`) to local filesystem paths
  - reuses existing sampling/parsing behavior where possible
- `LeanConversationService : ICosmosService`
  - stores messages in memory keyed by `thread_id` (or JSON file under temp/session folder)
- `LeanExecutionService : IDatabricksService`
  - executes generated script locally through Python/PySpark
  - captures stdout/stderr and returns execution errors for retry

## 4) Orchestrator behavior in lean mode

`TransformOrchestrator` currently assumes Spark + integrity loops.  
In lean mode, add a branch so the orchestration can complete without Databricks/ADLS output reads:

- keep change detection, profiling, pseudocode review, code generation
- execute generated code locally and run integrity checks against local output
- on failure, revise pseudocode from errors, regenerate code, and retry
- log clear lean-mode messages so UI still shows progress

## 5) Trigger payload (optional but recommended)

If you want per-request control from UI, extend request schema with mode:

- `runtime_mode: "full" | "lean"`

Precedence recommendation:
1. request `runtime_mode` (if provided)
2. fallback to `Runtime__Mode`
3. default `full`

---

## Frontend considerations (`frontend`)

Current UI can remain unchanged and use backend default mode.  
If you want a toggle:

- add mode selector on dashboard
- include `runtime_mode` in `startTransform` payload in `frontend\lib\api.ts`
- no change needed to status/messages/review polling contracts

---

## Lean local settings example (`src-dotnet/src/DataEngineeringAgent.Functions/local.settings.json`)

Use only what is needed for Durable Functions + OpenAI + local filesystem mode:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",

    "Runtime__Mode": "Lean",

    "OpenAi__Endpoint": "https://<your-openai-resource>.cognitiveservices.azure.com/",
    "OpenAi__DeploymentName": "gpt-4.1",
    "OpenAi__Temperature": "0.2",

    "Lean__MappingsRoot": "input_data",
    "Lean__DataRoot": "input_data",
    "Lean__OutputRoot": "output",

    "REPO_ROOT": "C:\\<path>\\data-engineering-agent"
  }
}
```

> In lean mode, avoid requiring Cosmos/Databricks/ADLS settings at startup.

---

## Implementation notes and guardrails

- Keep all existing full-mode code paths untouched.
- Make lean-mode behavior explicit in logs/messages to avoid confusion in UI.
- Ensure full-mode remains the default and is backward compatible.
- Never return success/completed in lean mode unless local Spark execution actually succeeded.
- Do not commit real secrets in `local.settings.json`; use placeholders or local-only files.

