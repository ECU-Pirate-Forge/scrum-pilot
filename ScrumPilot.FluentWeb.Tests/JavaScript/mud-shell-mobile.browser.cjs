// Playwright probe; authenticated context retained, existing pages untouched.
async (page, options = {}) => {
    const baseUrl = options.baseUrl ?? new URL("/web/", page.url()).href;
    const probe = await page.context().newPage();
    try {
        await probe.setViewportSize({ width: 390, height: 844 });
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
        await probe.goto(new URL("organizations/2/metrics?projectId=1", baseUrl).href);
        await probe.locator(".grid-stack-item").first().waitFor();
        await probe.waitForLoadState("networkidle");
        const bounds = await probe.evaluate(() => {
            const selectors = [
                "[aria-label='Toggle navigation menu']", ".appbar-logo-link",
                "button[aria-label^='Select organization:']", "button[aria-label^='Select project:']",
                "[aria-label='Toggle dark mode']", "[aria-label='User settings']", "[aria-label='Sign out']"
            ];
            const header = document.querySelector(".tenant-appbar");
            return {
                viewport: innerWidth, documentWidth: document.documentElement.scrollWidth,
                headerWidth: header?.getBoundingClientRect().width, headerScrollWidth: header?.scrollWidth,
                controls: selectors.map(selector => {
                    const element = document.querySelector(selector);
                    if (!element) return { selector, missing: true };
                    const rect = element.getBoundingClientRect();
                    return { selector, left: rect.left, right: rect.right, width: rect.width,
                        height: rect.height, name: element.getAttribute("aria-label") ?? element.querySelector("img")?.alt };
                }),
                labelsHidden: [...document.querySelectorAll(".appbar-selection-label")]
                    .every(label => getComputedStyle(label).display === "none")
            };
        });
        // Document width is reported separately: inherited metrics toolbar/widget
        // overflow must not be hidden or misreported as an app-bar repair.
        if (!bounds.headerWidth || bounds.headerWidth > 390 || bounds.headerScrollWidth > bounds.headerWidth + 1
            || !bounds.labelsHidden || bounds.controls.some(control =>
            control.missing || !control.name || control.left < 0 || control.right > 390 || control.width < 40 || control.height < 30))
            throw new Error(`Mud390px header overflow: ${JSON.stringify(bounds)}`);
        return bounds;
    } finally {
        await probe.close();
    }
}
