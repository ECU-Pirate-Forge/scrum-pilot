// Real published WASM startup + actual login form requests, without an AppHost.
const fs = require("node:fs");
const { chromium } = require("playwright");

async function main() {
    const manifest = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
    const credentials = JSON.parse(fs.readFileSync(manifest.CredentialFile, "utf8"));
    const browser = await chromium.launch({ channel: process.env.SCRUMPILOT_BROWSER_CHANNEL || "msedge" });
    try {
        for (const [site, origin] of [["web", manifest.Web], ["fluentweb", manifest.FluentWeb]]) {
            const context = await browser.newContext({ ignoreHTTPSErrors: true });
            let stage = "published-login-startup";
            try {
                const page = await context.newPage();
                await page.goto(origin + "/login", { waitUntil: "commit" });
                // Fluent required names include ", Required"; its placeholder
                // also exists on both the host and the native shadow input.
                const username = site === "fluentweb"
                    ? page.locator('input[placeholder="Enter your username"]')
                    : page.getByRole("textbox", { name: "Username", exact: true });
                const password = site === "fluentweb"
                    ? page.locator('input[placeholder="Enter your password"]')
                    : page.getByLabel("Password", { exact: true });
                await username.waitFor({ timeout: 60000 });
                stage = "public-fallback-configuration";
                const config = await (await context.request.get(origin + "/appsettings.json")).json();
                if (Object.keys(config).sort().join(",") !== "ApiBaseUrl,DetailedErrors" ||
                    config.ApiBaseUrl !== manifest.Api) throw new Error();
                stage = "actual-wasm-form-login";
                await username.fill(credentials.Email);
                await password.fill(credentials.Password);
                const login = page.waitForResponse(response =>
                    response.url() === manifest.Api + "/api/auth/login" &&
                    response.request().method() === "POST", { timeout: 45000 }).catch(() => null);
                const directory = page.waitForResponse(response =>
                    response.url() === manifest.Api + "/api/organizations" &&
                    response.request().headers().authorization?.startsWith("Bearer "), { timeout: 45000 }).catch(() => null);
                const submit = site === "fluentweb" ? page.locator('fluent-button[type="submit"]')
                    : page.getByRole("button", { name: /^sign in$/i });
                await submit.click();
                const [loginResponse, directoryResponse] = await Promise.all([login, directory]);
                if (loginResponse?.status() !== 200 || directoryResponse?.status() !== 200) throw new Error();
                console.log(JSON.stringify({ site, passed: true, actualPublishedWasm: true,
                    actualLoginForm: true, fallbackPublicKeys: ["ApiBaseUrl", "DetailedErrors"],
                    directApiLogin: 200, directAuthenticatedOrganizations: 200,
                    appHost: false, crossOriginCorsVerified: true }));
            } catch {
                console.log(JSON.stringify({ site, passed: false, stage }));
                throw new Error();
            } finally {
                await context.close();
            }
        }
    } finally {
        await browser.close();
    }
}
main().catch(() => {
    console.error("Standalone browser check failed; credentials and request details withheld.");
    process.exitCode = 1;
});
