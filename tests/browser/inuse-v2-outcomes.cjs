// Read-only browser acceptance of persisted outcomes from the isolated SQL failure tests.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
(async () => {
    fs.mkdirSync(out, { recursive: false });
    const client = await apiContext(request, api), browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 } });
    await context.route('**/*', route => ['localhost', '127.0.0.1'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort());
    const page = await context.newPage(), found = new Map();
    try {
        for (let number = 1; number <= 5; number++) {
            const list = await json(client, '/api/v1/in-use?search=OR-EXEC-&pageSize=50&page=' + number);
            for (const record of list.items) {
                const status = await json(client, `/api/v1/in-use/${record.id}/execution`), operation = status.operation;
                assert.equal(status.readiness.available, false, 'Corporate-style completion must stay disabled');
                if (!operation) continue;
                const key = operation.state === 'Failed' && operation.step === 5 ? 'attachment-confirmed-bpm-failed'
                    : operation.state === 'Unknown' && operation.step === 3 ? 'upload-unknown'
                    : operation.state === 'Unconfirmed' && operation.step === 6 ? 'or-state-unconfirmed' : null;
                if (key && !found.has(key)) found.set(key, { record, operation });
            }
            if (number * 50 >= list.total || found.size === 3) break;
        }
        assert.equal(found.size, 3, 'Run the isolated execution SQL scenarios before this read-only journey');
        await signIn(page, ui);
        for (const [name, { record, operation }] of found) {
            await navigate(page, ui, 'in-use/' + record.id);
            const stage = page.getByRole('region', { name: 'WASAS adımını onayla', exact: true });
            await stage.getByRole('status').first().waitFor();
            await page.waitForFunction(() => [...document.querySelectorAll('button')].some(b => b.textContent.trim() === 'İşlem durumunu yenile' && !b.disabled));
            await stage.evaluate(element => element.scrollIntoView({ block: 'center' }));
            assert.equal(await stage.getByRole('button', { name: 'Raporu ekle ve WASAS adımını onayla', exact: true }).isDisabled(), true);
            assert.equal(await stage.getByText('Ek doğrulandı ve OR kapalı durumu kaynaktan doğrulandı', { exact: true }).count(), 0);
            if (name === 'attachment-confirmed-bpm-failed') {
                assert.ok(operation.evidence.some(e => e.step === 'Attachment' && e.outcome === 'Verified'));
                await stage.getByText(/Ek kimliği ve bayt doğrulaması: Kaynak okumasıyla doğrulandı/).waitFor();
            }
            await page.screenshot({ path: path.join(out, name + '.png') });
        }
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, readOnly: true, corporate: false, cases: [...found].map(([name, value]) => ({ name, id: value.record.id, state: value.operation.state, step: value.operation.step })) }, null, 2));
    } finally { await client.dispose(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
