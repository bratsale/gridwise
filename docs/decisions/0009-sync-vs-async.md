# ADR 0009: Sinhrona i asinhrona komunikacija

## Kontekst

Klijent razgovara sa servisima, a servisi međusobno razmjenjuju podatke.
Preporuka mora odgovoriti za manje od 50 ms (p95) i raditi i kad Catalog ne radi.

## Odluka

- Klijent poziva servise sinhrono, preko HTTP-a (REST), jer korisnik čeka odgovor: preporuku, potvrdu izmjene ili sukob (409).
- Servisi međusobno komuniciraju samo asinhrono, događajima preko Kafke. Nema sinhronih poziva između servisa.
- Događaji su činjenice o onome što se desilo (PlanVersionCreated, RecommendationCalculated, PlanAccepted), a ne komande drugom servisu.
- Recommendation odgovara isključivo iz svoje kopije podataka.

## Zašto

- Sinhroni poziv veže dostupnost: ako Recommendation zove Catalog, radi samo kad rade oba (dva servisa sa po 99,9% daju 99,8%). Događaji tu vezu prekidaju.
- Mrežni poziv u svakoj preporuci troši dio budžeta od 50 ms.
- Producer ne zna ko sluša. Novi potrošač (notifikacije, analitika) se dodaje bez izmjene Cataloga ili Recommendationa.
- Kafka čuva događaje, pa novi ili prazan potrošač može pročitati sve od početka.

## Alternative

- Recommendation zove Catalog pri svakom zahtjevu, uz keš: jednostavnije, ali veže dostupnost i kašnjenje, a keš ionako treba invalidaciju. Odbačeno.
- gRPC između servisa: brži, ali i dalje sinhron, sa istim vezivanjem. Odbačeno.
- Zahtjev-odgovor preko Kafke: asinhroni transport sa sinhronim čekanjem; složenost bez koristi. Odbačeno.

## Posljedice

- Kopija kasni: nekoliko sekundi poslije izmjene preporuka može koristiti prethodnu verziju plana.
- Potrebni su outbox, idempotentni potrošači, DLQ i praćenje zaostatka (ADR 0005, 0006).
- Tok kroz više servisa je teže pratiti; correlation id putuje u omotu događaja.