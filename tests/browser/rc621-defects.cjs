const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
(async () => {
    fs.mkdirSync(out, { recursive: false });
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 } });
    await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
    const page = await context.newPage(), client = await apiContext(request, api);
    try {
        await signIn(page, ui);
        const id = crypto.randomUUID(), subject = 'Offset acceptance ' + id;
        await json(client, '/api/v1/announcements/' + id + '?version=0', { method: 'PUT', data: {
            ocoReference: 'OCO-LOCAL', subject, scope: 'Synthetic local test', announcementDate: '2026-09-17',
            workStart: '2026-09-18T08:00:37-03:00', workEnd: '2026-09-18T12:00:59+00:00',
            description: 'Synthetic', impact: 'Synthetic', checks: 'Synthetic', notes: '', to: ['reader@example.invalid'], cc: [],
            bannerRevision: 'bundle-v1', templateRevision: 'oco-table-v2', dateTextRevision: 'tr-v1', affectedServices: ['Synthetic service']
        }});
        await navigate(page, ui, 'announcements');
        if (await page.getByRole('button', { name: 'Şimdi değil', exact: true }).count()) await page.getByRole('button', { name: 'Şimdi değil', exact: true }).click();
        await page.getByRole('button', { name: 'Düzenle: ' + subject, exact: true }).click();
        await page.getByText('Güncel içerik kaydedildi', { exact: true }).waitFor();
        for (const [label, seconds, offset] of [['Çalışma başlangıcı', '37', '-03:00'], ['Çalışma bitişi', '59', '+00:00']]) {
            assert.ok(await page.getByLabel(label + ' saniye', { exact: true }).isVisible());
            assert.equal(await page.getByLabel(label + ' saniye', { exact: true }).inputValue(), seconds);
            assert.equal(await page.getByLabel(label + ' gösterim saat dilimi', { exact: true }).inputValue(), offset);
        }
        const sourceState = process.argv[6] === 'source-ready' ? 'İş kuyruğu hazır; kaynak bağlantısı henüz doğrulanmadı' : 'Kaynak toplama kapalı';
        await page.getByText(sourceState, { exact: true }).waitFor();
        await page.locator('#announcement-Subject').fill(subject + ' unsaved');
        await page.getByRole('button', { name: 'Durumu yenile', exact: true }).click();
        await page.getByText(sourceState, { exact: true }).waitFor();
        assert.equal(await page.locator('#announcement-Subject').inputValue(), subject + ' unsaved');
        await page.screenshot({ path: path.join(out, 'source-disabled-offsets-1366.png'), fullPage: true });
        // A separate page avoids discarding or silently saving the intentionally unsaved draft.
        const review = await context.newPage();
        await json(client, '/api/v1/in-use/refresh', { method: 'POST', data: { commandId: crypto.randomUUID() } });
        let record = (await json(client, '/api/v1/in-use?search=OR-DEMO-INUSE-01')).items[0];
        const previousArchives = (await json(client, '/api/v1/in-use/' + record.id)).archivedVersions;
        const answers = record.source.servers.flatMap(s => ['InternetOut', 'InternetIn', 'Microsegmented'].map(check => ({ serverId: s.id, check, value: 'No', evidence: 'Synthetic explicit answer' })));
        record = await json(client, '/api/v1/in-use/' + record.id + '/draft', { method: 'PUT', data: { expectedVersion: record.version, sourceVersion: record.sourceVersion, answers, notes: 'Local browser acceptance' } });
        await navigate(review, ui, 'in-use/' + record.id);
        await review.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
        await review.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).waitFor();
        await review.evaluate(() => { const original = window.secureOpsDownload; window.syntheticDownloadFailure = true;
            window.secureOpsDownload = (...args) => { if (window.syntheticDownloadFailure) throw new Error('Synthetic download failure'); return original(...args); }; });
        await review.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).click();
        await review.getByText('Arşiv hazır, indirme başlatılamadı', { exact: true }).waitFor();
        const archived = await json(client, '/api/v1/in-use/' + record.id);
        const expectedArchives = [record.version, ...previousArchives];
        assert.deepEqual(archived.archivedVersions, expectedArchives);
        const retained = await json(client, '/api/v1/in-use/' + record.id + '/report', { method: 'POST', data: { expectedVersion: record.version, archivedVersion: record.version } });
        await review.screenshot({ path: path.join(out, 'archive-download-failure-1366.png'), fullPage: true });
        await review.evaluate(() => { window.syntheticDownloadFailure = false; });
        const download = review.waitForEvent('download');
        await review.getByRole('button', { name: 'Arşivi indir', exact: true }).click();
        const file = path.join(out, 'retained.xlsx'); await (await download).saveAs(file);
        assert.equal(crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase(), retained.sha256);
        await navigate(review, ui, 'in-use/' + record.id);
        await review.getByText('Önceki WASAS raporları', { exact: true }).click();
        const repeat = review.waitForEvent('download');
        await review.getByRole('button', { name: 'Sürüm ' + record.version + ' (arşiv)', exact: true }).click();
        await (await repeat).saveAs(path.join(out, 'reloaded.xlsx'));
        assert.deepEqual(fs.readFileSync(file), fs.readFileSync(path.join(out, 'reloaded.xlsx')));
        assert.deepEqual((await json(client, '/api/v1/in-use/' + record.id)).archivedVersions, expectedArchives);
        await navigate(review, ui, 'access/me');
        await review.getByText('Kaynak inceleme', { exact: true }).waitFor();
        assert.equal(await review.getByText('Bu yetki için açıklama tanımlı değil.', { exact: true }).count(), 0);
        await review.screenshot({ path: path.join(out, 'access-labels-1366.png'), fullPage: true });
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, archivedVersion: record.version, archiveSha256: retained.sha256,
            checks: ['visible seconds/offsets', 'disabled source retains manual input', 'archive survives failed download', 'same bytes after reload', 'capability labels'], corporateCalls: false, smtp: false }, null, 2));
    } catch (error) { await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }); throw error; }
    finally { await client.dispose(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
