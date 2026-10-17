// Service Accounts guided tour journey (REHBER-UI, docs/service-accounts/GUIDED-TOUR-DESIGN.md). Synthetic data only; loopback hosts only.
// node service-accounts-guided-tour.cjs <playwright path> <UI URL> <API URL> <evidence directory>
// Preconditions: as service-accounts-event-flow.cjs (Demo API with Access/Session/ServiceAccounts on a disposable database from
// tests/sql/service-accounts/sa-sql-harness.ps1, demo:platform-admin with the Admin role and an 'All' scope, UI on HTTPS with
// DemoMode__ApiDemoActor=platform-admin, WASAS_CHROME set). What this proves: the tour opens only on request, highlights a real area,
// moves with the keyboard, says when a step's area is missing (an import before any preview), closes with Esc and gives focus back
// to the start button, docks at the bottom at 390 px without horizontal overflow, and works in the dark theme. REHBER-2 adds: the team-work
// page and the four administration tabs have a tour; the explanation can be folded and, at 640x400 (1280x800 at 200 % zoom), the
// highlighted area is not left under the docked box once it is folded. What it cannot prove:
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

// Starts the tour and presses İleri until the last step; returns the step titles in the order the tour showed them. The caller waits
// for the page's areas first (the tour only looks when it starts, and says so honestly when an area is not there yet).
async function walk(page, startId) {
    await page.locator(startId).waitFor({ state: 'visible' });
    await page.locator(startId).click();
    await box(page).waitFor();
    const seen = [];
    for (let i = 0; i < 12; i++) {
        await page.waitForFunction(() => document.activeElement?.hasAttribute('data-tour-focus'));
        seen.push(await focusedTitle(page));
        if (!await box(page).getByRole('button', { name: 'İleri' }).count()) break;
        await box(page).getByRole('button', { name: 'İleri' }).click();
        await page.waitForTimeout(150);
    }
    return seen;
}

