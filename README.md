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
- Giving an AI agent a funded wallet is risky. The MCP server defaults to testnet, refuses mainnet unless `KASPAMY_MCP_ALLOW_MAINNET=1`, and caps each send (10 KAS testnet / 1 KAS mainnet by default).

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
| `KASPAMY_HOME` | Override the wallet directory |
| Network | `testnet-10` (use `--network mainnet` explicitly for mainnet) |
| Node | mainnet `wss://eva.kaspa.green/kaspa/mainnet/wrpc/json`; testnet-10 `wss://vector-10.kaspa.green/kaspa/testnet-10/wrpc/json` then `wss://muon-10.kaspa.blue/...`, then the public PNN resolver. Override with `--node` or `KASPAMY_NODE` (JSON wRPC, `ws://` / `wss://`). |
| `KASPAMY_PASSWORD` | Keystore password (else prompted) |
| `KASPAMY_MNEMONIC` | Phrase for `import` (else hidden prompt) |

Keystore: AES-256-GCM, key from Argon2id (64 MiB, 3 iterations, 4 lanes), network bound as associated data. Format compatible with KasNetWallet. Existing wallets are never overwritten.

## CLI

```bash
kaspamy create                    # testnet-10, 24 words; phrase is NOT shown
kaspamy export-mnemonic           # interactive, type I UNDERSTAND
kaspamy import                    # phrase via hidden prompt or KASPAMY_MNEMONIC
kaspamy address --json
kaspamy balance --json
kaspamy fee --json                # node feerates + estimate for a typical transfer
kaspamy validate kaspatest:qq... --json
kaspamy send kaspatest:qq... 1.5           # summary, then type "yes"
kaspamy send kaspa:qq... 0.1 --network mainnet   # type "SEND MAINNET"; mainnet always needs --network mainnet
kaspamy send kaspatest:qq... 1.5 --yes --json    # non-interactive (scripts)
```

JSON envelope: `{"ok":true,"command":"address","data":{...}}` or `{"ok":false,"command":"...","error":"..."}`. Exit codes: 0 ok, 1 error, 2 cancelled, 64 usage.

## MCP server

Tools: `wallet_create` (never returns the phrase; tells you to run `kaspamy export-mnemonic`), `wallet_import`, `wallet_status`, `get_address`, `get_balance`, `estimate_fee`, `validate_address`, `send_kas` (needs `confirm: true`). Logs go to stderr only and are redacted.

| Env | Meaning |
|---|---|
| `KASPAMY_PASSWORD` | Required for create/import/send. Only source of the password. |
| `KASPAMY_MCP_ALLOW_MAINNET=1` | Enable mainnet (off by default). |
| `KASPAMY_MCP_MAX_KAS_TESTNET` / `KASPAMY_MCP_MAX_KAS_MAINNET` | Per-transaction cap (default 10 / 1 KAS). |
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

## Docs

- [docs/DECISIONS.md](docs/DECISIONS.md) — design decisions
- [docs/NKASPA-REQUESTS.md](docs/NKASPA-REQUESTS.md) — features wanted upstream in NKaspa

## License

MIT © 2026 jun chen. Portions adapted from KasNetWallet (MIT, same author).
