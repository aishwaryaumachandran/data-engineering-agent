# .NET Backend + Frontend Runtime Flow

This diagram reflects the current runtime behavior of the Next.js frontend and the `src-dotnet` Azure Functions backend.

```mermaid
flowchart TD
  U[Auditor Browser] --> FE1[Next.js Dashboard]
  FE1 -->|startTransform| API[Frontend API module]
  API -->|POST transform| NX[Next.js API rewrite]
  NX -->|to Azure Functions /api/transform| T1[StartTransform trigger]

  T1 --> ORCH[Durable Orchestrator]

  U --> FE2[Next.js Transform Page]
    FE2 -->|poll every 3s: getStatus/getMessages| API
  API -->|GET status| T2[GetTransformStatus trigger]
  API -->|GET messages| T3[GetMessages trigger]
    T3 --> COSMOS[(Cosmos conversation container)]

    FE2 -->|Approve / Reject / Feedback| API
  API -->|POST review| T4[SubmitReview trigger]
    T4 -->|RaiseEvent review| ORCH

    ORCH --> A1[Phase 1 ChangeDetectionActivity]
  A1 --> AC[(approved-code by client)]
    A1 --> ADLSM[(ADLS mappings)]
    A1 --> ADLSD[(ADLS data)]
    A1 --> AOAI[(Azure OpenAI)]

    A1 -->|reuse path| REUSE[Use existing pseudocode + PySpark]
    A1 -->|regen path| A2[Phase 2 ProfilingActivity]
    REUSE --> LOOP
    A2 --> AOAI
    A2 --> ADLSM
    A2 --> ADLSD

    A2 --> A3[Phase 3 pseudocode review loop]
    A3 -->|wait_for_external_event review| U
    U -->|approved=false| REV[RevisePseudocodeActivity]
    REV --> AOAI
    REV --> A3
    U -->|approved=true| A4A[Phase 4a CodeGenerationActivity]
    A4A --> AOAI

    subgraph LOOP[Phase 4b and 5 retry loop max 5]
      E1[SparkExecutionActivity] --> DBX[(Databricks Jobs)]
      DBX --> ADLSD
      DBX --> ADLSO[(ADLS output)]
      E1 -->|failure| FIX[FixCodeActivity]
      FIX --> AOAI
      FIX --> E1
      E1 -->|success| I1[IntegrityChecksActivity]
      I1 --> ADLSO
      I1 -->|fail| FIX
    end

    LOOP --> A6[Phase 6 output review]
    A6 -->|wait_for_external_event review| U
    U -->|approved=false| BACK[Set pysparkCode = null]
    BACK --> A2
    U -->|approved=true| SAVE[SaveCodeActivity]
    SAVE --> AC

    ORCH --> LOG[LogMessageActivity]
    LOG --> COSMOS

    SAVE --> DONE([TransformResult completed with output path])
```

## Notes

- Frontend API calls are centralized in `frontend/lib/api.ts`.
- Frontend routes are in `frontend/app/page.tsx` and `frontend/app/transform/[id]/page.tsx`.
- Backend trigger endpoints are in `src-dotnet/src/DataEngineeringAgent.Functions/Triggers/TransformTrigger.cs`.
- Orchestration flow is in `src-dotnet/src/DataEngineeringAgent.Functions/Orchestrators/TransformOrchestrator.cs`.
