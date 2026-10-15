# Ekip arkadaşı katılım planı (sahip için)

Karar tarihi: 2026-10-14. Yanıtlar: alan **henüz belirsiz**, **Mac + Claude Code**, **yalnız sentetik veri (TEST erişimi yok)**, kayıt için **GitHub Issues + `docs/team/WORK-LOG.md`**. Arkadaşa verilecek belge: [`CONTRIBUTING.md`](../../CONTRIBUTING.md). Bu dosya sahibe yöneliktir; GitHub ayarları sahip tarafından elle yapılır, bu dalda hiçbir ayar değiştirilmedi.

## 1. Görev dağılımı önerisi

### Sahiplik tablosu

| Alan | Sahibi | Arkadaş |
|---|---|---|
| Backend, API, Worker, SQL, güvenlik, sözleşmeler, paketleme (`src/SecureOps.Api`, `Worker`, `Infrastructure`, `Domain`, `sql/`, `scripts/`) | Codex / sahip | Dokunmaz; ihtiyacı Issue'ya yazar |
| Servis Hesapları modülü (backend + API + UI) | Claude (sahip oturumları) | Dokunmaz |
| Planned OCO Announcements, Turuncuhat entegrasyonu | Codex / TH koordinatörü | Dokunmaz |
| `src/SecureOps.Ui/` (Servis Hesapları sayfaları hariç) | Claude (sahip oturumları) | **Issue ile atanan dilim** (sahip, işi bir kez dosya/sayfa listesiyle verir) |
| Birim testler (UI ve saf mantık) | İlgili alanın sahibi | Atanan dilimin testleri |
| `docs/` (yeni, ADR olmayan; `docs/team/`, kılavuzlar) | Claude / sahip | Atanan belgeler |
| `AGENTS.md`, `CLAUDE.md`, ADR'ler, migration/ADR numaraları, merge | Yalnız sahip | Asla |
| Windows'a bağlı doğrulama (LocalDB SQL, IIS, JEA/WinRM, PS 5.1, paketleme) | Yalnız sahip | Asla; PR'da "çalıştırılmadı" yazar |

Alan belirsiz olduğu için önerilen başlangıç: ilk 1–2 PR **belge + birim test** (en düşük çakışma; ortamı ve akışı öğretir), sonra sahip belirli bir UI dilimini Issue ile atar. Böylece mevcut Claude UI oturumlarıyla aynı dosyaya dokunulmaz.

### Çakışma kuralları

1. Bir iş = bir dal = bir PR; dal `master`'dan, adı `feature|fix|docs/<konu>-YYYYMMDD`.
2. Aynı dosyaya iki kişi (ya da iki oturum) aynı anda dokunmaz. Önce `docs/team/WORK-LOG.md` satırı + Issue; sahip kendi oturumlarının "çalışıyor" satırını ortak merge dosyasında tutar ve çakışma görürse arkadaşa Issue'da söyler.
3. OpenAPI anlık görüntüsü yalnız ekleme; yeniden üretilen dosya commit edilmez.
4. Migration/ADR/G-numarası: önce `WORK-LOG.md` "Number requests", sahip onaylayıp yazar, sonra kullanılır (sahip aynı numarayı ortak merge dosyasına da işler).
5. Master dala **merge** ile alınır (rebase/force-push yok); her merge sonrası doğrulama yeniden koşulur.
6. Merge'ü yalnız sahip yapar; sahip merge'den önce ortak merge dosyasına bakar (mevcut düzen aynen sürer).

### Ortak merge dosyasının yerine arkadaş için ne?

Ortak dosya repo dışında ve sahibin bilgisayarında kalır; iki koordinatörün düzeni değişmez. Arkadaş için: (a) **GitHub Issue** (iş, tartışma, atama), (b) repoda [`docs/team/WORK-LOG.md`](WORK-LOG.md) (kim hangi dosyada çalışıyor + numara istekleri), (c) PR. Sahip/koordinatörler PR açılmadan önce `WORK-LOG.md` ve açık PR listesine bakar; numara onayı ve merge sırası kaydı sahipte kalır. Arkadaş merge dosyasını görmez.

