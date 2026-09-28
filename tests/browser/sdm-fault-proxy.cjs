// Test-only loopback proxy between the UI host and the API host (announcement-hosts.ps1 -UiApiProxyPort).
// Passes everything through, except that a one-shot mode can sever the next Jira-create response:
//   drop-before  the request never reaches the API (no server state can change)
//   drop-after   the API completes the request, then the UI's connection is reset
// This adds no product route, setting or header; it only simulates a lost response.
// node sdm-fault-proxy.cjs <listen port> <API port>
const http = require('node:http');
const listen = Number(process.argv[2]);
const target = { host: '127.0.0.1', port: Number(process.argv[3]) };
let mode = 'pass';
const createRoute = /^\/api\/v1\/operational-records\/[^/]+\/jira$/;

http.createServer((req, res) => {
    if (req.url.startsWith('/__proxy/mode/')) {
        mode = decodeURIComponent(req.url.substring('/__proxy/mode/'.length));
        res.end(mode);
        return;
    }
    const create = req.method === 'POST' && createRoute.test(req.url.split('?')[0]);
    if (create && mode === 'drop-before') {
        mode = 'pass';
        req.socket.destroy();
        return;
    }
    const severAfter = create && mode === 'drop-after';
    if (severAfter) mode = 'pass';
    const upstream = http.request({ ...target, method: req.method, path: req.url, headers: req.headers }, response => {
        if (severAfter) {
            response.resume();
            response.on('end', () => req.socket.destroy());
            return;
        }
        res.writeHead(response.statusCode, response.headers);
        response.pipe(res);
    });
    upstream.on('error', () => res.destroy());
    req.pipe(upstream);
}).listen(listen, '127.0.0.1', () => console.log(`fault proxy ${listen} -> ${target.port}`));
