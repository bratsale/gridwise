# ADR 0008: Granice servisa i vlasništvo nad podacima

## Kontekst

Zadatak traži servise koji se nezavisno isporučuju i od kojih svaki posjeduje svoje podatke. Administrator rijetko mijenja planove, a kupci često traže preporuke. Preporuka mora raditi i kad administratorski dio ne radi.

## Odluka

- Catalog posjeduje planove i poreske grupe sa svim verzijama. Jedino on ih mijenja i objavljuje događaje o promjenama.
- Recommendation posjeduje preporuke i prihvatanja. Planove i poreske grupe drži kao kopiju punjenu iz događaja i nikad ih ne mijenja.
- Pravila obračuna su biblioteka (GridWise.Pricing) bez stanja i zavisnosti, a ne servis.
- Svaki servis ima svoju bazu. Niko ne čita ni ne piše tuđu bazu; podaci se dijele samo preko događaja.
- Prihvatanje plana je u Recommendationu, uz preporuku na koju se odnosi.

## Zašto

- Granica prati razlog promjene i opterećenje: administratorsko pisanje je rijetko i mora biti strogo, a računanje je često i samo čita.
- Kopija u Recommendationu znači da kupac dobija preporuku i kad Catalog ne radi.
- Obračun je čista funkcija; kao servis bi dodao mrežni poziv, kašnjenje i tačku otkaza bez ikakve koristi.
- Preporuka i prihvatanje se provjeravaju zajedno (plan mora biti u preporuci), pa su u istoj bazi i istoj transakciji.

## Alternative

- Modularni monolit sa jednom bazom: jednostavniji, ali zadatak traži nezavisne servise. Poređenje je u README-u.
- Pricing kao zaseban servis: sinhroni poziv po svakoj preporuci. Odbačeno.
- Zajednička baza za oba servisa: skrivena veza preko tabela, promjena šeme lomi drugi servis. Odbačeno.
- Prihvatanja u posebnom servisu: preuranjeno dok nema više logike oko ugovora. Odbačeno za sada.

## Posljedice

- Kopija kasni za Catalogom (eventual consistency), obično ispod sekunde.
- Šema verzija postoji dvaput (izvor i kopija), a promjena događaja traži dogovor kroz ugovor (docs/events).
- Više infrastrukture za pokretanje i nadzor nego kod monolita.