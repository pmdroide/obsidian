# Anvil Editor MCP Server

Anvil exposes twelve tools through a local Streamable HTTP MCP server. The server uses the official C# SDK, pinned to `ModelContextProtocol.AspNetCore 2.2.0`, and operates on the running editor's engine through `IEditorBridge`. It does not require an LLM API key.

## Enable and connect

1. Build/run Anvil, then open **Window > MCP** (also available from the workspace tab + menu).
2. Check **Enable MCP**, choose a port if the default is occupied, and press **Apply**.
3. Configure your MCP client for **Streamable HTTP** using the displayed endpoint, normally `http://127.0.0.1:47831/mcp`.
4. Press **Copy token** and add the client HTTP header `Authorization: Bearer <token>`.
5. Keep **Read-only** enabled for inspection. Clear it to allow scene commands, saving and Play/Stop. This policy changes immediately; Apply persists the setting.
6. Discover tools and call `editor_get_status`. Wait for `ready: true`, then use its `sceneGeneration` for writes.

Enable/port preferences take effect when you press Apply. Unchecking Enable revokes automation immediately; Apply closes the listener and saves that preference. Closing the MCP tab leaves the server running; closing Anvil cancels queued commands and shuts down the host. Enabled settings restart the listener when a subsequent editor session attaches its bridge.

### Status logging

The **Console** records MCP listener startup/shutdown, startup failures and the first authenticated client request after each startup. Search for `MCP` or `Anvil:MCP`. These messages also go to the engine's `anvil-bridge.log` file.

Press **Log Status** in the MCP panel to log the current state, for example:

```text
MCP: listener=running; automation=active; bridge=attached; access=read-only; endpoint=http://127.0.0.1:47831/mcp
```

The listener and automation states are reported separately: unchecking Enable immediately disables automation while the listener stays open until Apply. `bridge=attached` confirms that the engine bridge is connected; a successful `editor_get_status` call confirms that the engine is responding. The client activity message confirms an authenticated request reached the listener and is emitted once per startup. It does not imply a client remains connected. Tokens, authorization headers and request arguments are omitted from these logs.

Code that owns an `EditorMcpHost` can call `LogStatus()` for the same report.

A client configuration may look like this; use your client's documented field names:

```json
{
  "mcpServers": {
    "anvil": {
      "type": "http",
      "url": "http://127.0.0.1:47831/mcp",
      "headers": {
        "Authorization": "Bearer <paste-token-here>"
      }
    }
  }
}
```

The endpoint is accessible on this computer. The client must support local Streamable HTTP connections and custom authorization headers. Hosted clients that cannot reach loopback need a different integration; a stdio adapter is not included.

Settings and the token are stored in `%LOCALAPPDATA%/Anvil/Mcp/settings.json`, in a directory restricted to the current Windows user. They are outside project Content and standalone game output. **Rotate token** restarts access with a new token; update the client's header afterwards. The token is masked in the panel and omitted from logs.

## Install with the harness installer

Run the installer from the repository root to choose **Claude Code, Cursor, Codex, VS Code**, or several at once:

```powershell
.\Tools\Scripts\install-mcp.ps1
```

The menu accepts names or numbers separated by commas, asks for user/project scope, and previews the affected files before writing. First enable Anvil MCP and press Apply so its saved endpoint/token exist. The installer registers that server; Anvil must remain running when clients use its tools.

The source launcher requires the .NET 10 SDK and builds only the small installer project. To run without the SDK, publish the standalone Windows x64 executable:

```powershell
.\Tools\Scripts\build-mcp-installer.ps1
.\Builds\McpInstaller\anvil-mcp.exe
```

The executable can be copied outside the checkout. It has its own .NET runtime and no engine/content dependency.

### Scripted installation

