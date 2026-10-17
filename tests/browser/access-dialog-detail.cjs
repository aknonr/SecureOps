// #79 Access action dialog (Rolleri değiştir, Erişimi kapat) and the user detail page at 1280, 640x450 (= 200 % of a 1280x900
// window) and 390 px, light and dark, by keyboard. Proves: no horizontal scroll; focus enters the dialog, stays inside it (Tab and
// Shift+Tab wrap), Esc and "Vazgeç" close it and return focus to the button that opened it; every role checkbox has a name;
// every control the keyboard reaches in the dialog draws a visible ring (>= 2 px); the dark-theme destructive confirm button is
// readable (>= 4.5:1); after a confirmed action focus lands on the outcome block, not <body>.
// Local only: Demo API (Access__DemoCompatibilityEnabled=true, Access__RepositoryProvider=InMemory) and Demo UI, both on loopback,
// synthetic data. The journey reads the other demo actor ("team-lead") through the API's own demo header (GET only) so it never acts on
// the signed-in administrator's own record (changing own roles revokes the own session). It adds no product route or setting.
// SECUREOPS_BROWSER_CHANNEL: browser channel for Playwright (default msedge); set it empty to use Playwright's own Chromium.
// node access-dialog-detail.cjs <playwright path> <UI URL> <API URL> <evidence directory>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { loopback, signIn } = require('./journey-support.cjs');
const { chromium } = require(process.argv[2]);
const ui = loopback(process.argv[3]), api = loopback(process.argv[4]);
const out = process.argv[5];
const channel = process.env.SECUREOPS_BROWSER_CHANNEL ?? 'msedge';

