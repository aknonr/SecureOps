const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const http = require('node:http'), { spawn } = require('node:child_process'), { once } = require('node:events');
const { chromium } = require(process.argv[2]);
const { loopback, navigate, signIn } = require('./journey-support.cjs');
const api = loopback(process.argv[3]), payload = path.resolve(process.argv[4]), out = path.resolve(process.argv[5]);
const port = Number(process.argv[6]);
assert.ok(Number.isInteger(port) && port > 1024 && port < 65534);
(async () => {
    fs.mkdirSync(out, { recursive: false });
    let mode = 'Disabled', child, browser;
    const events = [];
    const proxy = http.createServer((req, res) => {
        if (req.url === '/api/v1/announcements/source/readiness') {
            events.push({ mode, utc: new Date().toISOString() });
            res.setHeader('Content-Type', 'application/json');
            if (mode === 'Failed' || mode === 'Denied') {
                res.statusCode = mode === 'Denied' ? 403 : 503;
                res.end(JSON.stringify({ code: mode === 'Denied' ? 'AnnouncementSourceAccessDenied' : 'AnnouncementSourceUnavailable', correlationId: 'synthetic-source-failure' }));
            } else res.end(JSON.stringify({ state: mode, stage: 'synthetic', missing: [], workerState: mode, matchingWorkers: mode === 'Ready' ? 1 : 0, checkedAt: new Date().toISOString() }));
            return;
        }
        if (req.url === '/api/v1/announcements/source/profiles') { res.setHeader('Content-Type', 'application/json'); res.end('[]'); return; }
        const target = new URL(req.url, api);
        if (target.origin !== api.origin) { res.statusCode = 400; res.end(); return; }
        const upstream = http.request(target, { method: req.method, headers: { ...req.headers, host: api.host } }, response => {
            res.writeHead(response.statusCode, response.headers); response.pipe(res);
        });
        upstream.on('error', () => { res.statusCode = 502; res.end(); });
        req.pipe(upstream);
    });
    try {
        proxy.listen(port, '127.0.0.1'); await once(proxy, 'listening');
        const ui = loopback('https://localhost:' + (port + 1));
        child = spawn('dotnet', [path.join(payload, 'SecureOps.Ui.dll'), '--urls=' + ui.href], {
            cwd: payload, windowsHide: true, env: { ...process.env, DOTNET_ENVIRONMENT: 'Demo', ASPNETCORE_ENVIRONMENT: 'Demo',
                DemoMode__Enabled: 'true', DemoMode__AllowMockAuthentication: 'true', DemoMode__ApiDemoActor: 'platform-admin',
                Oidc__Enabled: 'false', DataProtection__Mode: 'Ephemeral', DataProtection__ApplicationName: 'SourceFailureAcceptance',
                IdentityLookupApi__BaseAddress: 'http://127.0.0.1:' + port + '/' }, stdio: ['ignore', 'pipe', 'pipe']
        });
        await new Promise((resolve, reject) => {
            const timer = setTimeout(() => reject(Error('UI startup timed out')), 30000);
            child.stdout.on('data', data => { if (data.toString().includes('Now listening on:')) { clearTimeout(timer); resolve(); } });
            child.once('exit', code => { clearTimeout(timer); reject(Error('UI exited ' + code)); });
        });
        browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 } });
        await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
        const page = await context.newPage();
        await signIn(page, ui); await navigate(page, ui, 'announcements');
        if (await page.getByRole('button', { name: 'Şimdi değil', exact: true }).count()) await page.getByRole('button', { name: 'Şimdi değil', exact: true }).click();
        await page.getByRole('button', { name: 'Yeni duyuru', exact: true }).click();
        await page.getByText('Kaynak toplama kapalı', { exact: true }).waitFor();
        await page.locator('#announcement-OcoReference').fill('OCO-MANUAL-PRESERVED');
        await page.locator('#announcement-Subject').fill('Retained unsaved operator work');
        for (const [state, label] of [['Failed', 'Kaynak kullanılabilirliği doğrulanamadı. Elle düzenlemeleriniz korunur.'],
            ['ConfigurationMissing', 'Kaynak yapılandırması eksik'], ['NoWorker', 'Güncel Worker kaydı yok; işler bekleyebilir'],
            ['WrongQueue', 'Çalışan Worker bu kuyruğu dinlemiyor'], ['Ready', 'İş kuyruğu hazır; kaynak bağlantısı henüz doğrulanmadı'],
            ['Failed', 'Kaynak kullanılabilirliği doğrulanamadı. Elle düzenlemeleriniz korunur.'], ['Denied', 'Kaynak sorgulama yetkiniz yok']]) {
            mode = state;
            await page.getByRole('button', { name: 'Durumu yenile', exact: true }).click();
            await page.getByText(label, { exact: true }).waitFor();
            assert.equal(await page.getByText('Kaynak toplama kapalı', { exact: true }).count(), 0);
            if (state === 'Failed') assert.equal(await page.getByText('İş kuyruğu hazır; kaynak bağlantısı henüz doğrulanmadı', { exact: true }).count(), 0);
            assert.equal(await page.locator('#announcement-Subject').inputValue(), 'Retained unsaved operator work');
            if (state === 'Denied') await page.getByText('synthetic-source-failure', { exact: true }).waitFor();
            await page.screenshot({ path: path.join(out, events.length + '-' + state + '.png'), fullPage: true });
        }
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, events, transport: 'isolated loopback UI-to-API proxy', corporateCalls: false }, null, 2));
    } finally {
        if (browser) await browser.close();
        if (child && child.exitCode === null) { const stopped = once(child, 'exit'); child.kill(); await stopped; }
        proxy.closeAllConnections(); await new Promise(resolve => proxy.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
