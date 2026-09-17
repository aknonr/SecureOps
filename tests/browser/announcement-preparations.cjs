// Isolated published Demo only; same arguments as announcements.cjs, without a mode.
const assert=require('node:assert/strict'), fs=require('node:fs'), path=require('node:path'), crypto=require('node:crypto');
const {chromium,request}=require(process.argv[2]);
const {loopback,navigate,signIn,apiContext,json}=require('./journey-support.cjs');
const ui=loopback(process.argv[3]), api=loopback(process.argv[4]), deniedUi=loopback(process.argv[5]), out=path.resolve(process.argv[6]);
(async()=>{
    fs.mkdirSync(out,{recursive:false});
    const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
    const context=await browser.newContext({ignoreHTTPSErrors:true,viewport:{width:1440,height:900}});
    await context.route('**/*',r=>['localhost','127.0.0.1'].includes(new URL(r.request().url()).hostname)?r.continue():r.abort());
    const page=await context.newPage(), client=await apiContext(request,api), denied=await apiContext(request,api,'team-lead');
    try {
        const id=crypto.randomUUID(), draft='/api/v1/announcements/'+id, root='/api/v1/announcements/preparations';
        const content={ocoReference:'OCO-SYNTHETIC',scope:'Yerel sistem',subject:'Hazırlık & Türkçe '+id.slice(0,8),announcementDate:'2026-09-14',
            workStart:'2026-09-14T23:59:37+03:00',workEnd:'2026-09-15T02:00:59+03:00',description:'Türkçe & <b> düz metin',impact:'Kısa kesinti',checks:'Sağlık kontrolü',notes:'',
            to:['reader@example.invalid'],cc:['copy@example.invalid'],bannerRevision:'bundle-v1',templateRevision:'oco-table-v2',dateTextRevision:'tr-v1',
            affectedServices:Array.from({length:155},(_,i)=>`Sentetik servis ${i+1} - Türkçe & uygulama hizmeti <b>`)};
        await json(client,draft+'?version=0',{method:'PUT',data:content});
        await signIn(page,ui); await navigate(page,ui,'announcements');
        if(await page.getByRole('button',{name:'Şimdi değil',exact:true}).count()) await page.getByRole('button',{name:'Şimdi değil',exact:true}).click();
        await page.getByRole('button',{name:'Düzenle: '+content.subject,exact:true}).click();
        await page.getByText('Güncel içerik kaydedildi',{exact:true}).waitFor();
        await page.getByRole('button',{name:'İncelemeye hazırla',exact:true}).click();
        await page.waitForURL(/announcements\/preparations\/[a-f0-9-]+$/);
        await page.getByText('Değişmez hazırlık · v1',{exact:true}).waitFor();
        const preparedId=new URL(page.url()).pathname.split('/').at(-1), prepared=await json(client,root+'/'+preparedId);
        assert.equal(prepared.state,'Prepared'); assert.equal(prepared.artifactType,'FinalAnnouncement');
        const frame=page.frameLocator('iframe[title="Hazırlanan duyuru"]');
        await frame.locator('body').waitFor(); assert.equal(await frame.locator('script').count(),0);
        assert.ok((await frame.locator('body').innerText()).includes(content.affectedServices[154]));
        assert.ok(await frame.locator('img').evaluateAll(a=>a.length===6&&a.every(i=>i.complete&&i.naturalWidth>0)));
        for(const width of [1440,390]) {
            await page.setViewportSize({width,height:844});
            if(width===390) await page.waitForFunction(()=>document.querySelector('.so-page').getBoundingClientRect().width>=innerWidth-50);
            await page.screenshot({path:path.join(out,'review-'+width+'.png'),fullPage:true,animations:'disabled'});
            assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false);
        }
        const downloadEvent=page.waitForEvent('download'); await page.getByRole('button',{name:'Hazırlanan maili indir',exact:true}).click();
        const file=path.join(out,'prepared.eml'); await (await downloadEvent).saveAs(file);
        assert.deepEqual(fs.readFileSync(file),Buffer.from(prepared.email,'base64'));
        await json(client,draft+'?version=1',{method:'PUT',data:{...content,subject:'Sonraki taslak'}});
        await navigate(page,ui,'announcements/preparations/'+preparedId); await frame.getByText('Türkçe & <b> düz metin',{exact:true}).waitFor();
        assert.equal((await json(client,root+'/'+preparedId)).fingerprint,prepared.fingerprint);
        await page.getByRole('link',{name:'Geçmiş',exact:true}).click(); await page.getByRole('row').filter({hasText:content.subject}).waitFor();
        await page.screenshot({path:path.join(out,'history-mobile.png'),fullPage:true});
        assert.equal((await denied.get(root+'/'+preparedId)).status(),403);
        await signIn(page,deniedUi); await navigate(page,deniedUi,'announcements/preparations/'+preparedId);
        await page.locator('.so-problem').waitFor(); assert.equal(await page.locator('iframe').count(),0);
        fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({state:'Prepared',downloadSha256:crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'),checks:'SQL snapshot, 155 services, six images, exact download, later-edit retention, history, desktop/mobile, denial; no send'},null,2));
    } finally {await client.dispose();await denied.dispose();await context.close();await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
