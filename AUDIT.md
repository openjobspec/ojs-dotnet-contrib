## 1. Summary

- A subsequent release-readiness pass changed package/build/release metadata only; the
  clean-code compatibility conclusions below still describe the production refactor.
- OJS-DNC-001 through OJS-DNC-007 are implemented with characterization tests at each compatibility-sensitive boundary.
- OJS-DNC-008, OJS-DNC-009, and OJS-DNC-010 passed the guardrail review without production splits or renames: the reviewed workflow, cron, worker lifecycle, handler, and record units are cohesive, and no additional dead private state was found.
- OJS-DNC-011 remains intentionally package-local. Health, environment, and AES decisions occur once per package; sharing them would add a cross-package abstraction or alter package-specific errors and wire behavior.
- OJS-DNC-012 names the two explicitly characterized timing policies. Ambiguous ignored-error behavior remains Deferred rather than choosing a new propagation or logging contract.
- Public APIs and extension methods, route templates/order/status/JSON, DI registrations/lifetimes/order, options/configuration shapes, package metadata, and dependencies are unchanged.

## 2. Findings and dispositions

| ID | disposition | implementation | programmatic evidence |
|---|---|---|---|
| OJS-DNC-001 | Implemented in preserved first pass | `MapOjsCron` remains the route declaration boundary; six request handlers are owned by `OjsCronEndpointHandlers`. | `OjsCronEndpointTests.MapOjsCron_PreservesRouteContractAndOrder` locks methods, templates (including the trailing slash), names, display names, and order. |
| OJS-DNC-002 | Implemented | `MapOjsWebhook` now declares only the route. Private units own request parsing, SDK `Job` mapping, scoped handler dispatch, and response policy in `OjsWebhookEndpointHandler.cs`. | `OjsWebhookEndpointTests` locks route metadata, exact JSON/status policies, complete SDK mapping, invalid-state fallback, scoped resolution/disposal, success, and handler failure. |
| OJS-DNC-003 | Implemented | `OjsEncryptionMiddleware` retains pipeline selection, buffering, body replacement, logging, and failure orchestration. `OjsAes256GcmPayloadCodec` owns the AES-256-GCM nonce/ciphertext/tag wire format. | `OjsEncryptionTests` locks a known AES vector, 12-byte nonce/16-byte tag layout, round trips, exact short-payload error, request buffering/rewind, exact replacement bytes/content length, and decryption-failure JSON. |
| OJS-DNC-004 | Implemented | ASP.NET and WorkerService configuration binding/environment precedence are separated from private DI graph registration in package-local concrete units. Public extension methods remain the only entry points. | Registration tests snapshot exact descriptor order, lifetime, instance/type/factory kind, optional graph ordering, and returned public builder/collection identity. Both environment suites lock configuration-before-environment precedence, queue splitting, and invalid concurrency behavior. |
| OJS-DNC-005 | Implemented, including an unambiguous race fix | Unread `SubscriptionEntry` event type/job type/handler payload was removed. One lock-protected private registry owns IDs, unsubscribe actions, count, and disposal state, preventing subscribe/dispose leakage while preserving the public service API. | `SubscriptionDispose_IsIdempotentAndStopsCallbackDelivery` verifies callback lifetime. `ConcurrentSubscriptionAndServiceDisposal_LeavesNoActiveCallbacks` verifies no registered callback survives the race; the focused suite passed five consecutive stress runs. |
| OJS-DNC-006 | Implemented / poll-only Deferred | The unused `OJSClient` constructor dependency and field were removed. Scoped listener resolution, event-type matching, and per-listener error isolation are owned by `OjsEventListenerDispatcher`. | `ListenerService_FiltersMapsScopesAndIsolatesListenerFailures` locks filtering, SDK event mapping, one shared dispatch scope, disposal, cancellation token flow, and continued delivery after one listener throws. Constructor reflection verifies the unused client dependency is absent. |
| OJS-DNC-007 | Implemented / remote execution Deferred | `OjsEncryptionService` owns separate local AES and remote HTTP client units and no longer retains unread options/AES/HTTP mixed state. Remote construction/disposal side effects remain, but no remote calls were invented. | `OjsEncryptionServiceTests` lock the known AES wire vector, prefix/error behavior, codec-only no-key behavior, invalid URL exception, singleton registration, and absence of direct options/`AesGcm`/`HttpClient` state on the public service. |
| OJS-DNC-008 | Retained after guardrail review | `OjsWorkflowService` remains cohesive around workflow operations. Chain/group symmetry is semantic, and moving public records would not separate an actor. | Existing workflow tests continue to cover mappings and public records; private-field review found no dead state. |
| OJS-DNC-009 | Retained except timing names in OJS-DNC-012 | Cron and worker lifecycle methods remain single-actor orchestration. No physical split was made solely for length. | Cron/parser, worker options, hosted-service, listener, and registration suites cover the retained behavior. |
| OJS-DNC-010 | Retained after naming review | Domain-qualified service/handler/listener names and narrow registration records remain unchanged; renaming would change public APIs or add no responsibility boundary. | Source search still finds no production `Manager`, `Helper`, or `Utils` role names. |
| OJS-DNC-011 | Retained package-locally | No cross-package health/config/encryption abstraction was introduced. The ASP.NET and WorkerService implementations have package-specific registration, error, prefix, and payload responsibilities despite similar concepts. | Package-local source search finds one health decision, one environment precedence unit, and one AES codec owner per package. Existing health, configuration, encryption, and registration tests lock their distinct observable behavior. |
| OJS-DNC-012 | Partially implemented; ambiguous errors Deferred | The cron duplicate-suppression window and shutdown progress interval are named internal `TimeSpan` policies. Broad boundary catches and public error bodies remain unchanged. | Tests lock `OjsCronSchedulerService.DuplicateSuppressionWindow == 1 minute` and `OjsWorkerBackgroundService.ShutdownProgressInterval == 5 seconds`. |

