// Actual native Chrome 200% zoom in a disposable profile, not CSS reflow emulation.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium } = require(process.argv[2]);
const { loopback, navigate, signIn } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), out = path.resolve(process.argv[4]);
(async () => {
    fs.mkdirSync(out, { recursive: false });
    const context = await chromium.launchPersistentContext(path.join(out, 'private-profile'), {
        executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true,
        ignoreHTTPSErrors: true, viewport: null, args: ['--window-size=1366,768'] });
    const page = await context.newPage(), measurements = [];
    try {
        await page.goto('chrome://settings/appearance');
        assert.equal(await page.evaluate(() => new Promise(resolve => chrome.settingsPrivate.setDefaultZoom(2,
            () => resolve(chrome.runtime.lastError?.message ?? null)))), null);
        await context.route('**/*', r => ['localhost', '127.0.0.1'].includes(new URL(r.request().url()).hostname) ? r.continue() : r.abort());
        await signIn(page, ui); await navigate(page, ui, 'dashboard');
        const panel = page.locator('.workflow-report');
        await panel.getByRole('checkbox').check();
        await panel.getByRole('button', { name: 'Raporu yenile', exact: true }).click();
        await panel.getByText('Hazırlanmış duyuru', { exact: true }).waitFor();
        for (const theme of ['light', 'dark']) {
            await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
            await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: theme === 'dark' ? /^Koyu/ : /^Aydınlık/ }).click();
            await panel.scrollIntoViewIfNeeded();
            const measurement = await panel.evaluate(el => {
                let bg = el;
                while (bg.parentElement && getComputedStyle(bg).backgroundColor === 'rgba(0, 0, 0, 0)') bg = bg.parentElement;
                const rgb = text => text.match(/[\d.]+/g).slice(0, 3).map(Number);
                const luminance = values => values.map(c => c / 255).map(c => c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4)
                    .reduce((n, c, i) => n + c * [.2126, .7152, .0722][i], 0);
                const color = getComputedStyle(el).color, background = getComputedStyle(bg).backgroundColor;
                const a = luminance(rgb(color)), b = luminance(rgb(background));
                return { inner: innerWidth, outer: outerWidth, dpr: devicePixelRatio, scale: visualViewport.scale,
                    overflow: document.documentElement.scrollWidth > innerWidth + 1, font: getComputedStyle(el).fontSize,
                    color, background, contrast: (Math.max(a, b) + .05) / (Math.min(a, b) + .05) };
            });
            assert.equal(measurement.dpr, 2); assert.equal(measurement.scale, 1); assert.ok(measurement.inner < 700);
            assert.equal(measurement.overflow, false); assert.ok(measurement.contrast >= 4.5);
            await panel.getByRole('button', { name: 'Raporu yenile', exact: true }).focus(); await page.keyboard.press('Tab');
            assert.notEqual(await page.evaluate(() => document.activeElement.tagName), 'BODY');
            measurements.push({ theme, zoom: 2, method: 'native Chrome default zoom', ...measurement });
            await page.screenshot({ path: path.join(out, `dashboard-200-${theme}.png`), fullPage: true });
        }
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, measurements,
            limitation: 'Sampled panel body contrast only, not full WCAG compliance certification' }, null, 2));
    } finally { await context.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
