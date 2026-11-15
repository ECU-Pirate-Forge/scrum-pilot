// Loopback-only test host for immutable published WASM artifacts.
const fs = require("node:fs");
const http = require("node:http");
const path = require("node:path");

const root = fs.realpathSync(process.argv[2]);
const port = Number(process.argv[3]);
const publicSettings = Buffer.from(JSON.stringify({
    ApiBaseUrl: process.argv[4], DetailedErrors: false
}));
// .NET 10 publish emits fingerprinted files and unversioned aliases in this
// manifest. Match a manifest-aware static host, without rewriting the publish.
const manifest = JSON.parse(fs.readFileSync(process.argv[5], "utf8"));
const aliases = new Map(manifest.Endpoints.filter(endpoint => !endpoint.Selectors?.length)
    .map(endpoint => [endpoint.Route, endpoint]));
const types = {
    ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript",
    ".json": "application/json", ".wasm": "application/wasm", ".css": "text/css",
    ".svg": "image/svg+xml", ".png": "image/png", ".ico": "image/x-icon",
    ".woff": "font/woff", ".woff2": "font/woff2", ".ttf": "font/ttf"
};
function insideRoot(file) {
    return file === root || file.startsWith(root + path.sep);
}
http.createServer((request, response) => {
    try {
        const pathname = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
        if (pathname.startsWith("/_blazor/")) {
            response.writeHead(404).end();
            return;
        }
        let body, type;
        if (pathname === "/appsettings.json") {
            body = publicSettings;
            type = "application/json";
        } else {
            const endpoint = aliases.get(pathname.slice(1));
            let file = path.resolve(root, endpoint?.AssetFile || "." + pathname);
            if (!insideRoot(file)) { response.writeHead(403).end(); return; }
            if (!fs.existsSync(file) || !fs.statSync(file).isFile()) {
                if (path.extname(pathname)) { response.writeHead(404).end(); return; }
                file = path.join(root, "index.html");
            }
            file = fs.realpathSync(file);
            if (!insideRoot(file)) { response.writeHead(403).end(); return; }
            body = fs.readFileSync(file);
            type = endpoint?.ResponseHeaders?.find(header => header.Name === "Content-Type")?.Value ||
                types[path.extname(file)] || "application/octet-stream";
        }
        response.writeHead(200, {
            "Content-Type": type, "Content-Length": body.length, "Cache-Control": "no-store"
        }).end(body);
    } catch {
        response.writeHead(500).end();
    }
}).listen(port, "127.0.0.1").on("error", () => { process.exitCode = 1; });
