# ADR 0007: Recommendation: popuštena izdržljivost upisa i kompaktno čuvanje

## Kontekst

POST /recommendations je trebao imati p95 ispod 50 ms. Mjerenje (Server-Timing,
1 VU, 205 planova): upis (preporuka + outbox) 35 ms od 46 ms ukupno, čitanje
iz baze 5 ms, račun i JSON po 1 ms. Eksperiment sa synchronous_commit = off
spustio je upis na 7,5 ms, a p95 sa 69 na 21 ms. Uz to je preporuka sa 200
planova imala oko 78 KB JSON-a (svi razredi cijene u svakoj stavci).

## Odluka

- Upis preporuke i outbox poruke radi se u jednoj transakciji sa
  SET LOCAL synchronous_commit = off, samo za POST /recommendations.
  Prihvatanje plana ostaje potpuno izdržljivo.
- Preporuka čuva rangiranu listu { planId, planVersionNo, name, total },
  bez razrada cijene. Stavke iz starog oblika (sa razradom) i dalje se čitaju.
- GET učitava sačuvane verzije planova i poreske grupe iz kopije, ponovo
  računa razradu pomoću Pricing i vraća isti odgovor kao POST. Redoslijed i
  ukupne cijene uzimaju se iz sačuvanih stavki. Prihvatanje koristi sačuvanu
  verziju i ukupnu cijenu.

## Zašto

- Preporuka je izvedeni podatak. Zapis i događaj su u istoj transakciji, pa
  se pri padu gube zajedno i stanje ostaje konzistentno.
- Verzije su nepromjenjive, pa je ponovni račun iz sačuvanih verzija isti
  rezultat; baš to traži zadatak (ponovljivost iz korištenih verzija).
- Pod opterećenjem fsync upisa je glavni trošak, a ne račun.

## Alternative

- Brži disk ili jači hardver: ne rješava dizajn, a ovdje nije opcija. Odbačeno.
- Keš u memoriji za čitanje: GET nije usko grlo, POST jeste. Odbačeno.
- synchronous_commit = off globalno: ugrozilo bi prihvatanja i Catalog-like
  podatke koji moraju preživjeti pad. Odbačeno.
- Čuvanje pune razrade: radi, ali je 3 puta veće i sporije za upis. Odbačeno.

## Posljedice

- Upravo vraćena preporuka može nestati ako baza padne u prozoru do oko pola sekunde (3 × wal_writer_delay);
  GET tada daje 404 (a njen događaj se nije objavio).
- GET košta ponovni račun i jedan upit za verzije.
- Ako se pravila cijena u kodu promijene, stare preporuke se ponovo računaju
  po novim pravilima (ukupne cijene ostaju sačuvane, razrada se može razlikovati).
- Poslije izmjene (isti hardver): 1 VU p95 25 ms, upis 7 ms od 15 ms.
