# Paket İçeriği — Secure Ops Repo İskeleti

Bu paket, **Secure Ops Automation & AI Analysis Hub** projesi için ajan dostu repo iskeletidir. Hem Cursor hem Claude Code hem GitHub Copilot ile çalışacak şekilde yapılandırılmıştır.

## Toplam İstatistik

- **65 dosya**
- **34 markdown dosyası** (dokümantasyon)
- **6 JSON Schema** + **6 örnek JSON** (contracts)
- **10 Cursor rule** (.mdc)
- **8 .NET csproj** (6 src + 2 tests, hepsi boş iskelet)
- **1 sln** + **3 ortak build dosyası** (Directory.Build.props, Directory.Packages.props, global.json)
- **3 ajan entry dosyası** (AGENTS.md, CLAUDE.md, .github/copilot-instructions.md)

## Ne Eklendi, Ne Eklenmedi

### ✅ Eklendi (bu oturumda)

- Tüm dokümantasyon (İngilizce, `docs/14-management-summary-tr.md` hariç).
- Tüm karar kayıtları (7 ADR).
- Tüm faz planları (Phase 0–8).
- Tüm Cursor kuralları (10 mdc).
- Tüm contract schema'ları ve örnekleri.
- .NET solution iskeleti (boş `.csproj`'lar — kod yok, sadece referanslar ve paket listesi).
- Tüm config dosyaları (`.editorconfig`, `.gitignore`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`).

### ⏸ Eklenmedi (kasıtlı — Faz 1'de eklenecek)

- Hiçbir uygulama kodu (`.cs`, `.razor`, `Program.cs` vb.).
- PowerShell scriptleri (`.ps1`, `.pssc`, `.psrc`).
- SQL migration dosyaları (DDL `docs/04-domain-model.md`'de, çalıştırılabilir formatı Faz 1'de).
- Test sınıfları.
- Runbook'lar (`docs/runbooks/`, Faz 1 Sprint 6'da üretilir).

Bu kararın gerekçesi: senin onayladığın kapsam **"Doküman + iskelet + Faz 0 task'ları"** idi. Faz 1 kod üretimi, ya Cursor/Claude Code üzerinden ya da Phase 1 sprint'lerinde manuel yapılır.

## Klasör Haritası

```
secure-ops-repo/
├── AGENTS.md                          ← Tüm ajanların okuyacağı ana giriş
├── CLAUDE.md                          ← Claude Code'a özel ek talimatlar
├── README.md                          ← İnsan kullanıcıya proje tanıtımı
├── .editorconfig                      ← Kod stili
├── .gitignore
├── global.json                        ← .NET SDK 8.0
├── Directory.Build.props              ← Tüm projelere uygulanan ortak ayarlar
├── Directory.Packages.props           ← Merkezi NuGet versiyon yönetimi
├── SecureOps.sln                      ← Solution dosyası (8 proje)
├── .cursor/rules/                     ← Cursor kuralları (10 .mdc)
├── .github/
│   └── copilot-instructions.md
├── docs/                              ← Proje hafızası (15 doküman + 7 ADR)
│   ├── 00-project-brief.md
│   ├── 01-current-operations-context.md
│   ├── 02-roadmap.md
│   ├── 03-architecture.md
│   ├── 04-domain-model.md
│   ├── 05-security-model.md
│   ├── 06-integrations.md
│   ├── 07-diagnostic-modules.md
│   ├── 08-audit-model.md
│   ├── 09-snapshot-change-safety.md
│   ├── 10-ai-rag-strategy.md
│   ├── 11-feasibility.md
│   ├── 12-mvp-backlog.md
│   ├── 13-definition-of-done.md
│   ├── 14-management-summary-tr.md    ← TEK Türkçe doküman (yönetim için)
│   └── adr/ADR-0001..0007-*.md
├── plans/                             ← Faz planları (Phase 0–8)
├── contracts/
│   ├── schemas/                       ← 6 JSON Schema
│   └── examples/                      ← 6 örnek payload
├── src/                               ← .NET projeleri (boş iskelet)
│   ├── SecureOps.Domain/
│   ├── SecureOps.Shared/
│   ├── SecureOps.Infrastructure/
│   ├── SecureOps.Api/
│   ├── SecureOps.Worker/
│   └── SecureOps.Ui/
├── tests/
│   ├── SecureOps.Tests.Unit/
│   └── SecureOps.Tests.Integration/
├── scripts/                           ← Faz 1'de doldurulacak (README var)
└── sql/                               ← Faz 1'de doldurulacak (README var)
```

## Bir Ajan Bu Repoyla Nasıl Konuşur

1. `AGENTS.md` okunur (mandatory reading order tanımlı).
2. Kullanıcı talebi gelir → ajan ilgili faz planını ve docs'u okur.
3. Karar verirken `.cursor/rules/050-security-audit-rules.mdc` boundary'lerine bakar.
4. Mimari değişiklik gerekiyorsa önce ADR önerisi yazar.
5. Kod üretirken `.cursor/rules/020-backend-dotnet-rules.mdc` ve ilgili kural dosyasını uygular.
6. Tamamlanınca `docs/13-definition-of-done.md` checklist'ine bakar.

## Ne Demek "Kişi Takibi Değil"

Bu çerçeveleme repoda 18 yerde, tutarlı dille korunur. Yönetime mailinde verdiğin söz buralarda kayıt altında:

- `AGENTS.md` 10 non-negotiable rule, madde 4
- `.cursor/rules/050-security-audit-rules.mdc` madde 10
- `docs/01-current-operations-context.md` Audit Reframing bölümü
- `docs/05-security-model.md` Audit Is Not Surveillance bölümü
- `docs/08-audit-model.md` reinforcement bölümü
- `docs/14-management-summary-tr.md` Audit Çerçevelemesi bölümü
- `.cursor/rules/060-ui-rules.mdc` UI enforcement bölümü
- `plans/PHASE-4-audit-and-alarm-response-verification.md` Anti-Surveillance Enforcement bölümü

İleride bir ajan veya geliştirici bir leaderboard veya "fastest responder" widget'ı önerse, repo bu öneriyi reddeder.

## Sonraki Adımlar (Senin İçin)

1. Bu repoyu yeni bir git deposu olarak başlat (`git init`).
2. `docs/pilot-servers.md` (henüz yok — Faz 0 task'ı) yaz: 15 aday pilot sunucu.
3. Faz 0'ı başlat: `plans/PHASE-0-discovery-and-project-setup.md`.
4. İlk hafta paydaş mailleri at, Bilgi Güv ve Siber Güv toplantılarını ayarla.
5. JEA PoC için bir test sunucusu iste.
6. Bittikten sonra: Cursor veya Claude Code ile Faz 1 Sprint 1'i başlat.

## Kontrol Edildi

- [x] Tüm JSON dosyaları geçerli (`json.load`).
- [x] Tüm csproj/props dosyaları geçerli XML (`ET.parse`).
- [x] Klasör hiyerarşisi `AGENTS.md` Directory Map'iyle eşleşir.
- [x] CONTOSO placeholder her yerde tutarlı.
- [x] Doküman dili kararı doğru uygulanmış (1 Türkçe, geri kalanı İngilizce).
- [x] 10 non-negotiable rule + 7 ADR çapraz referansla tutarlı.
- [x] Faz pre-condition'ları net.
