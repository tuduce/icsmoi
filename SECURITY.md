# Security Policy

icsmoi is a hobby project that controls real force-feedback hardware and
reads simulator telemetry. If you find a security issue, please report it
privately rather than opening a public issue.

## Reporting a vulnerability

Use GitHub's **[private security advisory](../../security/advisories/new)**
feature for this repository, or email **cristian.tuduce@gmail.com**.

Please include:

- A description of the issue and its potential impact.
- Steps to reproduce, if possible.
- If the issue could affect the **Effects safety gate** (i.e. could cause a
  hardware effect to be created/updated without the user explicitly enabling
  it, or prevent the toggle from disabling effects) — please say so
  explicitly, as that class of issue is treated as highest priority given
  this software drives physical hardware. See
  [`user_manual/08-safety.md`](user_manual/08-safety.md) for the safety model
  this relates to.

There is no bug bounty; this is an unfunded hobby project. You'll get a
response and credit (if wanted) once the issue is addressed.
