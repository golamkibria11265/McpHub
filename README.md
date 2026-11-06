# McpHub

A production-oriented [Model Context Protocol](https://modelcontextprotocol.io) server in .NET 10 that lets Claude use
your systems through small, audited, read-only-by-default tools. One host, one plugin-style module per integration.
Today: **GitHub, SQL Server, PostgreSQL, MongoDB and Postman (read, plus switch-gated writes)**. Planned: SQL Server, Jira, LinkedIn, Facebook.

> Status: first scaffold. It was written without access to a .NET SDK or NuGet, so **run the first build yourself**
> (see "First run") and fix anything the compiler reports before deploying.

## Architecture

```
src/
  McpHub.Core/            Contracts only: IIntegrationModule, IToolExecutor, ICredentialProvider, ICallerContext, IAuditLog
  McpHub.Infrastructure/  Shared plumbing: resilient HttpClient, credential provider, audit logger
  McpHub.Host/            Composition root: Streamable HTTP transport, JWT auth, rate limiting, module loader
  Modules/
    McpHub.Modules.GitHub/
      Tools/              MCP adapters: describe the tool to the model, delegate (no logic)
      Services/           One class per use case
      Clients/            The only code that speaks HTTP to GitHub
      Models/             Compact outputs returned to the model (+ internal API shapes and mapper)
      Access/             Validation and allow-list policies
tests/McpHub.UnitTests/
deploy/                   systemd unit, Nginx site, env template (no Docker)
```

Dependency direction: `Tools -> Services -> Clients`. Modules depend on `Core` and `Infrastructure`, never on `Host`.
Every tool call passes through `IToolExecutor`, which enforces the caller's scope, writes the audit entry, and turns
failures into safe messages.

## Security model

- **Inbound (Claude -> McpHub):** OAuth 2.0 bearer tokens (JWT) validated against your identity provider; scopes such as
  `github.read` gate each tool. The server publishes OAuth protected-resource metadata so MCP clients can discover your
  identity provider. `Auth:Enabled=false` works only in `Development`; any other environment refuses to start.
- **Outbound (McpHub -> GitHub):** a credential obtained through `ICredentialProvider` (environment variable today, vault later).
  Claude never sees it. Use a fine-grained, read-only token limited to the repositories you need.
- **Allow-list:** `Modules:GitHub:AllowedOwners` is mandatory; the server never touches other owners even if the token could.
- **Input validation:** owners, repos, branches and paths are allow-listed by shape; search queries cannot add
  `repo:`/`org:`/`user:` qualifiers; files that usually hold secrets (`.env`, keys, state files) are blocked.
- **Prompt injection:** issue and PR text is untrusted. Tool descriptions say so and write tools are off by default: each module needs its `AllowWrite` (and `AllowDelete`) switch plus the `<module>.write` / `<module>.delete` scope. SQL and Mongo writes preview first (`confirm=true` applies), and GitHub refuses commits to protected branches.
- **Audit:** one structured log line per call (user, tool, outcome, duration). Arguments and results are never logged.
- **Modules are opt-in:** nothing is exposed unless `Modules:<Name>:Enabled=true`.

### Compliance notes (SSLWIRELESS / Bangladesh Bank / PCI-DSS)

- Everything a tool returns is sent to the model provider. Get written approval from compliance before enabling any module
  that can return customer or transaction data.
- **Never expose cardholder data** (PAN, CVV, tokens) through any module. For the future SQL Server module: read-only login,
  allow-listed views only, masked columns, row caps, query timeout.
- Commit author names and PR/issue authors are personal data; they are returned as normal repository metadata.
- Keep audit logs for the retention period your policy requires and ship them to a tamper-resistant store.
- Keep the server in a segment that can only reach the APIs it integrates with; internal databases need a separate hop.

## First run (local, no auth)

```bash
cd McpHub
dotnet restore                 # creates packages.lock.json files; commit them
dotnet test
# edit src/McpHub.Host/appsettings.Development.json: set Modules:GitHub:Credentials:Token and Modules:GitHub:AllowedOwners
# (this file is git-ignored; appsettings.Development.example.json is the committed template)
dotnet run --project src/McpHub.Host
```

Then **pin the SDK version**: `Directory.Packages.props` floats `ModelContextProtocol*` to the newest release for the first
restore. Replace `*-*` with the exact versions that were resolved, and re-run restore.

Try it with the MCP Inspector: `npx @modelcontextprotocol/inspector` and connect to `http://127.0.0.1:5080/mcp`
(Streamable HTTP). In Development, authentication is off.

## Deploy on Linux (systemd + Nginx, no Docker)

```bash
# on the build machine
dotnet publish src/McpHub.Host -c Release -o publish

# on the server
sudo useradd --system --no-create-home --shell /usr/sbin/nologin mcphub
sudo mkdir -p /opt/mcphub /etc/mcphub
sudo rsync -a --exclude appsettings.Production.json publish/ /opt/mcphub/   # never overwrite the server's config
# first time only: create the production config from the template and fill in the placeholders
sudo cp src/McpHub.Host/appsettings.Production.example.json /opt/mcphub/appsettings.Production.json
sudo chown mcphub:mcphub /opt/mcphub/appsettings.Production.json && sudo chmod 600 /opt/mcphub/appsettings.Production.json
sudo cp deploy/mcphub.service /etc/systemd/system/
sudo systemctl daemon-reload && sudo systemctl enable --now mcphub
sudo journalctl -u mcphub -f
```

Requires the .NET 10 ASP.NET Core runtime on the server. Install `deploy/nginx-mcphub.conf` as an Nginx site and issue a TLS
certificate for your domain. Health checks (local only): `curl http://127.0.0.1:5080/health/ready`.

Deploying a new version: publish, rsync, `sudo systemctl restart mcphub` (stateless server, so restarts are safe).

## Connect Claude

- **Claude Code:** `claude mcp add --transport http mcphub https://mcp.[YOUR_DOMAIN]/mcp`
- **Claude apps (custom connector):** add `https://mcp.[YOUR_DOMAIN]/mcp` as a custom connector. Your identity provider must
  support the OAuth flow the connector uses (check Anthropic's current connector documentation for requirements such as
  dynamic client registration), and the server must be reachable from the internet.

## Adding a new integration (about 10 minutes)

1. Create `src/Modules/McpHub.Modules.<Name>/` (copy the GitHub module's shape). The folder name must start with `McpHub.Modules.`.
2. Implement `IIntegrationModule`: register options, an HTTP client via `AddIntegrationHttpClient`, and your services.
3. Write a service per use case, a client per external system, and tool classes marked `[McpServerToolType]` that only call
   `IToolExecutor.ExecuteAsync(toolName, scope, ...)`.
4. Add `Modules:<Name>` to configuration (`Enabled: false` by default) and its credential as `Modules:<Name>:Credentials:Token` (inside the module's own section).
5. Add unit tests with a fake client. **No change to the host is needed**; the project reference glob and module loader pick it up.

Different auth style (OAuth refresh tokens for LinkedIn/Facebook, a connection string for SQL Server)? Implement or extend
`ICredentialProvider`; the module code stays the same.

## Roadmap

GitHub write tools (own `github.write` scope, explicit confirmation) -> Jira -> SQL Server (after compliance review) ->
LinkedIn / Facebook. Also: OpenTelemetry, per-user delegated credentials, vault-backed `ICredentialProvider`.
