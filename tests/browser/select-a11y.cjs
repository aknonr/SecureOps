// TH-13: every MudSelect exposes name, combobox/listbox/option semantics and keeps its keyboard behaviour.
// Usage: node select-a11y.cjs <playwright-core path> <ui url> <out dir> [api url]
// Needs only a Demo UI (and API) on loopback; no data is created. Routes with a MudSelect are listed below.
// With an API url that serves a synthetic In Use record (Simulation source) and two eligible reviewers, it also drives the reviewer
// assignment dialog by keyboard: the select shows the reviewer's label (never the raw id) and Escape closes the list, not the dialog.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium, request } = require(process.argv[2]);
const { loopback, navigate, signIn, apiContext, json } = require('./journey-support.cjs');
const ui = loopback(process.argv[3]), out = path.resolve(process.argv[4]);
const api = process.argv[5] ? loopback(process.argv[5]) : null;
const routes = ['in-use/reports', 'admin/sessions', 'service-accounts/work'];

async function expectSemantics(page, route) {
    // Count controls by MudSelect's own markup, not by the role the bridge adds, so a missed select is a failure.
    const controls = await page.locator('.mud-input-control.mud-select').count();
    assert.ok(controls > 0, route + ': page has a MudSelect');
    const bad = await page.evaluate(() => [...document.querySelectorAll('.mud-input-control.mud-select')].map(control => {
        const boxes = [...control.querySelectorAll('[role=combobox]')].filter(e => getComputedStyle(e).display !== 'none');
        const box = boxes[0];
        const named = box && (box.getAttribute('aria-label') || (box.getAttribute('aria-labelledby') || '').split(' ')
            .map(id => document.getElementById(id)?.textContent.trim()).join('').trim());
        return boxes.length === 1 && box.getAttribute('aria-haspopup') === 'listbox' && box.getAttribute('aria-expanded') === 'false' && named
            ? null : control.id + ':' + boxes.length + ':' + (named || '?');
    }).filter(Boolean));
    assert.deepEqual(bad, [], route + ': every select is exactly one named, collapsed combobox');
}

// The bridge writes aria-* one animation frame after Blazor changes the class, so state is polled, not read once.
const attribute = (box, name, value) => box.evaluate((e, [n, v]) => new Promise((resolve, reject) => {
    const started = Date.now();
    (function check() { if (e.getAttribute(n) === v) resolve(); else if (Date.now() - started > 3000) reject(new Error(n + ' is ' + e.getAttribute(n) + ', expected ' + v)); else requestAnimationFrame(check); }());
}), [name, value]);

async function keyboard(page, route, name) {
    const box = page.getByRole('combobox', { name, exact: true }).first();
    await box.waitFor();
    assert.equal(await page.getByLabel(name, { exact: true }).locator('visible=true').first().evaluate(e => e.getAttribute('role')), 'combobox', route + ': getByLabel reaches the combobox');
    await box.focus();
    const before = await box.textContent();
    await page.keyboard.press('Enter');
    await page.getByRole('listbox').waitFor();
    await attribute(box, 'aria-expanded', 'true');
    const options = page.getByRole('option');
    const count = await options.count();
    assert.ok(count > 1, route + ': options exposed');
    assert.equal(await box.getAttribute('aria-controls'), await page.getByRole('listbox').getAttribute('id'));
    assert.equal(await page.getByRole('option', { selected: true }).count(), 1, route + ': exactly one selected option');
    await page.keyboard.press('ArrowDown');
    await page.waitForFunction(() => {
        const slot = document.querySelector('.mud-select [role=combobox][aria-expanded="true"]');
        const id = slot?.getAttribute('aria-activedescendant');
        return id && document.getElementById(id)?.classList.contains('mud-selected-item');
    });
    await page.waitForFunction(text => document.querySelector('.mud-select [role=combobox][aria-expanded="true"]')?.textContent !== text, before);
    await page.keyboard.press('Escape');
    await page.getByRole('listbox').waitFor({ state: 'hidden' });
    await attribute(box, 'aria-expanded', 'false');
    assert.equal(await box.evaluate(e => e === document.activeElement), true, route + ': focus stays on the combobox after Escape');
    await page.keyboard.press('Space');
    await page.getByRole('listbox').waitFor();
    await options.nth(0).click();
    await page.getByRole('listbox').waitFor({ state: 'hidden' });
    await attribute(box, 'aria-expanded', 'false');
}

async function assignmentDialog(page, record) {
    await navigate(page, ui, 'in-use/' + record.id);
    await page.getByRole('button', { name: 'İnceleyici ata/değiştir', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.waitFor();
    const box = dialog.getByRole('combobox', { name: 'İnceleyici', exact: true });
    await box.waitFor();
    assert.equal(await dialog.getByLabel('İnceleyici', { exact: true }).evaluate(e => e === document.querySelector('[role=combobox]')), true);
    assert.doesNotMatch(await box.textContent(), /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}/, 'the select shows a label, not the raw reviewer id');
    await box.focus();
    await page.keyboard.press('Enter');
    await attribute(box, 'aria-expanded', 'true');
    const options = page.getByRole('option');
    assert.ok(await options.count() >= 2, 'reviewer options exposed');
    await page.keyboard.press('Escape');
    await attribute(box, 'aria-expanded', 'false');
    assert.equal(await dialog.isVisible(), true, 'Escape closes the list, not the dialog');
    await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
}

(async () => {
    assert.equal(fs.existsSync(out), false, 'Use a fresh evidence directory');
    fs.mkdirSync(out, { recursive: true });
    const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const results = [];
    try {
        for (const theme of ['light', 'dark']) for (const width of [1440, 390]) {
            const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width, height: width === 390 ? 844 : 900 }, hasTouch: width === 390 });
            const page = await context.newPage();
            await context.route('**/*', route => ['localhost', '127.0.0.1'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort());
            await signIn(page, ui);
            await page.evaluate(t => localStorage.setItem('wasas.appearance', t), theme);
            for (const route of routes) {
                await navigate(page, ui, route);
                await expectSemantics(page, route);
                if (width === 1440 && route === 'in-use/reports') await keyboard(page, route, 'Durum');
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, route + ': no horizontal scroll at ' + width);
                results.push({ theme, width, route, selects: await page.locator('.mud-input-control.mud-select').count() });
            }
            await context.close();
        }
        if (api) {
            const client = await apiContext(request, api);
            const record = (await json(client, '/api/v1/in-use')).items.find(r => r.source.synthetic);
            assert.ok(record, 'A synthetic In Use record is required for the dialog check');
            const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
            const page = await context.newPage();
            await signIn(page, ui);
            await assignmentDialog(page, record);
            await context.close();
            results.push({ route: 'in-use assignment dialog', keyboard: true });
        }
        fs.writeFileSync(path.join(out, 'select-a11y.json'), JSON.stringify({ results }, null, 2));
        console.log('PASS select-a11y', JSON.stringify(results));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
