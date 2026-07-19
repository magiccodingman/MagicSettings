# Control plane and node identity

## Control-plane integration

MagicSettings enables a control-plane relationship but does not implement a universal control plane. The client initiates synchronization and sends public node identity, a request-bound proof, application and schema metadata, the schema manifest, last known remote revision, and optional migration review information.

The server answers with approval state and a node-specific remote snapshot. The snapshot is an in-memory top-priority layer. It is not copied into OS environment variables and is never serialized into the local file. A server implementation chooses its own storage, approval UI, grouping, policy, encryption-at-rest, and authorization distribution strategy.

## Endpoint bootstrap

A control-plane endpoint may be known during startup or discovered later. Default resolution order:

1. Explicit runtime `ConfigureAsync` call.
2. Dedicated OS environment variable, normally `MAGICSETTINGS_CONTROL_PLANE_ENDPOINT`.
3. A path in the persistent local JSON document, normally `MagicSettings:ControlPlane:Endpoint`.
4. Code fallback.
5. No endpoint; remote synchronization remains disabled.

Only local bootstrap sources participate. The effective runtime snapshot is deliberately not used because it includes remote overrides. Allowing the current remote authority to overwrite its own endpoint would let it redirect the node's trust relationship.

Persistent endpoint changes can be detected by the local file watcher. The new authority is authenticated before replacing the old endpoint. A failed transition retains the prior effective snapshot rather than accepting settings from an untrusted endpoint. External environment-variable edits are generally invisible to an already running process; restart or call `ConfigureAsync` for an immediate transition.

## Identity and enrollment

MagicSettings creates an ECDSA P-256 installation credential during first initialization. The identity has a stable node ID, credential ID, public key and fingerprint, private key, and creation timestamp. The public descriptor is safe to send during enrollment. The private key remains inside `IMagicNodeIdentityStore` and the signing service.

The default file store restricts Unix permissions to the current user. It is a portable baseline, not a hardware-backed vault. Implement `IMagicNodeIdentityStore` to use DPAPI, Keychain, TPM, HSM, Kubernetes secrets, or another platform facility.

If the identity file is lost, a replacement identity is a new unapproved node. A name, hostname, or application ID is not proof that the replacement is the previous node.

## API authentication proofs

The same approved node identity can authenticate to APIs other than the control plane. Do not transmit the private key and do not reuse one static signature as a bearer token.

`IMagicNodeAuthenticator.CreateProofAsync` creates a short-lived signature bound to API audience, HTTP method, normalized target URI, request-body SHA-256, issuance and expiration, one-time nonce, node ID, and credential ID. The receiving API uses `MagicNodeProofVerifier`, a credential registry, and a replay cache. The registry can be populated by an authentication service and cached by each API.

```csharp
services.AddHttpClient<MyApiClient>()
    .AddMagicNodeAuthentication("MyApi");
```

The handler sends `Authorization: MagicNode <encoded-proof>`. Capturing a proof does not authorize a different endpoint, body, method, audience, or replay.

Never add a general-purpose private-key export method for convenience. A component that genuinely needs TLS client-certificate integration should receive a narrowly scoped handler or key-store integration rather than raw exportable bytes.

## Rotation, reset, and revocation

`IMagicNodeIdentityManager.RotateAsync` keeps the node ID and creates a new credential ID and keypair. It returns a continuity proof signed by the old credential. `MagicCredentialRotationService` verifies this proof and may mark the old credential `Retiring` while approving or holding the new credential. The client carries the continuity proof on its next normal sync; no server callback is required.

`ResetAsync` is destructive and requires explicit confirmation. It creates a new node ID and credential and clears the current remote snapshot. There is no continuity claim; the server must approve it again. Use reset when the old private key is lost or suspected compromised. Use rotation for ordinary renewal while the old credential remains trustworthy.

Local reset does not invalidate a stolen copy of the old key. The authorization server must revoke the old credential, and relying APIs must refresh authorization caches. `MagicCredentialAdministrationService` provides storage-agnostic approve and revoke helpers. Applications should choose an authorization-staleness policy appropriate to their risk.

## Outages and stale state

Remote snapshots are sticky by default. Losing WAN connectivity does not automatically erase settings required for LAN operation. MagicSettings retains the last known good remote snapshot, marks synchronization disconnected or faulted, continues using lower layers, retries later, and rejects malformed replacements.

Individual remote values may be marked `Expiring`. Expiration is appropriate for short-lived grants or tokens, not ordinary hostnames, ports, or offline operating policy. Configuration freshness and credential validity are separate concepts.
