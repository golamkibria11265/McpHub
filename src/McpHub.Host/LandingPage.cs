using System.Net;

namespace McpHub.Host;

/// <summary>Development-only landing page served at "/". Self-contained: no external assets.</summary>
internal static class LandingPage
{
    public static string Render(string environment, IEnumerable<string> modules)
    {
        var list = modules.ToList();
        var cards = list.Count == 0
            ? """<p class="empty">No modules enabled. Set <code>Modules:&lt;Name&gt;:Enabled=true</code> in appsettings.</p>"""
            : string.Concat(list.Select(m => $"""
                <div class="module"><span class="check">&#10003;</span><div><strong>{WebUtility.HtmlEncode(m)}</strong><small>Module active</small></div></div>
                """));

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>McpHub</title>
              <style>
                :root {
                  --bg: #070b18; --panel: rgba(255,255,255,.045); --line: rgba(255,255,255,.09);
                  --text: #e8ecf6; --muted: #8b95b0; --a: #7c8cff; --b: #22d3ee; --c: #c084fc; --ok: #34d399; --bad: #f87171;
                }
                * { box-sizing: border-box; }
                html, body { height: 100%; }
                body {
                  margin: 0; color: var(--text); background: var(--bg); overflow-x: hidden;
                  font-family: Inter, ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
                }
                body::before {
                  content: ""; position: fixed; inset: 0; z-index: 0; pointer-events: none;
                  background-image: linear-gradient(var(--line) 1px, transparent 1px), linear-gradient(90deg, var(--line) 1px, transparent 1px);
                  background-size: 48px 48px;
                  -webkit-mask-image: radial-gradient(ellipse at 50% 0%, #000 10%, transparent 70%);
                          mask-image: radial-gradient(ellipse at 50% 0%, #000 10%, transparent 70%);
                  opacity: .55;
                }
                .orb { position: fixed; border-radius: 50%; filter: blur(90px); opacity: .35; z-index: 0; pointer-events: none; }
                .orb.o1 { width: 420px; height: 420px; background: var(--a); top: -140px; left: -100px; animation: float 14s ease-in-out infinite; }
                .orb.o2 { width: 380px; height: 380px; background: var(--b); top: 120px; right: -120px; animation: float 17s ease-in-out infinite reverse; }
                .orb.o3 { width: 320px; height: 320px; background: var(--c); bottom: -120px; left: 35%; opacity: .22; animation: float 20s ease-in-out infinite; }
                @keyframes float { 50% { transform: translate(30px, 40px); } }

                .wrap { position: relative; z-index: 1; max-width: 980px; margin: 0 auto; padding: 72px 24px 40px; }

                .hero { text-align: center; }
                .logo {
                  width: 72px; height: 72px; margin: 0 auto 22px; border-radius: 20px; display: grid; place-items: center;
                  font-size: 34px; font-weight: 800; color: #fff;
                  background: linear-gradient(135deg, var(--a), var(--b));
                  box-shadow: 0 0 0 1px rgba(255,255,255,.2) inset, 0 18px 50px rgba(124,140,255,.45);
                }
                h1 {
                  margin: 0; font-size: clamp(40px, 7vw, 68px); font-weight: 800; letter-spacing: -.035em; line-height: 1.05;
                  background: linear-gradient(120deg, #fff 20%, var(--a) 55%, var(--b));
                  -webkit-background-clip: text; background-clip: text; color: transparent;
                }
                .sub { margin: 14px auto 0; max-width: 560px; color: var(--muted); font-size: 18px; line-height: 1.55; }

                .connect {
                  margin: 34px auto 0; max-width: 640px; display: flex; align-items: center; gap: 10px;
                  padding: 8px 8px 8px 18px; border-radius: 14px; background: var(--panel); border: 1px solid var(--line);
                  backdrop-filter: blur(10px);
                }
                .connect .lbl { font-size: 11px; font-weight: 700; letter-spacing: .1em; color: var(--muted); }
                .connect code { flex: 1; text-align: left; font-family: ui-monospace, "Cascadia Code", Consolas, monospace; font-size: 15px; color: var(--b); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
                button {
                  cursor: pointer; border: 0; border-radius: 10px; padding: 10px 18px; font-weight: 600; font-size: 14px; color: #0b1020;
                  background: linear-gradient(135deg, var(--a), var(--b)); transition: transform .12s, filter .12s;
                }
                button:hover { transform: translateY(-1px); filter: brightness(1.1); }

                .stats { display: grid; gap: 14px; grid-template-columns: repeat(3, 1fr); margin-top: 44px; }
                .stat { padding: 20px 22px; border-radius: 16px; background: var(--panel); border: 1px solid var(--line); backdrop-filter: blur(10px); }
                .stat .k { font-size: 12px; font-weight: 600; letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }
                .stat .v { margin-top: 10px; font-size: 26px; font-weight: 700; display: flex; align-items: center; gap: 10px; }
                .dot { width: 11px; height: 11px; border-radius: 50%; background: var(--muted); }
                .dot.ok { background: var(--ok); animation: pulse 2s infinite; }
                .dot.bad { background: var(--bad); }
                @keyframes pulse { 0% { box-shadow: 0 0 0 0 rgba(52,211,153,.6); } 70%,100% { box-shadow: 0 0 0 10px rgba(52,211,153,0); } }

                h2 { margin: 48px 0 16px; font-size: 13px; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; color: var(--muted); }

                .endpoints { border-radius: 16px; background: var(--panel); border: 1px solid var(--line); overflow: hidden; backdrop-filter: blur(10px); }
                .row { display: flex; align-items: center; gap: 16px; padding: 16px 20px; text-decoration: none; color: inherit; border-top: 1px solid var(--line); transition: background .15s; }
                .row:first-child { border-top: 0; }
                a.row:hover { background: rgba(255,255,255,.05); }
                .tag { min-width: 52px; text-align: center; font-size: 11px; font-weight: 800; padding: 4px 8px; border-radius: 7px; letter-spacing: .04em; }
                .tag.get { background: rgba(52,211,153,.14); color: var(--ok); }
                .tag.mcp { background: rgba(192,132,252,.16); color: var(--c); }
                .row code { font-family: ui-monospace, "Cascadia Code", Consolas, monospace; font-size: 15px; font-weight: 600; min-width: 150px; }
                .row span.d { color: var(--muted); font-size: 14px; flex: 1; }
                .row .go { color: var(--a); font-size: 13px; font-weight: 600; }

                .modules { display: grid; gap: 14px; grid-template-columns: repeat(auto-fill, minmax(230px, 1fr)); }
                .module { display: flex; align-items: center; gap: 14px; padding: 16px 18px; border-radius: 14px; background: var(--panel); border: 1px solid var(--line); }
                .module .check { width: 34px; height: 34px; border-radius: 10px; display: grid; place-items: center; background: rgba(52,211,153,.15); color: var(--ok); font-weight: 800; }
                .module strong { display: block; font-size: 15px; }
                .module small { color: var(--muted); font-size: 12px; }
                .empty { color: var(--muted); }
                .empty code { color: var(--b); }

                footer { margin-top: 56px; padding-top: 20px; border-top: 1px solid var(--line); display: flex; justify-content: space-between; flex-wrap: wrap; gap: 8px; color: var(--muted); font-size: 12px; }

                @media (max-width: 720px) {
                  .stats { grid-template-columns: 1fr; }
                  .row { flex-wrap: wrap; }
                  .row span.d { flex-basis: 100%; }
                }
              </style>
            </head>
            <body>
              <div class="orb o1"></div><div class="orb o2"></div><div class="orb o3"></div>
              <main class="wrap">
                <section class="hero">
                  <div class="logo">M</div>
                  <h1>McpHub</h1>
                  <p class="sub">One secure gateway that exposes your integrations to AI assistants through the Model Context Protocol.</p>
                  <div class="connect">
                    <span class="lbl">MCP URL</span>
                    <code id="url">/mcp</code>
                    <button id="copy" type="button">Copy</button>
                  </div>
                </section>

                <section class="stats">
                  <div class="stat"><div class="k">Server status</div><div class="v"><span id="dot" class="dot"></span><span id="status">Checking&hellip;</span></div></div>
                  <div class="stat"><div class="k">Environment</div><div class="v">{{WebUtility.HtmlEncode(environment)}}</div></div>
                  <div class="stat"><div class="k">Modules enabled</div><div class="v">{{list.Count}}</div></div>
                </section>

                <h2>Endpoints</h2>
                <div class="endpoints">
                  <div class="row"><span class="tag mcp">MCP</span><code>/mcp</code><span class="d">Streamable HTTP endpoint. Connect with an MCP client such as MCP Inspector.</span></div>
                  <a class="row" href="/health/live"><span class="tag get">GET</span><code>/health/live</code><span class="d">Liveness: is the process up?</span><span class="go">Open &rarr;</span></a>
                  <a class="row" href="/health/ready"><span class="tag get">GET</span><code>/health/ready</code><span class="d">Readiness: are dependencies healthy?</span><span class="go">Open &rarr;</span></a>
                </div>

                <h2>Modules</h2>
                <div class="modules">{{cards}}</div>

                <footer><span>McpHub &middot; Development landing page</span><span>Not served in Production</span></footer>
              </main>

              <script>
                const url = location.origin + "/mcp";
                document.getElementById("url").textContent = url;
                document.getElementById("copy").addEventListener("click", async (e) => {
                  try { await navigator.clipboard.writeText(url); e.target.textContent = "Copied!"; }
                  catch { e.target.textContent = "Copy failed"; }
                  setTimeout(() => e.target.textContent = "Copy", 1500);
                });
                fetch("/health/ready").then(r => r.text()).then(t => {
                  const ok = t.trim() === "Healthy";
                  document.getElementById("dot").className = "dot " + (ok ? "ok" : "bad");
                  document.getElementById("status").textContent = ok ? "Healthy" : t.trim() || "Unhealthy";
                }).catch(() => {
                  document.getElementById("dot").className = "dot bad";
                  document.getElementById("status").textContent = "Unreachable";
                });
              </script>
            </body>
            </html>
            """;
    }
}
