// Service Accounts guided tour journey (REHBER-UI, docs/service-accounts/GUIDED-TOUR-DESIGN.md). Synthetic data only; loopback hosts only.
// node service-accounts-guided-tour.cjs <playwright path> <UI URL> <API URL> <evidence directory>
// Preconditions: as service-accounts-event-flow.cjs (Demo API with Access/Session/ServiceAccounts on a disposable database from
// tests/sql/service-accounts/sa-sql-harness.ps1, demo:platform-admin with the Admin role and an 'All' scope, UI on HTTPS with
// DemoMode__ApiDemoActor=platform-admin, WASAS_CHROME set). What this proves: the tour opens only on request, highlights a real area,
// moves with the keyboard, says when a step's area is missing (an import before any preview), closes with Esc and gives focus back
// to the start button, docks at the bottom at 390 px without horizontal overflow, and works in the dark theme. What it cannot prove:
// screen-reader speech, Windows/IIS, corporate TEST, a caller without a capability (unit-tested).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, capture, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]);
const api = loopback(process.argv[4]);
const out = path.resolve(process.argv[5]);
const suffix = Date.now().toString(36).toUpperCase();
const accountName = `SYN_SVC_TOUR_${suffix}`;
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

const box = page => page.locator('[data-tour-box]');
const focusedTitle = page => page.evaluate(() => document.activeElement?.hasAttribute('data-tour-focus') ? document.activeElement.textContent.trim() : null);
const noHorizontalScroll = page => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1);

// The frame must sit over the element the step names, not somewhere on the page.
async function ringCovers(page, target) {
    return page.evaluate(name => {
        const ring = document.querySelector('.so-tour-ring');
        const element = [...document.querySelectorAll('[data-tour]')].find(e => e.dataset.tour === name && e.getClientRects().length > 0);
        if (!ring || ring.hidden || !element) return false;
        const r = ring.getBoundingClientRect(), t = element.getBoundingClientRect();
        return r.left <= t.left && r.top <= t.top && r.right >= t.right && r.bottom >= t.bottom;
    }, target);
}

(async () => {
    setTimeout(() => { console.error('journey watchdog expired'); process.exit(2); }, 10 * 60 * 1000).unref();
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME, headless: true });
    const admin = await apiContext(request, api);
    const me = await json(admin, '/api/v1/service-accounts/me');
    assert.equal(me.scopeKind, 'All', 'bootstrap scope missing');
    const detail = await json(admin, '/api/v1/service-accounts/accounts', { method: 'POST', data: { accountName, domain: 'SYN', reason: 'Sentetik rehber turu yolculuğu' } });
    const id = detail.summary.id;

    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    current = page;
    page.setDefaultTimeout(20000);
    const errors = [];
    page.on('pageerror', e => errors.push(String(e)));
    try {
        await step('list: the tour does not open on its own; the button starts it on the first step', async () => {
            await signIn(page, ui);
            await navigate(page, ui, 'service-accounts');
            const start = page.locator('#sa-list-tour-start');
            await start.waitFor();
            assert.equal(await box(page).count(), 0, 'no automatic tour');
            await start.focus();
            await page.keyboard.press('Enter');
            await box(page).waitFor();
            await page.waitForFunction(() => document.activeElement?.hasAttribute('data-tour-focus'));
            assert.equal(await focusedTitle(page), 'Takibinizdeki işler');
            assert.match(await box(page).innerText(), /Adım 1 \/ \d/);
            assert.ok(await ringCovers(page, 'sa-work-summary'), 'the frame covers the work summary');
            await capture(page, out, 'tour-list-first');
        });

        await step('keyboard: İleri and Geri move, focus follows the heading', async () => {
            await box(page).getByRole('button', { name: 'İleri' }).press('Enter');
            await page.waitForFunction(() => document.activeElement?.textContent.trim() !== 'Takibinizdeki işler' && document.activeElement?.hasAttribute('data-tour-focus'));
            const second = await focusedTitle(page);
            assert.ok(second && second !== 'Takibinizdeki işler');
            await box(page).getByRole('button', { name: 'Geri' }).click();
            await page.waitForFunction(() => document.activeElement?.textContent.trim() === 'Takibinizdeki işler');
        });

        await step('Esc closes and focus returns to the start button', async () => {
            await page.keyboard.press('Escape');
            await box(page).waitFor({ state: 'detached' });
            await page.waitForFunction(() => document.activeElement?.id === 'sa-list-tour-start');
            assert.equal(await page.locator('.so-tour-ring').count(), 0);
        });

        await step('imports before a preview: later steps are skipped and the box says so', async () => {
            await navigate(page, ui, 'service-accounts/imports');
            await page.locator('#sa-imports-tour-start').click();
            await box(page).waitFor();
            for (let i = 0; i < 4; i++) await box(page).getByRole('button', { name: 'İleri' }).click();
            await page.locator('[data-tour-skip]', { hasText: 'Atlanan adım' }).waitFor();
            assert.match(await page.locator('[data-tour-skip]').innerText(), /Satırlar ve kararlar/);
            assert.equal(await focusedTitle(page), 'Geçmiş aktarımlar');
            await box(page).getByRole('button', { name: 'Bitir' }).click();
            await page.waitForFunction(() => document.activeElement?.id === 'sa-imports-tour-start');
        });

        await step('account page: the tab header is highlighted, the tab is not switched', async () => {
            await navigate(page, ui, `service-accounts/${id}`);
            await page.locator('#sa-detail-tour-start').click();
            await box(page).waitFor();
            let title = await focusedTitle(page);
            for (let i = 0; i < 8 && title !== 'Hangi sunucularda?'; i++) {
                await box(page).getByRole('button', { name: 'İleri' }).click();
                await page.waitForTimeout(150);
                title = await focusedTitle(page);
            }
            assert.equal(title, 'Hangi sunucularda?');
            assert.ok(await ringCovers(page, 'sa-tab-usage'));
            assert.equal(await page.locator('button.sa-tab-title[aria-current="true"]').innerText().then(t => t.startsWith('İşler')), true, 'the tour never switches tabs');
            await capture(page, out, 'tour-detail-usage');
            await page.keyboard.press('Escape');
        });

        await step('390 px and dark theme: the box docks at the bottom without horizontal overflow', async () => {
            await page.evaluate(() => localStorage.setItem('wasas.appearance', 'dark'));
            await page.setViewportSize({ width: 390, height: 844 });
            await navigate(page, ui, 'service-accounts');
            await page.locator('#sa-list-tour-start').click();
            await box(page).waitFor();
            const rect = await box(page).boundingBox();
            assert.ok(rect && Math.abs(rect.y + rect.height - 844) <= 2 && rect.width >= 388, 'docked to the bottom, full width');
            assert.ok(await noHorizontalScroll(page), 'no horizontal scroll at 390 px');
            await capture(page, out, 'tour-390-dark');
            await page.keyboard.press('Escape');
            await page.evaluate(() => localStorage.setItem('wasas.appearance', 'light'));
        });

        assert.deepEqual(errors, [], 'page errors');
    } finally {
        fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify({ suffix, account: accountName, results }, null, 2));
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
