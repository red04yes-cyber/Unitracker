# Secret scanning and Moodle endpoint hardening

Date: 12 November 2026

## Secret-scanning policy

The repository extends Gitleaks' default rules and retains the application-specific
Moodle-token and DPAPI-blob rules. The allowlist is deliberately match-scoped:
it contains only exact deterministic dummy literals used by security fixtures.
It does not exclude directories or files.

Consequently, the history and working-tree scans include source code, tests,
documentation, examples, workflow files and repository-root files. The allowlist
uses `regexTarget = "match"` so an allowed fixture literal does not exempt the
rest of its file or line. The CI invocation keeps `--redact` enabled.

When adding a new dummy credential fixture, add the narrowest exact pattern
needed and verify that it cannot match a realistic credential. Never add a
directory-wide or file-wide exception to make a scan pass.

## Moodle endpoint policy

`MoodleClient` now rejects loopback endpoints by default, for both HTTP and
HTTPS. This prevents the normal application connection flow from sending a
user's Moodle web-service token to a service listening on the local machine.

The low-level client retains an explicit `allowLoopbackHttp: true` opt-in for
local development and isolated tests. `MoodleApplicationService` does not
expose that opt-in and continues to construct the client with its secure
default. The shared `OutboundEndpointPolicy` and all AI-related code are
unchanged.

Other existing endpoint controls remain in force: absolute URL validation,
credential/query/fragment rejection, private and link-local host rejection,
HTTPS for remote endpoints, disabled redirects, POST token transport, bounded
responses, request timeout and same-origin download checks.

## Regression coverage

- The client rejects loopback HTTP and HTTPS unless the explicit development
  opt-in is supplied.
- The application service rejects IPv4 loopback, HTTPS loopback and localhost
  before storing a token.
- The Security Scan workflow checks the full Git history and working tree with
  Gitleaks; the Windows workflow runs the complete .NET test suite and WPF
  release smoke test.

No live Moodle endpoint or real credential is used by these tests.
