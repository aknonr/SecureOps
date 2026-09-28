// Test-only loopback proxy between the UI host and the API host (announcement-hosts.ps1 -UiApiProxyPort).
// Passes everything through, except that a mode can sever Jira-create requests:
//   drop-after       forward the next create, then reset the UI's connection (one-shot)
//   drop-before-all  never forward a create until the mode is reset
//   drop-after-all   forward the first create and reset the UI's connection; drop every later create
// .NET SocketsHttpHandler transparently resends a request whose connection closes before any response
// byte, so the "-all" modes are what make a lost response reach the UI as an uncertain outcome.
// This adds no product route, setting or header; it only simulates a lost response.
// node sdm-fault-proxy.cjs <listen port> <API port>
const http = require('node:http');
const listen = Number(process.argv[2]);
const target = { host: '127.0.0.1', port: Number(process.argv[3]) };
let mode = 'pass';
let forwarded = 0;
const createRoute = /^\/api\/v1\/operational-records\/[^/]+\/jira$/;

http.createServer((req, res) => {
    if (req.url.startsWith('/__proxy/mode/')) {
        mode = decodeURIComponent(req.url.substring('/__proxy/mode/'.length));
        res.end(mode);
        return;
    }
    if (req.url === '/__proxy/forwarded-creates') {
        res.end(String(forwarded));
        return;
    }
    const create = req.method === 'POST' && createRoute.test(req.url.split('?')[0]);
    if (create && (mode === 'drop-before-all' || mode === 'drop-after-all-dropping')) {
        req.socket.destroy();
        return;
    }
    const severAfter = create && (mode === 'drop-after' || mode === 'drop-after-all');
    if (severAfter) mode = mode === 'drop-after' ? 'pass' : 'drop-after-all-dropping';
    if (create) forwarded++;
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
