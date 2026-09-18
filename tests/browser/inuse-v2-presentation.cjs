const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium } = require(process.argv[2]);
const { loopback, navigate, signIn } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), out = path.resolve(process.argv[4]), id = process.argv[5];
(async () => {
    fs.mkdirSync(out, { recursive: true });
    const results = [];
    for (const zoom of [1, 2]) {
        const profile = path.join(out, 'profile-' + zoom);
        assert.equal(fs.existsSync(profile), false, 'Fresh test-owned browser profile required');
        fs.mkdirSync(path.join(profile, 'Default'), { recursive: true });
        // Chromium stores default native zoom per storage partition; this is not CSS zoom or pinch emulation.
        fs.writeFileSync(path.join(profile, 'Default', 'Preferences'), JSON.stringify({ partition: { default_zoom_level: { x: Math.log(zoom) / Math.log(1.2) } } }));
        const context = await chromium.launchPersistentContext(profile, { executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe',
            headless: true, ignoreHTTPSErrors: true, viewport: null, args: ['--window-size=1366,768'] });
        try {
            await context.route('**/*', route => ['localhost', '127.0.0.1'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort());
            const page = await context.newPage();
            await signIn(page, ui); await navigate(page, ui, 'in-use/' + id);
            await page.locator('.inuse-answer-table').waitFor();
            for (const theme of ['light', 'dark']) {
                await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
                await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: theme === 'dark' ? 'Koyu' : 'Aydınlık' }).click();
                await page.waitForFunction(dark => document.documentElement.classList.contains('so-night') === dark, theme === 'dark');
                const metrics = await page.evaluate(() => {
                    const rgb = value => { const n = value.match(/[\d.]+/g)?.map(Number); return n?.length >= 3 ? [n[0], n[1], n[2], n[3] ?? 1] : [0, 0, 0, 0]; };
                    const composite = (a, b) => a.slice(0, 3).map((n, i) => n * a[3] + b[i] * (1 - a[3]));
                    const luminance = c => c.map(n => { const v = n / 255; return v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; }).reduce((n, v, i) => n + v * [.2126, .7152, .0722][i], 0);
                    const contrast = [...document.querySelectorAll('.so-inuse p,.so-inuse small,.so-inuse select,.so-inuse h3,.so-inuse label')]
                        .filter(e => e.getClientRects().length && !e.disabled && (e.textContent || '').trim()).map(e => {
                            const ancestors = []; for (let n = e; n; n = n.parentElement) ancestors.unshift(n);
                            let bg = [255, 255, 255]; for (const n of ancestors) bg = composite(rgb(getComputedStyle(n).backgroundColor), bg);
                            const fg = composite(rgb(getComputedStyle(e).color), bg), a = luminance(fg), b = luminance(bg);
                            return { selector: e.tagName + '.' + e.className, ratio: (Math.max(a, b) + .05) / (Math.min(a, b) + .05) };
                        });
                    return { innerWidth, outerWidth, devicePixelRatio, visualScale: visualViewport.scale, overflow: document.documentElement.scrollWidth > innerWidth + 1,
                        bodyFont: getComputedStyle(document.querySelector('.so-inuse')).fontSize, minimumMeasuredContrast: Math.min(...contrast.map(c => c.ratio)), lowContrast: contrast.filter(c => c.ratio < 4.5) };
                });
                assert.equal(metrics.devicePixelRatio, zoom); assert.equal(metrics.visualScale, 1); assert.equal(metrics.overflow, false);
                assert.equal(metrics.bodyFont, '16px'); assert.ok(metrics.minimumMeasuredContrast >= 4.5, JSON.stringify(metrics.lowContrast));
                await page.locator('[data-answer-check=InternetOut]').first().focus(); await page.keyboard.press('Tab');
                const focus = await page.evaluate(() => { const r = document.activeElement.getBoundingClientRect(); return { tag: document.activeElement.tagName, top: r.top, bottom: r.bottom, height: innerHeight }; });
                assert.equal(focus.tag, 'SELECT'); assert.ok(focus.top >= 0 && focus.bottom <= focus.height);
                await page.screenshot({ path: path.join(out, `${theme}-native-${zoom * 100}-viewport.png`) });
                await page.screenshot({ path: path.join(out, `${theme}-native-${zoom * 100}-full.png`), fullPage: true });
                results.push({ theme, nativeZoom: zoom, metrics, focus });
            }
        } finally { await context.close(); }
    }
    fs.writeFileSync(path.join(out, 'presentation.json'), JSON.stringify({ results, note: 'Measured listed rendered text only; not a WCAG compliance assertion.',
        zoomReference: 'https://raw.githubusercontent.com/chromium/chromium/main/chrome/browser/ui/zoom/chrome_zoom_level_prefs.cc' }, null, 2));
})().catch(error => { console.error(error); process.exitCode = 1; });
