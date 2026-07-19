# Secrets and security

## Secrets

Bulk provider snapshots and on-demand secrets are separate abstractions. Use `IMagicSecretProvider.GetAsync<T>` for a secret that should be fetched only when needed. The returned lease may include expiration metadata and should be disposed when the caller is finished.

MagicSettings cannot guarantee erasure of managed strings. For extremely sensitive values, prefer byte buffers and platform key stores that support non-exportable keys. Remote or environment secrets are not written into the persistent JSON file. Mark paths sensitive so diagnostics redact them.

The built-in HTTP control-plane transport implements `IMagicSecretTransport`, so normal one-call initialization registers `IMagicSecretProvider` automatically. A custom transport can opt in by implementing the same interface.

Each request is bound to authority audience, secret endpoint, requested name, short lifetime, and one-time nonce. The server uses `MagicSecretService` plus an application-provided `IMagicSecretResolver`; MagicSettings does not dictate server storage.

## Security boundaries

MagicSettings can keep transient values out of the file, authenticate possession of a node credential, bind signatures to exact requests, reject expired or replayed proofs, support rotation and revocation records, pin a control-plane public-key fingerprint, and preserve destructive migration effects for review.

It cannot protect a secret from an attacker controlling the process or machine, securely erase arbitrary managed strings, make an untrusted endpoint safe merely because it uses HTTPS, revoke a stolen key without server-side revocation distribution, or infer whether separately installed nodes should share policy.

The default file identity store is intentionally replaceable. High-security deployments should use an OS or hardware-backed store with non-exportable keys.