```powershell
# Preview selected harnesses; no files, folders or backups are created.
.\Tools\Scripts\install-mcp.ps1 --harness claudecode,cursor,codex --dry-run

# Install one harness without the final prompt.
.\Tools\Scripts\install-mcp.ps1 --harness codex --yes

# Install every supported harness.
.\Tools\Scripts\install-mcp.ps1 --harness all --yes

# Refresh an existing entry after changing the port or rotating the token.
.\Tools\Scripts\install-mcp.ps1 --harness all --replace --yes

# Show options or target paths.
.\Tools\Scripts\install-mcp.ps1 --help
.\Tools\Scripts\install-mcp.ps1 --list
```

The same flags work on `anvil-mcp.exe`, or directly with `dotnet run --project Tools/McpInstaller/McpInstaller.csproj -- <flags>`. `claude` and `claude-code` are aliases for `claudecode`; harness flags can be repeated. In a script or redirected terminal, supply the harnesses and `--yes`, or use `--dry-run`.

### Scope and credentials

User scope is the default. It stores the current Anvil token in private client settings so desktop clients work without shell environment setup. Project scope stores a reference to `ANVIL_MCP_TOKEN` instead of copying the live token into repository files.

| Harness | User target | Project target |
|---|---|---|
| Claude Code | `%USERPROFILE%/.claude.json`, under `mcpServers` | `.mcp.json` |
| Cursor | `%USERPROFILE%/.cursor/mcp.json` | `.cursor/mcp.json` |
| Codex | `%USERPROFILE%/.codex/config.toml`, or `$env:CODEX_HOME/config.toml` | `.codex/config.toml` |
| VS Code | `%APPDATA%/Code/User/mcp.json` | `.vscode/mcp.json` |

