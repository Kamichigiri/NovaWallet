# AI Usage

AI tools were used as an implementation accelerator and design-review assistant.

## Tools used

- ChatGPT & Claude — architecture discussion, code generation, business & logic review.
- GitHub Copilot — local completion/refactoring assistance where applicable.

## Example prompts

1. "Design a .NET 8 wallet transfer flow that is safe under concurrent debit requests."
2. "Review an idempotent transfer endpoint and identify replay, duplicate-key, and transaction-ordering edge cases."
3. "Design a MS SQL Server schema for wallet balance, ledger entries and immutable audit records using integer kobo amounts."

## Example AI mistake caught

A naive first approach can read the wallet balance without database locks outside the transaction scope. Under concurrent requests this permits two requests to observe the same balance and both spend it. The final implementation instead performs the balance check while the wallet row is locked inside a database transaction and uses deterministic lock ordering for the two participating wallets.

Another common AI error is representing amounts as decimal/double at the application boundary. The implementation keeps the money path as integer kobo.

