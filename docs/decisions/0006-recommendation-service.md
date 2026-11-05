# ADR 0006: Recommendation servis: kopija podataka, potrošač i preporuke

## Kontekst

Recommendation računa cijene svih planova za potrošnju i poresku grupu,
čuva rezultat i omogućava prihvatanje plana. Ne smije zvati Catalog dok
odgovara, pa drži vlastitu kopiju planova i poreskih grupa iz događaja,
koji mogu stići više puta i van redoslijeda.

## Odluka

- Vlastita baza. Kopija: plan_versions (razredi kao JSONB) i
  tax_group_versions, sa svim verzijama, uključujući buduće.
- Potrošač upisuje verziju sa INSERT ... ON CONFLICT (id, version_no)
  DO NOTHING. Offset se commituje tek poslije upisa u bazu; automatski
  commit je isključen.
- Greške: pokvarena poruka i nepoznata verzija formata idu odmah u
  recommendation.dlq; nepoznat tip se preskače; nedostupna baza se ponavlja dok ne prođe, bez commita offseta; ostale greške imaju 3 pokušaja, pa DLQ.
- Prazna kopija se puni čitanjem topica od početka (ADR 0005).
- POST /recommendations računa sa verzijama važećim danas (UTC), čuva
  nepromjenjiv rezultat sa korištenim verzijama i u istoj transakciji upisuje RecommendationCalculated u outbox. Prazna kopija daje 503, nepoznata poreska grupa 400.
- Prihvatanje prihvata verziju plana iz preporuke, jednom po preporuci, i objavljuje PlanAccepted.

## Zašto

- Kopija se uvijek čita cijela i nikad ne mijenja, pa je JSONB za razrede jednostavniji od tabele; provjere je već uradio Catalog.
- Verzije su nepromjenjive, pa je ključ verzije ujedno ključ deduplikacije i redoslijed nije bitan.
- Kad padne baza, ponavljanje sa DLQ-om bi poslalo ispravne poruke u DLQ; zato se infrastruktura čeka, a u DLQ idu samo neobradive poruke.
- Sačuvane verzije čine svaku preporuku ponovljivom.

## Alternative

- Razredi kao tabela, kao u Catalogu: stroži model bez koristi za kopiju. Odbačeno.
- Tabela obrađenih događaja: suvišna kad je upis prirodno idempotentan; ostaje za potrošače sa sporednim efektima (npr. email). Odbačeno ovdje.
- Automatski commit offseta: može zapamtiti poruku prije upisa i izgubiti je pri padu. Odbačeno.
- Keš kopije u memoriji: brže čitanje, ali složenije; uvodi se samo ako mjerenje pokaže potrebu. Odbačeno za sada.

## Posljedice

- Kopija kasni za Catalogom koliko kasni potrošač.
- Nedostupna baza zaustavlja obradu particije dok se ne vrati.
- Dva ista zahtjeva daju dvije preporuke; to su dva izračuna.