# ADR 0011: Asinhrona obrada u paketima (batch)

## Kontekst

Administrator šalje CSV sa do 100.000 kupaca. API mora odmah odgovoriti, obrada se dijeli na dijelove kroz Kafku, radnici se skaliraju, korisnik vidi progres, neispravni redovi ne zaustavljaju posao, pad radnika ne smije ponoviti završene dijelove, a posao se može otkazati.

## Odluka

- Novi servis Batch sa svojom bazom posjeduje poslove: fajl, dijelove, progres, rezultate i otkazivanje.
- Radnici su instance Recommendationa u režimu worker (zaseban kontejner, lokalno dvije instance). Računaju iz postojeće kopije planova preko biblioteke Pricing.
- Batch dijeli fajl na dijelove od 500 redova i šalje ih preko outboxa na batch.chunks (ključ = id posla i broj dijela, da se dijelovi jednog posla raspodijele na sve particije; 6 particija). Poruka nosi same redove.
- Radnik u jednoj transakciji upisuje obrađeni dio (processed_chunks) i outbox red sa rezultatom na batch.results, pa tek onda commituje offset.
- Batch upisuje rezultat dijela sa ON CONFLICT DO NOTHING i iz toga računa progres. Progres se šalje preko SSE-a.
- U letu je najviše oko 20 dijelova. Otkazivanje zaustavlja slanje novih, a rezultati već poslatih se ignorišu.
- Neispravan red se prijavljuje u greškama dijela i ne zaustavlja posao.

## Zašto

- Batch ne treba svoju kopiju planova ni sinhrone pozive Recommendationu.
- Poruka sa redovima znači da radnik ne čita tuđu bazu.
- Ključ (posao, dio) čini obradu i upis rezultata idempotentnim, pa ponovljen dio ne mijenja ishod.
- Prozor daje kontrolu protoka i otkazivanje bez kontrolnog kanala prema radnicima.

## Alternative

- Radnici u Batchu sa vlastitom kopijom planova: dupliran potrošač i obračun. Odbačeno.
- Radnik zove Recommendation preko HTTP-a po kupcu: 100.000 sinhronih poziva. Odbačeno.
- Sve dijelove poslati odmah i otkazivati preko kontrolnog topica: svaki radnik mora čitati sve otkaze. Odbačeno.
- Fajl na dijeljenom disku umjesto redova u poruci: zajednički disk je skrivena veza između servisa. Odbačeno.

## Posljedice

- Poslije otkazivanja se završi do 20 dijelova uzalud.
- Rezultat zavisi od stanja kopije u trenutku obrade dijela; različiti dijelovi mogu vidjeti različite verzije ako se plan promijeni usred posla. Svaki rezultat bilježi korištene verzije.
- Treći servis i treća baza u lokalnom okruženju.