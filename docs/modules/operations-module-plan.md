# Operations implementation scope

Read [Operations Module Foundation](operations-foundation.md) for the current behavior and ownership. This branch implements the flight scheduling baseline from `main`; it does not inherit the separate HandOver implementation.

The current scope is the flight register, single/batch scheduling, basic planning edits, direct planned-service and staff lists, existing authorization, and optimistic concurrency. Work orders, actual work, history, status changes, handover, review, reports, and mobile operations will be considered individually in later work; this document does not authorize their implementation.

Verify this baseline with the solution build, retained flight domain/application checks, portal checks, SQL endpoint authorization/concurrency tests, and a SQL migration test covering preservation of flight planning data. Record actual verification results in the change report rather than carrying forward results from retired features.
