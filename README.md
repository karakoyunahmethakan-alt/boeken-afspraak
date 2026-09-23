# Boeken Afspraak

ASP.NET Core (.NET 8) + SQLite backend voor het inplannen van boeken-ophaalafspraken via een QR-code op een flyer. Voorkomt dubbele boekingen met een database-constraint (geen los "Claude moet dit bijwerken"-stapje meer nodig).

## Hoe het werkt

- **Klant** scant de QR-code → `index.html` → kiest dag/tijd (10 vaste momenten van 18:00–20:00, 12 min. uit elkaar) → vult gegevens in → boekt. De prijsindicatie is een vaste schaal: elk vol blok van 10 boeken telt voor €3,50.
- **Slot-vergrendeling**: een unieke database-index op (datum, tijdstip) zorgt dat twee mensen nooit hetzelfde moment kunnen boeken, zelfs bij gelijktijdige verzoeken. Geannuleerde afspraken geven het slot automatisch weer vrij.
- **Bevestiging**: klant krijgt een e-mail met een persoonlijke link (`beheer.html?token=...`) om te annuleren of te verzetten. Jij krijgt een e-mail met een `.ics`-bijlage die je mailprogramma automatisch als agenda-item kan herkennen.
- **Beheer**: `/admin` — inloggen met e-mail/wachtwoord (JWT), lijst met alle afspraken, annuleren, CSV-export voor de boekhouding.

## Lokaal draaien

```bash
cd BoekenAfspraak.Api
dotnet restore
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "een-lange-willekeurige-string-hier"
dotnet user-secrets set "App:PublicBaseUrl" "http://localhost:5000"
ADMIN_EMAIL="jij@example.com" ADMIN_PASSWORD="kies-een-wachtwoord" dotnet run
```

Open `http://localhost:5000`. De SQLite-database komt in `App_Data/afspraken.db` (lokaal) of `/data` (Docker/Railway).

> `ADMIN_EMAIL`/`ADMIN_PASSWORD` worden alleen gebruikt om het **eerste** admin-account aan te maken, bij een lege database. Daarna kun je ze weglaten.

## Verplichte configuratie (environment variables)

| Variabele | Doel |
|---|---|
| `Jwt__SigningKey` | Lange willekeurige string voor het ondertekenen van admin-login-tokens. **Verplicht**, de app start niet zonder. |
| `App__OwnerEmail` | Jouw e-mailadres — hier komen nieuwe-afspraak-meldingen binnen. |
| `App__PublicBaseUrl` | De publieke URL van de site (zonder trailing slash), bv. `https://boeken.up.railway.app`. Wordt gebruikt in de links die naar klanten gaan. |
| `Brevo__ApiKey` | API-sleutel van je Brevo-account (zie hieronder). |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD` | Alleen nodig bij de allereerste start om het admin-account aan te maken. |

(Let op de dubbele underscore `__` — dat is hoe ASP.NET Core geneste config-secties uit environment variables leest, bv. `Brevo__ApiKey`.)

## Brevo instellen (transactionele e-mail)

E-mail gaat via Brevo's HTTP API (`https://api.brevo.com/v3/smtp/email`) in plaats van SMTP — Railway blokkeert uitgaande SMTP-verbindingen (poort 587), maar gewoon HTTPS-verkeer (poort 443) werkt wel.

1. Maak een gratis account aan op [brevo.com](https://www.brevo.com) (voorheen Sendinblue).
2. Ga naar **SMTP & API** in je accountinstellingen en maak een **API key** aan (geen SMTP-key, de "API key" onder het tabblad "API Keys").
3. Verifieer het afzenderadres dat je gaat gebruiken (Brevo dashboard → **Senders** → voeg je e-mailadres toe en bevestig via de verificatiemail).
4. Zet:
   - `Brevo__ApiKey` = de aangemaakte API key
   - `App__OwnerEmail`, en `Brevo:SenderEmail`/`Brevo:SenderName` in `appsettings.json` indien je een ander afzenderadres wilt dan de standaardwaarde.

## Prijsindicatie

De prijsindicatie is altijd een vaste schaal: elk vol blok van 10 boeken telt voor €3,50 (een aangebroken blok telt niet mee). Dit is instelbaar via `App:FlatBlockSize` / `App:FlatPricePerBlock` in `appsettings.json`.

## Deployen op Railway

1. Push deze map naar een GitHub-repo.
2. Railway → **New Project → Deploy from GitHub repo** → kies de repo. Railway herkent de `Dockerfile` automatisch.
3. **Volume toevoegen**: Railway dashboard → Settings → Volumes → mount op `/data` (dit is waar de SQLite-database en geüploade foto's blijven staan — zonder dit ben je alles kwijt bij elke herstart).
4. **Environment variables** instellen in het Railway dashboard (zie tabel hierboven) — inclusief `ADMIN_EMAIL`/`ADMIN_PASSWORD` voor de allereerste opstart.
5. Zodra de eerste deploy klaar is, kopieer je de Railway-URL naar `App__PublicBaseUrl` en herstart je de service.
6. **Automatisch deployen**: staat al aan — elke push naar de gekoppelde branch (standaard `main`) triggert automatisch een nieuwe build + deploy. Niets extra's nodig.
7. Zet de Railway-URL (of een eigen domein daaraan gekoppeld) in de QR-code op je flyers.

## CSV-export

`/admin` → knop "Exporteer CSV" (of `GET /api/admin/appointments/export.csv` met een geldig admin-token). Bevat alle afspraken (ook geannuleerde, voor een volledig overzicht) met datum, status, klantgegevens, aantal boeken, geschatte prijs en of er verzet is.

## Bekende beperkingen (bewuste keuzes voor v1)

- SQLite is prima voor dit volume (buurtflyer, niet duizenden aanvragen/dag). Bij sterke groei: overstappen naar PostgreSQL is een kleine wijziging in `Program.cs` (`UseNpgsql` i.p.v. `UseSqlite`).
- Geen tweeweg-synchronisatie met Google Calendar — de `.ics`-bijlage in de meldingsmail is de gekozen, eenvoudigere route (zie eerdere afspraak in de chat).
- Rate-limiting/spampreventie op het boekingsformulier is niet ingebouwd; voeg zo nodig een eenvoudige honeypot-veld of IP-rate-limit toe als misbruik een probleem wordt.