## 3. Compatibility evidence

- Webhook route template, HTTP method, endpoint name, display name, statuses, response JSON fields/messages, handler selection, SDK mapping, and scope disposal are characterized.
- Encryption uses the existing `[12-byte nonce][ciphertext][16-byte tag]` layout and exact UTF-8 request replacement behavior.
- ASP.NET and WorkerService DI tests snapshot registration order and descriptor shape without changing registrations or lifetimes.
- Environment variables remain higher precedence than bound configuration only when non-empty; invalid concurrency retains the bound/default value.
- Subscription disposal invokes the SDK unsubscribe callback once and is race-safe.
- Listener dispatch retains configured event filtering, SDK data mapping, one scope per event, and per-listener exception isolation.
- Worker encryption retains local-only encrypt/decrypt behavior even when a codec URL is configured.

## 4. Deferred

- WorkerService event-listener poll-only behavior: no polling endpoint, cursor, retry, or cancellation contract is specified.
- Remote codec execution in both packages: request/response schema, authentication, retry, fallback, timeout, and local-versus-remote precedence require product intent.
- ASP.NET `OjsWorkerHostedService` fire-and-forget start failures: propagating, retaining for `StopAsync`, or logging would each create a different lifecycle/error contract.
- `OjsEventHandlerRegistration` consumption, ASP.NET worker timeout/poll/heartbeat/shutdown option consumption, and workflow `name` transmission require product behavior decisions.
- Cron pause/resume backend mutation, cron parser validation edge cases, and exception-message exposure remain bug/security-contract decisions rather than local responsibility refactors.
- No public records, protocol strings, route fragments, option properties, or package boundaries were commonized.

## 5. Out of scope

- Public API or extension-method changes.
- Route, status, JSON, middleware order, DI descriptor/lifetime/order, options/configuration shape, package metadata, framework, or dependency changes.
- New polling or remote codec features.
- Sibling repositories, staging, commits, pushes, merges, releases, or stash operations.
