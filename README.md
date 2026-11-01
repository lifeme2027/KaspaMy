# KaspaMy

A small, self-custodial Kaspa wallet for the command line and for AI assistants (MCP), built on .NET 8.

- `kaspamy` — CLI: create / import / address / balance / fee / validate / send / export-mnemonic. Every command supports `--json`.
- `kaspamy-mcp` — stdio [Model Context Protocol](https://modelcontextprotocol.io) server (official C# SDK, `ModelContextProtocol` 1.2.0).
- `KaspaMy.Core` — keystore, HD derivation, node access, transaction building.

Kaspa primitives (BIP39, BIP32, addresses, signing, wRPC JSON client) come from **[NKaspa](https://www.nuget.org/packages/NKaspa) `0.1.10-alpha`**, pinned exactly (`[0.1.10-alpha]`) from nuget.org. Wallet code is adapted from KasNetWallet (same author, MIT); see file headers.

## ⚠️ Security warnings — read first

- **Alpha software. Not audited.** Bugs can lose funds. Use testnet-10 (the default) or amounts you can afford to lose.
- **No promises of profit or value.** KaspaMy is a tool, not financial advice.
- Your recovery phrase is the wallet. KaspaMy never prints it except via `kaspamy export-mnemonic`, which needs an interactive terminal and a typed `I UNDERSTAND`. Write it down offline.
- `KASPAMY_PASSWORD` in the environment is convenient but **less secure** than the prompt (other processes/users and shell history may see it). The MCP server can *only* read the password from this variable — never from a tool argument — so the AI never sees it.
- Giving an AI agent a funded wallet is risky. The MCP server defaults to testnet, refuses mainnet unless `KASPAMY_MCP_ALLOW_MAINNET=1`, caps each send including the fee (10 KAS testnet / 1 KAS mainnet by default) and the total per day (50 / 2 KAS), only talks to allowlisted nodes, and disables `wallet_import` unless `KASPAMY_MCP_ALLOW_IMPORT=1`. `confirm=true` is set by the agent, so it is **not** human approval; your MCP client must ask you.

## Build

```bash
dotnet build -c Release
dotnet test                      # unit + CLI + MCP stdio tests
KASPAMY_TN10_IT=1 dotnet test    # also hit a live testnet-10 node (read-only)
dotnet publish src/KaspaMy.Cli -c Release -o out/cli
dotnet publish src/KaspaMy.Mcp -c Release -o out/mcp
```

Requires the .NET 8 SDK.

## Files and configuration

| What | Default |
|---|---|
| Wallet file | `%APPDATA%\KaspaMy\wallet.<network>.json` (Windows), `~/.config/KaspaMy/...` (Linux/macOS; `Environment.SpecialFolder.ApplicationData`) |
| `KASPAMY_HOME` | Override the wallet directory. Next to each wallet: `wallet.<network>.json.lock` (lock file, safe to leave) and, for MCP, `mcp-spend.json` (daily spend ledger). |
| Network | `testnet-10` (use `--network mainnet` explicitly for mainnet) |
| Node | mainnet `wss://eva.kaspa.green/kaspa/mainnet/wrpc/json`; testnet-10 `wss://vector-10.kaspa.green/kaspa/testnet-10/wrpc/json` then `wss://muon-10.kaspa.blue/...`, then the public PNN resolver. Override with `--node` or `KASPAMY_NODE` (JSON wRPC, `ws://` / `wss://`). |
| `KASPAMY_PASSWORD` | Keystore password (else prompted). Ignored by `export-mnemonic`, which always asks you to type it. |
| `KASPAMY_MNEMONIC` | Phrase for `import` (else hidden prompt) |

Keystore: AES-256-GCM, key from Argon2id (64 MiB, 3 iterations, 4 lanes), network bound as associated data. Format compatible with KasNetWallet. Existing wallets are never overwritten.

## CLI

```bash
kaspamy create                    # testnet-10, 24 words; phrase is NOT shown
kaspamy export-mnemonic           # interactive: type I UNDERSTAND, then type the password (env var ignored)
kaspamy import                    # phrase via hidden prompt or KASPAMY_MNEMONIC
kaspamy address --json            # needs the password: the address is decrypted and verified
kaspamy status --json             # file header only: shows `unverifiedAddress` + a warning; never use it to receive funds
kaspamy balance --json
kaspamy fee --json                # node feerates + estimate for a typical transfer
kaspamy validate kaspatest:qq... --json
kaspamy send kaspatest:qq... 1.5           # summary, then type "yes"
kaspamy send kaspa:qq... 0.1 --network mainnet   # type "SEND MAINNET"; mainnet always needs --network mainnet
kaspamy send kaspatest:qq... 1.5 --yes --json    # non-interactive (scripts)
```

Concurrent `create`/`import` are safe: a lock file serializes them and the wallet file is published atomically without overwrite, so exactly one succeeds and the others get "already exists". Uppercase bech32 addresses (`KASPA:QQ...`) are accepted; mixed case is rejected. A modified wallet file is reported as "modified or damaged", distinct from "Wrong password." (files from older builds without a checksum give a combined message).

JSON envelope: `{"ok":true,"command":"address","data":{...}}` or `{"ok":false,"command":"...","error":"..."}`. Exit codes: 0 ok, 1 error, 2 cancelled, 64 usage.

## MCP server

Tools: `wallet_create` (never returns the phrase; tells you to run `kaspamy export-mnemonic`), `wallet_import`, `wallet_status`, `get_address`, `get_balance`, `estimate_fee`, `validate_address`, `send_kas` (needs `confirm: true`). Logs go to stderr only and are redacted.

| Env | Meaning |
|---|---|
| `KASPAMY_PASSWORD` | Required for create/import/send, and to show a (verified) address or balance. Only source of the password. |
| `KASPAMY_MCP_ALLOW_MAINNET=1` | Enable mainnet (off by default). |
| `KASPAMY_MCP_MAX_KAS_TESTNET` / `KASPAMY_MCP_MAX_KAS_MAINNET` | Per-transaction cap on amount **+ fee** (default 10 / 1 KAS). |
| `KASPAMY_MCP_DAILY_MAX_KAS_TESTNET` / `KASPAMY_MCP_DAILY_MAX_KAS_MAINNET` | Cumulative amount + fee per **UTC** calendar day, persisted in `mcp-spend.json` (0600) across restarts (default 50 / 2 KAS). A missing (after first use), empty, `null`, unreadable or future-dated ledger blocks all MCP sends until you delete `mcp-spend.json` and `mcp-spend.anchor` by hand. Limit values are plain non-negative decimals in KAS (`5`, `0.5`; no `1e3`, no `5,5`). **`0` forbids sending on that network.** A negative or otherwise invalid value makes `kaspamy-mcp` refuse to start (message on stderr, exit code 78); there is no fallback to the default. Values above 1,000,000 KAS are clamped with a warning. Unset or blank = default. |
| `KASPAMY_MCP_ALLOW_IMPORT=1` | Enable `wallet_import` (off by default: a phrase typed into an AI chat is exposed to the model, its provider and logs; use `kaspamy import` instead). |
| `KASPAMY_MCP_NODES` | Comma-separated extra node URLs the agent may pass as `node`. Allowed otherwise: the built-in public nodes and `KASPAMY_NODE`. Anything else is rejected. |
| `KASPAMY_VERBOSE=1` | Info logs on stderr. |

Claude Desktop (`claude_desktop_config.json`) or Cursor (`~/.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "kaspamy": {
      "command": "/path/to/out/mcp/kaspamy-mcp",
      "args": [],
      "env": {
        "KASPAMY_PASSWORD": "use-a-long-unique-password",
        "KASPAMY_MCP_MAX_KAS_TESTNET": "5"
      }
    }
  }
}
```

Or without publishing: `"command": "dotnet", "args": ["run", "--project", "/path/to/kaspamy/src/KaspaMy.Mcp"]`.

### Daily-limit ledger: known limitation

The daily ledger (`mcp-spend.json` + `mcp-spend.anchor` in `KASPAMY_HOME`) protects against a misbehaving agent, **not** against someone with write access to `KASPAMY_HOME`. Such a user can reset today's total by deleting both files (treated as first use) or by rewriting `Day` to a past date; the anchor is only created by the first real send, so an install that never sent has no anchor. Anyone with that access can normally also read `KASPAMY_PASSWORD`, so keep `KASPAMY_HOME` private (0700) and do not give the agent shell or file-write access to it.

## Docs

- [docs/DECISIONS.md](docs/DECISIONS.md) — design decisions
- [docs/NKASPA-REQUESTS.md](docs/NKASPA-REQUESTS.md) — features wanted upstream in NKaspa

## License

MIT © 2026 jun chen. Portions adapted from KasNetWallet (MIT, same author).
