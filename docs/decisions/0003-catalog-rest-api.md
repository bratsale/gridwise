# ADR 0003: REST API Catalog servisa, istovremena izmjena i idempotentnost

## Kontekst

Catalog izlaže API za administratora: kreiranje, pregled, izmjenu i
povlačenje planova i poreskih grupa. API mora odražavati model iz ADR 0002 (nepromjenjive verzije), spriječiti da dva administratora prepišu jedan drugog i spriječiti da ponovljen zahtjev za kreiranje napravi duplikat.

## Odluka

1. Izmjena je kreiranje nove verzije: POST /plans/{id}/versions (isto za
poreske grupe). Odgovor 201.
2. Povlačenje je DELETE /plans/{id}. Datum ide kao query parametar
effectiveFrom (podrazumijevano danas, UTC), bez tijela. Odgovor 204.
Plan i dalje postoji, njegova najnovija verzija ima status retired.
3. GET /plans/{id} vraća najnoviju verziju, ne verziju važeću danas.
Catalog je API za administratora; koja verzija važi na neki dan određuje Recommendation.
4. Istovremena izmjena: GET vraća ETag sa version_no, izmjena i        povlačenje zahtijevaju If-Match. Zastarjela verzija ili trka na primarnom ključu daju 409, nedostajući If-Match daje 400.
5. Kreiranje plana zahtijeva zaglavlje Idempotency-Key. Ključ, hash tijela i odgovor upisuju se u istoj transakciji kao plan, verzija i outbox. Ponovljen ključ sa istim tijelom vraća sačuvani odgovor; sa drugim tijelom 422. Istovremeni isti zahtjevi: drugi čeka na jedinstveni ključ u bazi, pa vraća sačuvani odgovor. Ključevi stariji od 24 h se brišu.
6. Poreske grupe nemaju Idempotency-Key: ključ je code iz tijela, pa ponovljeno kreiranje daje 409 umjesto duplikata.
7. Novac se u JSON-u prenosi kao broj. Server ga čita direktno u decimal. Klijent ne računa iznose, samo ih prikazuje; sve iznose računa server.
8. Greške su ProblemDetails (RFC 9457); greške validacije su po polju (400).

## Zašto

- POST na /versions opisuje ono što se zaista dešava: nastaje novi resurs, ništa se ne prepisuje.
- DELETE doslovno odgovara zahtjevu "brisanje povlači plan"; datum kao
query parametar ne zavisi od tijela koje HTTP za DELETE ne definiše.
- If-Match/ETag je standardni HTTP mehanizam za optimistično zaključavanje.
- Idempotency-Key je opšti obrazac za ponovljive POST zahtjeve i radi bez obzira na to ko generiše identifikator.

## Alternative

- PUT /plans/{id} za izmjenu: poznatiji, ali obećava zamjenu resursa. Odbačeno.
- POST /plans/{id}/retire: ne zloupotrebljava DELETE, ali odstupa od
formulacije zadatka. Odbačeno.
- Identifikator koji generiše klijent (PUT /plans/{id} sa uuid-om):
jednostavnije, bez tabele ključeva, ali zahtijeva da svaki klijent generiše id. Odbačeno.
- 412 i 428 umjesto 409 i 400: precizniji, ali jedan kod za sukob je
jednostavniji za klijenta. Odbačeno.
- Novac kao string: bez rizika preciznosti na klijentu, ali nezgodniji za upotrebu; rizik je uklonjen pravilom da klijent ne računa. Odbačeno.

## Mane

- DELETE vraća 204, a resurs i dalje postoji (sa statusom retired).
- Ponovljena izmjena ili povlačenje (isti If-Match) daje 409, ne isti odgovor; klijent mora ponovo učitati stanje.
- Tabela idempotency_keys traži čišćenje.
- Ključ stariji od 24 h više ne štiti od duplikata.

## Stil API-ja i validacija

- API je pisan kao Minimal API. Svaki resurs ima svoj fajl sa metodom koja registruje rute (npr. MapPlanEndpoints); Program.cs ih samo poziva. Endpoint je tanak: čita zaglavlja, poziva servis, mapira rezultat na HTTP kod. Logika je u servisu.
- Validacija ima dva nivoa. Oblik zahtjeva (obavezna polja, opsezi) provjerava framework preko DataAnnotations. Poslovna pravila (razredi rastu, tačno jedan razred bez granice i to poslednji, effective_from nije prije prethodne verzije) su u jednoj metodi domena, sa jediničnim testovima, nezavisno od HTTP-a.
- Alternativa: kontroleri. Poznatiji i inače praktičniji obrazac, ali za servise sa desetak endpointa ne donose ništa što Minimal API nema, a oslanjaju se na više konvencija, plus, htio sam da probam nešto novo :). Odbačeno.