These destinations/formats follow [Claude Code's scopes](https://code.claude.com/docs/en/mcp), [Cursor's MCP configuration](https://cursor.com/docs/mcp), [OpenAI's Codex MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), and [VS Code's MCP configuration](https://code.visualstudio.com/docs/agent-customization/mcp-servers).

```powershell
# Share server definitions with this project, keeping the token outside them.
.\Tools\Scripts\install-mcp.ps1 --harness all --scope project --project . --yes

# Use an environment-variable reference even in private user settings.
.\Tools\Scripts\install-mcp.ps1 --harness cursor --token-env ANVIL_MCP_TOKEN --yes

# Target a custom client profile, portable installation or VS Code Insiders.
.\Tools\Scripts\install-mcp.ps1 --harness cursor --config 'C:\MyProfile\mcp.json' --yes

# Read another Anvil settings file, if needed.
.\Tools\Scripts\install-mcp.ps1 --harness codex --settings 'C:\MyAnvil\settings.json' --yes
```

For environment-based entries, run the **Prepare the connection** block below before launching the client. Each harness receives its own variable syntax. Installing an entry does not set persistent Windows environment variables. After rotation, reload the environment variable; static user entries need a `--replace` refresh. If the endpoint changes, reinstall with `--replace` in either mode.

`--config` requires exactly one explicit harness and overrides its destination. Use it for custom profiles; a customized `CLAUDE_CONFIG_DIR` requires an explicit Claude JSON path. `--url http://127.0.0.1:PORT/mcp` overrides the saved endpoint. Combining `--url` and `--token-env` permits configuration before the Anvil settings file exists. `--name` chooses a different server entry name, default `anvil`.

### Preserving existing configuration

All selected files are parsed and planned before the first write. Identical entries are left unchanged. A conflicting named server requires `--replace` or another `--name`; replacement affects that server and its authentication/options, preserving unrelated servers and settings. JSON edits preserve unrelated bytes, comments and trailing commas. TOML edits preserve unrelated syntax, including multiline strings and comments. Invalid files, duplicate JSON keys and unsupported compact TOML layouts are refused without rewriting them. Convert compact Codex entries to normal `[mcp_servers.NAME]` tables if needed.

Each changed existing file receives a timestamped `*.anvil-mcp.*.bak` containing its original bytes. New files/backups are restricted to the current Windows user; existing configuration permissions are preserved. Writes are atomic per file and reject changes detected since the preview. If an IO error interrupts a multi-harness installation, earlier completed files remain installed; their printed backups allow restoration. Symbolic-link files require `--config` pointing to the real file so the installer does not replace the link. Tokens are omitted from console previews and error output.

Restart the selected client, inspect its MCP server list, and ask it to call `editor_get_status` through `anvil`. Registration does not install the harness application or change Anvil's Read-only setting.

## Use with Claude Code and Codex manually

These examples use **PowerShell on the same Windows computer as Anvil**. Start Anvil and press Apply in its MCP panel first. Keep the editor running while using either client. This server authenticates with Anvil's token; no MCP OAuth login is required.

### Prepare the connection

In the terminal you will use to launch the client, load the saved endpoint and token without printing the token:

```powershell
Set-Location C:\Dev\GitHub\obsidian
$anvilMcpSettingsPath = Join-Path $env:LOCALAPPDATA 'Anvil\Mcp\settings.json'
$anvilMcpSettings = Get-Content -LiteralPath $anvilMcpSettingsPath -Raw | ConvertFrom-Json
$anvilMcpUrl = "http://127.0.0.1:$($anvilMcpSettings.Port)/mcp"
$env:ANVIL_MCP_TOKEN = $anvilMcpSettings.Token
```

The variable contains the raw token, without the `Bearer ` prefix. These environment settings last for this PowerShell session and its child processes; repeat this block in a new terminal or after rotating the token.

### Claude Code

Register Anvil for this checkout, then start Claude Code from the same terminal:

```powershell
claude mcp add --transport http --scope local anvil $anvilMcpUrl --header 'Authorization: Bearer ${ANVIL_MCP_TOKEN}'
claude mcp list
claude
```

Keep the single quotes around the header: PowerShell passes the `${ANVIL_MCP_TOKEN}` reference literally so Claude Code can expand it from the environment. `--scope local` stores the entry privately for this project in `~/.claude.json`. See [Claude Code MCP setup and scopes](https://code.claude.com/docs/en/mcp).

In Claude Code, open `/mcp`, check that `anvil` connects, and allow the requested tools when prompted. For a first read:

> Use the anvil MCP to call editor_get_status and list the objects in the current scene. Report the scene name and mode.

For an existing `anvil` entry, remove it before running the registration command again:

```powershell
claude mcp remove --scope local anvil
```

If your team prefers a project `.mcp.json`, merge this entry with any existing servers instead of using local registration:

```json
{
  "mcpServers": {
    "anvil": {
      "type": "http",
      "url": "http://127.0.0.1:47831/mcp",
      "headers": {
        "Authorization": "Bearer ${ANVIL_MCP_TOKEN}"
      }
    }
  }
}
```

Use the panel's actual port, set the environment variable before launching Claude Code, and accept project-server approval if requested. This configuration stores a variable reference; keep the live token outside version control. See [Claude Code environment expansion](https://code.claude.com/docs/en/mcp#environment-variable-expansion-in-mcpjson).

### Codex CLI

After the preparation block, register Anvil and start Codex in that terminal:

```powershell
codex mcp add anvil --url $anvilMcpUrl --bearer-token-env-var ANVIL_MCP_TOKEN
codex mcp list
codex
```

Open `/mcp` in Codex to inspect active servers, then use the same read prompt above. `codex mcp list` checks registration; a successful status tool call checks the live editor connection. The HTTP/bearer options were checked against installed Codex CLI 0.160.0. See the [official OpenAI MCP documentation](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

For manual configuration, merge the equivalent table into the default user file `%USERPROFILE%\.codex\config.toml`:

```toml
[mcp_servers.anvil]
url = "http://127.0.0.1:47831/mcp"
bearer_token_env_var = "ANVIL_MCP_TOKEN"
```

Use the panel's actual port. If `CODEX_HOME` is customized, use its `config.toml`. To replace the CLI registration after a port change:

```powershell
codex mcp remove anvil
codex mcp add anvil --url $anvilMcpUrl --bearer-token-env-var ANVIL_MCP_TOKEN
```

### Codex desktop app or IDE extension

Codex clients on the same host share MCP configuration. Restart the client after editing it, and make sure its process receives `ANVIL_MCP_TOKEN`. A desktop app opened from the Start menu does not inherit a variable set only in a separate PowerShell window. See [OpenAI's client configuration guidance](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

For a client without that environment variable, an alternative is this table in your **private user** `config.toml`, replacing the token placeholder using **Copy token** in Anvil:

```toml
[mcp_servers.anvil]
url = "http://127.0.0.1:47831/mcp"
http_headers = { Authorization = "Bearer <paste-token-here>" }
```

Replace the previous table rather than adding a duplicate, and use either `bearer_token_env_var` or the static Authorization header. Keep the private file out of the repository. Static headers are documented in the [OpenAI configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference).

### Editing and troubleshooting

Once inspection works, clear **Read-only** in Anvil and request an edit:

> Use the anvil MCP. Get the current scene generation and available model keys. Create a Cube named MCP Test at (0, 0, 2), then report its ID and final transform. Leave the scene unsaved.

To persist it, request `scene_save` separately, with an allowed path such as `Scenes/McpExample.obsc`. The assistant should use fresh request IDs for new edits and reuse an ID only for an identical retry.

| Symptom | What to check |
|---|---|
| Connection refused or server unavailable | Anvil is running, Enable MCP is checked, Apply completed, and the client URL matches the panel. |
| HTTP 401 | Reload the preparation block after token rotation and restart the client. For a static header, paste the new token. |
| Configured but tools missing | Start a new client session and inspect `/mcp`; check any client/admin tool restrictions. |
| `ready: false` | Wait for engine initialization, then call `editor_get_status` again. |
| `READ_ONLY` or `WRONG_MODE` | Clear Read-only in Anvil and stop Play before scene edits or saves. |
| `SCENE_CHANGED` | Read status again and use its new scene generation. |
| `OPERATION_PENDING` | Query `editor_get_operation` with the original request ID before retrying. |
| Client runs in WSL, a container or a remote session | Its loopback may refer to a different environment. Run the client on the Windows host for this setup. |

The command syntax was checked against installed Claude Code 2.1.220 and Codex CLI 0.160.0, plus the official documentation linked above. This section documents configuration; it does not register either client or change your local client settings.

## Tools

| Tool | Arguments / behavior |
|---|---|
| `editor_get_status` | Fresh status: readiness, editor instance ID, read-only policy, scene generation, revision, timestamp, scene name/path, dirty state, mode and selection. |
| `scene_list_objects` | Optional `contains`, `kind`, `offset` (default 0) and `limit` (default 50, maximum 100). Returns total and next offset with transforms, components and bones. |
| `scene_get_object` | `objectId`. Returns one object's copied details; bones are nested under their owning gameobject. |
| `assets_list_models` | Optional name filter and pagination, at most 100 keys per page. Use these exact keys for creation. |
| `components_list_types` | Registered component IDs, display names, repeatability and main-camera support. |
| `editor_get_operation` | `requestId`. Returns pending, completed, cancelled or failed, with the completed result when retained. |
| `scene_select_object` | `requestId`, `sceneGeneration`, optional `objectId`. Omit the object ID to clear selection. |
| `scene_set_transform` | `requestId`, `sceneGeneration`, `objectId`, optional `position`, `rotation`, `scale`. At least one transform field is required. |
| `scene_create_object` | `requestId`, `sceneGeneration`, `modelKey`; optional `name`, `position`, `rotation`, `scale`. Returns the created ID. |
| `scene_add_component` | `requestId`, `sceneGeneration`, `objectId`, `componentType`. Returns the attachment instance ID. |
| `scene_save` | `requestId`, `sceneGeneration`, optional `path`. Saves the current file, or Save As to a source `Scenes/*.obsc` path. |
| `editor_set_play_mode` | `requestId`, `sceneGeneration`, `mode` set to `play` or `stop`. Waits for the requested transition. |

All commands require writes enabled. Scene edits, selection and saves require **Edit mode**. Play runs scripts; Stop restores the edited scene and discards runtime changes. Transform/object/component changes mark the scene dirty and do not save automatically.

Transforms use world-space position in engine units with **Z up**. Vectors are `{ "x": 0, "y": 0, "z": 2 }`, rotations are quaternions `{ "x": 0, "y": 0, "z": 0, "w": 1 }`. Non-zero quaternions are normalized; non-finite values are rejected. Position/scale magnitudes are bounded to 1,000,000, and scale factors must have absolute magnitude at least 0.00001.

Gameobjects and decals accept position, rotation and scale. Lights accept position and rotation; bones accept **bone-local rotation offsets** only. The main camera (`-1`) supports inspection and Script Behaviour attachment, but has no selection/transform proxy for these tools. Negative bone IDs are valid when they exist in the current scene.

Save paths are resolved against the source `Engine/Content` root rather than Anvil's engine-bin working directory. Paths must remain inside its `Scenes` folder with an `.obsc` extension. Junction/symlink paths, including lighting sidecars, are rejected. Existing scene serialization handles `.probes` and `.reflections`. A scene opened outside the source Scenes folder needs an explicit allowed Save As path.

## Example workflow

Read status, list model keys, then call `scene_create_object` with these tool arguments:

```json
{
  "requestId": "create-crate-001",
  "sceneGeneration": "<from-editor_get_status>",
  "modelKey": "Cube",
  "name": "AI Crate",
  "position": { "x": 0, "y": 0, "z": 2 }
}
```

Use the returned object ID in `scene_set_transform`:

```json
{
  "requestId": "move-crate-001",
  "sceneGeneration": "<current-generation>",
  "objectId": 42,
  "position": { "x": 3, "y": 0, "z": 2 },
  "scale": { "x": 2, "y": 2, "z": 2 }
}
```

Then save explicitly:

```json
{
  "requestId": "save-scene-001",
  "sceneGeneration": "<current-generation>",
  "path": "Scenes/McpExample.obsc"
}
```

These are tool arguments; the MCP SDK supplies the JSON-RPC transport envelope.

## Completion, retries and limits

A successful write confirms that the command executed on the game thread and returns copied final state. It does not wait for the editor UI's throttled snapshots, which still publish every six frames.

Use a unique `requestId` for each intended command. An identical retry returns the retained outcome without executing again. Reusing an ID with different arguments returns `REQUEST_ID_CONFLICT`. Up to 256 commands are retained for the current editor instance, evicting completed entries first. This is a bounded retry window, not persistent deduplication; after an editor restart or eviction, inspect the scene before issuing another write.

Every write includes the `sceneGeneration` from a recent status result. Replacing the active scene changes this token. Queued commands recheck it at execution and fail with `SCENE_CHANGED` rather than modifying a different scene. Revisions identify completed captures/commands, not an exhaustive change history for every frame.

The engine queue holds at most 64 automation operations and processes at most four per frame. The HTTP host limits active requests to 16 and connections to 32, accepts request bodies up to 256 KiB, and replaces tool results over 4 MiB with `RESULT_TOO_LARGE`. Use filters/smaller pages for reads. If a large write result is rejected, inspect status or `editor_get_operation` before retrying.

Commands have a ten-second wait limit. Cancellation before execution prevents the edit. Once an operation starts, cancellation cannot roll back scene changes or file writes. A caller can receive `OPERATION_PENDING` while the command continues; query `editor_get_operation` with its request ID to recover the outcome. Disabling/closing MCP cancels queued work immediately. A command already executing finishes before engine disposal.

Application failures use MCP tool errors with codes such as `EDITOR_NOT_READY`, `READ_ONLY`, `WRONG_MODE`, `SCENE_CHANGED`, `OBJECT_NOT_FOUND`, `INVALID_ARGUMENT`, `QUEUE_FULL`, `COMPONENT_EXISTS` and `SAVE_FAILED`.

## Code wiring

```text
MCP client -> loopback HTTP /mcp
           -> EditorReadTools / EditorWriteTools
           -> EditorAutomationService
           -> IEditorBridge.ExecuteAutomationAsync
           -> bounded game-thread queue in EditorAutomation.cs
           -> copied result -> MCP response
```

| Code | Responsibility |
|---|---|
| `Editor/Anvil/Mcp/EditorMcpHost.cs` | Asynchronous embedded ASP.NET Core host, SDK registration, loopback binding, authorization, Host/Origin validation and request/result limits. |
| `Editor/Anvil/Mcp/EditorAutomationService.cs` | Bridge attachment, access policy, bounded retry/result retention, timeout handling and recent command outcomes. |
| `Editor/Anvil/Mcp/EditorMcpContracts.cs` | JSON DTOs and copies of component/bone data. |
| `Editor/Anvil/Mcp/Tools/*.cs` | Explicitly registered tools, schemas, descriptions and side-effect annotations. |
| `Editor/Anvil/Mcp/EditorMcpSettings.cs` | User settings and token persistence with Windows directory permissions. |
| `Editor/Anvil/Models/McpSettingsViewModel.cs` | Application-owned connection controls and MCP workspace panel state. |
| `Engine/Editor/EditorAutomation.cs` | Typed engine commands, validation, completion, queue budget, cancellation and scene generation. No MCP dependency. |
| `Engine/Editor/EditorBridge.cs` | Shared creation helper, scene-swap generation changes and queue draining before normal snapshot publication. |
| `Editor/Anvil/Controls/MonoGameHost.cs` | Cancels automation before engine teardown. |
| `Tests/Components/McpChecks.cs` | Engine, service and real HTTP/SDK integration checks. |

`MainWindowViewModel.AttachBridge` attaches the automation service when `MainWindow` receives `MonoGameHost.BridgeReady`. Engine readiness starts at `EditorBridge.Bind`, so bridge construction alone cannot admit commands. Closing the window revokes access before viewport disposal. HTTP hosting uses `StartAsync`/`StopAsync` alongside Avalonia and never takes over its main loop.

The SDK and `Microsoft.AspNetCore.App` framework reference live in Anvil. Standalone Engine does not reference the SDK or start a listener. Server code rejects any browser Origin header, checks loopback Host values and exact endpoint path, and authenticates every request with a constant-time token-hash comparison. CORS is not enabled.

## Validation and remaining scope

Run from the repository root:

```powershell
dotnet build Editor/Anvil/Anvil.csproj
dotnet run --project Tests/Components/Components.csproj -- --mcp
dotnet run --project Tests/Components/Components.csproj
dotnet run --project Editor/Anvil/Anvil.csproj
```

The focused checks exercise real C# SDK 2.2.0 discovery/calls over loopback HTTP, game-thread completion, retries/conflicts, stale IDs, invalid transforms, queue limits, cancellation, read-only policy changes, timeout recovery, save/reload, save failures, Play restrictions, shutdown, and rejected credentials/Host/Origin headers. They use a temporary scene/settings folder and do not require a graphics device.

Viewport capture, scene load/new/delete tools, generic component property edits, materials/environment tools, imports/builds/bakes, stdio adapters, remote hosting and command-history undo remain future work. Existing Undo/Redo menu entries are not wired; MCP edits are not advertised as undoable. Use explicit typed tools rather than arbitrary code execution.

For background, see [Editor Architecture](Editor_Architecture.md) and [Engine Wiring](Engine_Wiring.md). SDK references: [official package guide](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/getting-started.md), [transport guide](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md), [MCP tools specification](https://modelcontextprotocol.io/specification/2026-07-28/server/tools), and [HTTP transport/security](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http).
