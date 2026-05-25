# TODOS

Project-level follow-ups surfaced during reviews. Each entry captures the
**what**, **why**, **context**, **depends-on**, and **priority** so future-you
(or a contributor) can pick it up cold without re-reading the conversation
that surfaced it.

Priority legend:
- **P1** — blocks a release.
- **P2** — should land in the next maintenance release.
- **P3** — capture-and-defer; revisit at the next planning cycle.

---

### P2 — Unit test for `RedactConnectionString` helper

- **What:** Add unit tests for
  [`src/Syed.Messaging.RabbitMq/RabbitMqTransport.cs::RedactConnectionString`](src/Syed.Messaging.RabbitMq/RabbitMqTransport.cs).
  Three cases minimum:
  1. URI with userinfo: `amqp://user:pass@host:5672/` → output has empty
     username and password components (no `user:pass@` segment).
  2. URI without userinfo: `amqp://host:5672/` → unchanged round-trip.
  3. Garbage / unparseable input: returns the `<unparseable>` fallback
     instead of throwing.
- **Why:** The helper landed in v1.2.2 as the fix for credential leakage on
  connection-failure log lines (see the v1.2.2 changelog in
  [README.md](README.md) and the migration guide's "Credentials, secrets,
  and production hardening" section). It's a small `UriBuilder` wrapper, so
  the bug risk is low — but the failure mode (silent un-redaction under a
  future refactor) is the exact behavior we just shipped to fix. Locking
  it with a test prevents regression.
- **Context:** Surfaced by `/plan-eng-review` on 2026-05-24 during review of
  the post-v1.2.2 smoke-test plan. The method is currently `internal static`,
  so the test project at `tests/Syed.Messaging.RabbitMq.Tests/` will need
  either `[InternalsVisibleTo("Syed.Messaging.RabbitMq.Tests")]` on the
  RabbitMq project OR the method should be promoted to `public static` on
  a small dedicated helper class. Recommended: the latter — the method has
  no production dependency on transport state, so it belongs in a
  free-standing utility class (e.g.
  `src/Syed.Messaging.RabbitMq/Internal/ConnectionStringRedactor.cs`).
- **Depends on / blocked by:** Nothing. Can land any time.
- **Priority:** P2 — would land in a v1.2.3 maintenance release, not
  blocking Phase 1 (chaos-by-default) work.
