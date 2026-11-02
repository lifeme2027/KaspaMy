# NKaspa feature requests (for 本地1)

KaspaMy depends on `NKaspa` **0.1.11-alpha** (exact pin) and does not modify it. These are gaps found while building KaspaMy. Workarounds live in KaspaMy only until NKaspa covers them.

## R1 — Public node resolver
NKaspa ships no equivalent of rusty-kaspa's `Resolver` (`rpc/wrpc/client/Resolvers.toml`, `GET {resolver}/v2/kaspa/{network}/{tls|any}/wrpc/json`).
- Workaround: `KaspaMy.Core/NodeEndpoints.cs` has a minimal resolver plus hard-coded nodes. On 2026-11-01 the public resolvers we tried returned 404/5xx, so the real working path is hard-coded nodes.
- Request: `KaspaResolver` with the official resolver list, TLS/any selection, timeouts and fallback; ideally `KaspaWrpcJsonClient.ConnectAsync(network)` via resolver.

## R2 — Known public endpoints per network
Request: a documented constant/list of JSON wRPC endpoints for mainnet and testnet-10 (KaspaMy uses `eva.kaspa.green` mainnet, `vector-10.kaspa.green` / `muon-10.kaspa.blue` TN10).

## R3 — Fee/mass estimate without keys
To estimate a fee before unlocking a wallet, KaspaMy uses a fixed typical mass (2036 for 1-in/2-out P2PK Schnorr). Request: a helper that computes compute+storage mass (KIP-9) for a hypothetical shape (n inputs, outputs with amounts) without signing keys, and exposes the current network minimum feerate.

## R4 — DAA-aware mass policy
KaspaMy copies KasNetWallet's `MassPolicy` wrapper to pick parameters by DAA score (activation of storage-mass rules on mainnet/testnet). Request: `NKaspa.Policy.MassPolicy.ForNetwork(network, daaScore)` as the single source of truth.

## R5 — Secret-safe mnemonic type
KaspaMy wraps phrases in its own `SecretText` (masked `ToString`, refuses JSON) so they cannot be logged by accident. Request: a redacting type or an opt-in `Reveal()` API, and zeroable key buffers where possible.

## R6 — Typed wRPC errors
Connection/RPC failures surface as generic exceptions (WebSocket/timeout), which KaspaMy maps itself. Request: typed exceptions (unreachable, wrong network, not synced, no utxoindex) so wallets can fail over cleanly.