const luminance = color => { const [r, g, b] = color.match(/[\d.]+/g).slice(0, 3).map(Number).map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; }); return 0.2126 * r + 0.7152 * g + 0.0722 * b; };
const contrast = (a, b) => { const [x, y] = [luminance(a), luminance(b)]; return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
const demo = async (actor, route) => (await fetch(new URL(route, api), { headers: { 'X-SecureOps-Demo-Actor': actor } })).json();

const TRIGGERS = [
    { id: 'access-user-replace-roles', name: 'Rolleri değiştir', confirm: 'Rolleri uygula' },
    { id: 'access-user-disable', name: 'Erişimi kapat', confirm: 'Erişimi kapat' },
];
const SIZES = [[1280, 900], [640, 450], [390, 844]];
const inDialog = () => document.querySelector('.mud-dialog')?.contains(document.activeElement) === true;

(async () => {
    const observations = [];
    const note = text => { observations.push(text); console.log(text); };
    fs.mkdirSync(out, { recursive: true });
    await demo('team-lead', '/api/v1/access/me');
    const target = (await demo('platform-admin', '/api/v1/access/users')).find(user => user.corporateIdentity === 'demo:team-lead');
    assert.ok(target, 'the team-lead demo user exists');
    const detail = new URL(`access/users/${target.userId}`, ui).href;

    const browser = await chromium.launch({ ...(channel ? { channel } : {}), headless: true });
    const errors = [];
    try {
        for (const mode of ['light', 'dark']) {
            const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
            await context.addInitScript(value => { try { localStorage.setItem('wasas.appearance', value); } catch { } }, mode);
            const page = await context.newPage();
            page.on('pageerror', error => errors.push(error.message));
            await signIn(page, ui);
            const open = async size => {
                await page.setViewportSize({ width: size[0], height: size[1] });
                await page.goto(detail);
                await page.locator('#access-user-disable').waitFor({ timeout: 15000 });
                await page.waitForTimeout(800);
            };
            const settled = () => page.waitForTimeout(450); // the global button rule transitions `all`; read the outline settled

            for (const size of SIZES) {
                await open(size);
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `detail @${size[0]} ${mode}: page overflows`);
                note(`detail @${size[0]} ${mode}: no horizontal scroll`);
                await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
                const pageRings = [];
                for (let press = 0; press < 14 && pageRings.length < 7; press++) {
                    await page.keyboard.press('Tab');
                    await settled();
                    const ring = await page.evaluate(() => {
                        const element = document.activeElement;
                        if (!element.closest('.so-page') || !element.matches('.mud-button-root, a, summary')) return null;
                        const style = getComputedStyle(element), box = element.getBoundingClientRect();
                        return { name: (element.getAttribute('aria-label') || element.textContent).trim().replace(/\s+/g, ' ').slice(0, 24), style: style.outlineStyle, width: parseFloat(style.outlineWidth), height: box.height };
                    });
                    if (ring) pageRings.push(ring);
                }
                assert.ok(pageRings.length >= 5, `detail @${size[0]} ${mode}: the keyboard reached only ${pageRings.length} controls`);
                assert.deepEqual(pageRings.filter(ring => ring.style === 'none' || ring.width < 2), [], `detail @${size[0]} ${mode}: focus ring under 2 px ${JSON.stringify(pageRings)}`);
                assert.deepEqual(pageRings.filter(ring => ring.height < 24), [], `detail @${size[0]} ${mode}: target under 24 px`);
                note(`detail @${size[0]} ${mode}: ${pageRings.length} keyboard-reached controls (buttons, links, summaries) draw a ring >= 2 px`);

                for (const trigger of TRIGGERS) {
                    await open(size);
                    const button = page.locator('#' + trigger.id);
                    await button.focus();
                    await page.keyboard.press('Enter');
                    await page.locator('.mud-dialog').waitFor();
                    await page.waitForTimeout(700);
                    const label = `${trigger.name} @${size[0]} ${mode}`;

                    // Layout: inside the viewport, no horizontal scroll, actions reachable.
                    const box = await page.evaluate(() => { const dialog = document.querySelector('.mud-dialog'), r = dialog.getBoundingClientRect(); return { left: r.left, right: r.right, overflow: dialog.scrollWidth > dialog.clientWidth + 1, page: document.documentElement.scrollWidth > innerWidth + 1, actionsBottom: Math.max(...[...dialog.querySelectorAll('.mud-dialog-actions button')].map(b => b.getBoundingClientRect().bottom)), height: innerHeight }; });
                    assert.ok(box.left >= 0 && box.right <= size[0] + 1, `${label}: dialog outside the viewport`);
                    assert.equal(box.overflow || box.page, false, `${label}: horizontal scroll`);
                    assert.ok(box.actionsBottom <= box.height + 1, `${label}: actions outside the viewport`);

                    // 1. Focus enters the dialog on open.
                    assert.equal(await page.evaluate(inDialog), true, `${label}: focus did not enter the dialog`);

                    // 2. Tab and Shift+Tab never leave it (paced: MudBlazor's trap reacts through script).
                    for (const key of ['Tab', 'Shift+Tab']) {
                        for (let press = 0; press < 14; press++) {
                            await page.keyboard.press(key);
                            await page.waitForTimeout(120);
                            assert.equal(await page.evaluate(inDialog), true, `${label}: ${key} press ${press + 1} left the dialog`);
                        }
                    }

                    // 3. Role checkboxes have a name; every control reached inside the dialog draws a ring of at least 2 px.
                    if (trigger.id === 'access-user-replace-roles') {
                        const checkboxes = await page.evaluate(() => [...document.querySelectorAll('.mud-dialog input[type=checkbox]')].map(input => (input.closest('label')?.textContent ?? '').trim()));
                        assert.ok(checkboxes.length >= 2 && checkboxes.every(name => name.length > 2), `${label}: unnamed role checkbox ${JSON.stringify(checkboxes)}`);
                    }
                    await page.evaluate(() => { document.querySelector('.so-dialog-subject').focus(); });
                    const rings = [];
                    for (let press = 0; press < 14; press++) {
                        await page.keyboard.press('Tab');
                        await settled();
                        const ring = await page.evaluate(() => {
                            const element = document.activeElement;
                            if (!element.closest('.mud-dialog')) return null;
                            const holder = element.matches('textarea, input[type=text]') ? element.closest('.mud-input-outlined') : element;
                            const style = getComputedStyle(holder);
                            return { name: (element.getAttribute('aria-label') || element.closest('label')?.textContent || element.textContent || element.tagName).trim().replace(/\s+/g, ' ').slice(0, 24), style: style.outlineStyle, width: parseFloat(style.outlineWidth) };
                        });
                        if (ring && !rings.some(seen => seen.name === ring.name)) rings.push(ring);
                    }
                    assert.ok(rings.length >= 3, `${label}: the keyboard reached only ${rings.length} controls`);
                    assert.deepEqual(rings.filter(ring => ring.style === 'none' || ring.width < 2), [], `${label}: focus ring under 2 px ${JSON.stringify(rings)}`);

                    // 4. Dark: the confirm button of a destructive action is readable.
                    if (mode === 'dark') {
                        const colors = await page.evaluate(() => { const b = document.querySelector('.so-dialog-buttons .mud-button-filled'); const s = getComputedStyle(b); return { fg: s.color, bg: s.backgroundColor }; });
                        assert.ok(contrast(colors.fg, colors.bg) >= 4.5, `${label}: confirm contrast ${contrast(colors.fg, colors.bg).toFixed(2)}`);
                    }
                    if (size[0] === 390 && mode === 'light') await page.screenshot({ path: path.join(out, `dialog-${trigger.id}-390.png`), animations: 'disabled' });

                    // 5. Esc closes and returns focus to the opener; so does "Vazgeç".
                    await page.keyboard.press('Escape');
                    await page.locator('.mud-dialog').waitFor({ state: 'detached' });
                    await page.waitForTimeout(500);
                    assert.equal(await page.evaluate(() => document.activeElement.id), trigger.id, `${label}: Esc did not return focus to the opener`);
                    await button.focus(); await page.keyboard.press('Enter');
                    await page.locator('.mud-dialog').waitFor(); await page.waitForTimeout(500);
                    await page.getByRole('button', { name: 'Vazgeç', exact: true }).click();
                    await page.locator('.mud-dialog').waitFor({ state: 'detached' });
                    await page.waitForTimeout(500);
                    assert.equal(await page.evaluate(() => document.activeElement.id), trigger.id, `${label}: Vazgeç did not return focus to the opener`);
                    note(`${label}: focus in, trapped (Tab and Shift+Tab), ${rings.length} controls with rings >= 2 px, Esc and Vazgeç return focus`);
                }
            }

            // 6. A confirmed action: focus lands on the outcome block, never <body> (the re-read replaces the buttons).
            if (mode === 'light') {
                await open([1280, 900]);
                await page.locator('#access-user-replace-roles').focus();
                await page.keyboard.press('Enter');
                await page.locator('.mud-dialog').waitFor(); await page.waitForTimeout(700);
                await page.getByRole('button', { name: 'Rolleri uygula', exact: true }).focus();
                await page.keyboard.press('Enter');
                await page.locator('.user-outcome').waitFor({ timeout: 15000 });
                await page.waitForTimeout(800);
                const landed = await page.evaluate(() => ({ body: document.activeElement === document.body, outcome: document.activeElement.classList.contains('user-outcome') }));
                assert.equal(landed.body, false, 'after a confirmed action focus dropped to <body>');
                assert.equal(landed.outcome, true, 'after a confirmed action focus is not on the outcome block');
                note('confirmed action: focus is on the outcome block');
            }
            await context.close();
        }
        assert.deepEqual(errors, [], 'page errors');
        fs.writeFileSync(path.join(out, 'access-dialog-detail.txt'), observations.join('\n') + '\n');
        console.log('access-dialog-detail: PASS');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
