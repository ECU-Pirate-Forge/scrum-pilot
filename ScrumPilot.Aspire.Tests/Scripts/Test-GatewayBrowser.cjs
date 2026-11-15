// Optional real-browser transport check. Requires Playwright in the operator's
// tooling environment, not either WASM client's package graph.
// node Test-GatewayBrowser.cjs <restricted browser-session.json>
const fs = require("node:fs");
const { chromium } = require("playwright");

async function main() {
    const manifest = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
    const credentials = JSON.parse(fs.readFileSync(manifest.CredentialFile, "utf8"));
    const browser = await chromium.launch({
        channel: process.env.SCRUMPILOT_BROWSER_CHANNEL || "msedge",
        headless: true
    });
    try {
        for (const [site, address] of [["web", manifest.Web], ["fluentweb", manifest.FluentWeb]]) {
            // Developer certificate exception is confined to this test context.
            const context = await browser.newContext({ ignoreHTTPSErrors: true });
            let browserStage = "navigation";
            try {
                const page = await context.newPage();
                // This is a transport check, not a visual/CDN readiness check.
                await page.goto(address, { waitUntil: "commit" });
                await page.waitForFunction(() => !!document.querySelector("base"));
                browserStage = "same-origin-transport";
                const result = await page.evaluate(async ({ site, credentials }) => {
                    let stage = "login";
                    try {
                        const base = `/${site}/_api/webapi/`;
                        const login = await fetch(base + "api/auth/login", {
                            method: "POST", redirect: "error",
                            headers: { "Content-Type": "application/json" },
                            body: JSON.stringify({ userName: credentials.Email, password: credentials.Password })
                        });
                        if (login.status !== 200 || login.redirected) throw new Error();
                        const { token } = await login.json();
                        const headers = { Authorization: `Bearer ${token}` };
                        stage = "authenticated-organizations";
                        const organizations = await fetch(base + "api/organizations", { headers, redirect: "error" });
                        if (organizations.status !== 200 || organizations.redirected ||
                            new URL(organizations.url).origin !== location.origin) throw new Error();
                        const directory = await organizations.json();
                        // The gateway integration regression creates this isolated fixture.
                        const organizationId = directory.find(item => item.name === "Gateway regression")?.organizationId;
                        if (!organizationId) throw new Error();
                        stage = "authenticated-negotiate";
                        const hub = base + `hubs/organizations/${organizationId}/planning-poker`;
                        const negotiate = await fetch(hub + "/negotiate?negotiateVersion=1", {
                            method: "POST", headers, redirect: "error"
                        });
                        if (negotiate.status !== 200 || negotiate.redirected) throw new Error();
                        const negotiation = await negotiate.json();
                        stage = "authenticated-websocket-handshake";
                        const address = new URL(hub, location.origin);
                        address.protocol = location.protocol === "https:" ? "wss:" : "ws:";
                        address.searchParams.set("id", negotiation.connectionToken);
                        address.searchParams.set("access_token", token);
                        await new Promise((resolve, reject) => {
                            const socket = new WebSocket(address);
                            const timer = setTimeout(() => { socket.close(); reject(new Error()); }, 15000);
                            socket.onopen = () => socket.send('{"protocol":"json","version":1}\u001e');
                            socket.onerror = () => { clearTimeout(timer); reject(new Error()); };
                            socket.onmessage = event => {
                                clearTimeout(timer);
                                const handshake = JSON.parse(String(event.data).split("\u001e")[0]);
                                socket.close();
                                if (handshake.error) reject(new Error());
                                else resolve();
                            };
                        });
                        return { site, passed: true, login: 200, organizations: 200,
                            negotiate: 200, signalR: "authenticated WebSocket handshake",
                            redirected: false, sameOrigin: true };
                    } catch {
                        // Never return credentials, tokens, request URLs, or browser errors.
                        return { site, passed: false, stage };
                    }
                }, { site, credentials });
                console.log(JSON.stringify(result));
                if (!result.passed) throw new Error();
            } catch (error) {
                console.log(JSON.stringify({ site, passed: false, browserStage,
                    failureType: error.name === "TimeoutError" ? "timeout" : "browser-operation" }));
                throw error;
            } finally {
                await context.close();
            }
        }
    } finally {
        await browser.close();
    }
}

main().catch(() => {
    console.error("Browser gateway verification failed; credentials and transport URLs are withheld.");
    process.exitCode = 1;
});
