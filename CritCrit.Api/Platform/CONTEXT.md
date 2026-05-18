# Platform Context

Cross-cutting platform concerns: audit, auth, configuration, errors, Marten persistence, tenancy.

## Language

<!-- Populated by /grill-with-docs as terms get resolved. -->

**Tenant**:
The isolation boundary for data and access in the platform.
_Avoid_: Customer, account

**Configuration Schema**:
The typed definition that constrains the values an org node may set under a given key.
_Avoid_: Schema doc, config spec

## Relationships

<!-- Populated as the model crystallises. -->

## Flagged Ambiguities

<!-- Anything contested — record here so the next /grill-with-docs run resolves it. -->
