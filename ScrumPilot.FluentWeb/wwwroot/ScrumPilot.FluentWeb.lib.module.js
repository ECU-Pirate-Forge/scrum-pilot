// Aspire's standalone gateway exposes only browser-safe proxy configuration.
export async function onRuntimeConfigLoaded(config) {
    try {
        const response = await fetch(new URL('_blazor/_configuration', document.baseURI));
        if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return;
        const serverConfig = await response.json();
        const environment = serverConfig?.webAssembly?.environment ?? {};
        config.environmentVariables ??= {};
        for (const [key, value] of Object.entries(environment)) {
            if (/^services__webapi__(https|http)__\d+$/i.test(key) ||
                key === 'OTEL_SERVICE_NAME' || key === 'OTEL_RESOURCE_ATTRIBUTES' ||
                key === 'OTEL_EXPORTER_OTLP_ENDPOINT') {
                config.environmentVariables[key] = value;
            }
        }
    } catch {
        // A direct static/dev-server run uses its wwwroot appsettings API endpoint.
    }
}