## 2. GitHub yetkisi (sahip adımları, Türkçe arayüz/İngilizce menü adlarıyla)

Önerilen en az yetki: **Write** (Admin/Maintain değil). Daha da sıkı seçenek: arkadaş hiç collaborator olmaz, repoyu **fork** eder ve fork'tan PR açar (public repoda mümkün); dal ve push yetkisi gerekmez, ama `WORK-LOG.md` ve Issue akışı aynı kalır. Aşağıdaki adımlar Write içindir.

**A. Arkadaşın hazırlığı**
1. GitHub hesabında 2FA'yı açtığını doğrula (kişisel repoda sahip bunu zorlayamaz; arkadaştan yazılı onay iste).

**B. Collaborator daveti**
1. `github.com/aknonr/SecureOps` → **Settings** → **Collaborators** (şifre istenirse onayla).
2. **Add people** → arkadaşın GitHub kullanıcı adını yaz → **Add to repository**.
3. Açılan kutuda rolü **Write** seç (Read/Triage de seçilebilir; Maintain/Admin seçme). Daveti gönder; arkadaş e-postadan kabul eder.

**C. `master` için ruleset (arkadaşı eklemeden ÖNCE yap)**
1. **Settings** → **Rules** → **Rulesets** → **New ruleset** → **New branch ruleset**.
2. **Ruleset name:** `master-koruma`; **Enforcement status:** *Active*.
3. **Bypass list** → **Add bypass** → **Repository admin** rolünü ekle; modu **Allow for pull requests only** seç. (Neden: tek onaylayıcı sensin; PR yazarı kendi PR'ını onaylayamaz. Bypass olmazsa kendi PR'larını merge edemezsin. Arkadaş admin olmadığı için bypass kullanamaz.)
4. **Target branches** → **Add target** → **Include default branch**.
5. **Branch rules** altında işaretle:
   - **Restrict deletions**
   - **Block force pushes**
   - **Require a pull request before merging** → *Required approvals:* **1**; **Dismiss stale pull request approvals when new commits are pushed**; **Require approval of the most recent reviewable push**; (*Require conversation resolution* isteğe bağlı).
   - **Require status checks to pass**: repoda şu an CI iş akışı (`.github/`) yok; **işaretleme**. İleride bir CI eklenirse o zaman açılır.
6. **Create**.
7. Dal silme yetkisi için: **Settings** → **General** → *Automatically delete head branches* açık olabilir; zorunlu değil.

**D. Ek (hepsi isteğe bağlı, ücretsiz)**
1. **Settings** → **Advanced Security** (ya da *Code security*) → **Secret scanning** ve **Push protection**: Enable.
2. **Settings** → **Actions** → **General**: workflow yok; fork PR'ları için *Require approval for all outside collaborators* seçili kalsın.
3. Repoya hiçbir secret/variable ekleme (zaten yok).

**E. Doğrulama (sahip yapar)**: arkadaşın hesabıyla `master`'a doğrudan push denemesi reddedilmeli; dal + PR akışı çalışmalı; sen kendi PR'ını merge edebilmelisin.

## 3. Yapılmayanlar / doğrulanmayanlar

- GitHub ayarlarına dokunulmadı; menü adları GitHub'ın mevcut arayüzüne göre yazıldı, tıklarken değişmiş olabilir.
- Mac'te build/test/format koşulmadı (Mac yok); `CONTRIBUTING.md` Mac adımları belgeden/rehberlerden derlendi, ilk gün arkadaşla birlikte denenmeli. Windows'a özgü testlerin (`WindowsFact`) Mac'te atlanması beklenir; birim paketin tamamının Mac'te yeşil olduğu doğrulanmadı.

## 4. AGENTS.md "Ownership" için tek satır önerisi (sahip onaylarsa yazılır)

```
- **Team collaborator (Mac, Claude Code; no TEST access):** scope assigned per GitHub Issue; works under `CONTRIBUTING.md` and `docs/team/WORK-LOG.md`; Windows-bound verification, ADRs, migration/ADR numbers and merges stay with the owner.
```
