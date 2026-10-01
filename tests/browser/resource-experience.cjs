// Usage: node tests/browser/resource-experience.cjs <playwright-core path> <UI URL> <API URL> <ordinary|manager|denied> [evidence directory] [axe-core path]
// The existing Demo UI must use team-lead. Only this fixed synthetic actor's roles are changed.
// Run denied last: it disables that local synthetic actor. No API proxy or browser storage is used.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const { chromium } = require(process.argv[2]);
const ui = new URL(process.argv[3]);
const apiUrl = new URL(process.argv[4]);
const mode = process.argv[5];
const out = process.argv[6];
const axePath = process.argv[7];
assert.ok(['ordinary', 'manager', 'denied'].includes(mode));
for (const url of [ui, apiUrl]) assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(url.hostname), 'Loopback only');
const actor = 'team-lead';
const checks = [];
const accessibility = [];
const pass = message => { checks.push(message); console.log('PASS: ' + message); };

async function api(route, method = 'GET', body, who = 'platform-admin', expected = 200) {
    const response = await fetch(new URL('api/v1/' + route, apiUrl), {
        method, headers: { 'Content-Type': 'application/json', 'X-SecureOps-Demo-Actor': who },
        body: body ? JSON.stringify(body) : undefined
    });
    assert.equal(response.status, expected, `${method} ${route}`);
    return response.json();
}

async function navigate(page, route) {
    let listener;
    const connected = new Promise((resolve, reject) => {
        const deadline = setTimeout(() => reject(new Error('No interactive Blazor circuit')), 15000);
        listener = socket => socket.on('framereceived', ({ payload }) => {
            if (payload.toString().includes('JS.BeginInvokeJS')) { clearTimeout(deadline); resolve(); }
        });
        page.on('websocket', listener);
    });
    try { await page.goto(new URL(route, ui).href); await connected; }
    finally { page.off('websocket', listener); }
}

async function eventually(read, expected) {
    for (let i = 0; i < 60; i++) {
        const value = await read();
        if (JSON.stringify(value) === JSON.stringify(expected)) return;
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.deepEqual(await read(), expected);
}

async function screenshot(page, name) {
    if (!out) return;
    fs.mkdirSync(out, { recursive: true });
    await page.screenshot({ path: path.join(out, name + '.png'), fullPage: true });
}

async function audit(page, name, scope = '.so-resource-page') {
    await screenshot(page, name);
    if (await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)) {
        console.log(await page.locator('body *').evaluateAll(elements => elements.filter(e => e.getBoundingClientRect().right > innerWidth + 1).slice(0, 12).map(e => ({ tag: e.tagName, class: e.className, rect: e.getBoundingClientRect().toJSON() }))));
    }
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `${name}: horizontal overflow`);
    if (!axePath) return;
    await page.addScriptTag({ path: path.join(axePath, 'axe.min.js') });
    const result = await page.evaluate(async target => {
        const report = await axe.run(target, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } });
        return report.violations.map(item => ({ id: item.id, impact: item.impact, targets: item.nodes.map(node => node.target) }));
    }, scope);
    accessibility.push({ name, violations: result });
}

async function theme(page, dark) {
    await page.getByRole('button', { name: 'Hesap menüsü', exact: true }).click();
    await page.locator('.so-user-menu-popover.mud-popover-open .mud-list-item').filter({ hasText: dark ? /^Koyu/ : /^Aydınlık/ }).click();
    await page.waitForFunction(value => document.documentElement.classList.contains('so-dark') === value, dark);
}

async function layouts(page, name) {
    for (const dark of [false, true]) {
        await theme(page, dark);
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
            await page.waitForTimeout(800);
            const suffix = `${name}-${dark ? 'dark' : 'light'}-${width === 390 ? 'mobile' : 'desktop'}`;
            await audit(page, suffix);
            await screenshot(page, suffix);
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        await page.waitForTimeout(800);
    }
    await theme(page, false);
    pass(`${name}: desktop/mobile and light/dark, no horizontal overflow; axe checked when supplied`);
}

