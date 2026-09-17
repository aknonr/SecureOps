const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
const code = process.argv[6], resumed = process.argv[7] === 'resumed';
(async () => {
    const start = Date.now(), observations = [];
    fs.mkdirSync(out, { recursive: true });
    const client = await apiContext(request, api);
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
    const page = await context.newPage();
    await context.route('**/*', route => ['localhost', '127.0.0.1'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort());
    try {
        await signIn(page, ui);
        const me = await json(client, '/api/v1/access/me');
        let record = (await json(client, '/api/v1/in-use?search=' + code)).items.find(item => item.source.code === code);
        assert.equal(record?.source.title, 'Synthetic RFC reporter acceptance');
        record = await json(client, '/api/v1/in-use/' + record.id);
        async function list(view, present) {
            await navigate(page, ui, 'in-use?q=' + code + '&view=' + view);
            await page.getByLabel('In Use görünüm', { exact: true }).waitFor();
            await page.getByText((present ? '1' : '0') + ' kayıtlı eşleşme', { exact: true }).waitFor();
            await page.waitForFunction(expected => document.querySelectorAll('.so-inuse-records > li').length === expected, present ? 1 : 0);
            assert.equal(await page.getByLabel('In Use görünüm', { exact: true }).inputValue(), view);
            if (present) assert.match(await page.locator('.inuse-assignee').innerText(), new RegExp(record.assigneeLabel || 'Atanmamış'));
        }
        async function detail() {
            await navigate(page, ui, 'in-use/' + record.id);
            await page.getByRole('region', { name: 'İnceleme durumu', exact: true }).waitFor();
        }
        async function assign(id) {
            await detail();
            await page.getByRole('button', { name: 'İnceleyici ata/değiştir', exact: true }).click();
            await page.getByLabel('In Use inceleyicisi', { exact: true }).selectOption(id || '');
            await page.getByLabel('Atama gerekçesi', { exact: true }).fill('Synthetic assignment visibility acceptance');
            await page.getByRole('button', { name: 'Atamayı kaydet', exact: true }).click();
            await page.getByText('Yerel atama kaydedildi.', { exact: true }).waitFor();
            record = await json(client, '/api/v1/in-use/' + record.id);
            assert.equal(record.assigneeId, id);
            assert.equal(record.assignedBy, me.userId);
            assert.match(await page.getByRole('region', { name: 'İnceleme durumu', exact: true }).innerText(), new RegExp(record.assigneeLabel || 'Atanmamış'));
        }
        if (!resumed) {
            await assign(me.userId);
            await list('mine', true);
            await page.locator('.so-inuse-records a').first().click();
            await page.getByRole('link', { name: 'Listeye dön', exact: true }).click();
            await page.waitForURL(url => url.searchParams.get('view') === 'mine');
            await page.locator('.inuse-assignee').waitFor();
            await list('unassigned', false);
            await detail();
            await page.getByRole('button', { name: 'İnceleyici ata/değiştir', exact: true }).click();
            const other = await page.getByLabel('In Use inceleyicisi', { exact: true }).locator('option').evaluateAll(options => options.find(o => o.textContent.startsWith('Sentetik İnceleyici')).value);
            assert.notEqual(other, me.userId);
            await assign(other);
            await list('mine', false);
            await list('all', true);
            await assign(null);
            await list('unassigned', true);
            await assign(other);
            await page.getByLabel('Yanıtlanacak sunucu', { exact: true }).selectOption(record.source.servers[0].id);
            await page.getByLabel(record.source.servers[0].id + ' InternetOut', { exact: true }).selectOption('Yes');
            await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
            await page.getByText('Yerel inceleme taslağı kaydedildi.', { exact: true }).waitFor();
            record = await json(client, '/api/v1/in-use/' + record.id);
            assert.equal(record.draft.reviewedBy, me.userId);
            assert.notEqual(record.assigneeId, record.draft.reviewedBy);
            const history = await json(client, '/api/v1/operations/InUse/' + record.id + '/events');
            const saved = history.events.find(event => event.action.endsWith('DraftSaved'));
            assert.equal(saved.initiator.id, me.userId);
            assert.equal(saved.assigneeId, other);
            assert.equal(saved.sourceCloser, null);
            assert.equal(saved.schemaVersion, 1);
            fs.writeFileSync(path.join(out, 'checkpoint.json'), JSON.stringify({ record, history }, null, 2));
        } else {
            const saved = JSON.parse(fs.readFileSync(path.join(out, 'checkpoint.json')));
            assert.deepEqual(record, saved.record);
            assert.deepEqual(await json(client, '/api/v1/operations/InUse/' + record.id + '/events'), saved.history);
            await list('mine', false);
            await list('unassigned', false);
            await list('all', true);
        }
        await detail();
        for (const theme of ['light', 'dark']) {
            if (theme === 'dark') {
                await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
                await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: 'Koyu' }).click();
            }
            for (const width of [1440, 1366, 390]) {
                await page.setViewportSize({ width, height: width === 390 ? 844 : width === 1366 ? 768 : 900 });
                await page.evaluate(() => document.fonts.ready);
                if (width === 390) await page.waitForFunction(() => document.querySelector('.so-drawer')?.getBoundingClientRect().right <= 1);
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
                await page.screenshot({ path: path.join(out, `${resumed ? 'restart' : 'assigned'}-${theme}-${width}.png`), fullPage: true });
                observations.push({ theme, width, zoom: 1, overflow: false });
            }
            await page.setViewportSize({ width: 1440, height: 900 });
        }
        fs.writeFileSync(path.join(out, resumed ? 'restart.json' : 'result.json'), JSON.stringify({ passed: true, seconds: (Date.now() - start) / 1000, observations, steps: 'Assign self; mine; retained list context; reassign; unassign; cross-assignee save; immutable event attribution; restart exact state comparison' }, null, 2));
    } catch (error) { await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }); throw error; }
    finally { await client.dispose(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
