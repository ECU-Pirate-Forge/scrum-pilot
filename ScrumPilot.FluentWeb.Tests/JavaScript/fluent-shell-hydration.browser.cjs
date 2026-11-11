// Playwright probe function: reuse an authenticated context, but never its open pages.
// Optional helperBaseUrl serves isolated published static assets; API/runtime config
// still use the real gateway. The helper must implement endpoint-manifest aliases.
async (page, options = {}) => {
    const baseUrl = options.baseUrl ?? new URL("/fluentweb/", page.url()).href;
    const projectId = options.projectId ?? 1;
    const results = [];
    for (const organizationId of options.organizationIds ?? [2, 3]) {
        const probe = await page.context().newPage();
        const settingsWrites = [];
        const apiFailures = [];
        try {
            if (options.helperBaseUrl) {
                await probe.route(`${baseUrl}**`, async route => {
                    const relative = route.request().url().slice(baseUrl.length);
                    if (relative.startsWith("_api/") || relative.startsWith("_blazor/")) {
                        await route.continue();
                        return;
                    }
                    const response = await probe.request.get(new URL(relative, options.helperBaseUrl).href);
                    await route.fulfill({ response });
                });
            }
            await probe.addInitScript(() => {
                window.__tenantShellProbe = { changes: [], navigations: [] };
                document.addEventListener("change", event => {
                    const item = event.target;
                    if (item?.tagName === "FLUENT-MENU-ITEM")
                        window.__tenantShellProbe.changes.push({ checked: item.checked, label: item.textContent.trim() });
                }, true);
                for (const method of ["pushState", "replaceState"]) {
                    const original = history[method].bind(history);
                    history[method] = (state, title, url) => {
                        window.__tenantShellProbe.navigations.push(String(url));
                        return original(state, title, url);
                    };
                }
            });
            probe.on("request", request => {
                if (request.method() === "PUT" && new URL(request.url()).pathname.endsWith("/api/user/settings"))
                    settingsWrites.push(request.url());
            });
            probe.on("response", response => {
                if (response.url().includes("/_api/") && response.status() >= 400)
                    apiFailures.push({ url: response.url(), status: response.status() });
            });
            const route = `organizations/${organizationId}/scrum-board?projectId=${projectId}`;
            const expectedUrl = new URL(route, baseUrl).href;
            await probe.goto(expectedUrl);
            await probe.locator("[title='Select organization']").waitFor();
            await probe.waitForLoadState("networkidle");
            const state = await probe.evaluate(() => ({
                url: location.href,
                changes: window.__tenantShellProbe.changes,
                navigations: window.__tenantShellProbe.navigations,
                project: document.querySelector("[title='Select project']")?.getAttribute("aria-label")
            }));
            if (state.url !== expectedUrl || state.project === "Select project: none")
                throw new Error(`Cold deep link lost its project: ${JSON.stringify(state)}`);
            if (!state.changes.some(change => change.checked === true))
                throw new Error("No native checked-menu hydration was observed.");
            if (state.navigations.length)
                throw new Error(`Hydration navigated unexpectedly: ${JSON.stringify(state.navigations)}`);
            await probe.locator("[data-testid='scrum-board-lanes']").waitFor();

            // Exercise the warm navigation that previously produced false error toasts.
            await probe.locator(`a[href='organizations/${organizationId}/metrics?projectId=${projectId}']:visible`).click();
            await probe.waitForLoadState("networkidle");
            await probe.locator(`a[href='organizations/${organizationId}/scrum-board?projectId=${projectId}']:visible`).click();
            await probe.locator("[data-testid='scrum-board-lanes']").waitFor();
            await probe.waitForLoadState("networkidle");
            const falseErrors = await probe.getByText(/Unable to load PBIs from the server|Some board filter options could not be loaded\./).count();
            if (falseErrors || settingsWrites.length || apiFailures.length)
                throw new Error(`Shell side effects: ${JSON.stringify({ falseErrors, settingsWrites, apiFailures })}`);
            if (probe.url() !== expectedUrl)
                throw new Error(`Warm board lost scope: ${probe.url()}`);
            results.push({ organizationId, projectId, coldLinkPreserved: true, nativeHydrationObserved: true,
                warmBoardWithoutFalseErrors: true, settingsWrites: 0, apiFailures: [] });
        } finally {
            await probe.close();
        }
    }
    return results;
}