async function guide(page, canManage) {
    const preferences = await api('resources/me', 'GET', null, actor);
    const invitation = page.locator('[data-guide-invitation]');
    if (!preferences.guideDismissed) {
        await invitation.getByRole('button', { name: 'Başla', exact: true }).press('Enter');
    } else await page.getByRole('button', { name: 'Nasıl kullanılır?', exact: true }).press('Enter');
    const panel = page.getByRole('complementary', { name: 'Kullanım Rehberi' });
    await panel.waitFor();
    await eventually(() => panel.locator('h2').evaluate(e => e === document.activeElement), true);
    await panel.getByRole('button', { name: 'İleri', exact: true }).press('Enter');
    await eventually(() => panel.locator('.so-guide-heading').innerText(), `KULLANIM REHBERİ · 2 / ${canManage ? 5 : 4}`);
    await panel.getByRole('button', { name: 'Geri', exact: true }).press('Enter');
    await eventually(() => panel.locator('.so-guide-heading').innerText(), `KULLANIM REHBERİ · 1 / ${canManage ? 5 : 4}`);
    assert.equal(await panel.getByRole('button', { name: 'Geri', exact: true }).isDisabled(), true);
    await audit(page, `after-guide-${mode}`);
    for (let i = 0; i < (canManage ? 4 : 3); i++) {
        await panel.getByRole('button', { name: 'İleri', exact: true }).click();
        await eventually(() => panel.locator('.so-guide-heading').innerText(), `KULLANIM REHBERİ · ${i + 2} / ${canManage ? 5 : 4}`);
    }
    assert.equal(await panel.locator('a[href="admin/resources"]').count(), canManage ? 1 : 0);
    await panel.getByRole('button', { name: 'Bitir', exact: true }).press('Enter');
    await panel.waitFor({ state: 'hidden' });
    await eventually(() => page.locator('#resource-guide-replay').evaluate(e => e === document.activeElement), true);
    await page.getByRole('button', { name: 'Nasıl kullanılır?', exact: true }).click();
    await panel.locator('h2').press('Escape');
    await panel.waitFor({ state: 'hidden' });
    const after = await api('resources/me', 'GET', null, actor);
    assert.equal(after.guideDismissed, true);
    assert.deepEqual(after.sets, preferences.sets);
    assert.deepEqual(after.favourites, preferences.favourites);
    pass('Guide: invitation/replay, next/back/finish/Escape, focus restoration, authorized routes; no link or group mutations');
}

