const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { chromium, request } = require(process.argv[2]);
const { expect } = require(path.join(process.argv[2], 'test'));
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
const nativeZoom = process.env.WASAS_NATIVE_ZOOM === '1';
const screenMetrics = page => page.evaluate(() => ({ width: innerWidth, height: innerHeight,
    outerWidth, outerHeight, dpr: devicePixelRatio, scale: visualViewport.scale }));
async function setNativeZoom(page, factor) {
    await page.goto('chrome://settings/appearance');
    const observed = await page.evaluate(factor => new Promise(resolve => {
        chrome.settingsPrivate.setDefaultZoom(factor, () => {
            const error = chrome.runtime.lastError?.message;
            if (error) return resolve({ error });
            chrome.settingsPrivate.getDefaultZoom(value => resolve({ factor: value,
                error: chrome.runtime.lastError?.message ?? null }));
        });
    }), factor);
    assert.equal(observed.error, null);
    assert.equal(observed.factor, factor, 'Chrome must report the requested zoom setting');
    return observed.factor;
}
(async () => {
    assert.equal(fs.existsSync(out), false, 'Use a fresh evidence directory; retain previous runs');
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
    const browser = nativeZoom ? null : await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = nativeZoom ? await chromium.launchPersistentContext(path.join(out, 'private-profile'), {
        executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true,
        ignoreHTTPSErrors: true, viewport: null, hasTouch: true, args: ['--window-size=1366,768'] })
        : await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 }, hasTouch: true });
    const results = [];
    let zoom = null;
    try {
        const page = await context.newPage();
        if (nativeZoom) zoom = { baselineSetting: await setNativeZoom(page, 1) };
        await context.route('**/*', route => ['localhost', '127.0.0.1'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort());
        await signIn(page, ui);
        await navigate(page, ui, 'in-use/' + record.id);
        if (nativeZoom) {
            zoom.baseline = await screenMetrics(page);
            zoom.actualSetting = await setNativeZoom(page, 2);
            await navigate(page, ui, 'in-use/' + record.id);
            zoom.observed = await screenMetrics(page);
            assert.equal(zoom.observed.outerWidth, zoom.baseline.outerWidth);
            assert.ok(Math.abs(zoom.observed.dpr / zoom.baseline.dpr - 2) < 0.01);
            assert.ok(Math.abs(zoom.observed.width * 2 - zoom.baseline.width) <= 24);
            assert.equal(zoom.observed.scale, 1);
        }
        await expect(async () => {
            await page.getByLabel('Cevap görünümü', { exact: true }).selectOption('all');
            assert.ok(await page.locator('[data-answer-check=InternetOut]').count());
        }).toPass({ timeout: 15000 });
        await page.screenshot({ path: path.join(out, 'loaded.png'), fullPage: true });
        fs.writeFileSync(path.join(out, 'loaded.html'), await page.content());
        const answer = page.locator('[data-answer-check=InternetOut]').first();
        await answer.selectOption('Yes');
        for (const theme of ['light', 'dark']) {
            await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
            await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: theme === 'dark' ? /^Koyu/ : /^Aydınlık/ }).click();
        for (const width of nativeZoom ? [0] : [1366, 390]) {
            if (!nativeZoom) await page.setViewportSize({ width, height: 844 });
            const trigger = page.getByRole('button', { name: 'İnceleyici ata/değiştir', exact: true });
            if (width === 390) await trigger.tap(); else await trigger.click();
            const dialog = page.getByRole('dialog');
            await dialog.waitFor();
            await page.screenshot({ path: path.join(out, `assignment-open-${theme}-${width}.png`) });
            await page.getByRole('textbox', { name: /Atama gerekçesi/ }).fill('Synthetic dialog cancel');
            assert.equal(await dialog.evaluate(e => e.contains(document.activeElement)), true);
            const box = await dialog.boundingBox();
            const screen = await screenMetrics(page);
            if (nativeZoom) { assert.equal(screen.dpr, zoom.observed.dpr); assert.equal(screen.scale, 1); }
            assert.ok(box.x >= 0 && box.y >= 0 && box.x + box.width <= screen.width + 1 && box.y + box.height <= screen.height + 1);
            await page.screenshot({ path: path.join(out, `assignment-${theme}-${width}.png`) });
            await page.getByRole('button', { name: 'Vazgeç', exact: true }).click();
            await dialog.waitFor({ state: 'hidden' });
            assert.equal(await trigger.evaluate(e => e === document.activeElement), true, 'Focus returns to assignment trigger');
            await trigger.press('Enter');
            await dialog.waitFor();
            await page.keyboard.press('Escape');
            assert.equal(await dialog.isVisible(), true, 'The existing dialog requires explicit cancellation');
            await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).press('Enter');
            await dialog.waitFor({ state: 'hidden' });
            assert.equal(await answer.inputValue(), 'Yes');
            assert.equal((await json(client, `/api/v1/in-use/${record.id}`)).version, record.version);
            results.push({ theme, width, nativeZoom, screen, input: width === 390 ? 'touch' : 'mouse', keyboard: true,
                explicitKeyboardCancel: true, escapeDoesNotDismiss: true, focusReturn: true, dialogVisible: true, cancelPreservedAnswers: true });
        }
        }
        const candidates = await json(client, '/api/v1/in-use/assignees');
        const candidate = candidates.find(c => c.id !== record.assigneeId);
        assert.ok(candidate, 'The isolated fixture needs an existing eligible reviewer');
        const trigger = page.getByRole('button', { name: 'İnceleyici ata/değiştir', exact: true });
        for (const selected of [candidate, null]) {
            await trigger.click();
            const dialog = page.getByRole('dialog');
            await dialog.waitFor();
            const save = dialog.getByRole('button', { name: 'Atamayı kaydet', exact: true });
            assert.equal(await save.isDisabled(), true, 'Assignment requires a reason');
            await dialog.getByLabel('İnceleyici', { exact: true }).click();
            await page.getByRole('option', { name: selected?.label ?? 'Atanmamış', exact: true }).click();
            assert.equal((await json(client, `/api/v1/in-use/${record.id}`)).version, record.version,
                'Selecting a reviewer must not save');
            await dialog.getByRole('textbox', { name: /Atama gerekçesi/ }).fill('Synthetic explicit assignment acceptance');
            await save.click();
            await dialog.waitFor({ state: 'hidden' });
            await expect(async () => {
                const updated = await json(client, `/api/v1/in-use/${record.id}`);
                assert.equal(updated.assigneeId ?? null, selected?.id ?? null);
                assert.ok(updated.version > record.version);
            }).toPass({ timeout: 15000 });
            record = await json(client, `/api/v1/in-use/${record.id}`);
            assert.equal(await answer.inputValue(), 'Yes', 'Assignment preserves unsaved answers');
            assert.equal(await trigger.evaluate(e => e === document.activeElement), true);
        }
        await page.getByRole('button', { name: 'Kaydedilmemiş değişiklikleri geri al', exact: true }).click();
        await page.getByRole('button', { name: 'Geri al', exact: true }).click();
        await page.getByText('Son kayıtlı taslak geri yüklendi; dış sisteme işlem yapılmadı.', { exact: true }).waitFor();
        assert.equal(await answer.inputValue(), 'No');
        await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
        await page.getByText('İndirme adı (UTC):', { exact: false }).waitFor();
        const archived = await json(client, `/api/v1/in-use/${record.id}/report`, { method: 'POST', data: { expectedVersion: record.version, archivedVersion: report.version } });
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
        await navigate(page, ui, 'in-use/reports');
        await page.getByRole('textbox', { name: 'Rapor ara', exact: true }).fill(record.source.code);
        await page.getByRole('button', { name: 'Ara', exact: true }).click();
        await page.getByText(report.fileName, { exact: true }).waitFor();
        const row = page.getByRole('row').filter({ hasText: report.fileName });
        const downloadPromise = page.waitForEvent('download');
        await row.getByRole('button', { name: 'İndir', exact: true }).click();
        const download = await downloadPromise;
        assert.equal(download.suggestedFilename(), report.fileName);
        assert.equal(crypto.createHash('sha256').update(fs.readFileSync(await download.path())).digest('hex').toUpperCase(), report.sha256);
        await page.screenshot({ path: path.join(out, 'catalogue.png'), fullPage: true });
        const systemStatus = [];
        for (const theme of ['light', 'dark']) {
            await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
            await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: theme === 'dark' ? /^Koyu/ : /^Aydınlık/ }).click();
            for (const width of nativeZoom ? [0] : [1366, 390]) {
                if (!nativeZoom) await page.setViewportSize({ width, height: 844 });
                await navigate(page, ui, 'admin/system-status');
                const panel = page.getByRole('region', { name: 'İş akışı kontrolleri', exact: true });
                await expect(panel).toContainText('Henüz kontrol edilmedi');
                await page.getByRole('button', { name: 'Kurumsal entegrasyonları yenile', exact: true }).click();
                const check = panel.getByRole('button', { name: 'Durumu kontrol et', exact: true });
                await expect(check).toBeEnabled();
                await expect(panel).toContainText('Henüz kontrol edilmedi');
                await check.scrollIntoViewIfNeeded();
                await page.screenshot({ path: path.join(out, `system-status-before-${theme}-${width}.png`), fullPage: true });
                if (width === 390) await check.tap(); else await check.press('Enter');
                await expect(panel).toContainText('Kontrol tamamlandı.');
                await expect(check).toBeFocused();
                const firstDownload = page.waitForEvent('download');
                await panel.getByRole('button', { name: 'Tanılama raporunu indir', exact: true }).click();
                const saved = await firstDownload;
                assert.equal(saved.suggestedFilename(), 'wasas-api-operations.json');
                const reportBytes = fs.readFileSync(await saved.path());
                const statusReport = JSON.parse(reportBytes.toString('utf8'));
                assert.ok(statusReport.CapturedAt && statusReport.Source.CheckedAt);
                const displayedTime = await panel.locator('p').filter({ hasText: 'Son kontrol:' }).innerText();
                assert.ok(displayedTime.includes('UTC (+00:00)'));
                const repeatDownload = page.waitForEvent('download');
                await panel.getByRole('button', { name: 'Tanılama raporunu indir', exact: true }).click();
                assert.deepEqual(fs.readFileSync(await (await repeatDownload).path()), reportBytes);
                assert.equal(await panel.locator('p').filter({ hasText: 'Son kontrol:' }).innerText(), displayedTime);
                const details = panel.locator('summary', { hasText: 'Teknik ayrıntılar' });
                await details.press('Enter');
                assert.equal(await details.evaluate(e => e.parentElement.open), true);
                await details.press('Enter');
                const screen = await screenMetrics(page);
                if (nativeZoom) assert.equal(screen.dpr, zoom.observed.dpr);
                assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
                const box = await panel.boundingBox();
                assert.ok(box.x >= 0 && box.x + box.width <= screen.width + 1);
                await check.scrollIntoViewIfNeeded();
                await page.screenshot({ path: path.join(out, `system-status-after-${theme}-${width}.png`), fullPage: true });
                systemStatus.push({ theme, width, screen, nativeZoom, displayedTime, capture: statusReport.CapturedAt,
                    checked: statusReport.Source.CheckedAt, downloadUnchanged: true, focusPreserved: true });
            }
        }
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ recordId: record.id, reportName: report.fileName, sha256: report.sha256, results, zoom,
            systemStatus,
            explicitAssignmentAndUnassignment: true, undoUnsaved: true, lifecycleRestart: true, unchangedRedownload: true, corporateEffects: false }, null, 2));
    } finally { await context.close(); if (browser) await browser.close(); await client.dispose(); }
})().catch(error => { console.error(error); process.exit(1); });
