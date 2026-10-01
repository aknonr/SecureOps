// Same arguments as in-use-workspace.cjs, without mode. Run after its mapped synthetic SQL journey.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
(async () => {
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const client = await apiContext(request, api), errors = [], requests = [];
    try {
        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
        await context.route('**/*', r => ['127.0.0.1', 'localhost'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
        const page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        page.on('request', r => requests.push(new URL(r.url()).pathname));
        await signIn(page, ui);
        const before = await json(client, '/api/v1/in-use/overview');
        assert.equal(before.open, 0);
        assert.equal(before.unknown, before.total);
        assert.equal(before.oldest.length, 0);
        assert.ok(before.reportReady > 0);
        for (const theme of ['light', 'dark']) {
            await page.evaluate(value => localStorage.setItem('wasas.appearance', value), theme);
            await navigate(page, ui, '');
            const summary = page.getByLabel('In Use yönetim özeti', { exact: true });
            await summary.getByText('Excel hazır (yerel yapısal kontrol)', { exact: true }).waitFor();
            await summary.getByRole('button', { name: 'Kayıtlı özeti yenile' }).click();
            await capture(page, out, `progress-dashboard-${theme}`);
        }
        assert.deepEqual((await json(client, '/api/v1/in-use/overview')).refresh, before.refresh);
        await navigate(page, ui, 'in-use');
        await page.getByLabel('In Use kayıt ara').fill('OR-MAPPED-');
        await page.waitForFunction(() => document.querySelectorAll('.so-inuse-records li').length === 1);
        await capture(page, out, 'progress-list');
        let r = (await json(client, '/api/v1/in-use?search=OR-MAPPED-')).items[0];
        const missingServer = r.source.servers[1].id;
        r = await json(client, `/api/v1/in-use/${r.id}/draft`, { method: 'PUT', data: { expectedVersion: r.version, sourceVersion: r.sourceVersion,
            answers: r.draft.answers.map(a => a.serverId === missingServer && a.check === 'InternetIn' ? { ...a, value: 'Unknown' } : a), notes: '' } });
        await navigate(page, ui, `in-use/${r.id}`);
        await page.getByRole('alert').filter({ hasText: 'Kaynak açılış tarihi bilinmiyor' }).waitFor();
        await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
        await page.getByLabel('Excel sayfası').waitFor();
        await page.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).click();
        await page.getByRole('alert').filter({ hasText: `${missingServer}: İnternetten sunucuya erişim` }).waitFor();
        const goto = page.getByRole('button', { name: 'Alana git', exact: true });
        await goto.focus(); await goto.press('Enter');
        await page.waitForFunction(id => document.activeElement?.getAttribute('aria-label') === `${id} InternetIn`, missingServer);
        await capture(page, out, 'progress-validation');
        await page.getByLabel(`${missingServer} InternetIn`, { exact: true }).selectOption('No');
        await page.getByRole('button', { name: 'Cevapları kaydet', exact: true }).click();
        await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor();
        r = await json(client, `/api/v1/in-use/${r.id}`);
        await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
        await page.getByLabel('Excel sayfası').waitFor();
        const download = page.waitForEvent('download');
        await page.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).click();
        await download;
        const confirm = page.getByRole('button', { name: 'Bu sürüm için yerel tamamlama niyetini onayla', exact: true });
        await confirm.waitFor();
        await capture(page, out, 'progress-confirm-preview');
        await confirm.focus(); await confirm.press('Enter');
        await confirm.waitFor({ state: 'hidden' });
        await page.getByRole('status').filter({ hasText: 'Tamamlama: sözleşme nedeniyle engelli.' }).waitFor();
        const saved = await json(client, `/api/v1/in-use/${r.id}`);
        assert.equal(saved.completion.stage, 'Blocked');
        assert.equal(saved.completion.actorId, (await json(client, '/api/v1/access/me')).userId);
        assert.equal(saved.version, r.version + 1);
        assert.deepEqual(saved.draft, r.draft);
        const retained = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Bağlı arşivi indir', exact: true }).click();
        await retained;
        await capture(page, out, 'progress-blocked-intent');
        assert.equal(requests.some(p => /refresh|upload|bpm|jira/.test(p)), false);
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(out, 'progress-result.json'), JSON.stringify({ passed: true, pageErrors: errors, viewports: ['1440x900', '390x844'], externalRequests: 0 }, null, 2));
    } finally { await client.dispose(); await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
