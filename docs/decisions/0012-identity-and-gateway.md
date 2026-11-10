# ADR 0012: Identitet i API gateway

## Kontekst

Zadatak traži OIDC provajdera identiteta sa konfiguracijom u repozitorijumu, dvije uloge, gateway kao jedinu ulaznu tačku (rutiranje, provjera tokena, rate limiting, correlation id) i servise koji i sami provjeravaju uloge. Razvojna mašina ima 7,6 GB RAM-a; sistem bez Docker kontejnera već troši oko 4,9 GB, a stack oko 1,9 GB (izmjereno).

## Odluka

- Keycloak (start-dev, ugrađena baza, heap 384 MB, ograničenje 700 MB),
  realm gridwise izvezen u repozitorijum. Uloge admin i customer, test
  korisnici u README-u. Klijent za preglednik koristi Authorization Code + PKCE; skripte koriste poseban dev klijent.
- Keycloak je iza istog nginxa (/auth), pa preglednik vidi jednu adresu, a izdavač tokena je isti za sve. Servisi ključeve čitaju interno.
- Gateway je YARP servis: rutira /api/*, provjerava token, ograničava broj zahtjeva po korisniku i postavlja correlation id. nginx šalje /api samo njemu.
- Servisi ponovo provjeravaju token i uloge: izmjene u Catalogu i cijeli Batch samo admin; preporuke customer ili admin, a kupac vidi samo svoje (tuđa preporuka je 404).
- Događaji nose id korisnika samo radi revizije (changedBy u Catalogu), nikad token.
- Token je u pregledniku samo u memoriji; za SSE stream gateway prihvata token iz URL-a samo na toj ruti, izbacuje ga iz prosljeđenog URL-a i maskira u logovima i tragovima.

## Zašto

- Keycloak je standard i realm se verzioniše kao JSON. Sa ograničenim
  heapom troši ~520 MB, što stane u preostalu memoriju.
- Provjera i u gatewayu i u servisima: ko zaobiđe gateway (npr. preko porta za debug) i dalje nema pristup.
- Token u događaju bi istekao prije obrade (DLQ, ponovno punjenje kopije), a u topicu bez isteka bio bi zauvijek sačuvan kredencijal. Potrošač ne odlučuje o pravima po događaju; pristup Kafki se štiti na nivou brokera.
- 404 umjesto 403 za tuđu preporuku ne otkriva da preporuka postoji.

## Alternative

- Dex (~30 MB): lagan, ali uloge samo preko grupa i manje mogućnosti. Odbačeno; prvi izbor ako memorija ne bi bila dovoljna.
- Vlastiti IdP (.NET + OpenIddict, ~80 MB): novi servis za pisanje i održavanje. Odbačeno.
- Samo gateway provjerava uloge: jednostavnije, ali zaobilaženje gatewaya daje pun pristup. Odbačeno.
- Zakucani ključevi potpisa u realmu (da preživi ponovno pravljenje Keycloaka): privatni ključ u repozitorijumu. Odbačeno; servisi umjesto toga osvježavaju ključeve svakih 30 s.

## Posljedice

- Brojači rate limitinga su u memoriji jedne instance gatewaya; za više instanci treba zajedničko skladište (npr. Redis).
- Gateway vjeruje X-Forwarded-For samo od nginxa.
- Keycloak u dev režimu ponovo uvozi realm pri svakom pravljenju kontejnera; izmjene se rade u JSON-u.
- Kafka lokalno nema autentifikaciju servisa; u produkciji SASL i ACL po topicu.