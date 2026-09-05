OnlineVotingApplication
=======================

Review summary
--------------
This repository is a Razor Pages voting application targeting .NET 9 with ASP.NET Core Identity scaffolding. Structure and intent are clear. Missing or improvable areas:
- Clear configuration guidance for `appsettings.json` keys and secrets.
- Explicit dependency list (NuGet + client-side).
- Short architectural overview and where to extend business logic.
- Note about cross-origin stylesheet access (Browser Link / cssRules) is necessary for front-end debugging.

Tech stack (high level)
-----------------------
| Layer / Area            | Technologies / Packages                                    | Notes |
|-------------------------|------------------------------------------------------------|-------|
| Runtime & Framework     | .NET 9, ASP.NET Core Razor Pages                           | Target framework in project files |
| Authentication          | ASP.NET Core Identity                                      | Scaffolded Identity pages present |
| Data Access             | EF Core (e.g., `Microsoft.EntityFrameworkCore.SqlServer`)  | Migrations expected |
| Dev tooling             | Visual Studio 2022, dotnet CLI, EF Tools                   | Use CLI or VS tooling for migrations |
| Front-end               | Razor Pages, static files in `wwwroot`, optional CDNs      | Example: Font Awesome via CDN |
| Browser integration     | Browser Link (development convenience)                     | May cause cross-origin CSS access issues |

Architecture overview
---------------------
- Presentation: Razor Pages (`/Pages`) handle UI, page models implement page logic.
- Authentication/Authorization: Identity UI (scaffolded) and `services.AddDefaultIdentity` in `Program.cs`.
- Data layer: EF Core DbContext (stores votes, users, roles). Use repository or service pattern for business logic if needed.
- Services: Business services registered via DI (`services.AddScoped` / `AddTransient`) invoked by PageModels.
- Static assets: `wwwroot` contains local CSS/JS; external CDNs referenced for convenience.
- Dev-time tools: Browser Link injects scripts into served pages for live updates (optional).

Getting started (local dev)
---------------------------
Prerequisites:
- .NET 9 SDK
- Visual Studio 2022 (latest updates) OR `dotnet` CLI
- SQL Server / LocalDB or other supported DB

Steps:
1. Clone:
   git clone <repo-url>
   cd OnlineVotingApplication

2. Configure:
   - Edit `appsettings.json` (see Configuration section below).
   - If using secrets: `dotnet user-secrets init` and `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<conn>"`

3. Restore & apply migrations:
   dotnet restore
   dotnet ef database update

4. Run:
   - Visual Studio: F5 (IIS Express) or Ctrl+F5
   - CLI: dotnet run --project OnlineVotingApplication

Configuration (what to check / set)
-----------------------------------
- `ConnectionStrings:DefaultConnection` — DB connection string for EF Core.
- `Logging` — minimum level and sinks.
- Identity options (if customized) — lockout, password settings in `Program.cs` or `appsettings.json`.
- CORS (if APIs or cross-origin requests) — ensure `services.AddCors` and policies applied.
- `ASPNETCORE_ENVIRONMENT` — use `Development` locally to enable developer tools.
- Secrets: store production connection strings, keys and sensitive values out of repo (environment or user-secrets).

Common runtime issues and notes
------------------------------
- SecurityError: "Cannot access rules" — caused when a script tries to read `CSSStyleSheet.cssRules` for a stylesheet loaded from another origin (CDN). Browsers block access for cross-origin stylesheets. Fixes:
  - Host the stylesheet locally in `wwwroot`.
  - Use a CDN that provides CORS header `Access-Control-Allow-Origin: *` and ensure `crossorigin` attribute is set on the `<link>`.
  - Disable/avoid Browser Link or guard code that iterates `cssRules` to skip stylesheets where `sheet.cssRules` throws.
- DB migrations: ensure EF tools are run using the same project that contains `DbContext`.
- Identity pages: if scaffolded and customized, ensure routes and DI registrations in `Program.cs` were not removed.

Dependencies (what to verify / add)
----------------------------------
- NuGet (server-side):
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore.SqlServer` (or chosen provider)
  - `Microsoft.EntityFrameworkCore.Tools` (development)
  - `Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore` (optional)
- Client-side / CDN:
  - Font Awesome (consider local hosting to avoid cross-origin access)
  - Any JS libraries used (ensure versions declared in `libman`, `package.json`, or included in `wwwroot`)
- Dev tools:
  - `dotnet-ef` CLI tool (for migrations)
