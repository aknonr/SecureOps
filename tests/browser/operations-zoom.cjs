const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
(async () => {
    fs.mkdirSync(out, { recursive: false });
    const context = await chromium.launchPersistentContext(path.join(out, 'private-browser-profile'), {
        executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true,
        ignoreHTTPSErrors: true, viewport: null, reducedMotion: 'reduce', args: ['--window-size=1366,768']
    });
    const client = await apiContext(request, api), page = await context.newPage(), measurements = [];
    try {
        // Set native browser zoom in this disposable profile, not CSS zoom or CDP pinch emulation.
        await page.goto('chrome://settings/appearance');
        const error = await page.evaluate(() => new Promise(resolve =>
            chrome.settingsPrivate.setDefaultZoom(2, () => resolve(chrome.runtime.lastError?.message ?? null))));
        assert.equal(error, null);
        await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
        await signIn(page, ui);
        const record = (await json(client, '/api/v1/in-use?search=OR-DEMO-INUSE-01')).items[0];
        const routes = ['announcements', 'in-use', 'in-use/' + record.id, 'access/users', 'access/requests', 'access/roles', 'access/me'];
        for (const route of routes) {
            await navigate(page, ui, route);
            await page.locator('.so-loading').last().waitFor({ state: 'hidden' });
            if (route === 'announcements') {
                await page.getByRole('button', { name: /^Düzenle: OCO-SYNTHETIC/ }).first().click();
                await page.locator('#announcement-Subject').waitFor();
                await page.getByRole('button', { name: 'Elle düzenlemeye geç', exact: true }).click();
            }
            for (const theme of ['light', 'dark']) {
                await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
                await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: theme === 'dark' ? /^Koyu/ : /^Aydınlık/ }).click();
                await page.waitForFunction(t => document.documentElement.classList.contains('so-dark') === (t === 'dark'), theme);
                await page.evaluate(() => window.scrollTo(0, 0));
                const metric = await page.evaluate(() => ({ inner: innerWidth, outer: outerWidth, dpr: devicePixelRatio,
                    scale: visualViewport.scale, overflow: document.documentElement.scrollWidth > innerWidth + 1,
                    font: getComputedStyle(document.querySelector('.so-page')).fontSize }));
                assert.equal(metric.dpr, 2); assert.equal(metric.scale, 1); assert.equal(metric.outer, 1366);
                assert.ok(metric.inner < 700); assert.equal(metric.overflow, false);
                await page.keyboard.press('Tab');
                assert.equal(await page.evaluate(() => document.activeElement === document.body), false);
                measurements.push({ route, theme, zoom: 2, method: 'native Chrome default zoom, isolated profile', ...metric });
                await page.screenshot({ path: path.join(out, route.replaceAll('/', '-') + '-' + theme + '.png'), fullPage: true });
            }
        }
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, measurements }, null, 2));
    } catch (error) { await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }); throw error; }
    finally { await client.dispose(); await context.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
