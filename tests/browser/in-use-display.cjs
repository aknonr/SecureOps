// Published loopback hosts + ResourceSqlTests.InUse_DisplayAcceptance fixture only.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
const after = process.argv[6] === 'after';
(async () => {
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const client = await apiContext(request, api), errors = [], dialogs = [];
    try {
        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
        await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
        const page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message)); page.on('dialog', async d => { dialogs.push(d.message()); await d.dismiss(); });
        await signIn(page, ui);
        const stored = await json(client, '/api/v1/in-use?search=OR-DISPLAY-');
        assert.equal(stored.total, 2);
        const record = stored.items.find(r => r.source.code === 'OR-DISPLAY-4');
        assert.equal(record.source.synthetic, true);
        if (after && record.draft) await json(client, `/api/v1/in-use/${record.id}/draft`, { method: 'PUT', data: {
            expectedVersion: record.version, sourceVersion: record.sourceVersion, notes: '',
            answers: record.source.servers.flatMap(s => ['InternetOut', 'InternetIn', 'Microsegmented'].map(check => ({ serverId: s.id, check, value: 'Unknown', evidence: '' })))
        } });
        await navigate(page, ui, 'in-use');
        await page.waitForFunction(() => document.querySelector('#resource-guide-replay-inuse-list')?.disabled === false);
        await page.getByLabel('In Use kayıt ara', { exact: true }).fill('OR-DISPLAY-');
        await page.getByLabel('In Use görünüm', { exact: true }).focus();
        await page.waitForFunction(() => document.querySelectorAll('.so-inuse-records li').length === 2);
        await capture(page, out, 'list-light');
        await page.locator(`.so-inuse-records a[href="in-use/${record.id}"]`).last().click();
        await page.getByLabel('Servis öğesi tablosu', { exact: true }).waitFor();
        await page.locator('.so-inuse-assignment summary').click();
        await capture(page, out, 'detail-assignment-light');
        if (after) {
            assert.ok(!(await page.getByLabel('In Use inceleyicisi', { exact: true }).innerText()).includes('oidc:'));
            assert.ok((await page.getByLabel('In Use inceleyicisi', { exact: true }).innerText()).includes('Deniz Örnek'));
            assert.match(await page.locator('.so-inuse-evidence').first().innerText(), /Gözlem & Yönetim/);
            assert.match(await page.getByLabel('Servis öğesi tablosu', { exact: true }).innerText(), /Görüntü & Kontrol/);
            assert.match(await page.getByLabel('Servis öğesi tablosu', { exact: true }).innerText(), /Sorgulanmadı/);
            assert.equal(await page.locator('.so-inuse script').count(), 0);
            await page.getByLabel('In Use inceleyicisi', { exact: true }).selectOption('');
            await page.getByLabel('Atama gerekçesi', { exact: true }).fill('Synthetic optional review');
            await page.getByRole('button', { name: 'Atamayı kaydet', exact: true }).click();
            await page.getByText('Yerel atama kaydedildi.', { exact: true }).waitFor();
            const reviewer = await apiContext(request, api, 'team-lead');
            try {
                for (const route of ['/assignees', `/${record.id}`]) assert.equal((await reviewer.get('/api/v1/in-use' + route)).status(), 403);
                assert.equal((await reviewer.put(`/api/v1/in-use/${record.id}/assignment`, { data: { expectedVersion: record.version, assigneeId: null, reason: 'Denied' } })).status(), 403);
            } finally { await reviewer.dispose(); }
            for (const check of ['InternetOut', 'InternetIn', 'Microsegmented']) await page.getByLabel(`7001 ${check}`, { exact: true }).selectOption('No');
            await page.locator('.so-inuse-bulk summary').click();
            for (const box of await page.locator('.so-inuse-bulk input[type=checkbox]').all()) await box.check();
            await page.getByRole('button', { name: 'Değişiklikleri göster', exact: true }).click();
            await page.waitForFunction(() => document.querySelectorAll('.so-inuse-bulk li').length === 9);
            await capture(page, out, 'bulk-differences');
            await page.getByRole('button', { name: 'Gösterilen değişiklikleri onayla', exact: true }).click();
            await page.getByLabel('Yanıtlanacak sunucu', { exact: true }).selectOption('7002');
            await page.getByLabel('7002 InternetOut', { exact: true }).selectOption('Unknown');
            await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
            await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor();
            await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
            await page.getByLabel('Excel sayfası', { exact: true }).waitFor();
            await page.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).click();
            await page.getByRole('button', { name: 'Alana git', exact: true }).waitFor();
            assert.match(await page.getByRole('alert').last().innerText(), /synthetic-display-2 \(7002\).*Sunucudan internete erişim/);
            await page.waitForFunction(() => document.activeElement?.getAttribute('aria-label') === '7002 InternetOut');
            await capture(page, out, 'missing-answer');
            await page.getByLabel('7002 InternetOut', { exact: true }).selectOption('Yes');
            await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
            await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor();
            await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
            await page.getByLabel('Excel sayfası', { exact: true }).waitFor();
            assert.match(await page.getByLabel('Excel hücre önizlemesi', { exact: true }).innerText(), /Görüntü & Kontrol/);
            const downloadEvent = page.waitForEvent('download');
            await page.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).click();
            const download = await downloadEvent; await download.saveAs(path.join(out, download.suggestedFilename()));
            await capture(page, out, 'preview-archive');
            const saved = await json(client, `/api/v1/in-use/${record.id}`);
            assert.deepEqual(saved.source, record.source); assert.equal(saved.sourceHash, record.sourceHash);
            assert.equal(saved.assigneeId, null); assert.equal(saved.draft.answers.filter(a => a.value === 'Yes').length, 1);
        }
        await page.evaluate(() => localStorage.setItem('wasas.appearance', 'dark'));
        await navigate(page, ui, `in-use/${record.id}`); await page.getByLabel('Servis öğesi tablosu', { exact: true }).waitFor();
        await capture(page, out, 'detail-dark');
        const baseline = await json(client, `/api/v1/in-use/${record.id}`);
        const opener = page.locator('#resource-guide-replay-inuse-review'); await opener.focus(); await opener.press('Enter');
        const guide = page.getByRole('dialog', { name: 'Kullanım Rehberi', exact: true }); await guide.waitFor();
        await guide.press('ArrowRight'); await guide.press('ArrowLeft'); await guide.press('Escape');
        await page.waitForFunction(() => document.activeElement?.id === 'resource-guide-replay-inuse-review');
        assert.deepEqual(await json(client, `/api/v1/in-use/${record.id}`), baseline);
        assert.deepEqual(errors, []); assert.deepEqual(dialogs, []);
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ status: 'passed', after, errors, dialogs, corporate: false, vdi: false }));
    } finally { await client.dispose(); await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