async function ordinary(page, fixture) {
    await navigate(page, 'resources');
    assert.equal(await page.locator('.so-nav a[href="admin/resources"]').count(), 0);
    await guide(page, false);
    await page.getByLabel('Bağlantı ara', { exact: true }).fill(fixture.label);
    await eventually(() => page.locator('.so-resource-results-head [role="status"]').innerText(), '28 bağlantı');
    await eventually(() => page.locator('.so-resource-results .so-resource-item').count(), 25);
    await page.getByRole('button', { name: 'Sonraki', exact: true }).click();
    await eventually(() => page.locator('.so-resource-results .so-resource-item').count(), 3);
    const last = fixture.links[27];
    await page.getByRole('button', { name: `${last.name}: Favorilere ekle`, exact: true }).click();
    await page.getByRole('button', { name: /^Favorilerim/ }).click();
    await eventually(() => page.locator('.so-resource-results h3').allTextContents(), [last.name]);
    await page.getByRole('button', { name: `${last.name}: Favorilerden çıkar`, exact: true }).click();
    await page.getByText('Henüz favoriniz yok', { exact: true }).waitFor().catch(() => page.getByText('Eşleşen bağlantı yok', { exact: true }).waitFor());
    await page.getByRole('button', { name: 'Tüm bağlantılar', exact: true }).click();
    const environment = page.getByLabel('Ortam', { exact: true });
    await environment.fill(fixture.environment);
    await page.getByRole('option', { name: fixture.environment, exact: true }).waitFor();
    await environment.press('ArrowDown');
    await environment.press('Enter');
    await eventually(() => page.locator('.so-resource-results h3').allTextContents(), [last.name]);
    pass('Ordinary: search, page two, favourite add/remove and environment absent from page one');
    await page.getByRole('button', { name: 'Filtreleri temizle', exact: true }).click();
    await page.getByLabel('Bağlantı ara', { exact: true }).fill('Bulunmayan-' + fixture.suffix);
    await eventually(() => page.locator('.so-resource-results .so-resource-item').count(), 0);
    await page.getByRole('button', { name: 'Nasıl kullanılır?', exact: true }).click();
    const emptyGuide = page.locator('.so-resource-guide');
    for (let step = 2; step <= 3; step++) {
        await emptyGuide.getByRole('button', { name: 'İleri', exact: true }).click();
        await eventually(() => emptyGuide.locator('.so-guide-heading').innerText(), `KULLANIM REHBERİ · ${step} / 4`);
    }
    await emptyGuide.getByText(/Bu adımın kontrolü şu anda görünmüyor/).waitFor();
    assert.equal(await page.locator('.so-guide-target').count(), 0);
    await emptyGuide.getByRole('button', { name: 'Atla', exact: true }).press('Enter');
    await emptyGuide.waitFor({ state: 'hidden' });
    pass('Guide missing-target fallback and keyboard skip are safe in an empty result');
    await page.getByRole('button', { name: 'Tüm bağlantılara dön', exact: true }).click();
    await eventually(() => page.getByLabel('Bağlantı ara', { exact: true }).inputValue(), '');
    await page.getByLabel('Bağlantı ara', { exact: true }).fill('(SYN)');
    await eventually(() => page.locator('.so-resource-results-head [role="status"]').innerText(), '4 bağlantı');
    await layouts(page, 'after-links');

    const groupName = 'Günlük Kontroller ' + fixture.suffix;
    for (let i = 0; i < 3; i++) {
        const link = fixture.links[i];
        await page.getByLabel('Bağlantı ara', { exact: true }).fill(link.name);
        await page.getByRole('button', { name: `${link.name}: Grubuma kaydet`, exact: true }).click();
        const dialog = page.locator('.mud-dialog');
        if (i === 0) {
            if (await dialog.getByRole('button', { name: 'Yeni kişisel grup oluştur', exact: true }).count()) await dialog.getByRole('button', { name: 'Yeni kişisel grup oluştur', exact: true }).click();
            await dialog.getByLabel('Grup adı', { exact: true }).fill(groupName);
            await dialog.getByRole('button', { name: 'Oluştur ve kaydet', exact: true }).click();
        } else {
            await dialog.locator('.so-group-choices button').filter({ hasText: groupName }).click();
            await dialog.getByRole('button', { name: 'Grubuma kaydet', exact: true }).click();
        }
        await dialog.waitFor({ state: 'hidden' });
    }
    const preferences = await api('resources/me', 'GET', null, actor);
    fixture.groupId = preferences.sets.find(group => group.name === groupName).id;
    await api(`resources/me/sets/${fixture.groupId}/resolve`, 'GET', null, 'platform-admin', 404);
    await navigate(page, 'resources/sets');
    await page.locator('.so-group-choices button').filter({ hasText: groupName }).click();
    const detail = page.locator('.so-group-detail');
    const names = () => detail.locator('.so-set-body strong').allTextContents();
    await eventually(names, fixture.links.slice(0, 3).map(link => link.name));
    const hidden = fixture.links[1];
    let archived = await api('resources/links/' + hidden.id, 'PUT', { ...hidden, archived: true, expectedVersion: hidden.version });
    await page.getByRole('button', { name: 'Yenile', exact: true }).click();
    await eventually(names, [fixture.links[0].name, fixture.links[2].name]);
    await detail.getByRole('button', { name: 'Yeniden adlandır', exact: true }).click();
    await page.getByLabel('Grup adı', { exact: true }).fill(groupName + ' güncel');
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
    await page.locator('.mud-dialog').waitFor({ state: 'hidden' });
    await detail.getByRole('button', { name: 'Varsayılan yap', exact: true }).click();
    await detail.getByRole('button', { name: 'Varsayılanı kaldır', exact: true }).waitFor();
    await detail.getByRole('button', { name: `${fixture.links[2].name}: Yukarı taşı`, exact: true }).press('Enter');
    await eventually(names, [fixture.links[2].name, fixture.links[0].name]);
    await eventually(() => detail.locator('h2').evaluate(e => e === document.activeElement), true);
    assert.equal(await page.locator('[data-so-open-links]').count(), 0, 'Default and reorder do not open or prepare');
    await navigate(page, 'resources');
    const added = fixture.links[3];
    await page.getByLabel('Bağlantı ara', { exact: true }).fill(added.name);
    await page.getByRole('button', { name: `${added.name}: Grubuma kaydet`, exact: true }).click();
    await page.locator('.mud-dialog .so-group-choices button').filter({ hasText: groupName }).click();
    await page.locator('.mud-dialog').getByRole('button', { name: 'Grubuma kaydet', exact: true }).click();
    await page.locator('.mud-dialog').waitFor({ state: 'hidden' });
    await api('resources/links/' + hidden.id, 'PUT', { ...archived, archived: false, expectedVersion: archived.version });
    await navigate(page, 'resources/sets');
    await eventually(names, [fixture.links[2].name, hidden.name, fixture.links[0].name, added.name]);
    await detail.getByRole('button', { name: `${fixture.links[0].name}: Gruptan çıkar`, exact: true }).click();
    await eventually(names, [fixture.links[2].name, hidden.name, added.name]);
    await eventually(() => detail.locator('h2').evaluate(e => e === document.activeElement), true);
    pass('Group: first group from add journey, add/rename/default/order/explicit removal; hidden membership restored in place; Admin ownership denied');

    await page.evaluate(() => {
        window.resourceOpenCalls = [];
        const original = window.open;
        window.open = function (...args) {
            const call = { url: args[0], features: args[2], active: navigator.userActivation.isActive };
            const handle = original.apply(this, args);
            call.hasHandle = !!handle;
            window.resourceOpenCalls.push(call);
            return handle;
        };
    });
    await detail.getByRole('button', { name: 'Açmak için hazırla', exact: true }).click();
    await page.locator('[data-so-open-links]').waitFor();
    assert.deepEqual(await page.evaluate(() => window.resourceOpenCalls), []);
    await page.locator('[data-so-open-links]').press('Enter');
    await page.locator('[data-so-open-status]:visible').waitFor();
    const calls = await page.evaluate(() => window.resourceOpenCalls);
    assert.deepEqual(calls.map(call => call.url), [fixture.links[2].url, hidden.url, added.url]);
    assert.ok(calls[0].active && calls.every(call => call.features === 'noopener,noreferrer'));
    assert.doesNotMatch(await page.locator('[data-so-open-status]').innerText(), /engellendi|başarıyla açıldı/i);
    const popup = page.waitForEvent('popup');
    await page.locator('[data-so-open-url]').nth(1).click();
    const target = await popup;
    await target.waitForLoadState();
    assert.equal(await target.evaluate(() => window.opener), null);
    await target.close();
    await page.getByRole('button', { name: 'Düzenlemeye dön', exact: true }).click();
    assert.equal(await page.locator('[data-so-open-links]').count(), 0);
    pass('Opening: fresh prepare opens nothing; explicit keyboard activation, ordered selection, isolation, usable individual fallback and honest null-handle status');
    await layouts(page, 'after-groups');
    await detail.getByRole('button', { name: 'Grubu sil', exact: true }).click();
    await page.locator('.mud-dialog').getByRole('button', { name: 'Sil', exact: true }).click();
    await page.locator('.mud-dialog').waitFor({ state: 'hidden' });
    await eventually(async () => (await api('resources/me', 'GET', null, actor)).sets.some(group => group.id === fixture.groupId), false);
    pass('Group deletion persists');
    await navigate(page, 'admin/resources');
    await page.getByText('Bu alana erişim yetkiniz yok', { exact: true }).waitFor();
    await api('resources/categories', 'POST', { name: 'Denied synthetic' }, actor, 403);
    pass('Lead title does not grant management: menu hidden, direct route and API denied');
    await navigate(page, 'resources');
    await api('sessions/revoke', 'POST', { sessionId: fixture.sessionId, reason: 'Synthetic resource reauthentication verification' });
    const refresh = page.getByRole('button', { name: 'Yenile', exact: true });
    if (await refresh.isVisible()) await refresh.click().catch(error => {
        if (new URL(page.url()).pathname !== '/session-expired') throw error;
    });
    await page.waitForURL(url => url.pathname === '/session-expired');
    assert.equal(new URL(page.url()).searchParams.get('returnUrl'), 'resources');
    await screenshot(page, 'after-session-expired');
    await page.locator('a[href*="login"]').click();
    await page.locator('button[type=submit]').click();
    await page.waitForURL(url => url.pathname === '/resources');
    pass('Real local session revocation routes to expiration; reauthentication returns to resources');
}

