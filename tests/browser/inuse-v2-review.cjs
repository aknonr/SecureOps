const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
(async () => {
    fs.mkdirSync(out, { recursive: true });
    const client = await apiContext(request, api);
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 } });
    await context.route('**/*', route => ['localhost', '127.0.0.1'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort());
    const page = await context.newPage(), observations = [], checks = ['InternetOut', 'InternetIn', 'Microsegmented'];
    const answer = (server, check) => page.locator(`[data-answer-server="${server}"][data-answer-check="${check}"]`);
    const read = id => json(client, '/api/v1/in-use/' + id);
    const saved = async () => { await page.getByRole('button', { name: 'Cevapları kaydet', exact: true }).click(); await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor(); };
    try {
        const records = (await json(client, '/api/v1/in-use?search=OR-90000')).items;
        let first = records.find(r => r.source.code === 'OR-900001'), second = records.find(r => r.source.code === 'OR-900002');
        assert.equal(first.source.synthetic, true); assert.equal(first.draft, null); assert.equal(second.draft, null);
        await signIn(page, ui);
        await navigate(page, ui, 'in-use/' + first.id);
        await answer('1200001', 'InternetOut').waitFor();
        await page.screenshot({ path: path.join(out, 'before-answers.png'), fullPage: true });
        for (const check of checks) await answer('1200001', check).selectOption('No');
        await page.locator('.so-inuse-bulk input[type=checkbox]').check();
        await page.getByRole('button', { name: 'Değişiklikleri göster', exact: true }).click();
        await page.waitForFunction(() => document.querySelectorAll('.so-inuse-bulk li').length === 3);
        assert.equal(await page.locator('.so-inuse-bulk li').count(), 3);
        await page.getByRole('button', { name: 'Gösterilen değişiklikleri onayla', exact: true }).click();
        await page.getByRole('button', { name: 'Vazgeç', exact: true }).click();
        assert.equal((await read(first.id)).draft, null);
        await page.getByRole('button', { name: 'Gösterilen değişiklikleri onayla', exact: true }).click();
        await page.getByRole('button', { name: 'Gösterilen değişiklikleri uygula', exact: true }).click();
        await page.getByLabel('Cevap görünümü', { exact: true }).selectOption('all');
        await answer('1200002', 'InternetIn').selectOption('Yes');
        await page.locator('.inuse-policy input[type=checkbox]').check();
        await saved();
        first = await read(first.id);
        assert.deepEqual(first.draft.reviewedServers, first.source.servers);
        assert.equal(first.draft.answers.filter(a => a.origin?.kind === 'Bulk').length, 2);
        assert.equal(first.draft.answers.find(a => a.serverId === '1200002' && a.check === 'InternetIn').origin.kind, 'Individual');
        await navigate(page, ui, 'in-use/' + first.id);
        await page.getByLabel('Cevap görünümü', { exact: true }).selectOption('changed');
        await page.waitForFunction(() => document.querySelectorAll('[data-answer-check=InternetOut]').length === 0);
        assert.equal(await page.locator('[data-answer-check=InternetOut]').count(), 0);
        await page.getByLabel('Cevap görünümü', { exact: true }).selectOption('all');
        assert.equal(await answer('1200002', 'InternetIn').inputValue(), 'Yes');
        assert.equal(await answer('1200002', 'InternetOut').inputValue(), 'No');
        await page.getByRole('button', { name: 'Excel önizleme', exact: true }).click();
        await page.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).waitFor();
        await page.evaluate(() => {
            const download = window.secureOpsDownload;
            window.failInUseDownload = true;
            // Blazor caches the function reference; switch the fault inside the same wrapper.
            window.secureOpsDownload = (...args) => { if (window.failInUseDownload) throw new Error('Synthetic download failure after archive'); return download(...args); };
        });
        await page.getByRole('button', { name: "WASAS'a arşivle ve indir", exact: true }).click();
        await page.locator('.so-problem').waitFor();
        const afterFailedDownload = await read(first.id);
        assert.deepEqual(afterFailedDownload.archivedVersions, [first.version]);
        await page.evaluate(() => { window.failInUseDownload = false; });
        const downloadPromise = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Arşivi indir', exact: true }).click();
        const download = await downloadPromise;
        const downloaded = await download.path();
        const report = await json(client, `/api/v1/in-use/${first.id}/report`, { method: 'POST', data: { expectedVersion: first.version, archivedVersion: first.version } });
        assert.equal(crypto.createHash('sha256').update(fs.readFileSync(downloaded)).digest('hex').toUpperCase(), report.sha256);
        assert.deepEqual(report.sheets.find(s => s.name === 'NMS').rows[1].slice(18), ['Hayır', 'Hayır', 'Evet', 'Hayır']);
        assert.equal(report.sheets.find(s => s.name === 'NMS').rows[1][6], '[Genel]');
        assert.equal(report.sheets.find(s => s.name === 'NMS').rows[1][2], '000123456789012345678901');
        await page.getByRole('button', { name: 'Raporu ekle ve WASAS adımını onayla', exact: true }).click();
        await page.getByRole('button', { name: 'Bu talep ve sürüm için onaylıyorum', exact: true }).click();
        let execution;
        for (let i = 0; i < 30; i++) {
            execution = await json(client, `/api/v1/in-use/${first.id}/execution`);
            if (execution.operation?.state === 'Unconfirmed') break;
            await new Promise(resolve => setTimeout(resolve, 500));
        }
        assert.equal(execution.operation?.state, 'Unconfirmed', JSON.stringify(execution));
        assert.equal(execution.operation.verificationMode, 'WasasActivityManual');
        assert.equal(execution.operation.evidence.some(e => e.step === 'Closure'), false);
        assert.equal(execution.operation.reportSha256, report.sha256);
        await page.getByRole('button', { name: 'İşlem durumunu yenile', exact: true }).click();
        await page.getByText(/WASAS onay isteği iletildi/).waitFor();
        assert.equal(await page.getByText(/OR kapandı/).count(), 0);
        await page.screenshot({ path: path.join(out, 'synthetic-activity-acknowledged.png'), fullPage: true });
        await page.getByRole('button', { name: 'Kaynakta kontrol ettim', exact: true }).click();
        await page.getByRole('button', { name: 'Kontrol ettim, manuel onayı kaydet', exact: true }).click();
        await page.getByText(/Manuel doğrulama: operatör WASAS adımının tamamlandığını bildirdi/).waitFor();
        execution = await json(client, `/api/v1/in-use/${first.id}/execution`);
        assert.equal(execution.operation.state, 'Unconfirmed');
        assert.equal(execution.operation.evidence.at(-1).code, 'OperatorAttestedWasasActivityCompleted');
        assert.equal((await read(first.id)).trackingOnly, false, 'Manual attestation is not verified activity completion');
        const sameReport = await json(client, `/api/v1/in-use/${first.id}/report`, { method: 'POST', data: { expectedVersion: first.version, archivedVersion: first.version } });
        assert.equal(sameReport.content, report.content);
        await navigate(page, ui, 'in-use/' + second.id);
        await answer('1200001', 'InternetOut').waitFor();
        await page.locator('.inuse-answer-table tr').filter({ has: answer('1200001', 'InternetOut') }).getByRole('button', { name: 'İnceleme geçmişi', exact: true }).click();
        await page.locator('.inuse-history article').waitFor();
        assert.match(await page.locator('.inuse-history').innerText(), /OR-900001/);
        for (const box of await page.locator('.inuse-history article input[type=checkbox]').all()) await box.check();
        await page.getByRole('button', { name: 'Seçili önceki cevapları al', exact: true }).click();
        await page.getByRole('button', { name: 'Seçili cevapları al', exact: true }).click();
        await saved();
        second = await read(second.id);
        assert.equal(second.draft.answers.filter(a => a.origin?.kind === 'PreviousReview').length, 3);
        assert.equal(second.draft.answers.filter(a => a.serverId === '1200003' && a.value === 'Unknown').length, 3);
        await navigate(page, ui, 'in-use/' + second.id);
        await page.getByLabel('Cevap görünümü', { exact: true }).selectOption('all');
        assert.equal(await answer('1200001', 'InternetOut').inputValue(), 'No');
        for (const theme of ['light', 'dark']) {
            await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
            await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: theme === 'dark' ? 'Koyu' : 'Aydınlık' }).click();
            await page.waitForFunction(dark => document.documentElement.classList.contains('so-night') === dark, theme === 'dark');
            for (const width of [1366, 1440, 390, 683]) {
                await page.setViewportSize({ width, height: width <= 683 ? 844 : 768 });
                if (width <= 683) await page.waitForFunction(() => document.querySelector('.so-drawer')?.getBoundingClientRect().right <= 1);
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
                await answer('1200003', 'InternetOut').focus(); await page.keyboard.press('Tab');
                assert.equal(await page.evaluate(() => document.activeElement?.tagName), 'SELECT');
                await page.screenshot({ path: path.join(out, `reuse-${theme}-${width}.png`), fullPage: true });
                observations.push({ theme, width, zoom: 1, reflowOnly: width === 683, bodyFont: await page.locator('.so-inuse').evaluate(e => getComputedStyle(e).fontSize), overflow: false });
            }
            await page.setViewportSize({ width: 1366, height: 768 });
        }
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, corporate: false, first: first.id, second: second.id,
            reportSha256: report.sha256, operation: execution.operation, observations,
            interactions: { firstOrIndividualAnswerEdits: 4, bulkCopiedAnswers: 3, overriddenCopies: 1, saves: 1, finalExternalConfirmations: 1,
                secondOrPreviousAnswerSelections: 3, secondOrRepeatedAnswerEdits: 0, secondOrReuseConfirmations: 1, secondOrSaves: 1 },
            native200PercentZoom: 'Not measured; 683px check is explicitly reflow simulation, not native zoom.' }, null, 2));
    } catch (error) { await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }); throw error; }
    finally { await client.dispose(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
