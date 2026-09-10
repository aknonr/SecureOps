// Published loopback Demo only; five fresh circuits, one explicit refresh per circuit.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.argv[2]);
const { loopback, navigate, signIn, capture } = require('./journey-support.cjs');
(async () => {
    const ui = loopback(process.argv[3]), out = path.resolve(process.argv[4]);
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true });
    await context.route('**/*', r => ['127.0.0.1', 'localhost'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
    const page = await context.newPage(), durations = [], errors = [];
    page.on('pageerror', e => errors.push(e.message));
    try {
        await signIn(page, ui);
        for (let attempt = 0; attempt < 5; attempt++) {
            await navigate(page, ui, 'operational-records');
            const start = performance.now();
            await page.getByRole('button', { name: 'Kaynağı yenile', exact: true }).click();
            await page.getByText('Sınırlı kaynak yenilemesi tamamlandı.', { exact: false }).waitFor();
            durations.push(Math.round(performance.now() - start));
        }
        await capture(page, out, 'refresh-notice');
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(out, 'refresh-results.json'), JSON.stringify({ attempts: durations.length, durationsMs: durations, errors }, null, 2));
        console.log(JSON.stringify({ durationsMs: durations, errors }));
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