async function manager(page, fixture) {
    await navigate(page, 'resources');
    await page.locator('a[href="admin/resources"]').filter({ hasText: 'Bağlantı Yönetimi' }).waitFor();
    await guide(page, true);
    await navigate(page, 'admin/resources');
    await page.getByRole('button', { name: 'Kategoriler', exact: true }).click();
    await page.getByRole('button', { name: 'Yeni kategori', exact: true }).click();
    const dialog = page.locator('.mud-dialog');
    await dialog.getByLabel('Ad', { exact: true }).fill('Invalid <synthetic>');
    await dialog.getByRole('button', { name: 'Kaydet', exact: true }).click();
    await dialog.locator('.so-problem').waitFor();
    assert.equal(await dialog.getByLabel('Ad', { exact: true }).inputValue(), 'Invalid <synthetic>');
    await dialog.getByLabel('Ad', { exact: true }).fill('Yönetilen Örnek ' + fixture.suffix);
    await dialog.getByRole('button', { name: 'Kaydet', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
    fixture.managedCategory = (await api('resources/categories')).find(category => category.name === 'Yönetilen Örnek ' + fixture.suffix);
    assert.ok(fixture.managedCategory);
    const row = page.locator('tr').filter({ hasText: fixture.managedCategory.name });
    await row.getByRole('button').click();
    await dialog.getByLabel('Ad', { exact: true }).fill(fixture.managedCategory.name + ' taslak');
    const winner = await api('resources/categories/' + fixture.managedCategory.id, 'PUT', { ...fixture.managedCategory, displayOrder: 5, expectedVersion: fixture.managedCategory.version });
    await dialog.getByRole('button', { name: 'Kaydet', exact: true }).click();
    await dialog.getByText('Kayıt siz düzenlerken değişti', { exact: true }).waitFor();
    assert.equal(await dialog.getByLabel('Ad', { exact: true }).inputValue(), fixture.managedCategory.name + ' taslak');
    assert.equal(await dialog.getByRole('button', { name: 'Kaydet', exact: true }).isDisabled(), true);
    await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).click();
    fixture.managedCategory = winner;
    await row.getByRole('button').click();
    await dialog.getByRole('checkbox', { name: 'Arşivlenmiş', exact: true }).check();
    await dialog.getByRole('button', { name: 'Kaydet', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
    await row.waitFor({ state: 'hidden' });
    await page.getByRole('checkbox', { name: 'Arşivlenenleri göster', exact: true }).check();
    await row.getByRole('button').click();
    await dialog.getByRole('checkbox', { name: 'Arşivlenmiş', exact: true }).uncheck();
    await dialog.getByRole('button', { name: 'Kaydet', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
    await page.getByRole('checkbox', { name: 'Arşivlenenleri göster', exact: true }).uncheck();
    await page.getByRole('button', { name: 'Bağlantılar', exact: true }).click();
    await page.getByRole('button', { name: 'Yeni bağlantı', exact: true }).click();
    await dialog.getByLabel('Ad', { exact: true }).fill('Yönetilen bağlantı ' + fixture.suffix);
    await dialog.getByLabel('Adres (URL)', { exact: true }).fill(new URL('harmless/managed', ui).href);
    await dialog.getByLabel('Amaç', { exact: true }).fill('Sentetik yerel doğrulama uygulaması.');
    await dialog.getByRole('button', { name: 'Sıra: Artır', exact: true }).press('Enter');
    await eventually(() => dialog.getByLabel('Sıra', { exact: true }).inputValue(), '1');
    await dialog.getByRole('button', { name: 'Sıra: Azalt', exact: true }).press('Enter');
    await eventually(() => dialog.getByLabel('Sıra', { exact: true }).inputValue(), '0');
    await audit(page, 'after-management-form', '.mud-dialog');
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(800);
    await audit(page, 'after-management-form-mobile', '.mud-dialog');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.waitForTimeout(800);
    await dialog.getByRole('button', { name: 'Kaydet', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
    await page.getByLabel('Bağlantı ara', { exact: true }).fill('Yönetilen bağlantı ' + fixture.suffix);
    await eventually(() => page.locator('.so-resource-results-head [role="status"]').innerText(), '1 bağlantı');
    const linkRow = page.locator('tr').filter({ hasText: 'Yönetilen bağlantı ' + fixture.suffix });
    await linkRow.getByRole('button').click();
    await dialog.getByLabel('Erişim notu', { exact: true }).fill('Sadece yerel ve sentetik içerik.');
    await dialog.getByRole('checkbox', { name: 'Arşivlenmiş', exact: true }).check();
    await dialog.getByRole('button', { name: 'Kaydet', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
    await linkRow.waitFor({ state: 'hidden' });
    await page.getByRole('checkbox', { name: 'Arşivlenenleri göster', exact: true }).check();
    await linkRow.waitFor();
    await linkRow.getByText('Arşivlenmiş', { exact: true }).waitFor();
    await linkRow.getByRole('button').click();
    assert.equal(await dialog.getByLabel('Erişim notu', { exact: true }).inputValue(), 'Sadece yerel ve sentetik içerik.');
    await dialog.getByRole('checkbox', { name: 'Arşivlenmiş', exact: true }).uncheck();
    await dialog.getByRole('button', { name: 'Kaydet', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
    pass('ResourceCurator: category/link create/edit/archive/restore, rejected-save correction and conflict draft retention');
    await page.getByLabel('Bağlantı ara', { exact: true }).fill('');
    await page.getByRole('checkbox', { name: 'Arşivlenenleri göster', exact: true }).uncheck();
    await layouts(page, 'after-management');
}

(async () => {
    const access = mode === 'denied'
        ? (await api('access/users')).find(user => user.corporateIdentity === 'demo:' + actor)
        : await api('access/me', 'GET', null, actor);
    assert.ok(access, 'Known task-local demo actor');
    assert.equal(access.authenticationSource, 'demo-api-bridge');
    if (mode === 'denied') {
        if (access.accessStatus.toLowerCase() !== 'disabled') await api(`access/users/${access.userId}/disable`, 'POST', { reason: 'Synthetic resource UI verification', expectedVersion: access.version });
    } else {
        await api(`access/users/${access.userId}/roles`, 'PUT', { roles: [mode === 'manager' ? 'ResourceCurator' : 'Lead'], reason: 'Synthetic resource UI verification', expectedVersion: access.version });
        const current = await api('access/me', 'GET', null, actor);
        assert.ok(current.capabilities.includes('Resources.View'));
        assert.equal(current.capabilities.includes('Resources.Manage'), mode === 'manager');
    }
    const suffix = '(SYN-' + randomUUID().slice(0, 6) + ')';
    const fixture = { suffix, label: 'Tarama ' + suffix, environment: 'Pilot-' + suffix, links: [] };
    if (mode === 'ordinary') {
        const demoName = 'Örnek Uygulamalar (SYN)';
        if (!(await api('resources/categories')).some(category => category.name === demoName)) {
            const demo = await api('resources/categories', 'POST', { name: demoName });
            const examples = [
                ['Hizmet Durumu', 'Uygulamaların güncel durumunu ve planlı bakım duyurularını inceleyin.'],
                ['Operasyon El Kitabı', 'Günlük kontrol adımları ve onaylı çalışma yönergeleri.'],
                ['İzleme Panosu', 'Sentetik hizmet göstergelerini tek görünümde takip edin.'],
                ['Talep Merkezi', 'Örnek talepleri ve takip kayıtlarını görüntüleyin.']
            ];
            for (const [index, [name, purpose]] of examples.entries()) await api('resources/links', 'POST', {
                categoryId: demo.id, name: name + ' (SYN)', purpose, environment: index < 2 ? 'Demo' : 'Test',
                url: new URL(`harmless/showcase-${index}`, ui).href, displayOrder: index
            });
        }
        fixture.category = await api('resources/categories', 'POST', { name: fixture.label });
        for (let i = 0; i < 28; i++) fixture.links.push(await api('resources/links', 'POST', {
            categoryId: fixture.category.id, name: `${fixture.label} ${String(i + 1).padStart(2, '0')}`, purpose: 'Sentetik yerel arama ve grup doğrulaması.',
            url: new URL(`harmless/${i}`, ui).href, displayOrder: i, environment: i === 27 ? fixture.environment : 'Demo'
        }));
    }
    const browser = await chromium.launch({ executablePath: process.env.WASAS_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true,
        ignoreDefaultArgs: ['--disable-popup-blocking'] });
    try {
        const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
        await context.route('**/harmless/**', route => route.fulfill({ contentType: 'text/html', body: '<title>Synthetic local target</title><h1>Local test target</h1>' }));
        const page = await context.newPage();
        page.setDefaultTimeout(10000);
        page.on('pageerror', error => { throw error; });
        const beforeSessions = await api('sessions/active?pageSize=100');
        await page.goto(new URL('login', ui).href);
        await page.locator('button[type=submit]').click();
        await page.waitForURL(url => !url.pathname.includes('login'));
        if (mode !== 'denied') {
            const afterSessions = await api('sessions/active?pageSize=100');
            const created = afterSessions.items.filter(session => !session.isCurrent && session.userId === access.userId
                && !beforeSessions.items.some(old => old.sessionId === session.sessionId));
            assert.equal(created.length, 1, 'Uniquely identify the task-created browser session');
            fixture.sessionId = created[0].sessionId;
        }
        if (mode === 'denied') {
            // Disabled actors cannot establish an active API session, so no resource circuit is expected.
            for (const route of ['resources', 'resources/sets', 'admin/resources']) {
                await page.goto(new URL(route, ui).href);
                await page.waitForURL(url => ['/session-expired', '/login', '/access-denied'].includes(url.pathname));
                assert.equal(await page.locator('.so-resource-page').count(), 0);
                assert.equal(await page.locator('.mud-nav-link[href="resources"], .mud-nav-link[href="resources/sets"], .mud-nav-link[href="admin/resources"]').count(), 0);
                await screenshot(page, 'after-denied');
            }
            await api('resources/me', 'GET', null, actor, 403);
            await api('resources/links', 'GET', null, actor, 403);
            pass('Disabled actor: session gate prevents resource navigation and all three direct routes; resource API returns 403');
        } else if (mode === 'ordinary') await ordinary(page, fixture);
        else await manager(page, fixture);
        assert.deepEqual(accessibility.filter(result => result.violations.length), [], 'Automated accessibility violations');
    } finally {
        await browser.close();
        if (fixture.groupId) {
            const preferences = await api('resources/me', 'GET', null, actor);
            if (preferences.sets.some(group => group.id === fixture.groupId)) await api(`resources/me/sets/${fixture.groupId}?expectedVersion=${preferences.version}`, 'DELETE', null, actor);
        }
        if (fixture.category) await api('resources/categories/' + fixture.category.id, 'PUT', { ...fixture.category, archived: true, expectedVersion: fixture.category.version });
        if (out) fs.writeFileSync(path.join(out, `browser-${mode}.json`), JSON.stringify({ mode, checks, accessibility }, null, 2));
    }
})().catch(error => { console.error(error.stack); process.exitCode = 1; });
