const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { chromium, request } = require(process.argv[2]);
const { expect } = require(path.join(process.argv[2], 'test'));
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
(async () => {
    fs.mkdirSync(out, { recursive: true });
    const client = await apiContext(request, api);
    await json(client, '/api/v1/access/me');
    await json(client, '/api/v1/in-use/refresh', { method: 'POST', data: { commandId: crypto.randomUUID() } });
    let record = (await json(client, '/api/v1/in-use')).items.find(r => r.source.synthetic && r.source.servers.length > 0);
    assert.ok(record, 'Synthetic record required');
    const checks = ['InternetOut', 'InternetIn', 'Microsegmented'];
    record = await json(client, `/api/v1/in-use/${record.id}/draft`, { method: 'PUT', data: {
        expectedVersion: record.version, sourceVersion: record.sourceVersion, notes: 'Synthetic repair review',
        answers: record.source.servers.flatMap(s => checks.map(check => ({ serverId: s.id, check, value: 'No', evidence: '' })))
    } });
    const report = await json(client, `/api/v1/in-use/${record.id}/report`, { method: 'POST', data: { expectedVersion: record.version, archive: true } });
    assert.deepEqual(report.sheets.map(s => s.name), ['NMS', 'CheckList_THY', 'CheckList_TEKNIK', 'Sunucular']);
    assert.equal(report.evidenceSheets.length, 2);
    assert.ok(report.fileName.startsWith('InUse_' + record.source.code + '_'));
    const bytes = Buffer.from(report.content, 'base64');
    assert.equal(crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase(), report.sha256);
    fs.writeFileSync(path.join(out, 'synthetic-corporate.xlsx'), bytes);
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 }, hasTouch: true });
    const results = [];
    try {
        await context.route('**/*', route => ['localhost', '127.0.0.1'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort());
        const page = await context.newPage();
        await signIn(page, ui);
        await navigate(page, ui, 'in-use/' + record.id);
        await expect(async () => {
            await page.getByLabel('Cevap görünümü', { exact: true }).selectOption('all');
            assert.ok(await page.locator('[data-answer-check=InternetOut]').count());
        }).toPass({ timeout: 15000 });
        await page.screenshot({ path: path.join(out, 'loaded.png'), fullPage: true });
        fs.writeFileSync(path.join(out, 'loaded.html'), await page.content());
        const answer = page.locator('[data-answer-check=InternetOut]').first();
        await answer.selectOption('Yes');
        for (const width of [1366, 390]) {
            await page.setViewportSize({ width, height: 844 });
            const trigger = page.getByRole('button', { name: 'İnceleyici ata/değiştir', exact: true });
            if (width === 390) await trigger.tap(); else await trigger.click();
            const dialog = page.getByRole('dialog');
            await dialog.waitFor();
            await page.screenshot({ path: path.join(out, `assignment-open-${width}.png`) });
            await page.getByRole('textbox', { name: /Atama gerekçesi/ }).fill('Synthetic dialog cancel');
            assert.equal(await dialog.evaluate(e => e.contains(document.activeElement)), true);
            const box = await dialog.boundingBox();
            assert.ok(box.x >= 0 && box.y >= 0 && box.x + box.width <= width + 1 && box.y + box.height <= 845);
            await page.screenshot({ path: path.join(out, `assignment-${width}.png`) });
            await page.getByRole('button', { name: 'Vazgeç', exact: true }).click();
            await dialog.waitFor({ state: 'hidden' });
            assert.equal(await answer.inputValue(), 'Yes');
            assert.equal((await json(client, `/api/v1/in-use/${record.id}`)).version, record.version);
            results.push({ width, input: width === 390 ? 'touch' : 'mouse', dialogVisible: true, cancelPreservedAnswers: true });
        }
        await page.getByRole('button', { name: 'Kaydedilmemiş değişiklikleri geri al', exact: true }).click();
        await page.getByRole('button', { name: 'Geri al', exact: true }).click();
        await page.getByText('Son kayıtlı taslak geri yüklendi; dış sisteme işlem yapılmadı.', { exact: true }).waitFor();
        assert.equal(await answer.inputValue(), 'No');
        await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
        await page.getByText('İndirme adı (UTC):', { exact: false }).waitFor();
        const archived = await json(client, `/api/v1/in-use/${record.id}/report`, { method: 'POST', data: { expectedVersion: record.version, archivedVersion: record.version } });
        assert.equal(archived.content, report.content);
        assert.equal(archived.fileName, report.fileName);
        await page.screenshot({ path: path.join(out, 'report-preview.png'), fullPage: true });
        for (const [label, action] of [['Kayıtlı cevapları sıfırla', 'Reset'], ['Taslağı kaldır', 'Discard']]) {
            await page.getByRole('button', { name: label, exact: true }).click();
            await page.getByRole('dialog').getByRole('button', { name: label, exact: true }).click();
            await expect(async () => {
                const current = await json(client, `/api/v1/in-use/${record.id}`);
                assert.equal(current.draftLifecycle.action, action);
                assert.equal(current.draft, null);
            }).toPass({ timeout: 15000 });
        }
        await page.reload();
        await page.getByRole('button', { name: 'Yeniden başla', exact: true }).click();
        await page.getByRole('dialog').getByRole('button', { name: 'Yeniden başla', exact: true }).click();
        await expect(async () => {
            const fresh = await json(client, `/api/v1/in-use/${record.id}`);
            assert.equal(fresh.discarded, false);
            assert.equal(fresh.draft, null);
            assert.equal(fresh.draftLifecycle.action, 'Restart');
        }).toPass({ timeout: 15000 });
        const fresh = await json(client, `/api/v1/in-use/${record.id}`);
        const retained = await json(client, `/api/v1/in-use/${record.id}/report`, { method: 'POST', data: { expectedVersion: fresh.version, archivedVersion: report.version } });
        assert.equal(retained.content, report.content);
        await page.screenshot({ path: path.join(out, 'restarted-draft.png'), fullPage: true });
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ recordId: record.id, reportName: report.fileName, sha256: report.sha256, results, undoUnsaved: true, lifecycleRestart: true, unchangedRedownload: true, corporateEffects: false }, null, 2));
    } finally { await context.close(); await browser.close(); await client.dispose(); }
})().catch(error => { console.error(error); process.exit(1); });
