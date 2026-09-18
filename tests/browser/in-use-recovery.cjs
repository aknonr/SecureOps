// node in-use-recovery.cjs <playwright-core> <ui-loopback> <api-loopback> <fresh-evidence> <proxy-port>
// UI must target this loopback-only proxy. Faults are test-process state, never product endpoints.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), http = require('node:http');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
const port = Number(process.argv[6]), started = Date.now(), calls = [], faults = [], checks = [];
assert.ok(Number.isInteger(port) && port > 1024 && port < 65536);
assert.equal(api.protocol, 'http:');
let fault, currentSession, held;
const signal = () => { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; };
function arm(method, suffix, mode) {
    const hit = signal(), release = signal();
    fault = { method, suffix, mode, hit, release }; held = release;
    return { hit: hit.promise, release: () => release.resolve() };
}
const proxy = http.createServer(async (req, res) => {
    const url = new URL(req.url, api);
    if (url.origin !== api.origin || !url.pathname.startsWith('/api/v1/')) { res.writeHead(403).end(); return; }
    calls.push({ method: req.method, path: url.pathname });
    const match = fault && req.method === fault.method && req.url.includes(fault.suffix) ? fault : null;
    if (match) {
        faults.push({ method: req.method, path: url.pathname, mode: match.mode });
        match.hit.resolve();
        if (match.mode !== 'transport') fault = null;
        if (match.mode === '503') { res.writeHead(503).end(); return; }
        if (match.mode === 'transport') { req.socket.destroy(); return; }
    }
    const upstream = http.request(url, { method: req.method, headers: { ...req.headers, host: api.host } }, response => {
        const chunks = [];
        response.on('data', chunk => chunks.push(chunk));
        response.on('end', async () => {
            const body = Buffer.concat(chunks);
            if (url.pathname === '/api/v1/sessions/current' && response.statusCode === 200) currentSession = JSON.parse(body).sessionId;
            if (match?.mode === 'hold') await match.release.promise;
            if (!res.destroyed) { res.writeHead(response.statusCode, response.headers); res.end(body); }
        });
    });
    upstream.on('error', () => { if (!res.destroyed) res.writeHead(503).end(); });
    req.pipe(upstream);
});
(async () => {
    assert.equal(fs.existsSync(out), false, 'Fresh evidence directory required'); fs.mkdirSync(out, { recursive: true });
    await new Promise((resolve, reject) => { proxy.once('error', reject); proxy.listen(port, '127.0.0.1', resolve); });
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
        headless: true, ignoreDefaultArgs: ['--disable-popup-blocking'] });
    const client = await apiContext(request, api), errors = [];
    let page;
    const button = name => page.getByRole('button', { name, exact: true });
    const ready = async () => page.waitForFunction(() => document.querySelector('.so-inuse') && !document.querySelector('.so-inuse [aria-busy=true]')
        && !document.querySelector('[id^=resource-guide-replay-inuse]')?.disabled);
    const saved = async () => { await button('Taslağı kaydet').click(); await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor(); await ready(); };
    try {
        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
        await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
        page = await context.newPage(); page.on('pageerror', e => errors.push(e.message));
        await signIn(page, ui);
        await navigate(page, ui, 'account');
        await page.waitForFunction(() => !document.querySelector('.so-loading'));
        await navigate(page, ui, 'in-use'); await ready();
        await button('In Use kaynağını yenile').click();
        await page.getByText('Sınırlı kaynak okuması tamamlandı.', { exact: true }).waitFor(); await ready();
        let record = (await json(client, '/api/v1/in-use?search=OR-DEMO-INUSE-01')).items[0];
        assert.equal(record.source.synthetic, true); assert.equal(record.source.servers.length, 2);
        const root = `/api/v1/in-use/${record.id}`, me = await json(client, '/api/v1/access/me');
        if (record.draft) record = await json(client, root + '/draft', { method: 'PUT', data: { expectedVersion: record.version, sourceVersion: record.sourceVersion,
            answers: record.source.servers.flatMap(s => ['InternetOut', 'InternetIn', 'Microsegmented'].map(check => ({ serverId: s.id, check, value: 'Unknown', evidence: '' }))), notes: '' } });
        const read = () => json(client, root);
        await page.getByLabel('In Use kayıt ara', { exact: true }).fill(record.source.code);
        await page.waitForFunction(() => document.querySelectorAll('.so-inuse-records > li').length === 1); await ready();
        await page.getByRole('link', { name: record.source.code, exact: true }).click(); await ready();
        await button('İnceleyici ata/değiştir').click();
        await page.getByLabel('In Use inceleyicisi', { exact: true }).selectOption(me.userId);
        await page.getByLabel('Atama gerekçesi', { exact: true }).fill('Synthetic retained assignment reason');
        for (const mode of ['503', 'transport']) {
            arm('PUT', '/assignment', mode); await button('Atamayı kaydet').click();
            await page.locator('.so-problem').waitFor(); await ready(); fault = null;
            assert.equal(await page.getByLabel('Atama gerekçesi', { exact: true }).inputValue(), 'Synthetic retained assignment reason');
            assert.equal(await page.getByLabel('In Use inceleyicisi', { exact: true }).inputValue(), me.userId);
            assert.equal((await read()).version, record.version);
        }
        await json(client, root + '/assignment', { method: 'PUT', data: { expectedVersion: record.version, assigneeId: null, reason: 'Synthetic concurrent assignment' } });
        await button('Atamayı kaydet').click(); await page.getByText('In Use veri sürümü değişti', { exact: true }).waitFor();
        await button('Cevapları koru ve güncel kayıtla karşılaştır').click();
        await page.getByLabel('Güncel kayıt karşılaştırması', { exact: true }).waitFor();
        await button('Farkları inceledim; yerel cevaplarla devam et').click();
        assert.equal(await page.getByLabel('Atama gerekçesi', { exact: true }).inputValue(), 'Synthetic retained assignment reason');
        await button('Atamayı kaydet').click(); await page.getByText('Yerel atama kaydedildi.', { exact: true }).waitFor(); await ready();
        assert.equal((await read()).assigneeId, me.userId);
        checks.push('refresh -> SQL records -> stable assignment; 503/transport and real assignment conflict preserve intent');
        const answer = check => page.locator(`[data-answer-server="demo-server-01"][data-answer-check="${check}"]`);
        await answer('InternetOut').selectOption('Yes');
        await page.getByRole('link', { name: 'Listeye dön', exact: true }).click();
        const dialog = page.getByRole('dialog'); await dialog.waitFor();
        await dialog.getByRole('button', { name: 'Sayfada kal', exact: true }).click(); await dialog.waitFor({ state: 'hidden' });
        await page.waitForFunction(() => !document.querySelector('.so-route-progress.is-active'));
        assert.equal(await answer('InternetOut').inputValue(), 'Yes'); assert.ok(new URL(page.url()).pathname.endsWith(record.id));
        for (const mode of ['503', 'transport']) {
            arm('PUT', '/draft', mode); await button('Taslağı kaydet').click(); await page.locator('.so-problem').waitFor(); await ready(); fault = null;
            assert.equal(await answer('InternetOut').inputValue(), 'Yes');
        }
        await answer('InternetIn').selectOption('No'); await answer('Microsegmented').selectOption('Yes');
        await page.locator('.so-inuse-bulk input[type=checkbox]').check();
        const prior = await read(); await button('Değişiklikleri göster').click();
        await page.waitForFunction(() => document.querySelectorAll('.so-inuse-bulk li').length === 3);
        assert.equal(await page.locator('.so-inuse-bulk li').count(), 3); assert.deepEqual(await read(), prior);
        await button('Gösterilen değişiklikleri onayla').click();
        await button('Gösterilen değişiklikleri uygula').click();
        await page.getByLabel('Cevap görünümü', { exact: true }).selectOption('all');
        const slow = arm('PUT', '/draft', 'hold'); await button('Taslağı kaydet').click(); await slow.hit;
        await page.waitForFunction(() => document.querySelector('[data-answer-server="demo-server-01"][data-answer-check="InternetOut"]')?.disabled && document.querySelector('input[aria-label="Atama gerekçesi"]')?.disabled);
        assert.equal(await answer('InternetOut').isDisabled(), true);
        assert.equal(await page.getByLabel('Atama gerekçesi', { exact: true }).isDisabled(), true);
        await page.getByRole('link', { name: 'Listeye dön', exact: true }).click();
        await page.getByText('İşlem sürüyor. Sonucu gördükten sonra sayfadan ayrılabilirsiniz.', { exact: true }).waitFor();
        assert.ok(new URL(page.url()).pathname.endsWith(record.id)); slow.release();
        await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor(); await ready();
        assert.equal((await read()).draft.answers.filter(a => a.value !== 'Unknown').length, 6);
        checks.push('canceled navigation; unsaved answers survive 503/transport; explicit bulk confirmation; pending command cannot lose newer edits');
        await navigate(page, ui, `in-use/${record.id}`); await ready();
        await page.getByLabel('Cevap görünümü', { exact: true }).selectOption('all');
        assert.equal(await answer('InternetOut').inputValue(), 'Yes');
        await page.locator('[data-answer-server="demo-server-02"][data-answer-check="InternetOut"]').selectOption('No'); await saved();
        const reviewed = await read(); assert.deepEqual(reviewed.source, record.source); assert.equal(reviewed.sourceHash, record.sourceHash);
        await button('Excel önizleme').click(); await page.getByLabel('Excel sayfası', { exact: true }).waitFor();
        const downloadEvent = page.waitForEvent('download'); await button("WASAS'a arşivle ve indir").click();
        const download = await downloadEvent; await download.saveAs(path.join(out, download.suggestedFilename())); await ready();
        assert.ok(fs.statSync(path.join(out, download.suggestedFilename())).size > 1000);
        const archived = await read(); assert.ok(archived.archivedVersions.includes(reviewed.version));
        for (const theme of ['light', 'dark']) {
            await page.evaluate(t => localStorage.setItem('wasas.appearance', t), theme);
            await navigate(page, ui, `in-use/${record.id}`); await ready();
            const beforeTour = await read(), opener = page.locator('#resource-guide-replay-inuse-review');
            for (let repeat = 0; repeat < 2; repeat++) {
                await opener.focus(); await opener.press('Enter');
                const guide = page.getByRole('dialog', { name: 'Kullanım Rehberi', exact: true }); await guide.waitFor();
                await guide.press('ArrowRight'); await guide.press('ArrowLeft'); await guide.press('Escape'); await guide.waitFor({ state: 'hidden' });
                await page.waitForFunction(() => document.activeElement?.id === 'resource-guide-replay-inuse-review');
            }
            assert.deepEqual(await read(), beforeTour); await capture(page, out, `reopened-${theme}`);
        }
        checks.push('persisted reopen, per-server exception, preview/download/archive; source rows/hash unchanged; desktop/mobile light/dark; tour keyboard/focus/nonmutation');
        await navigate(page, ui, 'in-use'); await ready();
        const delayed = arm('GET', 'search=OR-DEMO-INUSE-01', 'hold');
        await page.getByLabel('In Use kayıt ara', { exact: true }).fill('OR-DEMO-INUSE-01'); await delayed.hit;
        await page.getByLabel('In Use kayıt ara', { exact: true }).fill('NoSuchSyntheticRecoveryRecord');
        await page.getByLabel('In Use görünüm', { exact: true }).selectOption('unassigned');
        delayed.release(); await page.getByText('Kayıtlı eşleşme yok', { exact: true }).waitFor(); await ready();
        assert.equal(await page.locator('.so-inuse-records > li').count(), 0);
        checks.push('obsolete list response discarded after newer filter');
        const denied = await apiContext(request, api, 'team-lead');
        try {
            for (let repeat = 0; repeat < 2; repeat++) assert.equal((await denied.get(root)).status(), 403);
            assert.equal((await denied.put(root + '/draft', { data: { expectedVersion: reviewed.version, sourceVersion: reviewed.sourceVersion, answers: [], notes: '' } })).status(), 403);
        } finally { await denied.dispose(); }
        await navigate(page, ui, 'account'); await page.waitForFunction(() => !document.querySelector('.so-loading'));
        assert.ok(currentSession, 'Actual UI/API application session observed');
        await navigate(page, ui, `in-use/${record.id}`); await ready();
        await page.getByLabel('Cevap görünümü', { exact: true }).selectOption('all');
        await answer('InternetOut').selectOption('No');
        await json(client, '/api/v1/sessions/revoke', { method: 'POST', data: { sessionId: currentSession, reason: 'Synthetic browser expiry-path verification' } });
        await button('Taslağı kaydet').click(); await page.waitForURL(url => url.pathname === '/session-expired');
        assert.equal(await page.locator('.so-inuse').count(), 0);
        checks.push('repeated persisted 403; actual server session revocation redirects to reauthentication without implicit replacement');
        assert.deepEqual(errors, []);
        assert.equal(calls.some(c => /jira|upload|bpm|operational-records/.test(c.path)), false);
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, seconds: (Date.now() - started) / 1000, checks, faults, calls, errors,
            limitations: ['Simulation is not corporate integration', 'Idle/absolute clocks verified in existing manual-time tests; browser uses actual revocation',
                'Managed browser policy, screen reader and VDI not available', 'In Use source navigation has no configured link; no popup destination claim'] }, null, 2));
    } catch (error) {
        fs.writeFileSync(path.join(out, 'failure.json'), JSON.stringify({ passed: false, seconds: (Date.now() - started) / 1000, checks, faults, errors }, null, 2));
        if (page) await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }); throw error;
    }
    finally { fault = null; held?.resolve(); await client.dispose(); await browser.close(); proxy.closeAllConnections(); await new Promise(resolve => proxy.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode = 1; proxy.close(); });
