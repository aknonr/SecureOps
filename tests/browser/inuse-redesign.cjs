// In Use redesign journey (IU-UI): simplified list, status strip, closed/completed page, guide and tour. Synthetic data only; loopback hosts only.
// node inuse-redesign.cjs <playwright path> <UI URL> <API URL> <evidence directory>
// Preconditions: Demo API with Access__DemoCompatibilityEnabled=true and OperationalRecords__SourceProvider=Fake (local synthetic source, two records),
// UI on HTTPS with DemoMode__ApiDemoActor=platform-admin, WASAS_CHROME set (or Chrome in its default path).
// Proves: finished records are not on the default list and the closed page says honestly when the product knows none (the local synthetic source never
// completes a record), one badge and one next step per row with detail inside a disclosure, no horizontal overflow at 1440 / 390 px / 200 % zoom
// (720x450) in light and dark, the guide opens by request only, the tour moves with the keyboard, closes with Esc, never changes stored records
// and tells the truth about the completion gate on the record page. Not proven: screen-reader speech, Windows/IIS, corporate TEST, finished records
// (the synthetic source has none; the unit tests cover the wording).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]);
const api = loopback(process.argv[4]);
const out = path.resolve(process.argv[5]);
const results = [];
let current;

async function step(name, action) {
    const started = Date.now();
    try { await action(); results.push({ name, result: 'passed', seconds: (Date.now() - started) / 1000 }); }
    catch (error) {
        results.push({ name, result: 'failed', error: String(error.message || error).slice(0, 600) });
        await current?.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }).catch(() => {});
        throw error;
    }
}

const noOverflow = page => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1);
const shot = (page, name) => page.screenshot({ path: path.join(out, name + '.png'), fullPage: true, animations: 'disabled' });

async function tour(page, startId, expectedSteps) {
    const start = page.locator('#' + startId + '-start');
    await start.focus();
    await start.press('Enter');
    const box = page.locator('[data-tour-box]');
    await box.waitFor();
    for (let index = 1; index < expectedSteps; index++) {
        await page.waitForFunction(() => document.activeElement?.hasAttribute('data-tour-focus'));
        await box.getByRole('button', { name: 'İleri', exact: true }).press('Enter');
    }
    await box.press('Escape');
    await box.waitFor({ state: 'hidden' });
    await page.waitForFunction(id => document.activeElement?.id === id, startId + '-start');
}

(async () => {
    setTimeout(() => { console.error('journey watchdog expired'); process.exit(2); }, 10 * 60 * 1000).unref();
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const client = await apiContext(request, api);
    await json(client, '/api/v1/in-use/refresh', { method: 'POST', data: { commandId: crypto.randomUUID() } });
    const stored = await json(client, '/api/v1/in-use?search=OR-DEMO-INUSE');
    assert.ok(stored.total >= 1, 'synthetic In Use records missing');
    const first = stored.items.find(r => r.source.code === 'OR-DEMO-INUSE-01');

    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
    await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
    const page = await context.newPage();
    current = page;
    page.setDefaultTimeout(20000);
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await signIn(page, ui);

    try {
        await step('list: status strip, one badge and one next step per row, detail inside a disclosure', async () => {
            await navigate(page, ui, 'in-use');
            await page.getByRole('navigation', { name: 'In Use durum şeridi' }).waitFor();
            const rows = page.locator('.so-inuse-records > li');
            await rows.first().waitFor();
            const row = rows.first();
            assert.equal(await row.locator('.so-badge').count(), 1, 'one badge per row');
            assert.equal(await row.locator('.iur-next').count(), 1, 'one next step per row');
            assert.equal(await row.locator('details').evaluate(d => d.open), false, 'detail starts closed');
            assert.equal(await page.locator('.so-badge--critical').count(), 0, 'no red badge on a list row');
            await page.getByRole('link', { name: 'Ekibimizden çıkan / kapanan' }).first().waitFor();
            await shot(page, 'list-1440-light');
        });

        await step('closed page: honest empty state, nothing is called done without evidence', async () => {
            await page.getByRole('link', { name: 'Ekibimizden çıkan / kapanan' }).first().click();
            await page.waitForURL(/\/in-use\/closed/);
            await page.getByRole('heading', { name: 'Ekibimizden çıkan / kapanan kayıtlar' }).waitFor();
            await page.getByText('Bu liste nasıl belirlenir?').waitFor();
            const body = await page.locator('.so-inuse-closed').innerText();
            assert.match(body, /hiçbir kaydı tamamlanmış bilmiyor|kayıt · Son başarılı kaynak okuması/);
            assert.doesNotMatch(body, /OR kapandı/);
            await shot(page, 'closed-1440-light');
            await navigate(page, ui, 'in-use?view=tracking');
            await page.waitForURL(/\/in-use\/closed/);
        });

        await step('guide opens only on request and the tour moves with the keyboard without touching records', async () => {
            await navigate(page, ui, 'in-use');
            assert.equal(await page.locator('.iug-body').count(), 0, 'guide must not open by itself');
            assert.equal(await page.locator('[data-tour-box]').count(), 0, 'tour must not start by itself');
            const toggle = page.getByRole('button', { name: 'Nasıl kullanılır?' });
            await toggle.press('Enter');
            await page.locator('.iug-body').waitFor();
            assert.equal(await page.locator('.iug-steps > li').count(), 4);
            await toggle.press('Enter');
            const before = await json(client, '/api/v1/in-use?search=OR-DEMO-INUSE');
            await tour(page, 'inuse-list-tour', 6);
            assert.deepEqual(await json(client, '/api/v1/in-use?search=OR-DEMO-INUSE'), before, 'tour must not change stored records');
        });

        if (first) {
            await step('record page: the tour says whether the completion step is open or closed, and why', async () => {
                await navigate(page, ui, `in-use/${first.id}`);
                await page.getByLabel('Servis öğesi tablosu', { exact: true }).waitFor();
                await page.locator('#inuse-record-tour-start').click();
                const box = page.locator('[data-tour-box]');
                await box.waitFor();
                for (let i = 0; i < 5; i++) await box.getByRole('button', { name: 'İleri', exact: true }).press('Enter');
                const text = await box.innerText();
                assert.match(text, /4\. Talebe ekle ve tamamla/);
                assert.match(text, /Şu an kapalı: .+|Sunucu şu an izin veriyor|durum henüz alınamadı/);
                await box.press('Escape');
            });
        }

        await step('1440 / 390 / 200 % zoom, light and dark: no horizontal scroll on the list and the closed page', async () => {
            for (const theme of ['light', 'dark']) {
                await page.evaluate(t => localStorage.setItem('wasas.appearance', t), theme);
                for (const route of ['in-use', 'in-use/closed']) {
                    for (const [width, height] of [[1440, 900], [390, 844], [720, 450]]) {
                        await page.setViewportSize({ width, height });
                        await navigate(page, ui, route);
                        await page.locator('h1').waitFor();
                        assert.equal(await noOverflow(page), true, `${route} ${theme} ${width}px overflows`);
                        if (width !== 720) await shot(page, `${route.replace('/', '-')}-${width}-${theme}`);
                    }
                }
            }
            await page.setViewportSize({ width: 1440, height: 900 });
        });

        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ status: 'passed', results, corporate: false }, null, 2));
    } catch (error) {
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ status: 'failed', results, errors }, null, 2));
        throw error;
    } finally { await client.dispose(); await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
