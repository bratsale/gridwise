# ADR 0001: Pravila obračuna cijene

## Kontekst
Projektni zadatak ostavlja otvorena tri pravila: kako se primjenjuju cjenovni razredi, da li se popust primjenjuje prije ili poslije poreza, i na čemu se zasniva eko taksa. Novčani iznosi moraju biti tačni, a pravilo zaokruživanja mora biti dokumentovano.

## Odluka
1. Razredi su progresivni: svaki blok potrošnje obračunava se po cijeni po kWh svog razreda. Zadnji razred nema gornju granicu, prema propozicijama projektnog zadatka.
2. Popust plana primjenjuje se na zbir energije, prije poreza.
3. Eko taksa = kWh * stopa eko takse poreske grupe (pretpostavka: stopa je cijena po kWh).
4. Osnovica za PDV = neto energija + eko taksa.
5. Novac je `decimal`, zaokružuje se na 2 decimale, polovina naviše (od nule), poslije svakog množenja. Sabiranje i oduzimanje se ne zaokružuju, pa se svi prikazani iznosi tačno slažu.
6. Preporuka koristi verzije plana i poreske grupe koje važe na dan zahtjeva (UTC) i bilježi verzije koje je koristila.

Redoslijed obračuna:
    iznosi po razredima -> zbir -> popust -> neto -> eko taksa -> osnovica PDV-a -> PDV -> ukupno

## Razmotrene alternative
- Cijela potrošnja po cijeni razreda u koji upada. Odbačeno: ukupan iznos nije monoton (plan Standard: 300 kWh košta 24,00, a 301 kWh košta 21,07).
- Popust poslije poreza. Odbačeno: PDV bi se obračunao na iznos koji kupac ne plaća.
- Eko taksa kao postotak osnovice. Moguće čitanje ulaznih podataka, vidi posljedice.
- Zaokruživanje samo ukupnog iznosa. Odbačeno: prikazane stavke se ne moraju sabirati u ukupno.

## Posljedice
- Sva pravila su u biblioteci `GridWise.Pricing`, koja nema zavisnost od baze ni od poruka. Promjena pravila je promjena jedne funkcije i njenih testova.
- Ako je eko taksa zapravo postotak, iznosi se mijenjaju (Premium, 250 kWh, domaćinstvo: 24,10 naspram 22,76).
- Sistem bilježi koji je plan preporučen ili prihvaćen, ali ne modeluje ugovore ni naplatu.

## Pretpostavke
- Jedinica eko takse nije navedena u zadatku; pretpostavljena je cijena po kWh.
- Datumi se računaju u UTC.
- Razlomljene vrijednosti kWh su dozvoljene; negativna potrošnja se odbija.
- Plan čiji zadnji razred ima gornju granicu je neispravan i odbija se.