async function capture640(page, out, name) {
    fs.mkdirSync(out, { recursive: true });
    await page.screenshot({ path: path.join(out, name + '.png'), animations: 'disabled' });
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
            // The tour looks for each area when it starts; the button exists before the summary and the list have loaded.
            await page.locator('[data-tour="sa-work-summary"]').waitFor();
            await page.locator('[data-tour="sa-results"]').waitFor();
            assert.equal(await box(page).count(), 0, 'no automatic tour');
            await start.focus();
            await page.keyboard.press('Enter');
            await box(page).waitFor();
            await page.waitForFunction(() => document.activeElement?.hasAttribute('data-tour-focus'));
            assert.equal(await focusedTitle(page), 'Takibinizdeki işler');
            assert.match(await box(page).textContent(), /Adım 1 \/ \d/); // textContent: innerText returns the CSS upper-case form
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
            await page.locator('[data-tour="sa-import-source"]').waitFor();
            await page.locator('[data-tour="sa-import-history"]').waitFor();
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
            await page.locator('[data-tour="sa-tab-usage"]').waitFor();
            await page.locator('[data-tour="sa-summary"]').waitFor();
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
            assert.equal(await page.locator('button.sa-tab-title[aria-current="true"]').textContent().then(t => t.startsWith('İşler')), true, 'the tour never switches tabs'); // textContent: MudTabs upper-cases the header with CSS
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

        await step('team work page: the tour points at the summary, the links, the filter, the list and the manual run', async () => {
            await page.setViewportSize({ width: 1440, height: 900 });
            await navigate(page, ui, 'service-accounts/work');
            await page.locator('[data-tour="sa-work-summary"]').waitFor();
            await page.locator('[data-tour="sa-reminders-filter"]').waitFor();
            await page.locator('[data-tour="sa-reminders-run"]').waitFor();
            // The reminder list is a step only when there are reminders; a fresh synthetic database has none, so the tour must say it skipped it.
            const hasList = await page.locator('[data-tour="sa-reminders-list"]').count() > 0;
            const seen = await walk(page, '#sa-work-tour-start');
            assert.deepEqual(seen, ['Takibinizdeki işler', 'Hazır süzgeçli listeler', 'Hatırlatma süzgeci', ...(hasList ? ['Hatırlatmalar'] : []), 'Değerlendirmeyi şimdi çalıştır']);
            if (!hasList) assert.match(await page.locator('[data-tour-skip]').innerText(), /Hatırlatmalar/);
            await capture(page, out, 'tour-work');
            await page.keyboard.press('Escape');
            await page.waitForFunction(() => document.activeElement?.id === 'sa-work-tour-start');
        });

        await step('administration: each tab has its own tour, and the tour uses only the visible tab', async () => {
            await navigate(page, ui, 'service-accounts/admin');
            await page.locator('[data-tour="sa-admin-grant-form"]').waitFor();
            // The first-setup block is offered only while no scope was ever granted; the synthetic bootstrap actor already has one, so it is skipped honestly.
            const scope = await walk(page, '#sa-admin-scope-tour-start');
            assert.deepEqual(scope, ['Verilmiş kapsamlar', 'Kapsam ver']);
            assert.match(await page.locator('[data-tour-skip]').innerText(), /İlk kurulum/);
            await page.keyboard.press('Escape');
            await page.waitForFunction(() => document.activeElement?.id === 'sa-admin-scope-tour-start');
            await page.locator('button.sa-tab-title', { hasText: 'Kurum ve ekipler' }).click();
            await page.locator('[data-tour="sa-admin-team-form"]').waitFor({ state: 'visible' });
            assert.deepEqual(await walk(page, '#sa-admin-orgs-tour-start'), ['Kurumlar', 'Kurum ekle veya düzenle', 'Ekipler', 'Ekip ekle veya düzenle']);
            await page.keyboard.press('Escape');
            await page.locator('button.sa-tab-title', { hasText: 'gMSA yönlendirme' }).click();
            await page.locator('[data-tour="sa-admin-role-form"]').waitFor({ state: 'visible' });
            assert.deepEqual(await walk(page, '#sa-admin-routing-tour-start'), ['Yönlendirme kararı', 'Mevcut ayarlar', 'Rol ekle']);
            await page.keyboard.press('Escape');
            await page.locator('button.sa-tab-title', { hasText: 'Kişiler' }).click();
            await page.locator('[data-tour="sa-people-add"]').waitFor({ state: 'visible' });
            // Nobody was searched, so the result list and the selected person are not on the screen.
            assert.deepEqual(await walk(page, '#sa-admin-people-tour-start'), ['Kişi ara', 'Kişi kaydı ekle']);
            assert.match(await page.locator('[data-tour-skip]').innerText(), /Sonuçlar/);
            await capture(page, out, 'tour-admin-people');
            await page.keyboard.press('Escape');
        });

        await step('640x400 (200 % zoom): the explanation folds, the area stays above the docked box, no horizontal scroll', async () => {
            await page.setViewportSize({ width: 640, height: 400 });
            await navigate(page, ui, 'service-accounts');
            await page.locator('[data-tour="sa-work-summary"]').waitFor();
            await page.locator('[data-tour="sa-results"]').waitFor();
            await page.locator('#sa-list-tour-start').click();
            await box(page).waitFor();
            await page.waitForFunction(() => document.activeElement?.hasAttribute('data-tour-focus'));
            const toggle = box(page).locator('[data-tour-action="toggle"]');
            assert.equal(await toggle.getAttribute('aria-expanded'), 'true');
            const open = await box(page).boundingBox();
            await capture640(page, out, 'tour-640x400-open');
            await toggle.click();
            assert.equal(await toggle.getAttribute('aria-expanded'), 'false');
            await page.waitForFunction(() => document.querySelector('[data-tour-body]')?.hidden === true);
            const folded = await box(page).boundingBox();
            assert.ok(folded.height < open.height, 'folding makes the box smaller (' + open.height + ' -> ' + folded.height + ')');
            assert.equal(await page.evaluate(() => document.activeElement?.getAttribute('data-tour-action')), 'toggle', 'focus stays on the pressed button');
            // Walk every step: an area that fits above the box must end up above it; a taller one must keep its top on the screen.
            for (let i = 0; i < 8; i++) {
                await page.waitForTimeout(250);
                const verdict = await page.evaluate(() => {
                    const ring = document.querySelector('.so-tour-ring');
                    const panel = document.querySelector('[data-tour-box]').getBoundingClientRect();
                    if (!ring || ring.hidden) return { skipped: true };
                    const r = ring.getBoundingClientRect();
                    return { top: r.top, bottom: r.bottom, boxTop: panel.top, fits: r.height + 8 <= panel.top - 72 };
                });
                if (!verdict.skipped) {
                    assert.ok(verdict.top >= 0, 'the area starts on the screen: ' + JSON.stringify(verdict));
                    assert.ok(!verdict.fits || verdict.bottom <= verdict.boxTop + 2, 'an area that fits is not left under the box: ' + JSON.stringify(verdict));
                }
                assert.ok(await noHorizontalScroll(page), 'no horizontal scroll at 640x400');
                if (!await box(page).getByRole('button', { name: 'İleri' }).count()) break;
                await box(page).getByRole('button', { name: 'İleri' }).click();
            }
            await capture640(page, out, 'tour-640x400-folded');
            await page.keyboard.press('Escape');
            await page.waitForFunction(() => document.activeElement?.id === 'sa-list-tour-start');
            assert.equal(await page.evaluate(() => document.body.style.paddingBottom), '', 'the temporary bottom space is given back');
            await page.setViewportSize({ width: 1440, height: 900 });
        });

        assert.deepEqual(errors, [], 'page errors');
    } finally {
        fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify({ suffix, account: accountName, results }, null, 2));
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
