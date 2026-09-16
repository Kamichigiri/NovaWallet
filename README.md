# NovaWallet Ledger Service — .NET 8

A reference implementation for the NovaWallet Ledger Service take-home task. The design is intentionally focused on correctness under concurrent transfers, replay-safe idempotency, integer money arithmetic, auditability, and a runnable Docker Compose stack.

## Projects

- `NovaWallet.Api` — HTTP API, JWT middleware, Swagger, Problem Details, controllers.
- `NovaWallet.Application` — application services and contracts; transfer orchestration and invariants.
- `NovaWallet.Domain` — wallet, transfer, ledger and audit entities.
- `NovaWallet.Infrastructure` — EF Core + MS SQL Server persistence and row-locking query.
- `NovaWallet.Tests` — domain tests and an integration-test placeholder for the concurrency case.

## Requirements implemented

- Create wallet with zero balance.
- Get current NGN balance in kobo.
- Credit wallet.
- Atomic transfer with deterministic wallet lock ordering.
- MS SQL Server transaction + row-level locking to protect a source wallet from concurrent debits.
- Idempotency-Key required on transfer. Same key + same payload replays the original result; same key + different payload returns 409.
- Paginated newest-first statement.
- Server-side daily outbound limit of ₦500,000 (50,000,000 kobo), reset using WAT date.
- Separate append-only application audit records.
- JWT bearer authentication.
- RFC 7807-style structured errors.
- `docker compose up --build` starts API + MS SQL Server.

## Run

```bash
docker compose up --build
```

Swagger: `http://localhost:8080/swagger`


Swagger: `http://localhost:8080/swagger`

## Development JWT

In `Development`, call `POST /dev/auth/token` with:

```json
{ "subject": "developer-1" }
```

Use the returned token as `Authorization: Bearer <token>`.

## Example flow

1. Create token.
2. Create wallet A and wallet B.
3. Credit wallet A.
4. Call `POST /api/transfers` with an `Idempotency-Key`.
5. Repeat the exact request with the same key — it returns the original transfer instead of moving money twice.
6. Reuse the key with a different amount — receive `409 Conflict`.
7. Query the statement endpoint.

## Concurrency design

The transfer service uses a serializable database transaction and explicitly locks the source and destination wallet rows using PostgreSQL `FOR UPDATE`. Locks are acquired in ascending wallet-ID order to reduce deadlocks when two transfers run in opposite directions. The balance check and debit occur while the wallet row is locked, so a second concurrent transfer cannot observe the same spendable balance and both commit.

Idempotency is reserved inside the same transaction using PostgreSQL `INSERT ... ON CONFLICT DO NOTHING`. The key stores a SHA-256 fingerprint of the request and the transfer ID. A different payload using an existing key is rejected.

## Money

Money is always represented as `long` kobo values. No `float` or `double` participates in wallet arithmetic.

## Notes for production hardening

The take-home scope uses `EnsureCreated()` for simplicity. A production deployment should use versioned EF Core migrations and a controlled migration process. The development token endpoint must be removed or disabled outside development and the JWT signing key must come from a secret manager. Database access should use least-privilege credentials.

## Tests

The domain tests can run without a database. The concurrency integration test uses MS SQL Server and defaults to a separate local database named `novawallet_test`:

```bash
dotnet test
```

To use another test database, set `NOVAWALLET_TEST_DB`. The concurrency test creates/destroys its test database schema, so do not point it at a database containing data you need to keep.
