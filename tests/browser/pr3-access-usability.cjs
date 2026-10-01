// Combined API/UI acceptance: loopback, persisted local SQL and synthetic Demo users only.
// node pr3-access-usability.cjs <playwright path> <UI URL> <API URL> <private output>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]), out = path.resolve(process.argv[5]);
(async () => {
    fs.mkdirSync(out, { recursive: false });
    const context = await chromium.launchPersistentContext(path.join(out, 'private-browser-profile'), {
        executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true,
        ignoreHTTPSErrors: true, reducedMotion: 'reduce', viewport: null, args: ['--window-size=1440,900']
    });
    const page = await context.newPage(), errors = [], checks = [], measurements = [];
    const cdp = await context.newCDPSession(page);
    page.on('pageerror', e => errors.push(e.message));
    page.on('dialog', dialog => dialog.accept());
    const denied = await apiContext(request, api, 'team-lead');
    async function ready() { await page.locator('.so-loading').last().waitFor({ state: 'hidden' }); }
    async function theme(dark) {
        await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
        await page.locator('.so-user-menu-popover .mud-list-item').filter({ hasText: dark ? /^Koyu/ : /^Aydınlık/ }).click();
        await page.waitForFunction(value => document.documentElement.classList.contains('so-dark') === value, dark);
    }
    async function measure(route, name, zoom) {
        await page.evaluate(() => window.scrollTo(0, 0));
        const metric = await page.evaluate(() => ({ inner: innerWidth, outer: outerWidth, dpr: devicePixelRatio,
            scale: visualViewport.scale, overflow: document.documentElement.scrollWidth > innerWidth + 1 }));
        assert.equal(metric.overflow, false, route + ': overflow');
        if (zoom === 2) { assert.equal(metric.dpr, 2); assert.equal(metric.scale, 1); assert.ok(metric.inner < 750); }
        await page.keyboard.press('Tab');
        assert.equal(await page.evaluate(() => document.activeElement === document.body), false);
        measurements.push({ route, name, zoom, ...metric });
        if (zoom === 2) {
            // Native zoom requires physical metrics; CSS-sized full-page screenshots crop the right half.
            const layout = await cdp.send('Page.getLayoutMetrics');
            const screenshot = await cdp.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true,
                clip: { ...layout.contentSize, scale: 1 } });
            const bytes = Buffer.from(screenshot.data, 'base64');
            assert.ok(bytes.readUInt32BE(16) >= metric.outer - 40, 'Capture must include physical width');
            fs.writeFileSync(path.join(out, name + '.png'), bytes);
        } else {
            await page.screenshot({ path: path.join(out, name + '.png'), fullPage: true, animations: 'disabled' });
        }
    }
    try {
        await page.goto(new URL('login', ui).href);
        assert.equal(await page.locator('.so-auth-flights').evaluate(svg => svg.animationsPaused()), true);
        assert.equal(await page.locator('.so-auth-map').count(), 1);
        await page.screenshot({ path: path.join(out, 'login-reduced-motion.png'), fullPage: true });
        await signIn(page, ui);
        const routes = ['access/users', 'access/requests', 'access/roles', 'access/me', 'admin/system-status', 'dashboard'];
        for (const route of routes) {
            await navigate(page, ui, route); await ready();
            if (route === 'access/roles') {
                assert.equal(await page.locator('#role-name').isDisabled(), true, 'Protected initial role cannot be edited');
                await page.getByRole('button', { name: 'Yeni rol', exact: true }).click();
                await page.locator('#role-name').fill('Synthetic keyboard review');
                await page.locator('#role-purpose').fill('Synthetic keyboard review only, no apply');
                const checkbox = page.locator('.module-action input[type=checkbox]').first();
                await checkbox.focus(); await page.keyboard.press('Space');
                assert.equal(await checkbox.isChecked(), true);
                await page.getByRole('button', { name: 'Etkiyi incele', exact: true }).click();
                await page.getByRole('status', { name: 'Yetki değişikliği önizlemesi' }).waitFor();
                assert.equal(await page.getByRole('status', { name: 'Yetki değişikliği önizlemesi' }).evaluate(el => el === document.activeElement), true);
                assert.equal(await checkbox.isDisabled(), true, 'Reviewed proposal is locked');
                await page.getByRole('button', { name: 'Düzelt', exact: true }).click();
                checks.push('Keyboard toggle, authoritative preview focus and locked proposal, no apply');
            }
            if (route === 'admin/system-status') {
                const cards = page.locator('.so-integration');
                assert.equal(await cards.count(), 3);
                assert.ok((await cards.allTextContents()).some(text => text.includes('sınanmadı')));
                assert.equal(await page.locator('.so-integration--configured .so-status-badge--positive').count(), 0);
                checks.push('Configured is untested, no invented SQL/audit/Worker health');
            }
            for (const dark of [false, true]) {
                await theme(dark);
                for (const [width, height] of [[1440, 900], [390, 844]]) {
                    await page.setViewportSize({ width, height });
                    if (width === 390) await page.waitForFunction(() => document.querySelector('.so-drawer')?.getBoundingClientRect().right <= 1);
                    await measure(route, route.replaceAll('/', '-') + '-' + (dark ? 'dark' : 'light') + '-' + width, 1);
                }
                await page.setViewportSize({ width: 1440, height: 900 });
            }
            checks.push(route + ': desktop/mobile, both themes, keyboard focus');
        }
        await json(denied, '/api/v1/access/roles/preview', { method: 'POST', data: {
            code: 'DeniedPr3', name: 'Denied', purpose: 'Denied', expectedVersion: 0, capabilities: []
        }, expectedStatus: 403 });
        checks.push('Normal persisted API rejects unauthorized role preview');
        await page.goto('chrome://settings/appearance');
        const zoomResult = await page.evaluate(() => new Promise(resolve =>
            chrome.settingsPrivate.setDefaultZoom(2, () => resolve({ error: chrome.runtime.lastError?.message ?? null }))));
        assert.equal(zoomResult.error, null);
        await cdp.send('Emulation.clearDeviceMetricsOverride');
        for (const route of routes) {
            await navigate(page, ui, route); await ready();
            for (const dark of [false, true]) {
                await theme(dark);
                await measure(route, 'zoom200-' + route.replaceAll('/', '-') + '-' + (dark ? 'dark' : 'light'), 2);
            }
        }
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: true, synthetic: true,
            zoomMethod: 'Chrome settingsPrivate.setDefaultZoom(2), not DPR emulation', checks, measurements, errors }, null, 2));
    } catch (error) {
        fs.writeFileSync(path.join(out, 'failure.html'), await page.content());
        await page.screenshot({ path: path.join(out, 'failure.png'), fullPage: true });
        throw error;
    } finally { await denied.dispose(); await context.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
