# Infra Context

Aspire host orchestration: resource composition, dev-time service wiring, container/managed resource defaults.

## Language

<!-- Populated by /grill-with-docs as terms get resolved. -->

**Aspire Resource**:
A named resource (database, blob store, identity provider, API project) declared in the AppHost and consumed by other projects via reference.
_Avoid_: Service, dependency

**Connection Reference**:
The strongly-typed binding a consumer project receives for an Aspire Resource (e.g. an SDK-format connection string).
_Avoid_: Connection string, env var

## Relationships

<!-- Populated as the model crystallises. -->

## Flagged Ambiguities

<!-- Anything contested — record here so the next /grill-with-docs run resolves it. -->
