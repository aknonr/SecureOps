// Loopback Demo only: node api-csrf.cjs <playwright-core> <API URL> <evil URL> <evidence dir>
// Browser form headers are real; only Demo identity is injected in lieu of corporate Negotiate.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { chromium, request } = require(process.argv[2]);
const { loopback, apiContext } = require('./journey-support.cjs');
const api = loopback(process.argv[3]), evil = loopback(process.argv[4]);
const out = path.resolve(process.argv[5]);
const scanPath = '/api/v1/service-accounts/accounts/00000000-0000-0000-0000-000000000001/usage-scans';

(async () => {
    fs.mkdirSync(out, { recursive: true });
    const server = http.createServer((_req, res) => {
        res.writeHead(200, { 'Content-Type': 'text/html' });
        res.end(`<title>Synthetic cross-origin form</title><form action="${new URL(scanPath, api)}" method="post" enctype="multipart/form-data"><input type="file" name="file"><input name="runStatement" value="Synthetic only"><button type="submit">Submit</button></form>`);
    });
    await new Promise((resolve, reject) => {
        server.once('error', reject);
        server.listen(Number(evil.port), evil.hostname, resolve);
    });
    const admin = await apiContext(request, api);
    let browser;
    const evidence = { authentication: 'synthetic Demo actor, not Negotiate', checks: [] };
    try {
        const before = await (await admin.get('/api/v1/resources/categories')).json();
        browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || undefined, headless: true });
        const context = await browser.newContext({ ignoreHTTPSErrors: true });
        const page = await context.newPage();
        let formHeaders;
        await context.route(new URL(scanPath, api).href, async route => {
            formHeaders = await route.request().allHeaders();
            await route.continue({ headers: { ...formHeaders, 'X-SecureOps-Demo-Actor': 'platform-admin' } });
        });
        await page.goto(evil.href);
        await page.locator('input[type=file]').setInputFiles({ name: 'synthetic.json', mimeType: 'application/json', buffer: Buffer.from('{"synthetic":true}') });
        const responsePromise = page.waitForResponse(response => response.url().startsWith(new URL(scanPath, api).href));
        await page.locator('button').click();
        const response = await responsePromise;
        assert.equal(response.status(), 403);
        assert.equal((await response.json()).code, 'ApiCsrfRejected');
        assert.equal(formHeaders.origin, evil.origin);
        if (formHeaders['sec-fetch-site'] !== undefined) assert.equal(formHeaders['sec-fetch-site'], 'cross-site');
        assert.match(formHeaders['content-type'], /^multipart\/form-data; boundary=/);
        assert.equal(formHeaders['x-secureops-csrf'], undefined);
        evidence.checks.push({ name: 'real cross-site multipart form', status: 403, origin: formHeaders.origin,
            fetchSite: formHeaders['sec-fetch-site'] || null, csrfHeaderPresent: false });
        await page.screenshot({ path: path.join(out, 'cross-origin-rejected.png') });

        await context.route(new URL('/csrf-same-origin', api).href, route => route.fulfill({
            contentType: 'text/html', body: '<title>Synthetic same-origin client</title>'
        }));
        await page.goto(new URL('/csrf-same-origin', api).href);
        const sameOrigin = await page.evaluate(async () => {
            const result = await fetch('/api/v1/resources/categories', { method: 'POST', credentials: 'include',
                headers: { 'Content-Type': 'application/json', 'X-SecureOps-Demo-Actor': 'platform-admin', 'X-SecureOps-Csrf': '1' },
                body: JSON.stringify({ name: 'Synthetic CSRF same-origin acceptance' }) });
            return { status: result.status, body: await result.json() };
        });
        assert.equal(sameOrigin.status, 200);
        evidence.checks.push({ name: 'real same-origin browser write with intent', status: 200 });
        const after = await (await admin.get('/api/v1/resources/categories')).json();
        assert.equal(after.length, before.length + 1);

        const noHeader = await request.newContext({ baseURL: api.href, extraHTTPHeaders: { 'X-SecureOps-Demo-Actor': 'platform-admin' } });
        try {
            const blocked = await noHeader.post('/api/v1/resources/categories', { data: { name: 'Must not be stored' } });
            assert.equal(blocked.status(), 403);
            assert.equal((await blocked.json()).code, 'ApiCsrfRejected');
            assert.deepEqual(await (await admin.get('/api/v1/resources/categories')).json(), after);
            evidence.checks.push({ name: 'headerless server write changes no business records', status: 403 });
        } finally { await noHeader.dispose(); }
        evidence.passed = true;
    } finally {
        fs.writeFileSync(path.join(out, 'api-csrf-results.json'), JSON.stringify(evidence, null, 2));
        if (browser) await browser.close();
        await admin.dispose();
        await new Promise(resolve => server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
